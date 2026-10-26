namespace CodePublisher.Features.Deployment.Android;

/// <summary>
/// What <c>adb server-status</c> reported about the adb server on this computer. Google's own
/// troubleshooting for wireless debugging reads three fields off this output: the adb version, whether
/// mDNS is enabled, and which mDNS backend the server runs. Pure: no adb, no clock.
/// </summary>
public sealed record AndroidAdbServerStatus(
    string? Version,
    string? MdnsBackend,
    bool MdnsEnabled,
    bool MdnsBackendForced)
{
    /// <summary>First adb build with the current mDNS backend, which is also what Android 17 needs.</summary>
    public const int MinimumVersion = 37;

    /// <summary>The backend Google documents as current: an older library misses newer devices.</summary>
    public const string ModernMdnsBackend = "LIBADBMDNS";

    /// <summary>True when <c>adb server-status</c> answered with the fields this class reads.</summary>
    public bool IsKnown => !string.IsNullOrWhiteSpace(Version);

    /// <summary>First number of the version, so <c>37.0.0</c> compares as 37.</summary>
    public int MajorVersion => IsKnown && int.TryParse(Version!.Split('.')[0], out var major) ? major : 0;

    /// <summary>True when the server runs the mDNS backend Google documents as current.</summary>
    public bool HasModernMdnsBackend =>
        string.Equals(MdnsBackend, ModernMdnsBackend, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when every documented check passes, so nothing needs repairing.</summary>
    public bool IsHealthy => IsKnown && MajorVersion >= MinimumVersion && MdnsEnabled && HasModernMdnsBackend;

    /// <summary>The failed checks, in the order Google lists them.</summary>
    public IReadOnlyList<string> FailedChecks
    {
        get
        {
            var failed = new List<string>();
            if (!IsKnown)
            {
                failed.Add("adb server-status did not answer, so the adb server could not be checked.");
                return failed;
            }

            if (MajorVersion < MinimumVersion)
            {
                failed.Add($"adb is {Version}, and wireless debugging needs {MinimumVersion}.0.0 or newer.");
            }

            if (!MdnsEnabled)
            {
                failed.Add("the adb server has its mDNS discovery switched off.");
            }

            if (!HasModernMdnsBackend)
            {
                failed.Add($"the adb server uses the {MdnsBackend ?? "unknown"} mDNS backend, not {ModernMdnsBackend}.");
            }

            return failed;
        }
    }

    /// <summary>What the user has to change, as the shell commands Google documents for it.</summary>
    public string FixCommands
    {
        get
        {
            var fixes = new List<string>();
            if (MajorVersion < MinimumVersion)
            {
                fixes.Add("Update Android platform-tools to 37.0.0 or newer.");
            }

            if (!MdnsEnabled)
            {
                fixes.Add("Set the ADB_MDNS environment variable to 1.");
            }

            if (!HasModernMdnsBackend)
            {
                fixes.Add("Set the ADB_MDNS_OPENSCREEN environment variable to 0.");
            }

            if (fixes.Count > 0)
            {
                fixes.Add("Restart the adb server: adb kill-server, then adb start-server.");
            }

            return fixes.Count == 0 ? "Nothing to fix." : string.Join(' ', fixes);
        }
    }

    /// <summary>Parses <c>adb server-status</c> output. Unknown text leaves the fields empty.</summary>
    public static AndroidAdbServerStatus Parse(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return new AndroidAdbServerStatus(null, null, MdnsEnabled: true, MdnsBackendForced: false);
        }

        string? version = null;
        string? backend = null;
        var mdnsEnabled = true;
        var backendForced = false;

        foreach (var rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = rawLine.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var key = rawLine[..separator].Trim();
            var value = rawLine[(separator + 1)..].Trim().Trim('"');
            switch (key)
            {
                case "version":
                    version = value;
                    break;
                case "mdns_backend":
                    backend = value;
                    break;
                case "mdns_enabled":
                    mdnsEnabled = !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
                    break;
                case "mdns_backend_forced":
                    backendForced = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                    break;
            }
        }

        return new AndroidAdbServerStatus(version, backend, mdnsEnabled, backendForced);
    }
}

/// <summary>
/// What one attempt to put the adb server into a state that can discover devices achieved.
/// </summary>
public sealed record AndroidWirelessRepair(bool Restarted, AndroidAdbServerStatus Status, int AdvertisedServices, string Message)
{
    /// <summary>True when the server was restarted and now sees at least one advertised service.</summary>
    public bool FixedDiscovery => AdvertisedServices > 0;
}

/// <summary>
/// One adb installation, as <c>adb version</c> reports it. A machine can hold more than one
/// platform-tools copy (the SDK one and a package manager one), and they do not agree, so the newest
/// one is the one to use. Pure: the text is parsed, nothing is executed.
/// </summary>
public sealed record AndroidAdbVersion(int Major, int Minor, int Patch, string Reported) : IComparable<AndroidAdbVersion>
{
    /// <summary>First adb build with the mDNS backend Wi-Fi 2.0 devices need.</summary>
    public const int WirelessMinimumMajor = 37;

    /// <summary>True when this build is new enough for wireless debugging as documented.</summary>
    public bool SupportsWireless => Major >= WirelessMinimumMajor;

    /// <summary>
    /// Reads <c>adb version</c> output. The line is
    /// <c>Version 37.0.1-15733141</c>; the build suffix is dropped and an unreadable text leaves the
    /// version at 0 so it never wins a comparison.
    /// </summary>
    public static AndroidAdbVersion Parse(string? output)
    {
        var reported = string.Empty;
        if (!string.IsNullOrWhiteSpace(output))
        {
            foreach (var rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (rawLine.StartsWith("Version", StringComparison.OrdinalIgnoreCase))
                {
                    reported = rawLine.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .LastOrDefault() ?? string.Empty;
                    break;
                }
            }
        }

        var version = reported.Split('-')[0].Split('.');
        var major = version.Length > 0 && int.TryParse(version[0], out var m) ? m : 0;
        var minor = version.Length > 1 && int.TryParse(version[1], out var mi) ? mi : 0;
        var patch = version.Length > 2 && int.TryParse(version[2], out var pa) ? pa : 0;
        return new AndroidAdbVersion(major, minor, patch, reported);
    }

    public int CompareTo(AndroidAdbVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        return Major != other.Major
            ? Major.CompareTo(other.Major)
            : Minor != other.Minor
                ? Minor.CompareTo(other.Minor)
                : Patch.CompareTo(other.Patch);
    }

    /// <summary>Ordered comparison text, so logs and messages read the same everywhere.</summary>
    public string Display => Reported.Length > 0 ? Reported : "unknown version";
}

/// <summary>One adb this computer has, and whether the app is the one running it.</summary>
public sealed record AndroidAdbInstallation(string Path, AndroidAdbVersion Version, bool IsUsed);
