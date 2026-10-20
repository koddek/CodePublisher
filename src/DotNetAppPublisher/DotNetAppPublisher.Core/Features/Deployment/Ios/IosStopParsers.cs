using System.Text.Json;

namespace DotNetAppPublisher.Features.Deployment.Ios;

/// <summary>
/// Parsers and checks behind the Stop actions on iOS. CoreDevice's JSON for `device info apps` and
/// `device info processes` is not a published schema, so every lookup accepts the field-name variants
/// seen in the wild (plain paths, file:// URLs, nested path/url objects). Pure, so each shape is unit
/// tested instead of discovered on a device.
/// </summary>
public static class IosStopParsers
{
    private static readonly string[] BundleIdKeys = ["bundleIdentifier", "bundleId", "CFBundleIdentifier"];
    private static readonly string[] PathKeys = ["bundlePath", "bundleURL", "bundleUrl", "containerURL", "path", "url", "URL", "absoluteString"];
    private static readonly string[] PidKeys = ["processIdentifier", "pid", "processId"];
    private static readonly string[] ExecutableKeys = ["executable", "executablePath", "imagePath", "path", "url", "URL", "absoluteString"];
    private static readonly string[] AppArrays = ["apps", "applications"];
    private static readonly string[] ProcessArrays = ["runningProcesses", "processes"];

    /// <summary>Finds the on-device `.app` bundle path for a bundle id, or null when it is not installed.</summary>
    public static string? FindAppBundlePath(string? appsJson, string bundleId)
    {
        if (string.IsNullOrWhiteSpace(appsJson) || string.IsNullOrWhiteSpace(bundleId))
        {
            return null;
        }

        foreach (var entry in EnumerateEntries(appsJson, AppArrays))
        {
            if (!string.Equals(ReadString(entry, BundleIdKeys), bundleId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var path = FindAppBundlePathInEntry(entry);
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }

        return null;
    }

    /// <summary>
    /// Finds the PIDs of running processes whose executable lives inside the given `.app` bundle path.
    /// </summary>
    public static IReadOnlyList<int> FindRunningPids(string? processesJson, string? bundlePath)
    {
        var pids = new List<int>();
        if (string.IsNullOrWhiteSpace(processesJson) || string.IsNullOrWhiteSpace(bundlePath))
        {
            return pids;
        }

        var prefix = bundlePath.Trim().TrimEnd('/') + "/";
        foreach (var entry in EnumerateEntries(processesJson, ProcessArrays))
        {
            if (ReadInt(entry, PidKeys) is not { } pid || pids.Contains(pid))
            {
                continue;
            }

            var executable = FindExecutablePath(entry);
            if (executable is not null && executable.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                pids.Add(pid);
            }
        }

        return pids.OrderBy(pid => pid).ToArray();
    }

    /// <summary>
    /// True when a terminate/stop command failed only because the app is not running (or not
    /// installed), which is a no-op for the user rather than an error worth reporting.
    /// </summary>
    public static bool LooksLikeNotRunning(string? commandOutput) =>
        Contains(commandOutput, "not running")
        || Contains(commandOutput, "no such process")
        || Contains(commandOutput, "not installed")
        || Contains(commandOutput, "Failed to find")
        || Contains(commandOutput, "Unable to find");

    private static IReadOnlyList<JsonElement> EnumerateEntries(string json, string[] arrayNames)
    {
        var entries = new List<JsonElement>();
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return entries;
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("result", out var result))
            {
                return entries;
            }

            foreach (var name in arrayNames)
            {
                if (!result.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var entry in array.EnumerateArray())
                {
                    if (entry.ValueKind == JsonValueKind.Object)
                    {
                        entries.Add(entry.Clone());
                    }
                }
            }
        }

        return entries;
    }

    private static string? FindAppBundlePathInEntry(JsonElement entry)
    {
        var candidate = ReadString(entry, PathKeys);
        if (LooksLikeAppBundle(candidate))
        {
            return NormalizePath(candidate!);
        }

        // Unknown key names: take the first string in the entry that points at an .app bundle.
        if (entry.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in entry.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    var text = property.Value.GetString();
                    if (LooksLikeAppBundle(text))
                    {
                        return NormalizePath(text!);
                    }
                }
            }
        }

        return null;
    }

    private static string? FindExecutablePath(JsonElement entry)
    {
        var candidate = ReadString(entry, ExecutableKeys);
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            return NormalizePath(candidate);
        }

        // Unknown key names: take the first string that looks like an absolute path.
        if (entry.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in entry.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    var text = property.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(text) && text.StartsWith('/'))
                    {
                        return NormalizePath(text);
                    }
                }
            }
        }

        return null;
    }

    private static bool LooksLikeAppBundle(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && path.EndsWith(".app", StringComparison.OrdinalIgnoreCase)
        && path.Contains('/');

    private static string NormalizePath(string path)
    {
        var normalized = path.Trim();
        if (normalized.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["file://".Length..];
            try
            {
                normalized = Uri.UnescapeDataString(normalized);
            }
            catch (UriFormatException)
            {
            }
        }

        return normalized.TrimEnd('/');
    }

    private static string? ReadString(JsonElement element, string[] keys)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var key in keys)
        {
            if (!element.TryGetProperty(key, out var value))
            {
                continue;
            }

            switch (value.ValueKind)
            {
                case JsonValueKind.String:
                    var text = value.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                    break;
                case JsonValueKind.Object:
                    // Some fields are objects such as { "path": "..." } or { "absoluteString": "file:///..." }.
                    var nested = ReadString(value, PathKeys);
                    if (!string.IsNullOrWhiteSpace(nested))
                    {
                        return nested;
                    }
                    break;
            }
        }

        return null;
    }

    private static int? ReadInt(JsonElement element, string[] keys)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var key in keys)
        {
            if (!element.TryGetProperty(key, out var value))
            {
                continue;
            }

            switch (value.ValueKind)
            {
                case JsonValueKind.Number when value.TryGetInt32(out var number):
                    return number;
                case JsonValueKind.String when int.TryParse(value.GetString(), out var parsed):
                    return parsed;
            }
        }

        return null;
    }

    private static bool Contains(string? value, string candidate) =>
        value is not null && value.Contains(candidate, StringComparison.OrdinalIgnoreCase);
}
