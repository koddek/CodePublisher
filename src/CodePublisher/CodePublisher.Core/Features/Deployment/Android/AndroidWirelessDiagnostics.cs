namespace CodePublisher.Features.Deployment.Android;

/// <summary>
/// A single check run while diagnosing why wireless debugging does not connect.
/// </summary>
public sealed record WirelessDiagnosticCheck(string Id, string Title, bool Passed, string? Detail, string? Fix);

/// <summary>
/// The result of diagnosing a wireless debugging connection attempt.
/// </summary>
public sealed record WirelessDiagnosticResult(string Address, bool IsConnected, bool IsPaired,
    IReadOnlyList<WirelessDiagnosticCheck> Checks, string Summary)
{
    public bool AllPassed => Checks.All(check => check.Passed);
}

/// <summary>
/// Pure diagnostics for wireless (Wi-Fi) debugging. Given an address and the current adb target
/// list, this class figures out why a connection does not work and reports what to do about it.
/// No adb, no network, no clock — just rules over observed state.
/// </summary>
public static class AndroidWirelessDiagnostics
{
    /// <summary>
    /// Diagnoses why connecting to <paramref name="address"/> over Wi-Fi debugging fails.
    /// </summary>
    /// <param name="address">The IP:PORT the user entered or selected.</param>
    /// <param name="devices">The current adb target list (from `adb devices -l`).</param>
    /// <param name="pairedHosts">Hosts this computer has paired with this session.</param>
    /// <param name="mdnsServices">Wireless debugging services advertised on the network (from `adb mdns services`).</param>
    /// <param name="androidVersion">The device's Android version, when known.</param>
    /// <param name="apiLevel">The device's API level, when known.</param>
    /// <param name="adbServer">What `adb server-status` reported, when it was read.</param>
    public static WirelessDiagnosticResult Diagnose(
        string address,
        IReadOnlyList<AndroidDeviceInfo> devices,
        IReadOnlyDictionary<string, string?> pairedHosts,
        IReadOnlyList<AndroidWirelessService> mdnsServices,
        string? androidVersion = null,
        int? apiLevel = null,
        AndroidAdbServerStatus? adbServer = null)
    {
        var checks = new List<WirelessDiagnosticCheck>();

        var endpointOk = AndroidWirelessDebug.TryNormalizeAddress(address, out var endpoint, out var addressError);
        checks.Add(new(
            "address-format",
            "Address format",
            endpointOk,
            addressError,
            endpointOk ? null : "Enter the address as IP:PORT, for example 192.168.1.42:37123."));

        string? host = null;
        int port = 0;
        if (endpointOk && AndroidWirelessDebug.TryParseEndpoint(endpoint, out var parsedHost, out var parsedPort))
        {
            host = parsedHost;
            port = parsedPort;
        }

        // adb lists a Wi-Fi endpoint as soon as a `adb connect` opened a transport, including the
        // `offline` row a failed or half-open connect leaves behind. Only a target adb reports as usable
        // is a session, so an `offline` row is diagnosed as what it is instead of as connected.
        var listedTarget = devices.FirstOrDefault(d =>
            d.IsWireless && AndroidWirelessDebug.TryParseEndpoint(d.Serial, out var listedHost, out _) &&
            host is not null && string.Equals(listedHost, host, StringComparison.OrdinalIgnoreCase));
        var isConnected = listedTarget is { IsReady: true };

        var isPaired = pairedHosts.Keys.Any(k =>
            host is not null && string.Equals(k, host, StringComparison.OrdinalIgnoreCase));

        // --- Port sanity: pairing port vs debugging port ---
        var portHint = string.Empty;
        if (endpointOk && port > 0)
        {
            var pairingServices = mdnsServices.Where(s => s.IsPairingService).ToList();
            var debuggingServices = mdnsServices.Where(s => !s.IsPairingService).ToList();

            var onPairingPort = pairingServices.Any(s => s.Address.EndsWith($":{port}", StringComparison.Ordinal));
            var onDebugPort = debuggingServices.Any(s => s.Address.EndsWith($":{port}", StringComparison.Ordinal));

            if (onPairingPort && !onDebugPort)
            {
                portHint = "You entered the pairing port. Wireless debugging uses a different port for the debugging connection — look for the debugging port shown on the phone or in the scan results.";
            }
            else if (onDebugPort && !onPairingPort)
            {
                portHint = "This looks like a debugging port. Pairing uses a different port that closes when the pairing dialog on the phone closes.";
            }
        }

        checks.Add(new(
            "port-sanity",
            "Port is the debugging port (not the pairing port)",
            endpointOk && string.IsNullOrEmpty(portHint),
            portHint.Length > 0 ? portHint : null,
            portHint.Length > 0 ? "Use the debugging port shown under Wireless debugging on the device." : null));

        // --- Pairing state ---
        checks.Add(new(
            "paired",
            "Paired with this computer",
            isPaired,
            isPaired ? null : "This computer has not paired with the device yet.",
            isPaired ? null : "Pair first (QR code or pairing code), then connect."));

        // --- Connected state ---
        checks.Add(new(
            "connected",
            "Connected in the adb target list",
            isConnected,
            isConnected
                ? null
                : listedTarget is null
                    ? "The device is not listed as a connected Wi-Fi target."
                    : $"adb lists {listedTarget.Serial} as `{listedTarget.State}`, which is not a usable session.",
            isConnected ? null : "Press Connect next to the device in the wireless panel, or use Add a device > QR code."));

        // --- Target reachable / ready ---
        var targetReady = listedTarget is { IsReady: true };
        checks.Add(new(
            "target-ready",
            "Target accepts adb commands",
            targetReady,
            targetReady
                ? null
                : $"Target state: {(listedTarget is null ? "not listed" : AndroidWirelessPanel.Hint(listedTarget))}",
            targetReady ? null : "Make sure the device is awake, on the same network, and the debugging prompt was approved."));

        // --- mDNS discovery ---
        var sameHostAdvertised = mdnsServices.Any(s =>
            host is not null && string.Equals(s.Address, address, StringComparison.OrdinalIgnoreCase));
        checks.Add(new(
            "mdns-visible",
            "Device advertises on this network",
            sameHostAdvertised || isConnected,
            sameHostAdvertised ? null : "The device is not advertising a wireless debugging service on this network.",
            sameHostAdvertised ? null : "Turn on Developer options > Wireless debugging on the phone and keep both machines on the same Wi-Fi network."));

        // --- Android version guidance ---
        var versionGuidance = VersionGuidance(androidVersion, apiLevel);
        if (versionGuidance is not null)
        {
            checks.Add(versionGuidance);
        }

        // --- This computer's adb server, the check Google documents first ---
        if (adbServer is { IsKnown: true })
        {
            checks.Add(new(
                "adb-server",
                "This computer's adb is set up to find devices",
                adbServer.IsHealthy,
                adbServer.IsHealthy ? null : string.Join(' ', adbServer.FailedChecks),
                adbServer.IsHealthy ? null : adbServer.FixCommands));
        }

        var summary = BuildSummary(checks, isConnected, isPaired, endpointOk);
        return new WirelessDiagnosticResult(address, isConnected, isPaired, checks, summary);
    }

    private static WirelessDiagnosticCheck? VersionGuidance(string? androidVersion, int? apiLevel)
    {
        if (apiLevel is null && string.IsNullOrWhiteSpace(androidVersion))
        {
            return null;
        }

        var level = apiLevel;
        if (level is null && int.TryParse(androidVersion, out var parsed))
        {
            level = parsed;
        }

        // Android 11 (API 30) introduced wireless debugging with pairing.
        // Android 10 and older need the legacy adb tcpip step over USB first.
        if (level is <= 29)
        {
            return new WirelessDiagnosticCheck(
                "android-version",
                "Android version supports wireless debugging",
                false,
                $"Android {androidVersion ?? "unknown"} (API {level}) — wireless debugging with pairing needs Android 11 (API 30) or newer.",
                "Use Wi-Fi debugging over USB instead: connect the device over USB, press Wi-Fi debugging over USB, then connect to the reported address.");
        }

        if (level is >= 30 and <= 33)
        {
            return new WirelessDiagnosticCheck(
                "android-version",
                "Android version supports wireless debugging",
                true,
                $"Android {androidVersion} (API {level}) — pairing code or QR code pairing supported.",
                null);
        }

        // Android 14+ (API 34) and later: mDNS-based discovery is the recommended path.
        return new WirelessDiagnosticCheck(
            "android-version",
            "Android version supports wireless debugging",
            true,
            $"Android {androidVersion} (API {level}) — mDNS discovery and QR pairing supported.",
            null);
    }

    private static string BuildSummary(IReadOnlyList<WirelessDiagnosticCheck> checks, bool isConnected, bool isPaired, bool addressOk)
    {
        var failed = checks.Where(c => !c.Passed).ToList();
        if (failed.Count == 0)
        {
            return "Everything checks out. The device should connect over Wi-Fi debugging.";
        }

        if (isConnected)
        {
            return "The device is connected. Some checks still report issues, which may explain intermittent connections.";
        }

        if (isPaired && !isConnected)
        {
            return "Paired but not connected. Pairing is a one-time trust step; connect separately to start a live debugging session.";
        }

        var first = failed[0];
        return $"Connection not ready: {first.Title}. {first.Detail ?? string.Empty}";
    }
}
