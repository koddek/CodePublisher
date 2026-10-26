namespace CodePublisher.Features.Deployment.Android;

/// <summary>A wireless debugging service advertised by the device through mDNS.</summary>
public sealed record AndroidWirelessService(string Name, string Address, bool IsPairingService)
{
    /// <summary>Wi-Fi debugging 2.0 marker (Android 17 and newer), so <c>adb-1234 (2)</c> carries 2.</summary>
    public int? ServiceVersion { get; init; }

    /// <summary>True when the device advertises the Wi-Fi 2.0 service version.</summary>
    public bool IsWifiTwo => ServiceVersion >= 2;

    public string Kind => IsPairingService ? "pairing" : "connect";

    public string DisplayName => IsPairingService
        ? $"{Address} — pairing port (needs the code shown on the device)"
        : $"{Address} — debugging port";
}

/// <summary>Outcome of switching a USB-attached device to TCP/IP debugging.</summary>
public sealed record AndroidWirelessEnableResult(string? Address, string Message);

/// <summary>
/// Outcome of one <c>adb connect</c> to a wireless debugging endpoint. <c>adb connect</c> exits 0 and
/// prints a banner even when nothing connected, so <see cref="Connected"/> is decided by the state
/// <c>adb devices -l</c> reports for the endpoint, never by the command's exit code.
/// </summary>
public sealed record AndroidWirelessConnectResult(string Address, bool Connected, string State, string Message)
{
    /// <summary>True when adb listed the endpoint but the phone has not approved this computer's key.</summary>
    public bool NeedsApproval => string.Equals(State, AndroidDeviceState.Unauthorized, StringComparison.Ordinal);
}

/// <summary>Outcome of clearing this computer's wireless debugging state.</summary>
public sealed record AndroidWirelessReset(IReadOnlyList<string> Dropped, bool ServerRestarted, string Message);

/// <summary>
/// What one failed <c>adb connect</c> output means, with the change that fixes it. Unknown output
/// returns null from <see cref="AndroidWirelessDebug.ReadConnectFailure"/>, so the caller keeps its
/// general guidance instead of inventing a cause.
/// </summary>
public sealed record AndroidConnectFailure(string Cause, string Fix)
{
    /// <summary>True when the packets never reached the device, which is a network problem.</summary>
    public bool IsUnreachable { get; init; }
}

/// <summary>What one reachability probe against a phone address found.</summary>
public sealed record AndroidEndpointProbe(bool Reachable, string? SocketError)
{
    /// <summary>The socket error said in plain words, or null when the endpoint answered.</summary>
    public AndroidConnectFailure? Explanation => AndroidWirelessDebug.ReadSocketError(SocketError);

    /// <summary>True when the endpoint refused or nothing was listening on the port.</summary>
    public bool Refused => SocketError?.Contains("refused", StringComparison.OrdinalIgnoreCase) ?? false;
}

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

            // A newer device marks the service version inside the instance name:
            // `adb-R5CN20DFRKA-0z8hFB (2)  _adb-tls-connect._tcp  192.168.50.237:42857`.
            var version = (int?)null;
            if (parts.Length >= 3
                && parts[1] is { Length: >= 3 } marker
                && marker.StartsWith('(') && marker.EndsWith(')')
                && int.TryParse(marker[1..^1], out var parsedVersion))
            {
                version = parsedVersion;
                // The marker is dropped so the name keeps the shape of a version-less row.
                parts = parts.Where((_, index) => index != 1).ToArray();
            }

            var name = parts[0].TrimEnd('.');
            if (services.Any(service => string.Equals(service.Address, address, StringComparison.Ordinal)
                                        && service.IsPairingService == isPairing))
            {
                continue;
            }

            services.Add(new AndroidWirelessService(name, address, isPairing) { ServiceVersion = version });
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

    /// <summary>
    /// Merges the adb server's mDNS list with the operating system resolver's list into one row per
    /// service. The adb server's backend is not reliable on every host: it can report nothing while
    /// the system resolver sees the same phone, so both are read and the addresses are combined.
    /// adb rows win when both report the same endpoint, pairing rows come first, and an address is
    /// never listed twice.
    /// </summary>
    public static IReadOnlyList<AndroidWirelessService> MergeServices(
        IEnumerable<AndroidWirelessService> adbServices,
        IEnumerable<AndroidWirelessService> resolverServices)
    {
        var merged = new List<AndroidWirelessService>();
        var seenEndpoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var service in adbServices.Concat(resolverServices))
        {
            if (string.IsNullOrWhiteSpace(service.Address))
            {
                continue;
            }

            // The same phone can be advertised under a slightly different instance name by each
            // backend, so the endpoint (address and kind) is what identifies one service.
            var key = $"{service.Address}|{(service.IsPairingService ? "pair" : "connect")}";
            if (seenEndpoints.Add(key))
            {
                merged.Add(service);
            }
        }

        return merged
            .OrderBy(service => service.IsPairingService)
            .ThenBy(service => service.Address, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// The addresses worth connecting to for one host, in the order to try them: every debugging port
    /// advertised for that host now, then a fallback saved from an earlier session.
    ///
    /// A host can be advertised with two different ports at once, because the adb server's mDNS cache
    /// can hold the port from before wireless debugging restarted while the system resolver holds the
    /// current one. Pairing ports are never candidates, and no address is tried twice. The connect port
    /// changes every time wireless debugging restarts on the phone, so a saved address is only ever the
    /// last resort.
    /// </summary>
    public static IReadOnlyList<string> ConnectCandidates(
        IEnumerable<AndroidWirelessService> services,
        string? host,
        string? fallbackAddress)
    {
        var candidates = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(host))
        {
            foreach (var service in services)
            {
                if (service.IsPairingService
                    || !TryParseEndpoint(service.Address, out var serviceHost, out _)
                    || !string.Equals(serviceHost, host.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (seen.Add(service.Address))
                {
                    candidates.Add(service.Address);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(fallbackAddress) && seen.Add(fallbackAddress.Trim()))
        {
            candidates.Add(fallbackAddress.Trim());
        }

        return candidates;
    }

    /// <summary>
    /// The state <c>adb devices -l</c> reports for one Wi-Fi endpoint, or null when the endpoint is
    /// not listed at all. <c>adb connect</c> exits 0 and prints a banner even when the transport never
    /// becomes usable, so this is what decides whether a connect actually worked.
    /// </summary>
    public static string? FindWirelessTargetState(IEnumerable<AndroidDeviceListEntry> entries, string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        return entries.FirstOrDefault(entry =>
            string.Equals(entry.Serial, address.Trim(), StringComparison.OrdinalIgnoreCase))?.State;
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

    /// <summary>Finds the Wi-Fi IPv4 address in `adb shell ip addr show` or `ip -f inet addr` output.</summary>
    public static string? ExtractWifiIpAddressFromIpAddr(string? ipAddrOutput)
    {
        if (string.IsNullOrWhiteSpace(ipAddrOutput))
        {
            return null;
        }

        var isWlanSection = false;
        foreach (var rawLine in ipAddrOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (rawLine.Contains("wlan", StringComparison.OrdinalIgnoreCase))
            {
                isWlanSection = true;
            }
            else if (char.IsDigit(rawLine[0]) && rawLine.Contains(':'))
            {
                isWlanSection = false;
            }

            var parts = rawLine.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
            for (var index = 0; index < parts.Length - 1; index++)
            {
                if (string.Equals(parts[index], "inet", StringComparison.OrdinalIgnoreCase))
                {
                    var cidr = parts[index + 1];
                    var slash = cidr.IndexOf('/');
                    var ip = slash >= 0 ? cidr[..slash] : cidr;
                    if (LooksLikeIpv4(ip) && (isWlanSection || rawLine.Contains("wlan", StringComparison.OrdinalIgnoreCase)))
                    {
                        return ip;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Reads a socket error (from a reachability probe or from adb's own text) and says what it
    /// means for a phone on Wi-Fi. A phone that stopped answering its advertised port is the case
    /// that used to be indistinguishable: <c>adb pair</c> reports the same protocol fault whether the
    /// connection never opened or the phone answered and did not speak the protocol.
    /// </summary>
    public static AndroidConnectFailure? ReadSocketError(string? socketError)
    {
        if (string.IsNullOrWhiteSpace(socketError))
        {
            return null;
        }

        var failure = ReadConnectFailure(socketError);
        if (failure is null)
        {
            return null;
        }

        // A stale address is the reachable-looking case of an unreachable phone: the advertisement
        // was read a minute ago, the phone has since moved on, and only a fresh probe shows it.
        return failure.IsUnreachable
            ? failure with
            {
                Fix = failure.Fix + " An address from an earlier scan goes stale the moment the phone's " +
                      "Wi-Fi address or its wireless debugging restarts, so scan again rather than retrying the same one."
            }
            : failure;
    }

    /// <summary>
    /// What it means when adb lists a Wi-Fi endpoint that is not usable while a TCP connection to it
    /// opens. The phone's adbd is answering, so the network and the port are not the problem: the phone is
    /// not accepting this computer's key, which is what it does once this computer was forgotten on it
    /// (Wireless debugging > Paired devices > Forget, or Revoke adb debugging authorizations), or an
    /// earlier session was left half open. That row is also what <c>adb connect</c> reports as connected
    /// while the phone is not, which is why it is named here instead of being read as a network fault.
    /// Returns null for a state this class has nothing specific to say about.
    /// </summary>
    public static AndroidConnectFailure? ReadStalledTransport(string? state, bool endpointReachable)
    {
        if (!endpointReachable || string.IsNullOrWhiteSpace(state))
        {
            return null;
        }

        if (string.Equals(state, AndroidDeviceState.Unauthorized, StringComparison.Ordinal))
        {
            return new AndroidConnectFailure(
                "The phone is answering on that port and is waiting for this computer to be approved.",
                "Unlock the phone and accept the debugging prompt. If no prompt appears, open Developer options > " +
                "Wireless debugging on the phone, tap this computer under Paired devices, choose Forget, and pair " +
                "again with Add a device > Connect with QR code.");
        }

        if (!string.Equals(state, AndroidDeviceState.Offline, StringComparison.Ordinal)
            && !string.Equals(state, "connecting", StringComparison.Ordinal))
        {
            return null;
        }

        return new AndroidConnectFailure(
            "The phone is answering on that port, but the debugging session never became usable. That is what " +
            "happens when the phone no longer trusts this computer's key, and also when an earlier session was " +
            "left half open.",
            "On the phone, open Developer options > Wireless debugging, tap this computer under Paired devices and " +
            "choose Forget (or press Revoke adb debugging authorizations), then pair again with Add a device > " +
            "Connect with QR code: pairing again is what puts this computer's key back on the phone. Until that " +
            "happens this computer cannot be trusted by the phone, no matter which port is used.");
    }

    /// <summary>
    /// Reads what <c>adb pair</c> printed when it could not finish a pairing. The phone answers a
    /// pairing port only while its dialog is open, and a phone that no longer recognises this
    /// computer's key answers nothing a pairing client can read, so the two cases need different
    /// actions: a newer adb on one side, forgetting the computer on the other. Returns null for
    /// output that carries no known cause.
    /// </summary>
    public static AndroidConnectFailure? ReadPairingFailure(string? adbOutput)
    {
        if (string.IsNullOrWhiteSpace(adbOutput))
        {
            return null;
        }

        // The pairing handshake reached the phone but the phone did not speak the pairing protocol
        // back. This is what an adb older than the phone's pairing protocol produces, and what a
        // phone that revoked this computer's key produces.
        if (adbOutput.Contains("protocol fault", StringComparison.OrdinalIgnoreCase)
            || adbOutput.Contains("couldn't read status message", StringComparison.OrdinalIgnoreCase))
        {
            return new AndroidConnectFailure(
                "The pairing handshake never finished: either the connection to the pairing port never opened, "
                + "or the phone did not answer the pairing protocol.",
                "Update Android platform-tools to 37.0.0 or newer (the phone speaks a newer pairing " +
                "protocol than adb 36.x does), then pair again. If it still fails, the phone no longer " +
                "recognises this computer: on the phone open Developer options > Wireless debugging, " +
                "tap this computer under Paired devices, choose Forget, and pair again. adb prints this " +
                "same line when its connection to the pairing port never opened, so on macOS 15 and newer " +
                "check System Settings > Privacy & Security > Local Network first: while this app is not " +
                "allowed there, no pairing or debugging connection leaves this computer.");
        }

        // The phone accepted the TLS handshake and then refused the certificate this computer offered,
        // which is the phone saying it forgot this computer's key.
        if (adbOutput.Contains("certificate", StringComparison.OrdinalIgnoreCase)
            || adbOutput.Contains("SSLV3", StringComparison.OrdinalIgnoreCase))
        {
            return new AndroidConnectFailure(
                "The phone refused this computer's debugging key.",
                "On the phone, open Developer options > Wireless debugging, tap this computer under " +
                "Paired devices and choose Forget (or press Revoke adb debugging authorizations), then " +
                "pair again. Keeping the pairing dialog open on the phone while pairing matters: its " +
                "port closes when the dialog closes.");
        }

        return ReadConnectFailure(adbOutput);
    }

    private static bool LooksLikeIpv4(string value) =>
        value.Length >= 7
        && value.Count(character => character == '.') == 3
        && value.Split('.').All(part => byte.TryParse(part, out _));

    /// <summary>
    /// Reads what <c>adb connect</c> printed when it could not connect. adb reports the socket error
    /// on one line, <c>failed to connect to '192.168.50.237:41023': No route to host</c>, and the
    /// socket error is what separates "the phone is not on this network" from "the wrong port was
    /// used", which need different fixes. Returns null for output that carries no known socket error,
    /// so a success line or an unknown message never gets a wrong explanation.
    /// </summary>
    public static AndroidConnectFailure? ReadConnectFailure(string? adbOutput)
    {
        if (string.IsNullOrWhiteSpace(adbOutput))
        {
            return null;
        }

        if (adbOutput.Contains("no route to host", StringComparison.OrdinalIgnoreCase))
        {
            return new AndroidConnectFailure(
                "This computer has no route to the device, so the connection never left this computer.",
                "Check that the phone is on the same Wi-Fi network as this computer, that Wi-Fi and mobile "
                +                "data are not fighting each other, and that the address is the phone's current Wi-Fi "
                + "address. A phone that is asleep or whose Wireless debugging screen was closed can stop "
                + "answering while it still advertises its address, so open Developer options > Wireless "
                + "debugging on the phone, keep its screen on, and connect again. A phone hotspot is the "
                + "quickest way to rule out a network that blocks traffic between devices. On macOS 15 and "
                + "newer every device on the network fails this way while macOS is denying this app local "
                + "network access, which is silent and leaves the app out of the settings list on purpose: "
                + "if ping answers in Terminal but this app cannot reach any device, turn .NET App "
                + "Publisher on under System Settings > Privacy & Security > Local Network and start the "
                + "app again.")
            {
                IsUnreachable = true
            };
        }

        if (adbOutput.Contains("timed out", StringComparison.OrdinalIgnoreCase)
            || adbOutput.Contains("timeout", StringComparison.OrdinalIgnoreCase))
        {
            return new AndroidConnectFailure(
                "The device did not answer on that port.",
                "Keep the phone awake with the Wireless debugging screen open, then scan again: the "
                + "debugging port changes every time wireless debugging restarts, and the phone stops "
                + "answering on an old port.")
            {
                IsUnreachable = true
            };
        }

        if (adbOutput.Contains("connection refused", StringComparison.OrdinalIgnoreCase))
        {
            return new AndroidConnectFailure(
                "Nothing is accepting connections on that port.",
                "Open Developer options > Wireless debugging on the phone and connect to the port shown "
                + "there. A port that was open a minute ago has usually closed, and the pairing port is "
                + "a different one from the debugging port.");
        }

        if (adbOutput.Contains("unreachable", StringComparison.OrdinalIgnoreCase))
        {
            return new AndroidConnectFailure(
                "The device is unreachable from this network.",
                "Put both machines on the same network, or connect the phone to this computer's hotspot, "
                + "then connect again.")
            {
                IsUnreachable = true
            };
        }

        // The phone answered and finished the TLS handshake, then rejected this computer because its
        // public key is not in the phone's keystore. That is a missing pairing, not a network fault.
        if (adbOutput.Contains("failed to authenticate", StringComparison.OrdinalIgnoreCase)
            || adbOutput.Contains("not authenticated", StringComparison.OrdinalIgnoreCase))
        {
            return new AndroidConnectFailure(
                "The phone is reachable, but it does not trust this computer's debugging key.",
                "Pair again: press Connect with QR code, or type the code from Pair device with pairing "
                + "code. If the phone still refuses, open Developer options > Wireless debugging on the "
                + "phone, tap this computer under Paired devices, choose Forget, then pair again.");
        }

        if (adbOutput.Contains("unauthorized", StringComparison.OrdinalIgnoreCase))
        {
            return new AndroidConnectFailure(
                "The device refused the debugging key this computer offered.",
                "On the phone, open Developer options > Wireless debugging, tap the paired computer, and "
                + "choose forget it, then pair again.");
        }

        return null;
    }
}
