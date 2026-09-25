using System.Text;
using DotNetAppPublisher.Features.Publishing.Inspect;
using DotNetAppPublisher.Features.Publishing.Process;
using DotNetAppPublisher.Models;

namespace DotNetAppPublisher.Features.Deployment.Android;

public sealed class AndroidDeploymentService
{
    private readonly string? _adbPath;
    private readonly string? _emulatorPath;
    private readonly Func<string?> _apkSignerPath;
    private readonly Func<string, string?, string?, FileInfo?> _findApk;

    public AndroidDeploymentService(
        string? adbPath,
        string? emulatorPath,
        Func<string?> apkSignerPath,
        Func<string, string?, string?, FileInfo?> findApk)
    {
        _adbPath = adbPath;
        _emulatorPath = emulatorPath;
        _apkSignerPath = apkSignerPath;
        _findApk = findApk;
    }

    public async Task<string> InstallLatestApkAsync(
        PublishConfiguration configuration,
        string? deviceSerial,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(configuration.PublishPlatform, "Android", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Install APK is only available for Android projects.");
        }

        var adbPath = _adbPath ?? throw new InvalidOperationException("`adb` was not found. Install Android platform-tools or add adb to PATH.");
        var targetDevice = !string.IsNullOrWhiteSpace(deviceSerial)
            ? deviceSerial!.Trim()
            : throw new InvalidOperationException("No device selected. Select a device first.");

        string? projectNameHint = null;
        if (!string.IsNullOrWhiteSpace(configuration.ProjectDirectory))
        {
            var projectDirectory = ProjectInspector.CreateProjectDirectory(configuration.ProjectDirectory);
            projectNameHint = ProjectInspector.FindProjectFile(projectDirectory)?.Name is { } fileName
                ? Path.GetFileNameWithoutExtension(fileName)
                : null;
        }

        var outputDirectory = string.IsNullOrWhiteSpace(configuration.OutputDirectory)
            ? string.IsNullOrWhiteSpace(configuration.ProjectDirectory)
                ? throw new InvalidOperationException("No publish folder set. Set the Output Directory first, or select a project.")
                : ProjectInspector.GetDefaultOutputDirectory(
                    ProjectInspector.CreateProjectDirectory(configuration.ProjectDirectory),
                    configuration.Configuration,
                    configuration.TargetFramework,
                    configuration.RuntimeIdentifier)
            : configuration.OutputDirectory.Trim();

        var apk = _findApk(outputDirectory, projectNameHint, configuration.PackageId)
            ?? throw new InvalidOperationException($"No APK found in {outputDirectory}.");
        await EnsureApkHasValidSignatureAsync(apk.FullName, cancellationToken);

        var output = new StringBuilder();
        var exitCode = await ProcessRunner.RunAsync(
            [adbPath, "-s", targetDevice, "install", "-r", apk.FullName],
            text => output.Append(text),
            cancellationToken);

        if (exitCode != 0 && output.ToString().Contains("INSTALL_FAILED_UPDATE_INCOMPATIBLE", StringComparison.OrdinalIgnoreCase))
        {
            var packageName = ExtractPackageName(apk.Name);
            if (!string.IsNullOrWhiteSpace(packageName))
            {
                output.Clear();
                output.Append($"Existing package has incompatible signatures. Uninstalling {packageName} first...{Environment.NewLine}");
                var uninstallExitCode = await ProcessRunner.RunAsync(
                    [adbPath, "-s", targetDevice, "uninstall", packageName],
                    text => output.Append(text),
                    cancellationToken);

                if (uninstallExitCode == 0)
                {
                    output.Append($"Uninstalled {packageName}. Retrying install...{Environment.NewLine}");
                    exitCode = await ProcessRunner.RunAsync(
                        [adbPath, "-s", targetDevice, "install", apk.FullName],
                        text => output.Append(text),
                        cancellationToken);
                    if (exitCode == 0)
                    {
                        return $"Installed {apk.Name} on {targetDevice} after uninstalling incompatible version.{Environment.NewLine}{output}";
                    }
                }
                else
                {
                    output.Append($"Uninstall failed, attempting install anyway...{Environment.NewLine}");
                    exitCode = await ProcessRunner.RunAsync(
                        [adbPath, "-s", targetDevice, "install", apk.FullName],
                        text => output.Append(text),
                        cancellationToken);
                    if (exitCode == 0)
                    {
                        return $"Installed {apk.Name} on {targetDevice}.{Environment.NewLine}{output}";
                    }
                }
            }
        }

        if (exitCode == 0)
        {
            return $"Installed {apk.Name} on {targetDevice}.{Environment.NewLine}{output}";
        }

        throw new InvalidOperationException($"APK install failed for {apk.Name} on {targetDevice}.{Environment.NewLine}{output}");
    }

    public async Task<string> UninstallAsync(string packageId, string? deviceSerial, CancellationToken cancellationToken)
    {
        var adbPath = _adbPath ?? throw new InvalidOperationException("`adb` was not found. Install Android platform-tools or add adb to PATH.");
        if (string.IsNullOrWhiteSpace(packageId))
        {
            throw new InvalidOperationException("Package id is required to uninstall the app.");
        }

        var targetDevice = !string.IsNullOrWhiteSpace(deviceSerial)
            ? deviceSerial!.Trim()
            : throw new InvalidOperationException("No device selected. Select a device first.");
        var output = new StringBuilder();
        var exitCode = await ProcessRunner.RunAsync(
            [adbPath, "-s", targetDevice, "uninstall", packageId.Trim()],
            text => output.Append(text),
            cancellationToken);

        if (exitCode == 0)
        {
            return $"Uninstall requested for {packageId}.{Environment.NewLine}{output}";
        }

        throw new InvalidOperationException($"Uninstall failed for {packageId} on {targetDevice}.{Environment.NewLine}{output}");
    }

    public async Task<string> LaunchAsync(string packageId, string? deviceSerial, CancellationToken cancellationToken)
    {
        var adbPath = _adbPath ?? throw new InvalidOperationException("`adb` was not found. Install Android platform-tools or add adb to PATH.");
        if (string.IsNullOrWhiteSpace(packageId))
        {
            throw new InvalidOperationException("Package id is required to launch the app.");
        }

        var targetDevice = !string.IsNullOrWhiteSpace(deviceSerial)
            ? deviceSerial!.Trim()
            : throw new InvalidOperationException("No device selected. Select a device first.");
        var output = new StringBuilder();
        var exitCode = await ProcessRunner.RunAsync(
            [adbPath, "-s", targetDevice, "shell", "monkey", "-p", packageId.Trim(), "-c", "android.intent.category.LAUNCHER", "1"],
            text => output.Append(text),
            cancellationToken);

        if (exitCode == 0)
        {
            return $"Launch requested for {packageId}.{Environment.NewLine}{output}";
        }

        throw new InvalidOperationException($"Launch failed for {packageId} on {targetDevice}.{Environment.NewLine}{output}");
    }

    public async Task<string> PushFileToDownloadsAsync(string localFilePath, string? deviceSerial, CancellationToken cancellationToken)
    {
        var adbPath = _adbPath ?? throw new InvalidOperationException("`adb` was not found. Install Android platform-tools or add adb to PATH.");
        if (string.IsNullOrWhiteSpace(localFilePath))
        {
            throw new InvalidOperationException("File path is required.");
        }

        if (!File.Exists(localFilePath))
        {
            throw new InvalidOperationException($"File not found: {localFilePath}");
        }

        var targetDevice = !string.IsNullOrWhiteSpace(deviceSerial)
            ? deviceSerial!.Trim()
            : throw new InvalidOperationException("No device selected. Select a device first.");
        var output = new StringBuilder();
        var exitCode = await ProcessRunner.RunAsync(
            [adbPath, "-s", targetDevice, "push", localFilePath, "/storage/emulated/0/Download/"],
            text => output.Append(text),
            cancellationToken);
        var resultText = output.ToString();
        if (exitCode == 0)
        {
            return $"Pushed {Path.GetFileName(localFilePath)} to Downloads on {targetDevice}.{Environment.NewLine}{resultText}";
        }

        throw new InvalidOperationException($"Failed to push file to {targetDevice}.{Environment.NewLine}{resultText}");
    }

    public async Task<IReadOnlyList<string>> DiscoverEmulatorsAsync(CancellationToken cancellationToken)
    {
        var emulatorPath = _emulatorPath ?? throw new InvalidOperationException(
            "Android emulator tool was not found. Install Android SDK emulator tools or add `emulator` to PATH.");
        var output = new StringBuilder();
        var exitCode = await ProcessRunner.RunAsync([emulatorPath, "-list-avds"], text => output.Append(text), cancellationToken);
        if (exitCode != 0)
        {
            throw new InvalidOperationException("Failed to query emulators. Verify Android SDK emulator tools are installed.");
        }

        return output
            .ToString()
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<IReadOnlyList<AndroidDeviceInfo>> DiscoverAndroidDevicesAsync(CancellationToken cancellationToken)
    {
        var devices = new List<AndroidDeviceInfo>();
        var runningSerials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_adbPath is not null)
        {
            var adbOutput = new StringBuilder();
            var adbExitCode = await ProcessRunner.RunAsync([_adbPath, "devices", "-l"], text => adbOutput.Append(text), cancellationToken);
            if (adbExitCode != 0)
            {
                throw new InvalidOperationException($"Failed to query Android devices with adb (exit code {adbExitCode}).{Environment.NewLine}{adbOutput}");
            }

            foreach (var line in adbOutput.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Skip(1))
            {
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2 || !string.Equals(parts[1], "device", StringComparison.Ordinal))
                {
                    continue;
                }

                var serial = parts[0];
                runningSerials.Add(serial);
                var isEmulator = serial.StartsWith("emulator-", StringComparison.OrdinalIgnoreCase);
                var name = serial;
                if (isEmulator)
                {
                    name = await GetAvdNameAsync(serial, cancellationToken) ?? serial;
                }
                else
                {
                    name = ExtractModelFromAdbOutput(line) ?? serial;
                }

                devices.Add(new AndroidDeviceInfo(serial, name, isEmulator ? "emulator" : "device", true));
            }
        }

        var runningAvdNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var serial in runningSerials.Where(s => s.StartsWith("emulator-", StringComparison.OrdinalIgnoreCase)))
        {
            var avdName = await GetAvdNameAsync(serial, cancellationToken);
            if (!string.IsNullOrWhiteSpace(avdName))
            {
                runningAvdNames.Add(avdName);
            }
        }

        if (_emulatorPath is not null)
        {
            var avdOutput = new StringBuilder();
            var avdExitCode = await ProcessRunner.RunAsync([_emulatorPath, "-list-avds"], text => avdOutput.Append(text), cancellationToken);
            if (avdExitCode == 0)
            {
                foreach (var avdName in avdOutput.ToString()
                    .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Distinct(StringComparer.Ordinal)
                    .Where(avdName => !runningAvdNames.Contains(avdName)))
                {
                    devices.Add(new AndroidDeviceInfo(string.Empty, avdName, "emulator", false));
                }
            }
        }

        return devices
            .OrderByDescending(d => d.Type == "device" ? 0 : d.IsRunning ? 1 : 2)
            .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public Task<string> LaunchEmulatorAsync(string emulatorName, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var emulatorPath = _emulatorPath ?? throw new InvalidOperationException(
            "Android emulator tool was not found. Install Android SDK emulator tools or add `emulator` to PATH.");
        if (string.IsNullOrWhiteSpace(emulatorName))
        {
            throw new InvalidOperationException("Select an emulator first.");
        }

        var startInfo = new System.Diagnostics.ProcessStartInfo(emulatorPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-avd");
        startInfo.ArgumentList.Add(emulatorName.Trim());
        System.Diagnostics.Process.Start(startInfo);
        return Task.FromResult($"Launching emulator `{emulatorName.Trim()}`.");
    }

    public static string? GetFirstRunningDeviceSerial(IReadOnlyList<AndroidDeviceInfo> devices) =>
        devices.FirstOrDefault(d => d.IsRunning && d.Type == "device")?.Serial
        ?? devices.FirstOrDefault(d => d.IsRunning && d.Type == "emulator")?.Serial;

    private async Task<string?> GetAvdNameAsync(string serial, CancellationToken cancellationToken)
    {
        if (_adbPath is null)
        {
            return null;
        }

        try
        {
            var output = new StringBuilder();
            var exitCode = await ProcessRunner.RunAsync([_adbPath, "-s", serial, "emu", "avd", "name"], text => output.Append(text), cancellationToken);
            if (exitCode == 0)
            {
                return output.ToString().Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .FirstOrDefault()?.Replace("\r", string.Empty).Trim();
            }
        }
        catch
        {
        }

        return null;
    }

    private async Task EnsureApkHasValidSignatureAsync(string apkPath, CancellationToken cancellationToken)
    {
        var apkSignerPath = _apkSignerPath();
        if (string.IsNullOrWhiteSpace(apkSignerPath))
        {
            return;
        }

        var output = new StringBuilder();
        var exitCode = await ProcessRunner.RunAsync(
            [apkSignerPath, "verify", "--print-certs", apkPath],
            text => output.Append(text),
            cancellationToken);
        if (exitCode == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            $"APK signature verification failed for `{Path.GetFileName(apkPath)}`. " +
            "The APK appears unsigned or signed incorrectly. " +
            "Publish with proper signing (or use a debug-signed APK) and retry." +
            $"{Environment.NewLine}{output}");
    }

    private static string? ExtractModelFromAdbOutput(string line)
    {
        var modelMatch = System.Text.RegularExpressions.Regex.Match(line, @"model:(\S+)");
        return modelMatch.Success && modelMatch.Groups.Count > 1
            ? modelMatch.Groups[1].Value.Replace('_', ' ')
            : null;
    }

    private static string? ExtractPackageName(string apkFileName)
    {
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(apkFileName);
        foreach (var suffix in new[] { "-Signed", "-unsigned", "-debug", "-release" })
        {
            if (nameWithoutExtension.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return nameWithoutExtension[..^suffix.Length];
            }
        }

        var dashIndex = nameWithoutExtension.LastIndexOf('-');
        return dashIndex > 0 ? nameWithoutExtension[..dashIndex] : nameWithoutExtension;
    }
}
