namespace CodePublisher.Features.Deployment.Android;

/// <summary>What changed between two views of the adb target list.</summary>
public sealed record AndroidDeviceChange(
    IReadOnlyList<AndroidDeviceInfo> Added,
    IReadOnlyList<AndroidDeviceInfo> BecameReady,
    IReadOnlyList<AndroidDeviceInfo> Removed)
{
    public static AndroidDeviceChange None { get; } = new([], [], []);

    public bool HasChanges => Added.Count > 0 || BecameReady.Count > 0 || Removed.Count > 0;
}

/// <summary>
/// Keeps the device list honest while the app is open: every decision about "did anything change" and
/// "which target just appeared" lives here so the polling loop in the view model stays thin and the
/// rules are unit-tested. Pure: the caller supplies `adb` output and the list it is showing.
/// </summary>
public static class AndroidDeviceWatch
{
    /// <summary>
    /// Identity of the targets adb currently reports, cheap enough to compare on every poll. Serial plus
    /// the raw state, because that is exactly what changes when a device is plugged in, boots, is
    /// authorized, or drops offline. AVD rows that are not running have no serial and are left out.
    /// </summary>
    public static string Fingerprint(IReadOnlyList<AndroidDeviceListEntry> entries) =>
        Fingerprint(entries.Select(entry => (entry.Serial, entry.State)));

    /// <summary>Same identity, taken from the list the UI is showing.</summary>
    public static string Fingerprint(IEnumerable<AndroidDeviceInfo> devices) =>
        Fingerprint(devices.Select(device => (device.Serial, device.State)));

    /// <summary>True when the cheap probe disagrees with what the UI currently shows.</summary>
    public static bool NeedsRefresh(string shownFingerprint, IReadOnlyList<AndroidDeviceListEntry> probe) =>
        !string.Equals(shownFingerprint, Fingerprint(probe), StringComparison.OrdinalIgnoreCase);

    /// <summary>Compares two discovered lists by serial; not-running AVD entries are ignored.</summary>
    public static AndroidDeviceChange Compare(
        IReadOnlyList<AndroidDeviceInfo> before,
        IReadOnlyList<AndroidDeviceInfo> after)
    {
        var previous = Index(before);
        var current = Index(after);

        var added = current.Values
            .Where(device => !previous.ContainsKey(device.Serial))
            .OrderBy(AndroidDeviceInfo.Rank)
            .ThenBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var becameReady = current.Values
            .Where(device => previous.TryGetValue(device.Serial, out var previousDevice)
                             && !previousDevice.IsReady
                             && device.IsReady)
            .OrderBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var removed = previous.Values
            .Where(device => !current.ContainsKey(device.Serial))
            .OrderBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return added.Length == 0 && becameReady.Length == 0 && removed.Length == 0
            ? AndroidDeviceChange.None
            : new AndroidDeviceChange(added, becameReady, removed);
    }

    /// <summary>
    /// The target that a launch or a wireless connect just produced: usable, and not present in the list
    /// captured before the action. The serial the cheap probe saw come online settles it first, then the
    /// emulator's AVD name, so several fast starts cannot pick the wrong one.
    /// </summary>
    public static AndroidDeviceInfo? ChooseLaunchedTarget(
        IReadOnlyList<AndroidDeviceInfo> devices,
        IReadOnlyCollection<string> knownSerials,
        string? expectedAvdName = null,
        string? preferredSerial = null)
    {
        var candidates = devices
            .Where(device => !string.IsNullOrWhiteSpace(device.Serial)
                             && device.IsReady
                             && !knownSerials.Contains(device.Serial, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        if (candidates.Length == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(preferredSerial))
        {
            var preferred = candidates.FirstOrDefault(device => string.Equals(
                device.Serial,
                preferredSerial.Trim(),
                StringComparison.OrdinalIgnoreCase));
            if (preferred is not null)
            {
                return preferred;
            }
        }

        if (!string.IsNullOrWhiteSpace(expectedAvdName))
        {
            var named = candidates.FirstOrDefault(device => string.Equals(
                device.Name,
                expectedAvdName.Trim(),
                StringComparison.OrdinalIgnoreCase));
            if (named is not null)
            {
                return named;
            }
        }

        return candidates.OrderBy(AndroidDeviceInfo.Rank).First();
    }

    /// <summary>One line for the status bar describing what the refresh found, or null when nothing did.</summary>
    public static string? DescribeChange(AndroidDeviceChange change)
    {
        if (!change.HasChanges)
        {
            return null;
        }

        var parts = new List<string>();
        foreach (var device in change.Added.Where(device => device.IsReady))
        {
            parts.Add(device.IsEmulator
                ? $"{Label(device)} is online"
                : $"{Label(device)} connected over {device.ConnectionLabel}");
        }

        foreach (var device in change.BecameReady)
        {
            parts.Add($"{Label(device)} is ready");
        }

        foreach (var device in change.Removed)
        {
            parts.Add($"{Label(device)} disconnected");
        }

        foreach (var device in change.Added.Where(device => !device.IsReady))
        {
            parts.Add($"{Label(device)} appeared — {device.StateDetail}");
        }

        return parts.Count == 0 ? null : string.Join("; ", parts) + ".";
    }

    private static string Label(AndroidDeviceInfo device) =>
        string.IsNullOrWhiteSpace(device.Name) ? device.Serial : $"{device.Name} ({device.Serial})";

    private static string Fingerprint(IEnumerable<(string Serial, string State)> targets) =>
        string.Join(
            '\n',
            targets
                .Where(target => !string.IsNullOrWhiteSpace(target.Serial))
                .Select(target => $"{target.Serial.Trim()}\t{target.State}")
                .OrderBy(text => text, StringComparer.OrdinalIgnoreCase));

    private static Dictionary<string, AndroidDeviceInfo> Index(IReadOnlyList<AndroidDeviceInfo> devices)
    {
        var index = new Dictionary<string, AndroidDeviceInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var device in devices.Where(device => !string.IsNullOrWhiteSpace(device.Serial)))
        {
            index[device.Serial] = device;
        }

        return index;
    }
}
