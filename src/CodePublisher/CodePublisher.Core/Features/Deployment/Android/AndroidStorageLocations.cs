namespace CodePublisher.Features.Deployment.Android;

/// <summary>A public on-device folder offered as a one-click destination in the file browser.</summary>
public sealed record AndroidStorageLocation(string Label, string RemotePath);

/// <summary>
/// Public storage roots and remote path rules for the device file browser. Every path is expressed
/// through /sdcard, which the Android shell aliases to /storage/emulated/&lt;userId&gt; for the
/// foreground user on Android 4.4 and newer.
/// </summary>
public static class AndroidStorageLocations
{
    public const string InternalStorage = "/sdcard";
    public const string Downloads = "/sdcard/Download";

    public static IReadOnlyList<AndroidStorageLocation> QuickLocations { get; } =
    [
        new("Internal storage", InternalStorage),
        new("Downloads", Downloads),
        new("Pictures", "/sdcard/Pictures"),
        new("Screenshots", "/sdcard/Pictures/Screenshots"),
        new("Camera (DCIM)", "/sdcard/DCIM"),
        new("Documents", "/sdcard/Documents"),
        new("Movies", "/sdcard/Movies"),
        new("Music", "/sdcard/Music"),
        new("Podcasts", "/sdcard/Podcasts"),
        new("Ringtones", "/sdcard/Ringtones"),
        new("Alarms", "/sdcard/Alarms")
    ];

    public static string NormalizeRemotePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return InternalStorage;
        }

        var normalized = path.Trim().Replace('\\', '/');
        if (!normalized.StartsWith('/'))
        {
            normalized = "/" + normalized;
        }

        while (normalized.Length > 1 && normalized.EndsWith('/'))
        {
            normalized = normalized[..^1];
        }

        return normalized.Length == 0 ? InternalStorage : normalized;
    }

    public static string CombineRemotePath(string directory, string name) =>
        $"{NormalizeRemotePath(directory)}/{name}";

    /// <summary>Walks up one level, stopping at the internal storage root.</summary>
    public static string GetParentRemotePath(string? path)
    {
        var normalized = NormalizeRemotePath(path);
        if (string.Equals(normalized, InternalStorage, StringComparison.Ordinal))
        {
            return InternalStorage;
        }

        var separator = normalized.LastIndexOf('/');
        return separator <= 0 ? InternalStorage : normalized[..separator];
    }

    /// <summary>True when the path sits inside shared storage, where adb may create and delete files.</summary>
    public static bool IsWithinInternalStorage(string? path)
    {
        var normalized = NormalizeRemotePath(path);
        return string.Equals(normalized, InternalStorage, StringComparison.Ordinal)
            || normalized.StartsWith(InternalStorage + "/", StringComparison.Ordinal);
    }

    /// <summary>
    /// Wraps a path in single quotes for `adb shell`, escaping embedded apostrophes so folder and file
    /// names with spaces or quotes survive the remote shell.
    /// </summary>
    public static string QuoteForRemoteShell(string path) => $"'{path.Replace("'", "'\\''")}'";

    /// <summary>Destination form used by `adb push` when the target is a folder.</summary>
    public static string AsRemoteDirectory(string? path) => NormalizeRemotePath(path) + "/";
}
