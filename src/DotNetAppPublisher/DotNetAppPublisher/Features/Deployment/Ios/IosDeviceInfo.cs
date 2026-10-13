using System.Text.Json;
using System.Text.RegularExpressions;

namespace DotNetAppPublisher.Features.Deployment.Ios;

/// <summary>How the Mac currently reaches a real iPhone or iPad.</summary>
public static class IosDeviceTransport
{
    public const string Usb = "usb";
    public const string Network = "network";
    public const string Unknown = "unknown";
}

/// <summary>A physical iOS device reported by CoreDevice (`xcrun devicectl`) or `xctrace`.</summary>
public sealed record IosDeviceInfo(
    string Udid,
    string Name,
    string Model,
    string OsVersion,
    string Transport,
    bool IsPaired,
    bool IsDeveloperModeEnabled)
{
    public string TransportLabel => Transport switch
    {
        IosDeviceTransport.Network => "Wi-Fi",
        IosDeviceTransport.Usb => "USB",
        _ => "connection unknown"
    };

    public string OsLabel => string.IsNullOrWhiteSpace(OsVersion) ? string.Empty : $"iOS {OsVersion}";

    /// <summary>True when devicectl reports the device as paired and reachable, so deployment can run.</summary>
    public bool IsDeployable => IsPaired;

    public string StateDetail
    {
        get
        {
            if (!IsPaired)
            {
                return "not paired with this Mac — connect it over USB once and trust this computer";
            }

            return IsDeveloperModeEnabled
                ? string.Empty
                : "Developer Mode is off — enable Settings > Privacy & Security > Developer Mode on the device";
        }
    }

    public string DisplayName
    {
        get
        {
            var parts = new List<string> { $"{Name} ({TransportLabel})" };
            if (OsLabel.Length > 0)
            {
                parts.Add(OsLabel);
            }

            var identity = string.Join(" · ", parts);
            var detail = StateDetail;
            return detail.Length > 0 ? $"{identity} — {detail}" : identity;
        }
    }

    public static int Rank(IosDeviceInfo device) => device.IsDeployable ? 0 : 1;
}

/// <summary>
/// Parsers for the two command-line listings available on macOS. Pure, so both the CoreDevice JSON
/// shape and the `xctrace` text shape are covered by tests.
/// </summary>
public static class IosDeviceListParser
{
    private static readonly Regex DeviceLine = new(
        @"^(?<name>.+?)\s*\((?<os>[^()]+)\)\s*\((?<udid>[0-9A-Fa-f][0-9A-Fa-f\-]{7,})\)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Parses `xcrun devicectl list devices --json-output`, which CoreDevice writes as a file.</summary>
    public static IReadOnlyList<IosDeviceInfo> ParseDevicectlJson(string? json)
    {
        var devices = new List<IosDeviceInfo>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return devices;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return devices;
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("result", out var result)
                || !result.TryGetProperty("devices", out var deviceElements)
                || deviceElements.ValueKind != JsonValueKind.Array)
            {
                return devices;
            }

            foreach (var element in deviceElements.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var hardware = GetObject(element, "hardwareProperties");
                var properties = GetObject(element, "deviceProperties");
                var connection = GetObject(element, "connectionProperties");

                var udid = ReadString(hardware, "udid")
                    ?? ReadString(element, "identifier");
                if (string.IsNullOrWhiteSpace(udid))
                {
                    continue;
                }

                var model = ReadString(hardware, "marketingName")
                    ?? ReadString(hardware, "productType")
                    ?? ReadString(hardware, "deviceType")
                    ?? string.Empty;
                var name = ReadString(properties, "name") ?? (model.Length > 0 ? model : udid);

                devices.Add(new IosDeviceInfo(
                    udid,
                    name,
                    model,
                    ReadString(properties, "osVersionNumber") ?? string.Empty,
                    MapTransport(ReadString(connection, "transportType")),
                    string.Equals(ReadString(connection, "pairingState"), "paired", StringComparison.OrdinalIgnoreCase),
                    string.Equals(ReadString(properties, "developerModeStatus"), "enabled", StringComparison.OrdinalIgnoreCase)));
            }
        }

        return devices
            .GroupBy(device => device.Udid, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(IosDeviceInfo.Rank)
            .ThenBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Parses the `== Devices ==` section of `xcrun xctrace list devices`, used as a fallback when
    /// CoreDevice does not know the device (for example older iOS versions).
    /// </summary>
    public static IReadOnlyList<IosDeviceInfo> ParseXctraceList(string? output)
    {
        var devices = new List<IosDeviceInfo>();
        if (string.IsNullOrWhiteSpace(output))
        {
            return devices;
        }

        var inDevicesSection = false;
        foreach (var rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (rawLine.StartsWith("== Simulators ==", StringComparison.Ordinal))
            {
                break;
            }

            if (rawLine.StartsWith("== Devices ==", StringComparison.Ordinal))
            {
                inDevicesSection = true;
                continue;
            }

            if (!inDevicesSection)
            {
                continue;
            }

            var match = DeviceLine.Match(rawLine);
            if (!match.Success)
            {
                continue;
            }

            devices.Add(new IosDeviceInfo(
                match.Groups["udid"].Value,
                match.Groups["name"].Value.Trim(),
                string.Empty,
                match.Groups["os"].Value.Trim(),
                IosDeviceTransport.Unknown,
                true,
                true));
        }

        return devices;
    }

    private static JsonElement? GetObject(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object ? value : null;

    private static string? ReadString(JsonElement? element, string name)
    {
        if (element is not { } target
            || !target.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static string MapTransport(string? transportType) => transportType?.ToLowerInvariant() switch
    {
        "wired" or "usb" => IosDeviceTransport.Usb,
        "localnetwork" or "network" or "wifi" => IosDeviceTransport.Network,
        _ => IosDeviceTransport.Unknown
    };
}
