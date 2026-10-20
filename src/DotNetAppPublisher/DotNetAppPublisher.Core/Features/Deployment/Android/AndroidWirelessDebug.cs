namespace DotNetAppPublisher.Features.Deployment.Android;

/// <summary>A wireless debugging service advertised by the device through mDNS.</summary>
public sealed record AndroidWirelessService(string Name, string Address, bool IsPairingService)
{
    public string Kind => IsPairingService ? "pairing" : "connect";

    public string DisplayName => IsPairingService
        ? $"{Address} — pairing port (needs the code shown on the device)"
        : $"{Address} — debugging port";
}

/// <summary>Outcome of switching a USB-attached device to TCP/IP debugging.</summary>
public sealed record AndroidWirelessEnableResult(string? Address, string Message);

/// <summary>
/// Pure helpers for Wireless debugging (Android 11+) and the legacy `adb tcpip` flow. Device
/// discovery and process execution live in <see cref="AndroidDeploymentService"/>.
/// </summary>
public static class AndroidWirelessDebug
{
    /// <summary>Port used by the legacy `adb tcpip` workflow.</summary>
    public const int LegacyPort = 5555;

    private static readonly char[] Separators = [' ', '\t'];

    /// <summary>Parses `adb mdns services`, which lists pairing and connect services separately.</summary>
    public static IReadOnlyList<AndroidWirelessService> ParseMdnsServices(string? mdnsOutput)
    {
        var services = new List<AndroidWirelessService>();
        if (string.IsNullOrWhiteSpace(mdnsOutput))
        {
            return services;
        }

        foreach (var rawLine in mdnsOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var isPairing = rawLine.Contains("_adb-tls-pairing._tcp", StringComparison.Ordinal);
            var isConnect = rawLine.Contains("_adb-tls-connect._tcp", StringComparison.Ordinal);
            if (!isPairing && !isConnect)
            {
                continue;
            }

            var parts = rawLine.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                continue;
            }

            var address = parts[^1];
            if (!TryParseEndpoint(address, out _, out _))
            {
                continue;
            }

            var name = parts[0].TrimEnd('.');
            if (services.Any(service => string.Equals(service.Address, address, StringComparison.Ordinal)
                                        && service.IsPairingService == isPairing))
            {
                continue;
            }

            services.Add(new AndroidWirelessService(name, address, isPairing));
        }

        return services
            .OrderBy(service => service.IsPairingService)
            .ThenBy(service => service.Address, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Accepts `host:port`, and also a full row pasted from `adb mdns services` so the address can be
    /// copied straight out of the terminal or the device UI.
    /// </summary>
    public static bool TryNormalizeAddress(string? input, out string address, out string error)
    {
        address = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            error = "Enter the device address. Wireless debugging shows it as IP:PORT (for example 192.168.1.42:37123).";
            return false;
        }

        var parts = input.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        var candidate = parts.Length == 0 ? input.Trim() : parts[^1];
        if (!TryParseEndpoint(candidate, out _, out _))
        {
            error = $"`{candidate}` is not an address with a port. Use the IP:PORT shown under Wireless debugging.";
            return false;
        }

        address = candidate;
        return true;
    }

    public static bool TryNormalizePairingCode(string? input, out string code, out string error)
    {
        code = string.Empty;
        error = string.Empty;

        var candidate = input?.Trim() ?? string.Empty;
        if (candidate.Length is < 4 or > 8 || !candidate.All(char.IsDigit))
        {
            error = "Enter the pairing code exactly as shown on the device (digits only).";
            return false;
        }

        code = candidate;
        return true;
    }

    public static bool TryParseEndpoint(string? address, out string host, out int port)
    {
        host = string.Empty;
        port = 0;

        if (string.IsNullOrWhiteSpace(address))
        {
            return false;
        }

        var separator = address.LastIndexOf(':');
        if (separator <= 0 || separator == address.Length - 1)
        {
            return false;
        }

        host = address[..separator].Trim('[', ']');
        return host.Length > 0
            && int.TryParse(address[(separator + 1)..], out port)
            && port is > 0 and <= 65535;
    }

    /// <summary>Finds the Wi-Fi IPv4 address in `adb shell ip route` output.</summary>
    public static string? ExtractWifiIpAddress(string? ipRouteOutput)
    {
        if (string.IsNullOrWhiteSpace(ipRouteOutput))
        {
            return null;
        }

        foreach (var rawLine in ipRouteOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!rawLine.Contains("wlan", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = rawLine.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
            for (var index = 0; index < parts.Length - 1; index++)
            {
                if (string.Equals(parts[index], "src", StringComparison.Ordinal)
                    && LooksLikeIpv4(parts[index + 1]))
                {
                    return parts[index + 1];
                }
            }
        }

        return null;
    }

    /// <summary>Finds the Wi-Fi IPv4 address in `adb shell getprop` output (fallback for `ip route`).</summary>
    public static string? ExtractWifiIpAddressFromProperties(string? getpropOutput)
    {
        if (string.IsNullOrWhiteSpace(getpropOutput))
        {
            return null;
        }

        foreach (var rawLine in getpropOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!rawLine.Contains("wlan0.ipaddress", StringComparison.OrdinalIgnoreCase)
                && !rawLine.Contains("wlan0.ipv4", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var open = rawLine.IndexOf('[', rawLine.IndexOf(']') + 1);
            var close = rawLine.LastIndexOf(']');
            if (open >= 0 && close > open)
            {
                var value = rawLine[(open + 1)..close].Trim();
                if (LooksLikeIpv4(value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static bool LooksLikeIpv4(string value) =>
        value.Length >= 7
        && value.Count(character => character == '.') == 3
        && value.Split('.').All(part => byte.TryParse(part, out _));
}
