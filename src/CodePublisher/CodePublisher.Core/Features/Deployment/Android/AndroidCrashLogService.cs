using System.Text;
using CodePublisher.Features.Deployment.CrashLogs;
using CodePublisher.Features.Publishing.Process;

namespace CodePublisher.Features.Deployment.Android;

/// <summary>
/// Finds the crash reports of an app on an Android device or emulator.
///
/// Android does not keep an app's crash reports in one place, so the scan looks in the three
/// places a crash can be found at all:
///
/// 1. The app's own data folder (`/data/data/&lt;package&gt;`), where the app writes the crash
///    files it produces itself. Only a debuggable build can be read here, because adb is not
///    allowed into the data folder of a released app.
/// 2. Android's own crash store (the DropBox service), which holds the crash report the system
///    wrote for every app that crashed.
/// 3. The device log buffer, which still holds the stack trace of the last crash.
///
/// The service reads; it never writes to the device and never deletes anything.
/// </summary>
public sealed class AndroidCrashLogService
{
    /// <summary>DropBox tags that hold a crash or a hang.</summary>
    private static readonly string[] CrashStoreTags =
    [
        "system_app_crash",
        "data_app_crash",
        "system_app_anr",
        "data_app_anr"
    ];

    /// <summary>Files bigger than this are not read as text; a crash report is never this big.</summary>
    private const long MaxTextFileBytes = 2L * 1024 * 1024;

    /// <summary>How much of the log buffer is read. The crash is usually near the end of it.</summary>
    private const int LogcatLineLimit = 5000;

    private readonly string? _adbPath;

    public AndroidCrashLogService(string? adbPath)
    {
        _adbPath = adbPath;
    }

    /// <summary>Scans the device for the crash reports of the package.</summary>
    public async Task<CrashLogScan> ScanAsync(
        string serial,
        string packageId,
        CancellationToken cancellationToken)
    {
        var adbPath = RequireAdb();
        var target = RequireSerial(serial);
        var package = RequirePackage(packageId);

        var artifacts = new List<CrashLogArtifact>();
        var notes = new List<string>();

        if (!await IsInstalledAsync(adbPath, target, package, cancellationToken))
        {
            notes.Add($"{package} is not installed on {target}, so there are no crash reports from it.");
            return new CrashLogScan(artifacts, notes);
        }

        await AppendAppDataArtifactsAsync(adbPath, target, package, artifacts, notes, cancellationToken);
        await AppendCrashStoreArtifactsAsync(adbPath, target, package, artifacts, notes, cancellationToken);
        await AppendLogcatArtifactAsync(adbPath, target, package, artifacts, notes, cancellationToken);

        if (artifacts.Count == 0 && notes.Count == 0)
        {
            notes.Add($"No crash report for {package} was found on {target}.");
        }

        return new CrashLogScan(artifacts, notes);
    }

    /// <summary>
    /// The app's own data folder. A debuggable build can be read with `run-as`, which opens the
    /// app's data folder as the app itself. A released build refuses this, and the scan says so
    /// instead of pretending the folder is empty.
    /// </summary>
    private async Task AppendAppDataArtifactsAsync(
        string adbPath,
        string target,
        string package,
        List<CrashLogArtifact> artifacts,
        List<string> notes,
        CancellationToken cancellationToken)
    {
        var found = 0;
        foreach (var folder in CrashLogLocator.AndroidAppDataFolders)
        {
            var listing = await RunAdbAsync(
                adbPath,
                target,
                ["shell", "run-as", package, "ls", "-la", folder],
                cancellationToken);

            if (listing.Output.Contains("not debuggable", StringComparison.OrdinalIgnoreCase))
            {
                notes.Add(
                    $"The app's own data folder could not be opened: this build of {package} is not debuggable, " +
                    "so adb may not read /data/data/<package>. A debug build shows the crash files the app writes there.");
                return;
            }

            if (listing.Output.Contains("not an application", StringComparison.OrdinalIgnoreCase)
                || listing.Output.Contains("Unknown package", StringComparison.OrdinalIgnoreCase))
            {
                notes.Add(
                    $"The app's own data folder of {package} could not be opened: adb reports that this package has " +
                    "no app data folder it may read on this device.");
                return;
            }

            if (listing.ExitCode != 0)
            {
                // The folder simply does not exist, which is the normal case for most of them.
                continue;
            }

            var entries = AndroidDeviceFileListParser.Parse(listing.Output, folder);
            foreach (var entry in entries.Where(entry => entry.IsFile && CrashLogLocator.LooksLikeCrashFile(entry.Name)))
            {
                var remotePath = $"{folder}/{entry.Name}";
                var location = $"/data/data/{package}/{remotePath}";
                var isBinary = CrashLogLocator.IsBinaryDump(entry.Name);
                var isTooBig = entry.SizeBytes > MaxTextFileBytes;
                var content = isBinary || isTooBig
                    ? string.Empty
                    : await ReadAppFileAsync(adbPath, target, package, remotePath, cancellationToken);

                if (isBinary)
                {
                    content = "This file is a binary dump. It cannot be read as text; open it in the crash analysis " +
                        "tool that produced it.";
                }
                else if (isTooBig)
                {
                    content = $"This file is {AndroidDeviceFileEntry.FormatSize(entry.SizeBytes)}, which is too large " +
                        "to read as text. Copy it off the device to read it.";
                }

                artifacts.Add(new CrashLogArtifact(
                    entry.Name,
                    CrashLogSource.AppData,
                    location,
                    $"Written by the app into {CrashLogLocator.DescribeAppDataFolder("Android")}",
                    entry.SizeBytes,
                    entry.Modified,
                    content,
                    !isBinary));
                found++;
            }
        }

        if (found == 0)
        {
            notes.Add(
                $"The app's data folder of {package} holds no file that looks like a crash report. " +
                "An app only writes such a file when it has a crash reporter that writes one.");
        }
    }

    /// <summary>Android's own crash store. The system writes a report here for every crash.</summary>
    private async Task AppendCrashStoreArtifactsAsync(
        string adbPath,
        string target,
        string package,
        List<CrashLogArtifact> artifacts,
        List<string> notes,
        CancellationToken cancellationToken)
    {
        var found = 0;
        foreach (var tag in CrashStoreTags)
        {
            var dump = await RunAdbAsync(
                adbPath,
                target,
                ["shell", "dumpsys", "dropbox", "--print", tag],
                cancellationToken);

            if (dump.ExitCode != 0)
            {
                continue;
            }

            foreach (var entry in AndroidCrashLogParser.ParseCrashStore(dump.Output, package))
            {
                artifacts.Add(new CrashLogArtifact(
                    $"{entry.Tag} {entry.Timestamp}".Trim(),
                    CrashLogSource.AndroidCrashStore,
                    $"Android crash store ({entry.Tag}) on {target}",
                    $"The crash report Android itself wrote for {package}",
                    entry.Body.Length,
                    ParseTimestamp(entry.Timestamp),
                    entry.Body,
                    true));
                found++;
            }
        }

        if (found == 0)
        {
            notes.Add(
                $"Android's crash store holds no crash of {package}. That store keeps the reports of every app " +
                "that crashed, so an empty result means the app did not crash, or the report was already rotated out.");
        }
    }

    /// <summary>
    /// The device log buffer. It still holds the stack trace of the last crash, which is the one
    /// thing that is there even when the app writes no crash file of its own.
    /// </summary>
    private async Task AppendLogcatArtifactAsync(
        string adbPath,
        string target,
        string package,
        List<CrashLogArtifact> artifacts,
        List<string> notes,
        CancellationToken cancellationToken)
    {
        var logcat = await RunAdbAsync(
            adbPath,
            target,
            ["logcat", "-d", "-v", "threadtime", "-t", LogcatLineLimit.ToString()],
            cancellationToken);

        if (logcat.ExitCode != 0)
        {
            notes.Add("The device log could not be read.");
            return;
        }

        var lines = AndroidCrashLogParser.ExtractCrashLines(logcat.Output, package);
        if (lines.Count == 0)
        {
            notes.Add(
                "The device log holds no crash of the app. The log buffer is small and is overwritten, " +
                "so a crash from a while ago may be gone from it.");
            return;
        }

        var text = string.Join(Environment.NewLine, lines);
        artifacts.Add(new CrashLogArtifact(
            "Recent crash output (device log)",
            CrashLogSource.DeviceLog,
            $"device log buffer on {target}",
            "The stack trace the device wrote when the app crashed",
            text.Length,
            null,
            text,
            true));
    }

    private async Task<string> ReadAppFileAsync(
        string adbPath,
        string target,
        string package,
        string remotePath,
        CancellationToken cancellationToken)
    {
        var result = await RunAdbAsync(
            adbPath,
            target,
            ["shell", "run-as", package, "cat", AndroidStorageLocations.QuoteForRemoteShell(remotePath)],
            cancellationToken);

        return result.ExitCode == 0 ? result.Output : string.Empty;
    }

    private async Task<bool> IsInstalledAsync(
        string adbPath,
        string target,
        string package,
        CancellationToken cancellationToken)
    {
        var result = await RunAdbAsync(
            adbPath,
            target,
            ["shell", "pm", "path", package],
            cancellationToken);

        return result.ExitCode == 0
            && result.Output.Contains("package:", StringComparison.OrdinalIgnoreCase);
    }

    private static DateTimeOffset? ParseTimestamp(string timestamp) =>
        DateTimeOffset.TryParse(timestamp, out var parsed) ? parsed : null;

    private static async Task<(int ExitCode, string Output)> RunAdbAsync(
        string adbPath,
        string target,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        var exitCode = await ProcessRunner.RunAsync(
            [adbPath, "-s", target, .. arguments],
            text => output.Append(text),
            cancellationToken);
        return (exitCode, output.ToString());
    }

    private string RequireAdb() => _adbPath
        ?? throw new InvalidOperationException("`adb` was not found. Install Android platform-tools or add adb to PATH.");

    private static string RequireSerial(string serial) => string.IsNullOrWhiteSpace(serial)
        ? throw new InvalidOperationException("No device selected. Select a device first.")
        : serial.Trim();

    private static string RequirePackage(string packageId) => string.IsNullOrWhiteSpace(packageId)
        ? throw new InvalidOperationException("A package id is required to look for crash reports.")
        : packageId.Trim();
}
