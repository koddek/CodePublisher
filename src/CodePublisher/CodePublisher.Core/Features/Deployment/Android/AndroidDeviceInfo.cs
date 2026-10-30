namespace CodePublisher.Features.Deployment.Android;

/// <summary>Transport adb used to reach a target. Stable strings so view models never re-parse.</summary>
public static class AndroidDeviceConnection
{
    public const string Usb = "usb";
    public const string Wireless = "wireless";
    public const string Emulator = "emulator";
}

/// <summary>Connection state reported by `adb devices`. Only <see cref="Ready"/> targets accept work.</summary>
public static class AndroidDeviceState
{
    public const string Ready = "device";
    public const string Unauthorized = "unauthorized";
    public const string Offline = "offline";
    public const string Unknown = "unknown";
}

/// <summary>
/// One adb target: a physical phone or tablet on USB or Wi-Fi, or an emulator (running or offline).
/// </summary>
public sealed record AndroidDeviceInfo(
    string Serial,
    string Name,
    string Type,
    bool IsRunning,
    string State = AndroidDeviceState.Ready,
    string Connection = AndroidDeviceConnection.Usb,
    string Model = "",
    string AndroidVersion = "",
    string ApiLevel = "",
    string Abi = "")
{
    public bool IsEmulator => string.Equals(Type, "emulator", StringComparison.Ordinal);

    public bool IsWireless => string.Equals(Connection, AndroidDeviceConnection.Wireless, StringComparison.Ordinal);

    /// <summary>True when adb can run install, shell, and file operations against this target now.</summary>
    public bool IsReady =>
        IsRunning && string.Equals(State, AndroidDeviceState.Ready, StringComparison.Ordinal);

    public string ConnectionLabel => Connection switch
    {
        AndroidDeviceConnection.Wireless => "Wi-Fi",
        AndroidDeviceConnection.Emulator => "emulator",
        _ => "USB"
    };

    public string PlatformLabel => string.IsNullOrWhiteSpace(AndroidVersion)
        ? string.Empty
        : string.IsNullOrWhiteSpace(ApiLevel)
            ? $"Android {AndroidVersion}"
            : $"Android {AndroidVersion} (API {ApiLevel})";

    /// <summary>Plain-language explanation shown next to targets that cannot be used yet.</summary>
    public string StateDetail => State switch
    {
        AndroidDeviceState.Unauthorized => "unauthorized — approve the USB debugging prompt on the device",
        AndroidDeviceState.Offline when IsEmulator => "offline — launch to start",
        AndroidDeviceState.Offline => "offline — reconnect the cable, or connect again over Wi-Fi",
        "authorizing" => "authorizing — check the phone's screen for the USB debugging prompt",
        "no permissions" => "no permissions — check USB permissions or udev rules on this host",
        AndroidDeviceState.Unknown => "state unknown — run a refresh (adb is restarted on failure)",
        _ => string.Empty
    };

    /// <summary>
    /// Detail row shown under the device selector: Android version and API level, the CPU ABI that the
    /// Runtime Identifier has to match, the transport, and whether the target can accept work yet.
    /// Empty parts are dropped so an offline target still shows what is known about it.
    /// </summary>
    public string DetailSummary
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Model))
            {
                parts.Add(Model);
            }

            if (PlatformLabel.Length > 0)
            {
                parts.Add(PlatformLabel);
            }

            if (!string.IsNullOrWhiteSpace(Abi))
            {
                parts.Add(Abi);
            }

            parts.Add(ConnectionLabel);
            parts.Add(IsReady
                ? "ready"
                : StateDetail.Length > 0
                    ? StateDetail
                    : $"not ready ({State})");
            return string.Join("  ·  ", parts);
        }
    }

    /// <summary>Whether there is anything worth showing in the detail row.</summary>
    public bool HasDetail => DetailSummary.Length > 0;

    public string DisplayName
    {
        get
        {
            var identity = string.IsNullOrWhiteSpace(Serial)
                ? $"{Name} ({ConnectionLabel})"
                : $"{Name} ({Serial}, {ConnectionLabel})";
            if (!IsReady)
            {
                var detail = StateDetail.Length > 0 ? StateDetail : "not ready";
                return $"{identity} — {detail}";
            }

            return PlatformLabel.Length > 0 ? $"{identity} · {PlatformLabel}" : identity;
        }
    }

    /// <summary>
    /// Presentation order used by discovery and selection: ready physical devices first, then running
    /// emulators, then every target that cannot be used yet. Pure so tests pin the order.
    /// </summary>
    public static int Rank(AndroidDeviceInfo device) =>
        device.IsReady && !device.IsEmulator ? 0
        : device.IsReady ? 1
        : 2;
}
