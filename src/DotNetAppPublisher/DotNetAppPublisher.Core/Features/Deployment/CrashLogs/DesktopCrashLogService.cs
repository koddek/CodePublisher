namespace DotNetAppPublisher.Features.Deployment.CrashLogs;

/// <summary>
/// Finds the crash reports of a desktop app on this computer.
///
/// A desktop app has no shared device folder, so the reports live where the operating system
/// puts them: the DiagnosticReports folder on macOS, the local dumps folder and the Windows Error
/// Reporting archive on Windows. The app's own log files are in the app's own data folder, which
/// is a convention the app chooses, so the scan names that place instead of guessing it.
/// </summary>
public sealed class DesktopCrashLogService
{
    private const int MaxReports = 10;

    /// <summary>Scans this computer for the crash reports of the app.</summary>
    public Task<CrashLogScan> ScanAsync(string? appNameHint, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var artifacts = new List<CrashLogArtifact>();
        var notes = new List<string>();

        if (OperatingSystem.IsMacOS())
        {
            AppendMacOsReports(appNameHint, artifacts, notes);
        }
        else if (OperatingSystem.IsWindows())
        {
            AppendWindowsReports(appNameHint, artifacts, notes);
        }
        else
        {
            notes.Add("Crash reports of a desktop app are only read on macOS and Windows.");
            return Task.FromResult(new CrashLogScan(artifacts, notes));
        }

        notes.Add(
            "An app's own log files are in its own data folder, and the name of that folder is the app's own " +
            "choice. The reports found here are the ones the operating system wrote.");

        if (artifacts.Count == 0)
        {
            notes.Add("No crash report for the app was found on this computer.");
        }

        return Task.FromResult(new CrashLogScan(artifacts, notes));
    }

    private static void AppendMacOsReports(
        string? appNameHint,
        List<CrashLogArtifact> artifacts,
        List<string> notes)
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library",
            "Logs",
            "DiagnosticReports");

        if (!Directory.Exists(folder))
        {
            notes.Add($"The folder {folder} does not exist, so no crash report was written here.");
            return;
        }

        var files = Directory.EnumerateFiles(folder, "*.ips")
            .Concat(Directory.EnumerateFiles(folder, "*.crash"))
            .Where(file => MatchesHint(Path.GetFileNameWithoutExtension(file), appNameHint))
            .OrderByDescending(file => File.GetLastWriteTimeUtc(file))
            .Take(MaxReports)
            .ToArray();

        foreach (var file in files)
        {
            var info = new FileInfo(file);
            artifacts.Add(new CrashLogArtifact(
                info.Name,
                CrashLogSource.SystemCrashLog,
                file,
                "The crash report macOS wrote",
                info.Length,
                info.LastWriteTimeUtc,
                ReadTextSafely(info),
                true));
        }

        if (files.Length == 0)
        {
            notes.Add(
                $"The folder {folder} holds no crash report for the app. macOS writes one there for every app that crashed.");
        }
    }

    private static void AppendWindowsReports(
        string? appNameHint,
        List<CrashLogArtifact> artifacts,
        List<string> notes)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dumpFolder = Path.Combine(localAppData, "CrashDumps");
        if (Directory.Exists(dumpFolder))
        {
            foreach (var file in Directory.EnumerateFiles(dumpFolder, "*.dmp")
                         .Where(file => MatchesHint(Path.GetFileName(file), appNameHint))
                         .OrderByDescending(file => File.GetLastWriteTimeUtc(file))
                         .Take(MaxReports))
            {
                var info = new FileInfo(file);
                artifacts.Add(new CrashLogArtifact(
                    info.Name,
                    CrashLogSource.LocalDump,
                    file,
                    "The memory dump Windows wrote when the app crashed",
                    info.Length,
                    info.LastWriteTimeUtc,
                    string.Empty,
                    false));
            }
        }

        var werFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Microsoft",
            "Windows",
            "WER",
            "ReportArchive");

        if (Directory.Exists(werFolder))
        {
            foreach (var report in Directory.EnumerateFiles(werFolder, "Report.wer", SearchOption.AllDirectories)
                         .Where(file => MatchesHint(Path.GetFileName(Path.GetDirectoryName(file) ?? string.Empty), appNameHint))
                         .OrderByDescending(file => File.GetLastWriteTimeUtc(file))
                         .Take(MaxReports))
            {
                var info = new FileInfo(report);
                artifacts.Add(new CrashLogArtifact(
                    Path.GetFileName(Path.GetDirectoryName(report) ?? report),
                    CrashLogSource.SystemCrashLog,
                    report,
                    "The crash report Windows Error Reporting wrote",
                    info.Length,
                    info.LastWriteTimeUtc,
                    ReadTextSafely(info),
                    true));
            }
        }

        if (artifacts.Count == 0)
        {
            notes.Add(
                "Windows wrote no crash report for the app. The local dumps folder is empty unless local dumps are turned on, " +
                "and the Windows Error Reporting archive only holds the reports of the apps that crashed on this computer.");
        }
    }

    /// <summary>
    /// True when the name belongs to the app. With no name to go by, everything matches, because
    /// showing a report that turns out to be another app is better than hiding a real one.
    /// </summary>
    private static bool MatchesHint(string? name, string? appNameHint)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(appNameHint))
        {
            return true;
        }

        return name.Contains(appNameHint, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadTextSafely(FileInfo info)
    {
        try
        {
            return File.ReadAllText(info.FullName);
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }
}
