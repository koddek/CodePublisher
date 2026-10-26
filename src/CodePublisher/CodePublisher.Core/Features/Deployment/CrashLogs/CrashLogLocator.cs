namespace CodePublisher.Features.Deployment.CrashLogs;

/// <summary>
/// Where an app can keep its own crash reports, and what a crash report is called. Pure, because
/// the same rules decide what is looked for on a device, in a simulator container, and on this
/// computer.
///
/// The important part is what these rules are not: no platform puts an app's crash reports in a
/// fixed place. The folders below are the conventions that .NET MAUI, Xamarin, Unity, Flutter,
/// React Native, and the common crash reporters (Sentry, Crashlytics, Bugsnag) use, so the scan
/// looks in all of them instead of pretending there is one.
/// </summary>
public static class CrashLogLocator
{
    /// <summary>
    /// Folders inside an Android app's private data directory that can hold the app's own crash
    /// reports. The paths are relative to the app's data directory, which is where `run-as` starts.
    /// </summary>
    public static IReadOnlyList<string> AndroidAppDataFolders { get; } =
    [
        "files/logs",
        "files/crash",
        "files/crashes",
        "files/dumps",
        "files",
        "cache/logs",
        "cache"
    ];

    /// <summary>Folders inside an iOS app container that can hold the app's own crash reports.</summary>
    public static IReadOnlyList<string> IosAppDataFolders { get; } =
    [
        "Library/Logs",
        "Library/Caches",
        "Library/Application Support",
        "tmp"
    ];

    /// <summary>
    /// True when the file name looks like a crash report rather than ordinary app data. The names
    /// that mark one are the ones the common crash reporters and log writers use: a name that
    /// holds crash, stacktrace, or exception, the extensions that only crash reports use, and a
    /// log file whose name says what went wrong.
    /// </summary>
    public static bool LooksLikeCrashFile(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var fileName = name.Trim();
        var extension = Path.GetExtension(fileName);
        var withoutExtension = Path.GetFileNameWithoutExtension(fileName);

        if (extension.Equals(".dmp", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".hprof", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".ips", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".crash", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (extension.Equals(".log", StringComparison.OrdinalIgnoreCase)
            && (withoutExtension.Contains("crash", StringComparison.OrdinalIgnoreCase)
                || withoutExtension.Contains("error", StringComparison.OrdinalIgnoreCase)
                || withoutExtension.Contains("exception", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return fileName.Contains("crash", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("stacktrace", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("exception", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True when the file is a binary dump that cannot be shown as text. The text is still worth
    /// copying, because the path and the crash report around it explain what the dump belongs to.
    /// </summary>
    public static bool IsBinaryDump(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var extension = Path.GetExtension(name);
        return extension.Equals(".dmp", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".hprof", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The name of the app's own data folder on a platform, in the words the platform uses.
    /// .NET MAUI calls it AppData, and that name is what a user recognizes from their own app.
    /// </summary>
    public static string DescribeAppDataFolder(string platform) => platform switch
    {
        "Android" => "the app's private data folder (AppData, /data/data/<package>)",
        "iOS" => "the app's data container (AppData, Library/Logs inside it)",
        "macOS" => "the app's container (AppData, ~/Library/Containers)",
        "Windows" => "the app's local folder (AppData, %LOCALAPPDATA%)",
        _ => "the app's data folder"
    };
}
