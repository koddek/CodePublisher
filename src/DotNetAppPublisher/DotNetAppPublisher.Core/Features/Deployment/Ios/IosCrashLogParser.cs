using System.Text.Json;

namespace DotNetAppPublisher.Features.Deployment.Ios;

/// <summary>One file that CoreDevice listed on an iOS device.</summary>
public sealed record IosDeviceFileEntry(string Name, string Path, long SizeBytes, DateTimeOffset? Modified);

/// <summary>
/// Reads the file listing that `xcrun devicectl device info files` writes. CoreDevice's JSON is
/// not a published schema, so the parser walks the document and accepts the field names that
/// different Xcode versions use instead of expecting one exact shape.
/// </summary>
public static class IosCrashLogParser
{
    private static readonly string[] NameFields = ["name", "fileName", "filename", "displayName"];
    private static readonly string[] SizeFields = ["size", "fileSize", "sizeInBytes", "byteCount"];
    private static readonly string[] ModifiedFields = ["modifiedDate", "lastModified", "modificationDate", "dateModified"];

    /// <summary>
    /// The files in the listing. A file is taken when it has a name, and either a size or a
    /// modification time next to it, so the folders and other nodes of the document are skipped.
    /// </summary>
    public static IReadOnlyList<IosDeviceFileEntry> ParseFileListing(string? json)
    {
        var entries = new List<IosDeviceFileEntry>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return entries;
        }

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
            Walk(document.RootElement, entries, 0);
        }

        return entries
            .GroupBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderByDescending(entry => entry.Modified ?? DateTimeOffset.MinValue)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void Walk(JsonElement element, List<IosDeviceFileEntry> entries, int depth)
    {
        if (depth > 12)
        {
            return;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var name = ReadString(element, NameFields);
                if (name is not null)
                {
                    var size = ReadInt64(element, SizeFields);
                    var modified = ReadDate(element, ModifiedFields);
                    if (size is not null || modified is not null)
                    {
                        entries.Add(new IosDeviceFileEntry(
                            name,
                            ReadString(element, ["path", "relativePath", "filePath"]) ?? name,
                            size ?? 0,
                            modified));
                    }
                }

                foreach (var property in element.EnumerateObject())
                {
                    Walk(property.Value, entries, depth + 1);
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Walk(item, entries, depth + 1);
                }

                break;
        }
    }

    private static string? ReadString(JsonElement element, IReadOnlyList<string> names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text.Trim();
                }
            }
        }

        return null;
    }

    private static long? ReadInt64(JsonElement element, IReadOnlyList<string> names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
            {
                return number;
            }

            if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static DateTimeOffset? ReadDate(JsonElement element, IReadOnlyList<string> names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            if (DateTimeOffset.TryParse(value.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }
}
