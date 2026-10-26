namespace CodePublisher.Features.Deployment.Android;

/// <summary>One row of `adb devices -l`, before any device probing happens.</summary>
public sealed record AndroidDeviceListEntry(
    string Serial,
    string State,
    bool IsEmulatorSerial,
    bool IsWirelessSerial,
    string? Model,
    string? Product,
    string? UsbId);

/// <summary>
/// Parses `adb devices -l`. Pure and side-effect free, so every connection shape (USB, Wi-Fi,
/// emulator, unauthorized, offline) is covered by unit tests instead of a real device.
/// </summary>
public static class AndroidDeviceListParser
{
    private static readonly string[] KnownStates =
    [
        "device",
        "unauthorized",
        "offline",
        "connecting",
        "authorizing",
        "bootloader",
        "fastboot",
        "recovery",
        "sideload",
        "rescue",
        "host",
        "no permissions"
    ];

    public static IReadOnlyList<AndroidDeviceListEntry> Parse(string? adbDevicesOutput)
    {
        var entries = new List<AndroidDeviceListEntry>();
        if (string.IsNullOrWhiteSpace(adbDevicesOutput))
        {
            return entries;
        }

        foreach (var rawLine in adbDevicesOutput.Split(
                     ['\r', '\n'],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // adb prints daemon banners ("* daemon not running; starting now at tcp:5037") and a
            // "List of devices attached" header before the rows.
            if (rawLine.StartsWith('*') || rawLine.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var serial = FirstToken(rawLine);
            if (serial.Length == 0)
            {
                continue;
            }

            if (rawLine.Contains("no permissions", StringComparison.OrdinalIgnoreCase))
            {
                entries.Add(new AndroidDeviceListEntry(serial, "no permissions", false, false, null, null, null));
                continue;
            }

            var parts = rawLine.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                continue;
            }

            var state = parts[1].ToLowerInvariant();
            if (!KnownStates.Contains(state))
            {
                continue;
            }

            entries.Add(new AndroidDeviceListEntry(
                serial,
                state,
                serial.StartsWith("emulator-", StringComparison.OrdinalIgnoreCase),
                IsWirelessSerial(serial),
                ExtractField(parts, "model:")?.Replace('_', ' '),
                ExtractField(parts, "product:"),
                ExtractField(parts, "usb:")));
        }

        return entries;
    }

    /// <summary>
    /// A Wi-Fi target is listed as `host:port` (for example 192.168.1.42:37123) instead of a USB serial.
    /// </summary>
    public static bool IsWirelessSerial(string serial)
    {
        if (string.IsNullOrWhiteSpace(serial) || serial.StartsWith("emulator-", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var separator = serial.LastIndexOf(':');
        if (separator <= 0 || separator == serial.Length - 1)
        {
            return false;
        }

        return int.TryParse(serial[(separator + 1)..], out var port) && port is > 0 and <= 65535;
    }

    private static string FirstToken(string line)
    {
        var separator = line.IndexOfAny([' ', '\t']);
        return separator < 0 ? line : line[..separator];
    }

    private static string? ExtractField(IReadOnlyList<string> parts, string prefix)
    {
        foreach (var part in parts)
        {
            if (part.StartsWith(prefix, StringComparison.Ordinal) && part.Length > prefix.Length)
            {
                return part[prefix.Length..];
            }
        }

        return null;
    }
}
