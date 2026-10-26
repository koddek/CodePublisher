namespace CodePublisher.Features.Deployment.Android;

/// <summary>
/// One Wi-Fi target that adb lists but that cannot accept work yet (offline, unauthorized,
/// connecting), with the sentence the panel shows for it.
/// </summary>
public sealed record AndroidUnusableWirelessTarget(AndroidDeviceInfo Device, string Host, string Address, string Hint);

/// <summary>
/// What the wireless panel shows for one live adb target list. <see cref="Connected"/> holds only the
/// targets adb reports as usable, because <c>adb connect</c> exits 0 and prints a banner whether or not
/// a session exists: an endpoint adb lists as <c>offline</c> is a target the phone is not connected to,
/// so it must never be presented as connected. Pure: no adb, no clock.
/// </summary>
public sealed record AndroidWirelessPanelState(
    IReadOnlyList<AndroidDeviceInfo> Connected,
    IReadOnlyList<AndroidUnusableWirelessTarget> NotUsable,
    string StatusLine)
{
    /// <summary>True when at least one Wi-Fi target accepts adb commands right now.</summary>
    public bool HasConnected => Connected.Count > 0;

    /// <summary>True when adb lists a Wi-Fi target that is not usable yet.</summary>
    public bool HasNotUsable => NotUsable.Count > 0;
}

/// <summary>
/// Splits the live adb target list into the two halves the wireless panel shows: devices that are
/// connected over Wi-Fi (adb reports them as `device`, so they accept install, shell, and file work),
/// and Wi-Fi targets adb lists that are not usable, each with the reason and the next step.
///
/// This exists because a failed or half-open `adb connect` leaves an `offline` row behind. Reporting
/// that row as connected is what makes the app claim a connection the phone does not have, and it also
/// hides the Connect action, because the host looks already connected. Pure: no adb, no clock.
/// </summary>
public static class AndroidWirelessPanel
{
    public static AndroidWirelessPanelState Describe(IEnumerable<AndroidDeviceInfo>? devices)
    {
        var wireless = (devices ?? [])
            .Where(device => device.IsWireless && !string.IsNullOrWhiteSpace(device.Serial))
            .OrderBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(device => device.Serial, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var connected = wireless.Where(device => device.IsReady).ToArray();
        var notUsable = wireless
            .Where(device => !device.IsReady)
            .Select(device => new AndroidUnusableWirelessTarget(
                device,
                HostOf(device.Serial),
                device.Serial.Trim(),
                Hint(device)))
            .ToArray();

        return new AndroidWirelessPanelState(connected, notUsable, BuildStatusLine(connected, notUsable));
    }

    /// <summary>The host part of a Wi-Fi target's address, so one phone is identified the same way everywhere.</summary>
    public static string HostOf(string? address) =>
        AndroidWirelessDebug.TryParseEndpoint(address, out var host, out _) && !string.IsNullOrWhiteSpace(host)
            ? host
            : address?.Trim() ?? string.Empty;

    /// <summary>
    /// What is wrong with one listed Wi-Fi target and what to do about it, in one sentence. The state is
    /// adb's own word, and the action is the one the panel offers for that row (Connect).
    /// </summary>
    public static string Hint(AndroidDeviceInfo device) => device.State switch
    {
        AndroidDeviceState.Unauthorized =>
            "adb lists it as `unauthorized` — the phone has not approved this computer: unlock it and accept the " +
            "debugging prompt, or pair again.",
        AndroidDeviceState.Offline =>
            "adb lists it as `offline` — the phone is not accepting a session: press Connect with the phone awake " +
            "and Wireless debugging open.",
        "connecting" =>
            "adb lists it as `connecting` — the session has not settled: press Connect again.",
        "authorizing" =>
            "adb lists it as `authorizing` — look at the phone's screen for the debugging prompt.",
        "no permissions" =>
            "adb lists it as `no permissions` — this computer is not allowed to use the device.",
        _ => $"adb lists it as `{device.State}` — press Connect to try again."
    };

    /// <summary>
    /// The one-line state at the top of the wireless card. A Wi-Fi target that adb lists but cannot use
    /// is named as exactly that, so the panel never reads as connected while the phone is not.
    /// </summary>
    private static string BuildStatusLine(
        IReadOnlyList<AndroidDeviceInfo> connected,
        IReadOnlyList<AndroidUnusableWirelessTarget> notUsable) => connected.Count switch
    {
        1 => "1 device connected over Wi-Fi. It can be disconnected from the list below.",
        > 1 => $"{connected.Count} devices connected over Wi-Fi. Each can be disconnected from the list below.",
        _ when notUsable.Count == 1 =>
            $"No device is connected over Wi-Fi. {notUsable[0].Address} is listed as `{notUsable[0].Device.State}` " +
            "but cannot accept work yet — press Connect next to it to try again.",
        _ when notUsable.Count > 1 =>
            $"No device is connected over Wi-Fi. {notUsable.Count} Wi-Fi targets are listed but none is usable yet: " +
            string.Join(", ", notUsable.Select(target => $"{target.Address} (`{target.Device.State}`)")),
        _ => "No devices connected over Wi-Fi."
    };
}
