using System.Text;
using DotNetAppPublisher.Features.Deployment.CrashLogs;
using DotNetAppPublisher.Features.Publishing.Process;

namespace DotNetAppPublisher.Features.Deployment.Ios;

/// <summary>
/// Finds the crash reports of an app on iOS, on a real device and on a simulator.
///
/// iOS writes a crash report for every app that crashed, and the report is a readable text file
/// (a <c>.ips</c> file). On a real device the reports live in the system crash log domain, and the
/// app's own crash files live in its data container. On a simulator the reports land in the
/// Mac's DiagnosticReports folder, and the app's own files can be read straight from the
/// simulator's container on disk.
/// </summary>
public sealed class IosCrashLogService
{
    private const int MaxReports = 10;

    private readonly string? _xcrunPath;

    public IosCrashLogService(string? xcrunPath)
    {
        _xcrunPath = xcrunPath;
    }

    /// <summary>Scans a real iPhone or iPad for the crash reports of the bundle id.</summary>
    public async Task<CrashLogScan> ScanDeviceAsync(
        string udid,
        string bundleId,
        CancellationToken cancellationToken)
    {
        var xcrunPath = RequireXcrun();
        var target = RequireId(udid, "device");
        var bundle = RequireBundleId(bundleId);

        var artifacts = new List<CrashLogArtifact>();
        var notes = new List<string>();

        await AppendSystemCrashLogsAsync(xcrunPath, target, bundle, artifacts, notes, cancellationToken);
        await AppendAppContainerFilesAsync(xcrunPath, target, bundle, artifacts, notes, cancellationToken);

        if (artifacts.Count == 0)
        {
            notes.Add($"No crash report for {bundle} was found on {target}.");
        }

        return new CrashLogScan(artifacts, notes);
    }

    /// <summary>Scans a simulator for the crash reports of the bundle id.</summary>
    public async Task<CrashLogScan> ScanSimulatorAsync(
        string simulatorId,
        string bundleId,
        CancellationToken cancellationToken)
    {
        var xcrunPath = RequireXcrun();
        var target = RequireId(simulatorId, "simulator");
        var bundle = RequireBundleId(bundleId);

        var artifacts = new List<CrashLogArtifact>();
        var notes = new List<string>();

        var dataContainer = await GetAppContainerAsync(xcrunPath, target, bundle, "data", cancellationToken);
        if (dataContainer is not null)
        {
            AppendContainerFiles(dataContainer, artifacts, notes);
        }
        else
        {
            notes.Add($"The data container of {bundle} could not be read on {target}. The app may not be installed there.");
        }

        var appBundle = await GetAppContainerAsync(xcrunPath, target, bundle, "app", cancellationToken);
        if (appBundle is not null)
        {
            AppendDiagnosticReports(Path.GetFileNameWithoutExtension(appBundle), target, artifacts, notes);
        }

        if (artifacts.Count == 0)
        {
            notes.Add($"No crash report for {bundle} was found on {target}.");
        }

        return new CrashLogScan(artifacts, notes);
    }

    private async Task AppendSystemCrashLogsAsync(
        string xcrunPath,
        string target,
        string bundle,
        List<CrashLogArtifact> artifacts,
        List<string> notes,
        CancellationToken cancellationToken)
    {
        var entries = await ListDomainFilesAsync(
            xcrunPath,
            target,
            "systemCrashLogs",
            null,
            null,
            cancellationToken);

        if (entries is null)
        {
            notes.Add("The crash reports on the device could not be listed.");
            return;
        }

        if (entries.Count == 0)
        {
            notes.Add(
                "The device holds no crash report for the app. The system writes one there for every app that crashed.");
            return;
        }

        // The system crash log domain holds the reports of every app that crashed on the device.
        // A report belongs to this app when it names the bundle id, so the reports of the other
        // apps are read past instead of shown.
        var found = 0;
        var skipped = 0;
        foreach (var entry in entries.Take(MaxReports * 3))
        {
            if (found >= MaxReports)
            {
                break;
            }

            var content = await CopyDomainFileAsync(
                xcrunPath,
                target,
                "systemCrashLogs",
                null,
                entry.Name,
                cancellationToken);

            if (content is null)
            {
                notes.Add($"{entry.Name} is on the device but could not be copied from it.");
                continue;
            }

            if (!content.Contains(bundle, StringComparison.OrdinalIgnoreCase))
            {
                skipped++;
                continue;
            }

            artifacts.Add(new CrashLogArtifact(
                entry.Name,
                CrashLogSource.SystemCrashLog,
                $"system crash log on {target}",
                "The crash report iOS itself wrote",
                entry.SizeBytes,
                entry.Modified,
                content,
                true));
            found++;
        }

        if (found == 0)
        {
            notes.Add(skipped > 0
                ? $"The device holds {skipped} crash report(s) of other apps, and none of the app {bundle}."
                : "The device holds no crash report of the app.");
        }
    }

    private async Task AppendAppContainerFilesAsync(
        string xcrunPath,
        string target,
        string bundle,
        List<CrashLogArtifact> artifacts,
        List<string> notes,
        CancellationToken cancellationToken)
    {
        var found = 0;
        foreach (var folder in CrashLogLocator.IosAppDataFolders)
        {
            var entries = await ListDomainFilesAsync(
                xcrunPath,
                target,
                "appDataContainer",
                bundle,
                folder,
                cancellationToken);

            if (entries is null)
            {
                continue;
            }

            foreach (var entry in entries.Take(MaxReports))
            {
                if (!CrashLogLocator.LooksLikeCrashFile(entry.Name))
                {
                    continue;
                }

                var content = await CopyDomainFileAsync(
                    xcrunPath,
                    target,
                    "appDataContainer",
                    bundle,
                    entry.Path,
                    cancellationToken);

                if (content is null)
                {
                    notes.Add($"{entry.Name} is in the app's data container but could not be copied from the device.");
                }

                artifacts.Add(new CrashLogArtifact(
                    entry.Name,
                    CrashLogSource.AppData,
                    $"the app's data container on {target} ({folder})",
                    $"Written by the app into {CrashLogLocator.DescribeAppDataFolder("iOS")}",
                    entry.SizeBytes,
                    entry.Modified,
                    content ?? "This file could not be copied from the device.",
                    !CrashLogLocator.IsBinaryDump(entry.Name)));
                found++;
            }
        }

        if (found == 0)
        {
            notes.Add(
                "The app's data container holds no file that looks like a crash report. An app only writes such a " +
                "file when it has a crash reporter that writes one.");
        }
    }

    private void AppendContainerFiles(
        string container,
        List<CrashLogArtifact> artifacts,
        List<string> notes)
    {
        var found = 0;
        foreach (var folder in CrashLogLocator.IosAppDataFolders)
        {
            var directory = Path.Combine(container, folder.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var file in EnumerateCrashFiles(directory))
            {
                var name = Path.GetFileName(file);
                if (!CrashLogLocator.LooksLikeCrashFile(name))
                {
                    continue;
                }

                var info = new FileInfo(file);
                var isBinary = CrashLogLocator.IsBinaryDump(name);
                artifacts.Add(new CrashLogArtifact(
                    name,
                    CrashLogSource.AppData,
                    file,
                    $"Written by the app into {CrashLogLocator.DescribeAppDataFolder("iOS")}",
                    info.Length,
                    info.LastWriteTimeUtc,
                    isBinary
                        ? "This file is a binary dump. It cannot be read as text; open it in the crash analysis tool that produced it."
                        : ReadTextSafely(info),
                    !isBinary));
                found++;
            }
        }

        if (found == 0)
        {
            notes.Add(
                "The app's data container holds no file that looks like a crash report. An app only writes such a " +
                "file when it has a crash reporter that writes one.");
        }
    }

    /// <summary>
    /// The files of a container folder. A container can hold a folder the app may not read, and one
    /// unreadable folder must not stop the whole scan.
    /// </summary>
    private static IEnumerable<string> EnumerateCrashFiles(string directory)
    {
        var found = new List<string>();
        try
        {
            found.AddRange(Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Take(MaxReports * 4));
        }
        catch (UnauthorizedAccessException)
        {
            // The app may keep part of its container away from the tools that read it.
        }
        catch (IOException)
        {
        }

        return found;
    }

    private void AppendDiagnosticReports(
        string appName,
        string target,
        List<CrashLogArtifact> artifacts,
        List<string> notes)
    {
        if (string.IsNullOrWhiteSpace(appName))
        {
            return;
        }

        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library",
            "Logs",
            "DiagnosticReports");

        if (!Directory.Exists(folder))
        {
            return;
        }

        var found = 0;
        foreach (var file in Directory.EnumerateFiles(folder, $"{appName}*").Take(MaxReports))
        {
            var info = new FileInfo(file);
            artifacts.Add(new CrashLogArtifact(
                info.Name,
                CrashLogSource.SystemCrashLog,
                file,
                $"The crash report macOS wrote for {appName} in the simulator {target}",
                info.Length,
                info.LastWriteTimeUtc,
                ReadTextSafely(info),
                true));
            found++;
        }

        if (found == 0)
        {
            notes.Add(
                $"No crash report for {appName} was found in the macOS DiagnosticReports folder, which is where the " +
                "simulator writes the reports of the apps that crashed in it.");
        }
    }

    private async Task<IReadOnlyList<IosDeviceFileEntry>?> ListDomainFilesAsync(
        string xcrunPath,
        string target,
        string domainType,
        string? domainIdentifier,
        string? subdirectory,
        CancellationToken cancellationToken)
    {
        var jsonPath = Path.Combine(Path.GetTempPath(), $"dnap-devicectl-{Guid.NewGuid():N}.json");
        try
        {
            var arguments = new List<string>
            {
                "device",
                "info",
                "files",
                "--device",
                target,
                "--domain-type",
                domainType,
                "--json-output",
                jsonPath
            };

            if (!string.IsNullOrWhiteSpace(domainIdentifier))
            {
                arguments.Add("--domain-identifier");
                arguments.Add(domainIdentifier);
            }

            if (!string.IsNullOrWhiteSpace(subdirectory))
            {
                arguments.Add("--subdirectory");
                arguments.Add(subdirectory);
            }

            var exitCode = await ProcessRunner.RunAsync(
                [xcrunPath, .. arguments],
                _ => { },
                cancellationToken);

            if (exitCode != 0 || !File.Exists(jsonPath))
            {
                return null;
            }

            return IosCrashLogParser.ParseFileListing(await File.ReadAllTextAsync(jsonPath, cancellationToken));
        }
        catch (IOException)
        {
            return null;
        }
        finally
        {
            TryDelete(jsonPath);
        }
    }

    private async Task<string?> CopyDomainFileAsync(
        string xcrunPath,
        string target,
        string domainType,
        string? domainIdentifier,
        string source,
        CancellationToken cancellationToken)
    {
        var destination = Path.Combine(Path.GetTempPath(), $"dnap-crash-{Guid.NewGuid():N}.log");
        try
        {
            var arguments = new List<string>
            {
                "device",
                "copy",
                "from",
                "--device",
                target,
                "--domain-type",
                domainType,
                "--source",
                source,
                "--destination",
                destination
            };

            if (!string.IsNullOrWhiteSpace(domainIdentifier))
            {
                arguments.Add("--domain-identifier");
                arguments.Add(domainIdentifier);
            }

            var exitCode = await ProcessRunner.RunAsync(
                [xcrunPath, .. arguments],
                _ => { },
                cancellationToken);

            if (exitCode != 0 || !File.Exists(destination))
            {
                return null;
            }

            return await File.ReadAllTextAsync(destination, cancellationToken);
        }
        catch (IOException)
        {
            return null;
        }
        finally
        {
            TryDelete(destination);
        }
    }

    private async Task<string?> GetAppContainerAsync(
        string xcrunPath,
        string simulatorId,
        string bundleId,
        string kind,
        CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        var exitCode = await ProcessRunner.RunAsync(
            [xcrunPath, "simctl", "get_app_container", simulatorId, bundleId, kind],
            text => output.Append(text),
            cancellationToken);

        if (exitCode != 0)
        {
            return null;
        }

        // The command writes the path, and sometimes a line of advice before it.
        var path = output
            .ToString()
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(line => line.StartsWith('/'));

        return string.IsNullOrWhiteSpace(path) ? null : path;
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

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }

    private string RequireXcrun() => _xcrunPath
        ?? throw new InvalidOperationException("`xcrun` was not found. Install the Xcode command line tools to read iOS crash reports.");

    private static string RequireId(string id, string kind) => string.IsNullOrWhiteSpace(id)
        ? throw new InvalidOperationException($"No iOS {kind} selected. Select one first.")
        : id.Trim();

    private static string RequireBundleId(string bundleId) => string.IsNullOrWhiteSpace(bundleId)
        ? throw new InvalidOperationException("A bundle id is required to look for crash reports.")
        : bundleId.Trim();
}
