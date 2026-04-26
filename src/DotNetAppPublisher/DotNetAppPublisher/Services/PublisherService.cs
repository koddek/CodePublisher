using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using DotNetAppPublisher.Models;

namespace DotNetAppPublisher.Services;

public sealed record AndroidDeviceInfo(string Serial, string Name, string Type, bool IsRunning)
{
    public string DisplayName => IsRunning
        ? $"{Name} ({Serial})"
        : $"{Name} (Offline)";
}

public sealed class PublisherService
{
    public const string AndroidPlatform = "Android";
    public const string MacOsPlatform = "macOS";
    public const string WindowsPlatform = "Windows";
    public const string IosPlatform = "iOS";

    private const string MacAppIconFileName = "dotnet-app-publisher";
    private const string MacAppIconAssetPath = "avares://DotNetAppPublisher/Assets/dotnet-app-publisher.icns";

    private static readonly string[] DotnetCandidates =
    [
        "/usr/local/share/dotnet/dotnet",
        "/opt/homebrew/bin/dotnet",
        "/usr/local/bin/dotnet"
    ];

    private static readonly string[] EmulatorCandidates =
    [
        "/Users/omoi/Library/Android/sdk/emulator/emulator",
        "/Users/omoi/Android/Sdk/emulator/emulator",
        "/opt/homebrew/bin/emulator",
        "/usr/local/bin/emulator"
    ];

    public PublisherService()
    {
        DotnetPath = ResolveExecutable("dotnet", DotnetCandidates);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        AdbPath = ResolveExecutable("adb",
        [
            Path.Combine(home, "Library/Android/sdk/platform-tools/adb"),
            Path.Combine(home, "Android/Sdk/platform-tools/adb"),
            "/opt/homebrew/bin/adb",
            "/usr/local/bin/adb"
        ]);

        EmulatorPath = ResolveExecutable("emulator",
        [
            Path.Combine(home, "Library/Android/sdk/emulator/emulator"),
            Path.Combine(home, "Android/Sdk/emulator/emulator"),
            "/opt/homebrew/bin/emulator",
            "/usr/local/bin/emulator"
        ]) ?? ResolveExecutable("emulator", EmulatorCandidates);
    }

    public string? DotnetPath { get; }

    public string? AdbPath { get; }

    public string? EmulatorPath { get; }

    public string? ApkSignerPath => ResolveApkSignerPath();

    public string? XcrunPath => ResolveExecutable("xcrun", ["/usr/bin/xcrun"]);

    public string DotnetStatusText => DotnetPath is null ? "dotnet not found" : $"dotnet: {DotnetPath}";

    public string AdbStatusText => AdbPath is null ? "adb not found" : $"adb: {AdbPath}";

    public string EmulatorStatusText => EmulatorPath is null ? "emulator not found" : $"emulator: {EmulatorPath}";

    public ProjectMetadata LoadProjectMetadata(string projectDirectory, string configuration, string targetFramework, string runtimeIdentifier, string publishPlatform)
    {
        var projectDirectoryPath = CreateProjectDirectory(projectDirectory);
        var projectFile = FindProjectFile(projectDirectoryPath)
            ?? throw new InvalidOperationException($"No .csproj found in {projectDirectoryPath}.");
        var projectName = Path.GetFileNameWithoutExtension(projectFile.Name);

        return new ProjectMetadata(
            projectFile.FullName,
            GetDefaultOutputDirectory(projectDirectoryPath, configuration, targetFramework, runtimeIdentifier),
            GetProjectIdentifier(projectFile, projectName, publishPlatform),
            ReadTargetFramework(projectFile, publishPlatform),
            ReadVersion(projectFile, publishPlatform, isDisplay: true),
            ReadVersion(projectFile, publishPlatform, isDisplay: false),
            SupportsInternalVersion(publishPlatform));
    }

    public PublishCommandBundle BuildPublishCommandBundle(PublishConfiguration configuration)
    {
        var projectDirectory = CreateProjectDirectory(configuration.ProjectDirectory);
        var projectFile = FindProjectFile(projectDirectory)
            ?? throw new InvalidOperationException($"No .csproj found in {projectDirectory}.");

        ValidateProjectTargetFramework(projectFile, configuration.TargetFramework);

        var isAndroid = IsAndroidPlatform(configuration.PublishPlatform);
        var isMacOs = IsMacOsPlatform(configuration.PublishPlatform);
        var isWindows = IsWindowsPlatform(configuration.PublishPlatform);
        var isIos = IsIosPlatform(configuration.PublishPlatform);
        var formats = isAndroid ? GetSelectedFormats(configuration) : [];
        if (isAndroid && formats.Count == 0)
        {
            throw new InvalidOperationException("Select at least one package format: APK, AAB, or both.");
        }

        ValidateRuntimeForPlatform(configuration);

        if (DotnetPath is null)
        {
            throw new InvalidOperationException("`dotnet` was not found. Install the .NET SDK or add dotnet to PATH.");
        }

        var outputDirectory = string.IsNullOrWhiteSpace(configuration.OutputDirectory)
            ? GetDefaultOutputDirectory(projectDirectory, configuration.Configuration, configuration.TargetFramework, configuration.RuntimeIdentifier)
            : configuration.OutputDirectory.Trim();

        var customTrimProperty = DetectCustomTrimProperty(projectFile);
        var command = isAndroid
            ? BuildAndroidCommand(configuration, projectFile.FullName, customTrimProperty)
            : isMacOs
                ? BuildMacOsCommand(configuration, projectFile.FullName, customTrimProperty)
                : isWindows
                    ? BuildWindowsCommand(configuration, projectFile.FullName, customTrimProperty)
                    : isIos
                        ? BuildIosCommand(configuration, projectFile.FullName, customTrimProperty)
                        : throw new InvalidOperationException($"Unknown publish platform `{configuration.PublishPlatform}`.");

        if (isAndroid)
        {
            command.Add($"-p:AndroidPackageFormats={string.Join("%3B", formats)}");
        }

        command.Add("-o");
        command.Add(outputDirectory);

        return new PublishCommandBundle(
            command,
            MaskCommand(command),
            isAndroid ? MaskCommand(BuildVerifiedApkCommand(configuration, projectFile.FullName, outputDirectory, customTrimProperty)) : string.Empty,
            projectFile.FullName,
            outputDirectory);
    }

    public async Task<bool> PublishAsync(PublishConfiguration configuration, Action<string> writeOutput, CancellationToken cancellationToken)
    {
        var bundle = BuildPublishCommandBundle(configuration);
        var projectDirectory = CreateProjectDirectory(configuration.ProjectDirectory);
        var outputDirectory = new DirectoryInfo(bundle.OutputDirectory);

        writeOutput(Environment.NewLine + "=== Publish started ===" + Environment.NewLine);
        writeOutput(bundle.PreviewText + Environment.NewLine + Environment.NewLine);
        if (!string.IsNullOrWhiteSpace(bundle.VerifiedApkPreviewText))
        {
            writeOutput("Known-good APK-only command:" + Environment.NewLine);
            writeOutput(bundle.VerifiedApkPreviewText + Environment.NewLine + Environment.NewLine);
        }

        if (outputDirectory.Exists)
        {
            writeOutput($"Clearing output folder {outputDirectory.FullName}{Environment.NewLine}");
            await TryDeleteDirectorySafelyAsync(outputDirectory.FullName, writeOutput);
        }

        if (configuration.DeleteObj)
        {
            await CleanStalePlatformDirectoriesAsync(projectDirectory, configuration.Configuration, configuration.TargetFramework, configuration.RuntimeIdentifier, configuration.PublishPlatform, writeOutput);
        }

        if (configuration.DeleteBin)
        {
            await DeleteDirectoryIfPresentSafelyAsync(Path.Combine(projectDirectory.FullName, "bin"), writeOutput);
        }

        if (configuration.DeleteObj)
        {
            await DeleteDirectoryIfPresentSafelyAsync(Path.Combine(projectDirectory.FullName, "obj"), writeOutput);
        }

        writeOutput($"--- Running: {bundle.PreviewText} ---{Environment.NewLine}");
        var exitCode = await RunProcessAsync(bundle.CommandArguments, writeOutput, cancellationToken);

if (exitCode == 0)
        {
            if (IsMacOsPlatform(configuration.PublishPlatform) && configuration.CreateMacAppBundle)
            {
                var createdBundlePath = await EnsureMacAppBundleAsync(
                    bundle.OutputDirectory,
                    bundle.ProjectFilePath,
                    configuration.PackageId,
                    writeOutput,
                    cancellationToken);

                if (!string.IsNullOrWhiteSpace(createdBundlePath))
                {
                    writeOutput($"macOS app bundle ready: {createdBundlePath}{Environment.NewLine}");
                }
            }

            writeOutput(Environment.NewLine + "=== Publish completed successfully ===" + Environment.NewLine);
            PlayCompletionSound(success: true);
            return true;
        }

        writeOutput(Environment.NewLine + "=== Publish failed (exit code {exitCode}) ===" + Environment.NewLine);
        PlayCompletionSound(success: false);
        return false;
    }

    public void OpenPublishFolder(string outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
        {
            throw new InvalidOperationException($"Publish folder not found: {outputDirectory}");
        }

        if (OperatingSystem.IsMacOS())
        {
            Process.Start(new ProcessStartInfo("open", QuoteArgument(outputDirectory)) { UseShellExecute = false });
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = outputDirectory,
                UseShellExecute = true
            });
            return;
        }

        Process.Start(new ProcessStartInfo("xdg-open", QuoteArgument(outputDirectory)) { UseShellExecute = false });
    }

    public async Task<string> InstallLatestApkAsync(PublishConfiguration configuration, string? deviceSerial, CancellationToken cancellationToken)
    {
        if (!IsAndroidPlatform(configuration.PublishPlatform))
        {
            throw new InvalidOperationException("Install APK is only available for Android projects.");
        }

        var projectDirectory = CreateProjectDirectory(configuration.ProjectDirectory);
        var projectFile = FindProjectFile(projectDirectory)
            ?? throw new InvalidOperationException($"No .csproj found in {projectDirectory}.");

        var adbPath = AdbPath ?? throw new InvalidOperationException("`adb` was not found. Install Android platform-tools or add adb to PATH.");

        var targetDevice = !string.IsNullOrWhiteSpace(deviceSerial)
            ? deviceSerial!.Trim()
            : throw new InvalidOperationException("No device selected. Select a device first.");

        var outputDirectory = string.IsNullOrWhiteSpace(configuration.OutputDirectory)
            ? GetDefaultOutputDirectory(projectDirectory, configuration.Configuration, configuration.TargetFramework, configuration.RuntimeIdentifier)
            : configuration.OutputDirectory.Trim();

        var apk = FindBestApk(outputDirectory, Path.GetFileNameWithoutExtension(projectFile.Name), configuration.PackageId)
            ?? throw new InvalidOperationException($"No APK found in {outputDirectory}.");

        await EnsureApkHasValidSignatureAsync(apk.FullName, cancellationToken);

        var output = new StringBuilder();
        var exitCode = await RunProcessAsync(
            [adbPath, "-s", targetDevice, "install", "-r", apk.FullName],
            text => output.Append(text),
            cancellationToken);

        var resultText = output.ToString();
        if (exitCode != 0 && resultText.Contains("INSTALL_FAILED_UPDATE_INCOMPATIBLE", StringComparison.OrdinalIgnoreCase))
        {
            var packageName = ExtractPackageName(apk.Name);
            if (!string.IsNullOrWhiteSpace(packageName))
            {
                output.Clear();
                output.Append($"Existing package has incompatible signatures. Uninstalling {packageName} first...{Environment.NewLine}");
                var uninstallExitCode = await RunProcessAsync(
                    [adbPath, "-s", targetDevice, "uninstall", packageName],
                    text => output.Append(text),
                    cancellationToken);

                if (uninstallExitCode == 0)
                {
                    output.Append($"Uninstalled {packageName}. Retrying install...{Environment.NewLine}");
                    exitCode = await RunProcessAsync(
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
                    exitCode = await RunProcessAsync(
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

        throw new InvalidOperationException(
            $"APK install failed for {apk.Name} on {targetDevice}.{Environment.NewLine}{output}");
    }

    public async Task<string> UninstallAsync(string packageId, string? deviceSerial, CancellationToken cancellationToken)
    {
        var adbPath = AdbPath ?? throw new InvalidOperationException("`adb` was not found. Install Android platform-tools or add adb to PATH.");
        if (string.IsNullOrWhiteSpace(packageId))
        {
            throw new InvalidOperationException("Package id is required to uninstall the app.");
        }

        var targetDevice = !string.IsNullOrWhiteSpace(deviceSerial)
            ? deviceSerial!.Trim()
            : throw new InvalidOperationException("No device selected. Select a device first.");

        var output = new StringBuilder();
        var exitCode = await RunProcessAsync(
            [adbPath, "-s", targetDevice, "uninstall", packageId.Trim()],
            text => output.Append(text),
            cancellationToken);

        if (exitCode == 0)
        {
            return $"Uninstall requested for {packageId}.{Environment.NewLine}{output}";
        }

        throw new InvalidOperationException(
            $"Uninstall failed for {packageId} on {targetDevice}.{Environment.NewLine}{output}");
    }

    public async Task<string> LaunchAsync(string packageId, string? deviceSerial, CancellationToken cancellationToken)
    {
        var adbPath = AdbPath ?? throw new InvalidOperationException("`adb` was not found. Install Android platform-tools or add adb to PATH.");
        if (string.IsNullOrWhiteSpace(packageId))
        {
            throw new InvalidOperationException("Package id is required to launch the app.");
        }

        var targetDevice = !string.IsNullOrWhiteSpace(deviceSerial)
            ? deviceSerial!.Trim()
            : throw new InvalidOperationException("No device selected. Select a device first.");

        var output = new StringBuilder();
        var exitCode = await RunProcessAsync(
            [adbPath, "-s", targetDevice, "shell", "monkey", "-p", packageId.Trim(), "-c", "android.intent.category.LAUNCHER", "1"],
            text => output.Append(text),
            cancellationToken);

        if (exitCode == 0)
        {
            return $"Launch requested for {packageId}.{Environment.NewLine}{output}";
        }

        throw new InvalidOperationException(
            $"Launch failed for {packageId} on {targetDevice}.{Environment.NewLine}{output}");
    }

    public async Task<string> PushFileToDownloadsAsync(string localFilePath, string? deviceSerial, CancellationToken cancellationToken)
    {
        var adbPath = AdbPath ?? throw new InvalidOperationException("`adb` was not found. Install Android platform-tools or add adb to PATH.");

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
        var exitCode = await RunProcessAsync(
            [adbPath, "-s", targetDevice, "push", localFilePath, "/storage/emulated/0/Download/"],
            text => output.Append(text),
            cancellationToken);

        var resultText = output.ToString();
        if (exitCode == 0)
        {
            return $"Pushed {Path.GetFileName(localFilePath)} to Downloads on {targetDevice}.{Environment.NewLine}{resultText}";
        }

        throw new InvalidOperationException(
            $"Failed to push file to {targetDevice}.{Environment.NewLine}{resultText}");
    }

    private static string? ExtractPackageName(string apkFileName)
    {
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(apkFileName);
        var suffixes = new[] { "-Signed", "-unsigned", "-debug", "-release" };
        foreach (var suffix in suffixes)
        {
            if (nameWithoutExtension.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return nameWithoutExtension[..^suffix.Length];
            }
        }

        var dashIndex = nameWithoutExtension.LastIndexOf('-');
        if (dashIndex > 0)
        {
            return nameWithoutExtension[..dashIndex];
        }

        return nameWithoutExtension;
    }

    public Task<string> DeletePublishedDesktopAppAsync(string outputDirectory, CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
        {
            throw new InvalidOperationException($"Publish folder not found: {outputDirectory}");
        }

        var outputDirectoryInfo = new DirectoryInfo(outputDirectory);
        var appBundle = outputDirectoryInfo
            .EnumerateDirectories("*.app", SearchOption.AllDirectories)
            .OrderByDescending(directory => directory.LastWriteTimeUtc)
            .FirstOrDefault();

        if (appBundle is null)
        {
            throw new InvalidOperationException($"No .app bundle found under {outputDirectory}.");
        }

        appBundle.Delete(recursive: true);
        return Task.FromResult($"Deleted desktop app bundle {appBundle.FullName}.");
    }

    private readonly AvaloniaScreenshotService _screenshotService = new();

    public async Task<string> CopyWindowScreenshotToClipboardAsync(Window window, CancellationToken cancellationToken)
    {
        return await _screenshotService.CaptureWindowToClipboardAsync(window, cancellationToken);
    }

    public async Task<string> SaveWindowScreenshotToDiskAsync(Window window, string outputDirectory, CancellationToken cancellationToken)
    {
        return await _screenshotService.CaptureWindowToDiskAsync(window, outputDirectory, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> DiscoverEmulatorsAsync(CancellationToken cancellationToken)
    {
        var emulatorPath = EmulatorPath ?? throw new InvalidOperationException(
            "Android emulator tool was not found. Install Android SDK emulator tools or add `emulator` to PATH.");

        var output = new StringBuilder();
        var exitCode = await RunProcessAsync([emulatorPath, "-list-avds"], text => output.Append(text), cancellationToken);
        if (exitCode != 0)
        {
            throw new InvalidOperationException("Failed to query emulators. Verify Android SDK emulator tools are installed.");
        }

        var emulators = output
            .ToString()
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return emulators;
    }

    public async Task<IReadOnlyList<AndroidDeviceInfo>> DiscoverAndroidDevicesAsync(CancellationToken cancellationToken)
    {
        var devices = new List<AndroidDeviceInfo>();
        var runningSerials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (AdbPath is not null)
        {
            var adbOutput = new StringBuilder();
            var adbExitCode = await RunProcessAsync([AdbPath, "devices", "-l"], text => adbOutput.Append(text), cancellationToken);
            if (adbExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Failed to query Android devices with adb (exit code {adbExitCode}).{Environment.NewLine}{adbOutput}");
            }

            var lines = adbOutput.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var line in lines.Skip(1))
            {
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                {
                    continue;
                }

                var serial = parts[0];
                var state = parts[1];

                if (!string.Equals(state, "device", StringComparison.Ordinal))
                {
                    continue;
                }

                runningSerials.Add(serial);

                var isEmulator = serial.StartsWith("emulator-", StringComparison.OrdinalIgnoreCase);
                var name = serial;

                if (isEmulator)
                {
                    var avdName = await GetAvdNameAsync(serial, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(avdName))
                    {
                        name = avdName;
                    }
                }
                else
                {
                    var model = ExtractModelFromAdbOutput(line);
                    if (!string.IsNullOrWhiteSpace(model))
                    {
                        name = model;
                    }
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

		if (EmulatorPath is not null)
		{
			var avdOutput = new StringBuilder();
			var avdExitCode = await RunProcessAsync([EmulatorPath, "-list-avds"], text => avdOutput.Append(text), cancellationToken);
			if (avdExitCode == 0)
			{
				var avdNames = avdOutput
					.ToString()
					.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
					.Distinct(StringComparer.Ordinal);

				foreach (var avdName in avdNames)
				{
					if (!runningAvdNames.Contains(avdName))
					{
						devices.Add(new AndroidDeviceInfo(string.Empty, avdName, "emulator", false));
					}
				}
			}
		}

        return devices
            .OrderByDescending(d => d.Type == "device" ? 0 : d.IsRunning ? 1 : 2)
            .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string? GetFirstRunningDeviceSerial(IReadOnlyList<AndroidDeviceInfo> devices)
    {
        return devices
            .FirstOrDefault(d => d.IsRunning && d.Type == "device")
            ?.Serial
            ?? devices
                .FirstOrDefault(d => d.IsRunning && d.Type == "emulator")
                ?.Serial;
    }

    private async Task<string?> GetAvdNameAsync(string serial, CancellationToken cancellationToken)
    {
        if (AdbPath is null)
        {
            return null;
        }

        try
        {
            var output = new StringBuilder();
            var exitCode = await RunProcessAsync([AdbPath, "-s", serial, "emu", "avd", "name"], text => output.Append(text), cancellationToken);
            if (exitCode == 0)
            {
                var result = output.ToString().Trim();
                var firstLine = result.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
                return firstLine?.Replace("\r", string.Empty).Trim();
            }
        }
        catch
        {
            // Best effort.
        }

        return null;
    }

    private static string? ExtractModelFromAdbOutput(string line)
    {
        var modelMatch = Regex.Match(line, @"model:(\S+)");
        if (modelMatch.Success && modelMatch.Groups.Count > 1)
        {
            return modelMatch.Groups[1].Value.Replace('_', ' ');
        }

        return null;
    }

    public async Task<IReadOnlyList<string>> DiscoverIosSimulatorsAsync(CancellationToken cancellationToken)
    {
        var xcrunPath = XcrunPath ?? throw new InvalidOperationException(
            "`xcrun` was not found. Install Xcode command line tools to use iOS simulator actions.");

        var output = new StringBuilder();
        var exitCode = await RunProcessAsync([xcrunPath, "simctl", "list", "devices", "available", "--json"], text => output.Append(text), cancellationToken);
        if (exitCode != 0)
        {
            throw new InvalidOperationException("Failed to query iOS simulators. Verify Xcode command line tools are installed.");
        }

        using var document = JsonDocument.Parse(output.ToString());
        if (!document.RootElement.TryGetProperty("devices", out var devicesElement))
        {
            return [];
        }

        var simulators = new List<string>();
        foreach (var runtimeDevices in devicesElement.EnumerateObject())
        {
            _ = runtimeDevices;
            foreach (var device in runtimeDevices.Value.EnumerateArray())
            {
                if (!device.TryGetProperty("isAvailable", out var isAvailableElement) || !isAvailableElement.GetBoolean())
                {
                    continue;
                }

                var name = device.GetProperty("name").GetString();
                var udid = device.GetProperty("udid").GetString();
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(udid))
                {
                    continue;
                }

                simulators.Add($"{name} | {udid}");
            }
        }

        return simulators
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public Task<string> LaunchEmulatorAsync(string emulatorName, CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        var emulatorPath = EmulatorPath ?? throw new InvalidOperationException(
            "Android emulator tool was not found. Install Android SDK emulator tools or add `emulator` to PATH.");

        if (string.IsNullOrWhiteSpace(emulatorName))
        {
            throw new InvalidOperationException("Select an emulator first.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = emulatorPath,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("-avd");
        startInfo.ArgumentList.Add(emulatorName.Trim());
        Process.Start(startInfo);

        return Task.FromResult($"Launching emulator `{emulatorName.Trim()}`.");
    }

    public async Task<string> LaunchIosSimulatorAsync(string simulator, CancellationToken cancellationToken)
    {
        var xcrunPath = XcrunPath ?? throw new InvalidOperationException(
            "`xcrun` was not found. Install Xcode command line tools to use iOS simulator actions.");

        if (string.IsNullOrWhiteSpace(simulator))
        {
            throw new InvalidOperationException("Select an iOS simulator first.");
        }

        var simulatorId = ExtractSimulatorId(simulator);
        await EnsureIosSimulatorBootedAsync(xcrunPath, simulatorId, cancellationToken);
        return $"Launched iOS simulator `{simulator}`.";
    }

    public async Task<string> InstallIosAppAsync(string outputDirectory, string simulator, CancellationToken cancellationToken)
    {
        var xcrunPath = XcrunPath ?? throw new InvalidOperationException(
            "`xcrun` was not found. Install Xcode command line tools to use iOS simulator actions.");

        if (string.IsNullOrWhiteSpace(simulator))
        {
            throw new InvalidOperationException("Select an iOS simulator first.");
        }

        var appBundle = FindNewestAppBundle(outputDirectory)
            ?? throw new InvalidOperationException($"No .app bundle found in {outputDirectory}.");
        var simulatorId = ExtractSimulatorId(simulator);
        await EnsureIosSimulatorBootedAsync(xcrunPath, simulatorId, cancellationToken);

        var output = new StringBuilder();
        var exitCode = await RunProcessAsync([xcrunPath, "simctl", "install", simulatorId, appBundle.FullName], text => output.Append(text), cancellationToken);
        return exitCode == 0
            ? $"Installed {appBundle.Name} on {simulator}.{Environment.NewLine}{output}"
            : $"iOS app install failed for {appBundle.Name}.{Environment.NewLine}{output}";
    }

    public async Task<string> UninstallIosAppAsync(string packageId, string simulator, CancellationToken cancellationToken)
    {
        var xcrunPath = XcrunPath ?? throw new InvalidOperationException(
            "`xcrun` was not found. Install Xcode command line tools to use iOS simulator actions.");
        if (string.IsNullOrWhiteSpace(packageId))
        {
            throw new InvalidOperationException("Bundle id is required to uninstall the app.");
        }

        if (string.IsNullOrWhiteSpace(simulator))
        {
            throw new InvalidOperationException("Select an iOS simulator first.");
        }

        var simulatorId = ExtractSimulatorId(simulator);
        await EnsureIosSimulatorBootedAsync(xcrunPath, simulatorId, cancellationToken);
        var output = new StringBuilder();
        var exitCode = await RunProcessAsync([xcrunPath, "simctl", "uninstall", simulatorId, packageId.Trim()], text => output.Append(text), cancellationToken);
        return exitCode == 0
            ? $"Uninstall requested for {packageId} on {simulator}.{Environment.NewLine}{output}"
            : $"iOS uninstall failed for {packageId}.{Environment.NewLine}{output}";
    }

    public async Task<string> LaunchIosAppAsync(string packageId, string simulator, CancellationToken cancellationToken)
    {
        var xcrunPath = XcrunPath ?? throw new InvalidOperationException(
            "`xcrun` was not found. Install Xcode command line tools to use iOS simulator actions.");
        if (string.IsNullOrWhiteSpace(packageId))
        {
            throw new InvalidOperationException("Bundle id is required to launch the app.");
        }

        if (string.IsNullOrWhiteSpace(simulator))
        {
            throw new InvalidOperationException("Select an iOS simulator first.");
        }

        var simulatorId = ExtractSimulatorId(simulator);
        await EnsureIosSimulatorBootedAsync(xcrunPath, simulatorId, cancellationToken);
        var output = new StringBuilder();
        var exitCode = await RunProcessAsync([xcrunPath, "simctl", "launch", simulatorId, packageId.Trim()], text => output.Append(text), cancellationToken);
        return exitCode == 0
            ? $"Launch requested for {packageId} on {simulator}.{Environment.NewLine}{output}"
            : $"iOS launch failed for {packageId}.{Environment.NewLine}{output}";
    }

    public async Task<string> PushFileToSimulatorAsync(string localFilePath, string simulator, CancellationToken cancellationToken)
    {
        var xcrunPath = XcrunPath ?? throw new InvalidOperationException(
            "`xcrun` was not found. Install Xcode command line tools to use iOS simulator actions.");

        if (string.IsNullOrWhiteSpace(localFilePath))
        {
            throw new InvalidOperationException("File path is required.");
        }

        if (!File.Exists(localFilePath))
        {
            throw new InvalidOperationException($"File not found: {localFilePath}");
        }

        if (string.IsNullOrWhiteSpace(simulator))
        {
            throw new InvalidOperationException("Select an iOS simulator first.");
        }

        var simulatorId = ExtractSimulatorId(simulator);
        await EnsureIosSimulatorBootedAsync(xcrunPath, simulatorId, cancellationToken);

        var output = new StringBuilder();
        var exitCode = await RunProcessAsync(
            [xcrunPath, "simctl", "listapps", simulatorId],
            text => output.Append(text),
            cancellationToken);

        if (exitCode != 0)
        {
            throw new InvalidOperationException($"Failed to query simulator apps: {output}");
        }

        var json = output.ToString();
        var fileProviderPath = ExtractFileProviderPath(json);
        if (string.IsNullOrWhiteSpace(fileProviderPath))
        {
            throw new InvalidOperationException("Could not find File Provider storage path. Make sure the iOS Simulator is running.");
        }

        var storageDir = Path.Combine(fileProviderPath, "File Provider Storage");
        if (!Directory.Exists(storageDir))
        {
            Directory.CreateDirectory(storageDir);
        }

        var targetPath = Path.Combine(storageDir, Path.GetFileName(localFilePath));
        File.Copy(localFilePath, targetPath, true);

        return $"Pushed {Path.GetFileName(localFilePath)} to Files app on {simulator}.{Environment.NewLine}Location: On My iPhone";
    }

    private static string? ExtractFileProviderPath(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            foreach (var app in document.RootElement.EnumerateObject())
            {
                if (app.Value.TryGetProperty("CFBundleIdentifier", out var bundleId) &&
                    bundleId.GetString() == "com.apple.FileProvider")
                {
                    if (app.Value.TryGetProperty("GroupContainers", out var groupContainers))
                    {
                        if (groupContainers.TryGetProperty("group.com.apple.FileProvider.LocalStorage", out var localStorage))
                        {
                            var path = localStorage.GetString();
                            if (!string.IsNullOrWhiteSpace(path))
                            {
                                return path.Replace("file://", "");
                            }
                        }
                    }
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private static string? ReadVersion(FileInfo projectFile, string publishPlatform, bool isDisplay)
    {
        if (isDisplay)
        {
            if (IsAndroidPlatform(publishPlatform) || IsIosPlatform(publishPlatform))
            {
                return ReadProperty(projectFile, "ApplicationDisplayVersion", "Version", "InformationalVersion");
            }

            if (IsMacOsPlatform(publishPlatform))
            {
                return ReadProperty(projectFile, "Version", "InformationalVersion");
            }

            return ReadProperty(projectFile, "Version", "InformationalVersion");
        }

        if (IsAndroidPlatform(publishPlatform) || IsIosPlatform(publishPlatform))
        {
            return ReadProperty(projectFile, "ApplicationVersion", "FileVersion");
        }

        if (IsMacOsPlatform(publishPlatform))
        {
            return ReadProperty(projectFile, "FileVersion", "Version");
        }

        if (IsWindowsPlatform(publishPlatform))
        {
            return null;
        }

        return ReadProperty(projectFile, "FileVersion", "Version");
    }

    private static bool SupportsInternalVersion(string publishPlatform)
    {
        return IsAndroidPlatform(publishPlatform)
            || IsIosPlatform(publishPlatform)
            || IsMacOsPlatform(publishPlatform);
    }

    private static string? ResolveExecutable(string name, IReadOnlyList<string> candidates)
    {
        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathVariable))
        {
            foreach (var path in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = Path.Combine(path, name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private string? ResolveApkSignerPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new List<string>
        {
            Path.Combine(home, "Library/Android/sdk/build-tools"),
            Path.Combine(home, "Android/Sdk/build-tools"),
            "/opt/homebrew/share/android-commandlinetools/build-tools",
            "/usr/local/share/android-commandlinetools/build-tools"
        };

        foreach (var buildToolsRoot in candidates)
        {
            if (!Directory.Exists(buildToolsRoot))
            {
                continue;
            }

            var apksignerPath = Directory
                .EnumerateDirectories(buildToolsRoot)
                .Select(directory => Path.Combine(directory, "apksigner"))
                .Where(File.Exists)
                .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(apksignerPath))
            {
                return apksignerPath;
            }
        }

        return ResolveExecutable("apksigner",
        [
            "/opt/homebrew/bin/apksigner",
            "/usr/local/bin/apksigner"
        ]);
    }

    private async Task EnsureApkHasValidSignatureAsync(string apkPath, CancellationToken cancellationToken)
    {
        var apkSignerPath = ApkSignerPath;
        if (string.IsNullOrWhiteSpace(apkSignerPath))
        {
            return;
        }

        var output = new StringBuilder();
        var exitCode = await RunProcessAsync(
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

    private static void ValidateProjectTargetFramework(FileInfo projectFile, string targetFramework)
    {
        var selectedFramework = targetFramework.Trim();
        if (string.IsNullOrWhiteSpace(selectedFramework))
        {
            throw new InvalidOperationException("Target framework is required.");
        }

        var singleTarget = ReadProperty(projectFile, "TargetFramework");
        if (!string.IsNullOrWhiteSpace(singleTarget))
        {
            if (!string.Equals(singleTarget.Trim(), selectedFramework, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Target framework `{selectedFramework}` is not declared by {projectFile.Name}. Use `{singleTarget.Trim()}` or switch to a compatible project.");
            }

            return;
        }

        var multiTarget = ReadProperty(projectFile, "TargetFrameworks");
        if (string.IsNullOrWhiteSpace(multiTarget))
        {
            return;
        }

        var frameworks = multiTarget
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (!frameworks.Any(framework => string.Equals(framework, selectedFramework, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Target framework `{selectedFramework}` is not in `{projectFile.Name}` target frameworks: {string.Join(", ", frameworks)}.");
        }
    }

    private static void ValidateRuntimeForPlatform(PublishConfiguration configuration)
    {
        var runtimeIdentifier = configuration.RuntimeIdentifier.Trim();
        if (string.IsNullOrWhiteSpace(runtimeIdentifier))
        {
            throw new InvalidOperationException("Runtime identifier is required.");
        }

        if (IsAndroidPlatform(configuration.PublishPlatform))
        {
            if (!runtimeIdentifier.StartsWith("android-", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Android publishing requires an `android-*` runtime identifier.");
            }

            return;
        }

        if (IsMacOsPlatform(configuration.PublishPlatform))
        {
            var isMacRuntime = runtimeIdentifier.StartsWith("osx-", StringComparison.OrdinalIgnoreCase)
                || runtimeIdentifier.StartsWith("maccatalyst-", StringComparison.OrdinalIgnoreCase);

            if (!isMacRuntime)
            {
                throw new InvalidOperationException("macOS publishing requires an `osx-*` or `maccatalyst-*` runtime identifier.");
            }

            return;
        }

        if (IsWindowsPlatform(configuration.PublishPlatform))
        {
            if (!runtimeIdentifier.StartsWith("win-", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Windows publishing requires a `win-*` runtime identifier.");
            }

            return;
        }

        if (IsIosPlatform(configuration.PublishPlatform))
        {
            if (!runtimeIdentifier.StartsWith("ios-", StringComparison.OrdinalIgnoreCase)
                && !runtimeIdentifier.StartsWith("iossimulator-", StringComparison.OrdinalIgnoreCase))
            {
throw new InvalidOperationException("iOS publishing requires an `ios-*` or `iossimulator-*` runtime identifier.");
        }
    }
    }

    private static string ResolveScreenshotDirectory(string outputDirectory)
    {
        if (!string.IsNullOrWhiteSpace(outputDirectory) && Directory.Exists(outputDirectory))
        {
            return outputDirectory;
        }

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (!string.IsNullOrWhiteSpace(desktop))
        {
            return desktop;
        }

        return Path.GetTempPath();
    }

    private static DirectoryInfo CreateProjectDirectory(string projectDirectory)
    {
        if (string.IsNullOrWhiteSpace(projectDirectory))
        {
            throw new InvalidOperationException("Select a project directory first.");
        }

        var directory = new DirectoryInfo(projectDirectory.Trim());
        if (!directory.Exists)
        {
            throw new InvalidOperationException($"Project directory not found: {directory.FullName}");
        }

        return directory;
    }

    private static FileInfo? FindProjectFile(DirectoryInfo projectDirectory)
    {
        return projectDirectory
            .GetFiles("*.csproj", SearchOption.TopDirectoryOnly)
            .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static string GetDefaultOutputDirectory(DirectoryInfo projectDirectory, string configuration, string targetFramework, string runtimeIdentifier)
    {
        return Path.Combine(projectDirectory.FullName, "bin", configuration.Trim(), targetFramework.Trim(), runtimeIdentifier.Trim());
    }

    private static string? ReadProperty(FileInfo projectFile, params string[] propertyNames)
    {
        try
        {
            foreach (var document in LoadProjectPropertyDocuments(projectFile))
            {
                foreach (var propertyName in propertyNames)
                {
                    var value = document
                        .Descendants()
                        .FirstOrDefault(element => element.Name.LocalName == propertyName && !string.IsNullOrWhiteSpace(element.Value))
                        ?.Value
                        .Trim();

                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static IEnumerable<XDocument> LoadProjectPropertyDocuments(FileInfo projectFile)
    {
        yield return XDocument.Load(projectFile.FullName);

        foreach (var directory in EnumerateDirectories(projectFile.Directory))
        {
            var propsPath = Path.Combine(directory.FullName, "Directory.Build.props");
            if (!File.Exists(propsPath))
            {
                continue;
            }

            XDocument? document = null;
            try
            {
                document = XDocument.Load(propsPath);
            }
            catch
            {
            }

            if (document is not null)
            {
                yield return document;
            }
        }
    }

    private static IEnumerable<DirectoryInfo> EnumerateDirectories(DirectoryInfo? startDirectory)
    {
        for (var directory = startDirectory; directory is not null; directory = directory.Parent)
        {
            yield return directory;
        }
    }

    private static string? ReadTargetFramework(FileInfo projectFile, string publishPlatform)
    {
        var singleTarget = ReadProperty(projectFile, "TargetFramework");
        if (!string.IsNullOrWhiteSpace(singleTarget))
        {
            return singleTarget;
        }

        var multiTarget = ReadProperty(projectFile, "TargetFrameworks");
        if (string.IsNullOrWhiteSpace(multiTarget))
        {
            return null;
        }

        return multiTarget
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(framework => IsTargetFrameworkForPlatform(framework, publishPlatform))
            ?? multiTarget.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
    }

    private static bool IsTargetFrameworkForPlatform(string framework, string publishPlatform)
    {
        if (IsAndroidPlatform(publishPlatform))
        {
            return framework.Contains("android", StringComparison.OrdinalIgnoreCase);
        }

        if (IsMacOsPlatform(publishPlatform))
        {
            return framework.Contains("maccatalyst", StringComparison.OrdinalIgnoreCase);
        }

        if (IsWindowsPlatform(publishPlatform))
        {
            return framework.Contains("windows", StringComparison.OrdinalIgnoreCase)
                || framework.Equals("net10.0", StringComparison.OrdinalIgnoreCase);
        }

        if (IsIosPlatform(publishPlatform))
        {
            return framework.Contains("ios", StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    private static string? DetectCustomTrimProperty(FileInfo projectFile)
    {
        try
        {
            var projectText = File.ReadAllText(projectFile.FullName);
            var matches = Regex.Matches(projectText, @"\$\((?<name>[A-Za-z0-9_.-]*PublishTrimmed)\)");
            foreach (Match match in matches)
            {
                var propertyName = match.Groups["name"].Value;
                if (!string.IsNullOrWhiteSpace(propertyName) &&
                    !string.Equals(propertyName, "PublishTrimmed", StringComparison.Ordinal))
                {
                    return propertyName;
                }
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private List<string> BuildAndroidCommand(PublishConfiguration configuration, string projectFilePath, string? customTrimProperty)
    {
        var command = new List<string>
        {
            DotnetPath!,
            "publish",
            projectFilePath,
            "-f",
            configuration.TargetFramework.Trim(),
            "-c",
            configuration.Configuration.Trim(),
            "-r",
            configuration.RuntimeIdentifier.Trim(),
            $"-p:SelfContained={ToLowerInvariant(configuration.SelfContained)}",
            $"-p:AndroidLinkMode={configuration.AndroidLinkMode.Trim()}",
            $"-p:RunAOTCompilation={ToLowerInvariant(configuration.RunAotCompilation)}",
            $"-p:AndroidEnableProfiledAot={ToLowerInvariant(configuration.EnableProfiledAot)}"
        };

        if (string.IsNullOrWhiteSpace(customTrimProperty))
        {
            command.Add($"-p:PublishTrimmed={ToLowerInvariant(configuration.PublishTrimmed)}");
        }
        else
        {
            command.Add($"-p:{customTrimProperty}={ToLowerInvariant(configuration.PublishTrimmed)}");
        }

        if (!string.Equals(configuration.AndroidLinkMode.Trim(), "None", StringComparison.Ordinal))
        {
            command.Add($"-p:AndroidLinkTool={configuration.AndroidLinkTool.Trim()}");
            command.Add($"-p:AndroidCreateProguardMappingFile={ToLowerInvariant(configuration.CreateMappingFile)}");
        }

        if (!string.Equals(configuration.AndroidDexTool.Trim(), "d8", StringComparison.Ordinal))
        {
            command.Add($"-p:AndroidDexTool={configuration.AndroidDexTool.Trim()}");
        }

        if (configuration.EnableMultiDex)
        {
            command.Add("-p:AndroidEnableMultiDex=true");
        }

        if (!configuration.UseAapt2)
        {
            command.Add("-p:AndroidUseAapt2=false");
        }

        if (!configuration.EnableDesugar)
        {
            command.Add("-p:AndroidEnableDesugar=false");
        }

        switch (configuration.SignMode.Trim())
        {
            case "Sign":
                ValidateSigning(configuration);
                command.Add("-p:AndroidKeyStore=true");
                command.Add($"-p:AndroidSigningKeyStore={configuration.KeystorePath.Trim()}");
                command.Add($"-p:AndroidSigningKeyAlias={configuration.KeyAlias.Trim()}");
                command.Add($"-p:AndroidSigningStorePass={configuration.KeystorePassword}");
                command.Add($"-p:AndroidSigningKeyPass={configuration.KeyPassword}");
                break;
            case "Do Not Sign":
                command.Add("-p:AndroidKeyStore=false");
                break;
        }

        return command;
    }

    private List<string> BuildMacOsCommand(PublishConfiguration configuration, string projectFilePath, string? customTrimProperty)
    {
        var targetFramework = configuration.TargetFramework.Trim();
        var runtimeIdentifier = configuration.RuntimeIdentifier.Trim();
        var isMacCatalyst = targetFramework.Contains("maccatalyst", StringComparison.OrdinalIgnoreCase)
            || runtimeIdentifier.Contains("maccatalyst", StringComparison.OrdinalIgnoreCase);
        var isNativeOsxRid = runtimeIdentifier.StartsWith("osx-", StringComparison.OrdinalIgnoreCase);

        var command = new List<string>
        {
            DotnetPath!,
            "publish",
            projectFilePath,
            "-f",
            targetFramework,
            "-c",
            configuration.Configuration.Trim(),
            "-r",
            runtimeIdentifier,
            $"-p:SelfContained={ToLowerInvariant(configuration.SelfContained)}"
        };

        if (string.IsNullOrWhiteSpace(customTrimProperty))
        {
            command.Add($"-p:PublishTrimmed={ToLowerInvariant(configuration.PublishTrimmed)}");
        }
        else
        {
            command.Add($"-p:{customTrimProperty}={ToLowerInvariant(configuration.PublishTrimmed)}");
        }

        if (!isMacCatalyst && configuration.PublishReadyToRun)
        {
            command.Add("-p:PublishReadyToRun=true");
        }

        if (!isMacCatalyst && configuration.PublishSingleFile)
        {
            command.Add("-p:PublishSingleFile=true");
        }

        if (!isMacCatalyst && configuration.UseAppHost)
        {
            command.Add("-p:UseAppHost=true");
        }

        if (!isMacCatalyst && isNativeOsxRid && configuration.PublishAot)
        {
            command.Add("-p:PublishAot=true");
        }

        return command;
    }

    private List<string> BuildWindowsCommand(PublishConfiguration configuration, string projectFilePath, string? customTrimProperty)
    {
        var command = new List<string>
        {
            DotnetPath!,
            "publish",
            projectFilePath,
            "-f",
            configuration.TargetFramework.Trim(),
            "-c",
            configuration.Configuration.Trim(),
            "-r",
            configuration.RuntimeIdentifier.Trim(),
            $"-p:SelfContained={ToLowerInvariant(configuration.SelfContained)}",
            $"-p:UseAppHost={ToLowerInvariant(configuration.CreateWindowsExecutable)}"
        };

        if (string.IsNullOrWhiteSpace(customTrimProperty))
        {
            command.Add($"-p:PublishTrimmed={ToLowerInvariant(configuration.PublishTrimmed)}");
        }
        else
        {
            command.Add($"-p:{customTrimProperty}={ToLowerInvariant(configuration.PublishTrimmed)}");
        }

        if (configuration.PublishReadyToRun)
        {
            command.Add("-p:PublishReadyToRun=true");
        }

        if (configuration.PublishSingleFile)
        {
            command.Add("-p:PublishSingleFile=true");
        }

        return command;
    }

    private List<string> BuildIosCommand(PublishConfiguration configuration, string projectFilePath, string? customTrimProperty)
    {
        var runtimeIdentifier = configuration.RuntimeIdentifier.Trim();
        var isSimulatorRuntime = runtimeIdentifier.StartsWith("iossimulator-", StringComparison.OrdinalIgnoreCase);
        var command = new List<string>
        {
            DotnetPath!,
            isSimulatorRuntime ? "build" : "publish",
            projectFilePath,
            "-f",
            configuration.TargetFramework.Trim(),
            "-c",
            configuration.Configuration.Trim(),
            "-r",
            runtimeIdentifier,
            $"-p:SelfContained={ToLowerInvariant(configuration.SelfContained)}",
            "-p:UseAppHost=false",
            "-p:PublishTrimmed=true"
        };

        if (!string.IsNullOrWhiteSpace(customTrimProperty))
        {
            command.Add($"-p:{customTrimProperty}=true");
        }

        if (configuration.ArchiveOnBuild && !isSimulatorRuntime)
        {
            command.Add("-p:ArchiveOnBuild=true");
        }

        if (configuration.BuildIpa && !isSimulatorRuntime)
        {
            command.Add("-p:BuildIpa=true");
        }

        return command;
    }

    private IReadOnlyList<string> BuildVerifiedApkCommand(PublishConfiguration configuration, string projectFilePath, string outputDirectory, string? customTrimProperty)
    {
        var command = new List<string>
        {
            DotnetPath!,
            "publish",
            projectFilePath,
            "-f",
            configuration.TargetFramework.Trim(),
            "-c",
            configuration.Configuration.Trim(),
            "-r",
            configuration.RuntimeIdentifier.Trim(),
            "-p:SelfContained=true",
            "-p:AndroidLinkMode=None",
            "-p:RunAOTCompilation=false",
            "-p:AndroidEnableProfiledAot=false",
            "-p:AndroidPackageFormats=apk",
            "-o",
            outputDirectory
        };

        if (string.IsNullOrWhiteSpace(customTrimProperty))
        {
            command.Insert(9, "-p:PublishTrimmed=false");
        }
        else
        {
            command.Insert(9, $"-p:{customTrimProperty}=false");
        }

        return command;
    }

    private static void ValidateSigning(PublishConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration.KeystorePath))
        {
            throw new InvalidOperationException("Signing mode is Sign, but no keystore file was selected.");
        }

        if (string.IsNullOrWhiteSpace(configuration.KeyAlias))
        {
            throw new InvalidOperationException("Signing mode is Sign, but the key alias is empty.");
        }

        if (string.IsNullOrWhiteSpace(configuration.KeystorePassword))
        {
            throw new InvalidOperationException("Signing mode is Sign, but the store password is empty.");
        }

        if (string.IsNullOrWhiteSpace(configuration.KeyPassword))
        {
            throw new InvalidOperationException("Signing mode is Sign, but the key password is empty.");
        }
    }

    private static IReadOnlyList<string> GetSelectedFormats(PublishConfiguration configuration)
    {
        var formats = new List<string>();

        if (configuration.IncludeAab)
        {
            formats.Add("aab");
        }

        if (configuration.IncludeApk)
        {
            formats.Add("apk");
        }

        return formats;
    }

    private static string MaskCommand(IEnumerable<string> arguments)
    {
        var masked = arguments.Select(argument =>
        {
            if (argument.StartsWith("-p:AndroidSigningStorePass=", StringComparison.Ordinal))
            {
                return "-p:AndroidSigningStorePass=********";
            }

            if (argument.StartsWith("-p:AndroidSigningKeyPass=", StringComparison.Ordinal))
            {
                return "-p:AndroidSigningKeyPass=********";
            }

            return argument;
        });

        return string.Join(" ", masked.Select(QuoteArgument));
    }

    private static string QuoteArgument(string argument)
    {
        if (string.IsNullOrEmpty(argument))
        {
            return "\"\"";
        }

        if (argument.All(character => !char.IsWhiteSpace(character) && character != '"' && character != '\''))
        {
            return argument;
        }

        return "\"" + argument.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private static string ToLowerInvariant(bool value)
    {
        return value ? "true" : "false";
    }

    private static bool IsAndroidPlatform(string publishPlatform)
    {
        return string.Equals(publishPlatform, AndroidPlatform, StringComparison.Ordinal);
    }

    private static bool IsMacOsPlatform(string publishPlatform)
    {
        return string.Equals(publishPlatform, MacOsPlatform, StringComparison.Ordinal);
    }

    private static bool IsWindowsPlatform(string publishPlatform)
    {
        return string.Equals(publishPlatform, WindowsPlatform, StringComparison.Ordinal);
    }

    private static bool IsIosPlatform(string publishPlatform)
    {
        return string.Equals(publishPlatform, IosPlatform, StringComparison.Ordinal);
    }

    private static string ExtractSimulatorId(string simulator)
    {
        var separatorIndex = simulator.LastIndexOf('|');
        if (separatorIndex < 0)
        {
            return simulator.Trim();
        }

        return simulator[(separatorIndex + 1)..].Trim();
    }

    private static async Task EnsureIosSimulatorBootedAsync(string xcrunPath, string simulatorId, CancellationToken cancellationToken)
    {
        _ = Process.Start(new ProcessStartInfo("open", "-a Simulator")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        });

        var bootOutput = new StringBuilder();
        var bootExitCode = await RunProcessAsync([xcrunPath, "simctl", "boot", simulatorId], text => bootOutput.Append(text), cancellationToken);
        if (bootExitCode != 0 && !bootOutput.ToString().Contains("Booted", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"iOS simulator boot command returned {bootExitCode}.{Environment.NewLine}{bootOutput}");
        }

        var statusOutput = new StringBuilder();
        var statusExitCode = await RunProcessAsync([xcrunPath, "simctl", "bootstatus", simulatorId, "-b"], text => statusOutput.Append(text), cancellationToken);
        if (statusExitCode != 0)
        {
            throw new InvalidOperationException($"iOS simulator boot status check failed ({statusExitCode}).{Environment.NewLine}{statusOutput}");
        }
    }

    private static bool IsDirectoryInUse(string path)
    {
        try
        {
            var directory = new DirectoryInfo(path);
            if (!directory.Exists)
            {
                return false;
            }

            foreach (var file in directory.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                try
                {
                    using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                }
                catch (IOException)
                {
                    return true;
                }
                catch (UnauthorizedAccessException)
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return true;
        }
    }

    private static async Task<string?> FindProcessesLockingDirectoryAsync(string path)
    {
        try
        {
            if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
            {
                return null;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "lsof",
                Arguments = $"+D \"{path}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync(CancellationToken.None);

            if (string.IsNullOrWhiteSpace(output))
            {
                return null;
            }

            var processes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var line in lines.Skip(1))
            {
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    var processName = parts[0];
                    var pid = parts[1];
                    if (!string.IsNullOrWhiteSpace(processName) && !string.IsNullOrWhiteSpace(pid))
                    {
                        processes.Add($"{processName} (PID {pid})");
                    }
                }
            }

            return processes.Count > 0 ? string.Join(", ", processes) : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<bool> TryDeleteDirectorySafelyAsync(string path, Action<string> writeOutput)
    {
        if (!Directory.Exists(path))
        {
            return true;
        }

        if (IsCurrentProcessInsideDirectory(path))
        {
            writeOutput($"Skipping delete for active runtime folder {path}{Environment.NewLine}");
            return false;
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                writeOutput($"Deleted {path}{Environment.NewLine}");
                return true;
            }
            catch (IOException ex)
            {
                if (attempt == 0)
                {
                    writeOutput($"File lock detected on {path}, retrying...{Environment.NewLine}");
                    await Task.Delay(500);
                    continue;
                }

                var lockingProcesses = await FindProcessesLockingDirectoryAsync(path);
                if (!string.IsNullOrWhiteSpace(lockingProcesses))
                {
                    writeOutput($"Warning: Could not delete {path}{Environment.NewLine}");
                    writeOutput($" Files are in use by: {lockingProcesses}{Environment.NewLine}");
                    writeOutput($" Close the process and retry, or proceed anyway.{Environment.NewLine}");
                }
                else
                {
                    writeOutput($"Warning: Could not delete {path} — file is locked ({ex.Message}){Environment.NewLine}");
                }

                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                var lockingProcesses = await FindProcessesLockingDirectoryAsync(path);
                if (!string.IsNullOrWhiteSpace(lockingProcesses))
                {
                    writeOutput($"Warning: Could not delete {path}{Environment.NewLine}");
                    writeOutput($" Files are in use by: {lockingProcesses}{Environment.NewLine}");
                    writeOutput($" Close the process and retry, or proceed anyway.{Environment.NewLine}");
                }
                else
                {
                    writeOutput($"Warning: Could not delete {path} — access denied ({ex.Message}){Environment.NewLine}");
                }

                return false;
            }
        }

        return false;
    }

    private static async Task CleanStalePlatformDirectoriesAsync(DirectoryInfo projectDirectory, string configuration, string targetFramework, string runtimeIdentifier, string publishPlatform, Action<string> writeOutput)
    {
        var objConfigPath = Path.Combine(projectDirectory.FullName, "obj", configuration.Trim());
        if (!Directory.Exists(objConfigPath))
        {
            return;
        }

        var currentPlatformRidPrefixes = GetCurrentPlatformRidPrefixes(publishPlatform, targetFramework, runtimeIdentifier);
        var currentTfm = targetFramework.Trim();

        foreach (var tfmDirectory in Directory.GetDirectories(objConfigPath))
        {
            var tfmName = Path.GetFileName(tfmDirectory);

            if (string.Equals(tfmName, currentTfm, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var ridDirectory in Directory.GetDirectories(tfmDirectory))
            {
                var ridName = Path.GetFileName(ridDirectory);
                if (IsRidForCurrentPlatform(ridName, currentPlatformRidPrefixes))
                {
                    continue;
                }

                writeOutput($"Cleaning stale platform directory: {ridDirectory}{Environment.NewLine}");
                await TryDeleteDirectorySafelyAsync(ridDirectory, writeOutput);
            }

            if (!Directory.EnumerateFileSystemEntries(tfmDirectory).Any())
            {
                await TryDeleteDirectorySafelyAsync(tfmDirectory, writeOutput);
            }
        }
    }

    private static HashSet<string> GetCurrentPlatformRidPrefixes(string publishPlatform, string targetFramework, string runtimeIdentifier)
    {
        var prefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (IsAndroidPlatform(publishPlatform))
        {
            prefixes.Add("android-");
        }
        else if (IsMacOsPlatform(publishPlatform))
        {
            prefixes.Add("osx-");
            if (targetFramework.Contains("maccatalyst", StringComparison.OrdinalIgnoreCase)
                || runtimeIdentifier.Contains("maccatalyst", StringComparison.OrdinalIgnoreCase))
            {
                prefixes.Add("maccatalyst-");
            }
        }
        else if (IsWindowsPlatform(publishPlatform))
        {
            prefixes.Add("win-");
        }
        else if (IsIosPlatform(publishPlatform))
        {
            prefixes.Add("ios-");
            prefixes.Add("iossimulator-");
        }

        return prefixes;
    }

    private static bool IsRidForCurrentPlatform(string runtimeIdentifier, HashSet<string> currentPlatformPrefixes)
    {
        foreach (var prefix in currentPlatformPrefixes)
        {
            if (runtimeIdentifier.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task DeleteDirectoryIfPresentSafelyAsync(string path, Action<string> writeOutput)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        await TryDeleteDirectorySafelyAsync(path, writeOutput);
    }

    private static bool IsCurrentProcessInsideDirectory(string directoryPath)
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            return false;
        }

        var fullDirectoryPath = Path.GetFullPath(directoryPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        var fullProcessPath = Path.GetFullPath(processPath);
        return fullProcessPath.StartsWith(fullDirectoryPath, StringComparison.OrdinalIgnoreCase);
    }

    private static string? EnsureMacAppBundle(
        string outputDirectory,
        string projectFilePath,
        string packageId,
        Action<string> writeOutput)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return null;
        }

        if (!Directory.Exists(outputDirectory))
        {
            return null;
        }

        var normalizedOutput = Path.GetFullPath(outputDirectory);
        var appMarker = $"{Path.DirectorySeparatorChar}.app{Path.DirectorySeparatorChar}Contents{Path.DirectorySeparatorChar}MacOS";
        var macOsIndex = normalizedOutput.IndexOf(appMarker, StringComparison.OrdinalIgnoreCase);
        if (macOsIndex >= 0)
        {
            return normalizedOutput[..(macOsIndex + 4)];
        }

        var existingBundle = FindNewestAppBundle(outputDirectory);
        if (existingBundle is not null)
        {
            return existingBundle.FullName;
        }

        var projectName = Path.GetFileNameWithoutExtension(projectFilePath);
        var bundlePath = Path.Combine(outputDirectory, $"{projectName}.app");
        var contentsPath = Path.Combine(bundlePath, "Contents");
        var macOsPath = Path.Combine(contentsPath, "MacOS");
        var resourcesPath = Path.Combine(contentsPath, "Resources");
        Directory.CreateDirectory(macOsPath);
        Directory.CreateDirectory(resourcesPath);
        CopyMacAppIcon(resourcesPath);

        foreach (var sourceFilePath in Directory.EnumerateFiles(outputDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            var sourceFileName = Path.GetFileName(sourceFilePath);
            var destinationFilePath = Path.Combine(macOsPath, sourceFileName);
            if (string.Equals(sourceFilePath, destinationFilePath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            File.Copy(sourceFilePath, destinationFilePath, overwrite: true);
        }

        var executableName = DetermineExecutableName(macOsPath, projectName);
        if (!string.IsNullOrWhiteSpace(executableName))
        {
            var executablePath = Path.Combine(macOsPath, executableName);
            TryMarkExecutable(executablePath);
        }

        var bundleIdentifier = BuildBundleIdentifier(packageId, projectName);
        var plistPath = Path.Combine(contentsPath, "Info.plist");
        File.WriteAllText(
            plistPath,
            BuildInfoPlist(projectName, executableName ?? projectName, bundleIdentifier));

        writeOutput($"Created .app bundle at {bundlePath}{Environment.NewLine}");
        return bundlePath;
    }

    private static async Task<string?> EnsureMacAppBundleAsync(
        string outputDirectory,
        string projectFilePath,
        string packageId,
        Action<string> writeOutput,
        CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            if (!OperatingSystem.IsMacOS())
            {
                return (string?)null;
            }

            if (!Directory.Exists(outputDirectory))
            {
                return null;
            }

            var normalizedOutput = Path.GetFullPath(outputDirectory);
            var appMarker = $"{Path.DirectorySeparatorChar}.app{Path.DirectorySeparatorChar}Contents{Path.DirectorySeparatorChar}MacOS";
            var macOsIndex = normalizedOutput.IndexOf(appMarker, StringComparison.OrdinalIgnoreCase);
            if (macOsIndex >= 0)
            {
                return normalizedOutput[..(macOsIndex + 4)];
            }

            var existingBundle = FindNewestAppBundle(outputDirectory);
            if (existingBundle is not null)
            {
                return existingBundle.FullName;
            }

            var projectName = Path.GetFileNameWithoutExtension(projectFilePath);
            var bundlePath = Path.Combine(outputDirectory, $"{projectName}.app");
            var contentsPath = Path.Combine(bundlePath, "Contents");
            var macOsPath = Path.Combine(contentsPath, "MacOS");
            var resourcesPath = Path.Combine(contentsPath, "Resources");
            Directory.CreateDirectory(macOsPath);
            Directory.CreateDirectory(resourcesPath);
            CopyMacAppIcon(resourcesPath);

            foreach (var sourceFilePath in Directory.EnumerateFiles(outputDirectory, "*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sourceFileName = Path.GetFileName(sourceFilePath);
                var destinationFilePath = Path.Combine(macOsPath, sourceFileName);
                if (string.Equals(sourceFilePath, destinationFilePath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                File.Copy(sourceFilePath, destinationFilePath, overwrite: true);
            }

            var executableName = DetermineExecutableName(macOsPath, projectName);
            if (!string.IsNullOrWhiteSpace(executableName))
            {
                var executablePath = Path.Combine(macOsPath, executableName);
                TryMarkExecutable(executablePath);
            }

            var bundleIdentifier = BuildBundleIdentifier(packageId, projectName);
            var plistPath = Path.Combine(contentsPath, "Info.plist");
            File.WriteAllText(
                plistPath,
                BuildInfoPlist(projectName, executableName ?? projectName, bundleIdentifier));

        writeOutput($"Created .app bundle at {bundlePath}{Environment.NewLine}");
        return bundlePath;
        }, cancellationToken);
    }

    private static void CopyMacAppIcon(string resourcesPath)
    {
        var iconPath = Path.Combine(resourcesPath, $"{MacAppIconFileName}.icns");
        using var source = AssetLoader.Open(new Uri(MacAppIconAssetPath));
        using var destination = File.Create(iconPath);
        source.CopyTo(destination);
    }

    private static DirectoryInfo? FindNewestAppBundle(string outputDirectory)
    {
        var directory = new DirectoryInfo(outputDirectory);
        if (!directory.Exists)
        {
            return null;
        }

        return directory
            .EnumerateDirectories("*.app", SearchOption.AllDirectories)
            .OrderByDescending(bundle => bundle.LastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static string? DetermineExecutableName(string macOsPath, string projectName)
    {
        var preferredPath = Path.Combine(macOsPath, projectName);
        if (File.Exists(preferredPath))
        {
            return projectName;
        }

        var candidate = Directory
            .EnumerateFiles(macOsPath, "*", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .Where(file => !file.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Where(file => !file.Name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
            .Where(file => !file.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .Where(file => !file.Name.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(file => file.Length)
            .FirstOrDefault();

        return candidate?.Name;
    }

    private static void TryMarkExecutable(string executablePath)
    {
        try
        {
            if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
            {
                return;
            }

            if (!File.Exists(executablePath))
            {
                return;
            }

            File.SetUnixFileMode(
                executablePath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
        catch
        {
            // Best effort.
        }
    }

    private static string BuildBundleIdentifier(string packageId, string projectName)
    {
        if (!string.IsNullOrWhiteSpace(packageId))
        {
            var normalizedPackageId = packageId.Trim().Replace('_', '.');
            if (normalizedPackageId.Contains('.', StringComparison.Ordinal))
            {
                return normalizedPackageId;
            }
        }

        var normalizedName = Regex.Replace(projectName.ToLowerInvariant(), @"[^a-z0-9]+", string.Empty);
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            normalizedName = "dotnetapppublisher";
        }

        return $"com.{normalizedName}.app";
    }

    private static string? GetProjectIdentifier(FileInfo projectFile, string projectName, string publishPlatform)
    {
        var identifier = ReadProperty(
            projectFile,
            "PackageId",
            "ApplicationId",
            "PackageName",
            "ApplicationIdentifier",
            "CFBundleIdentifier");

        if (!string.IsNullOrWhiteSpace(identifier))
        {
            return identifier.Trim();
        }

        if (IsWindowsPlatform(publishPlatform))
        {
            return null;
        }

        return BuildBundleIdentifier(string.Empty, projectName);
    }

    private static string BuildInfoPlist(string projectName, string executableName, string bundleIdentifier)
    {
        return $$"""
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDevelopmentRegion</key>
  <string>en</string>
  <key>CFBundleExecutable</key>
  <string>{{executableName}}</string>
  <key>CFBundleIdentifier</key>
  <string>{{bundleIdentifier}}</string>
  <key>CFBundleInfoDictionaryVersion</key>
  <string>6.0</string>
  <key>CFBundleName</key>
  <string>{{projectName}}</string>
  <key>CFBundleDisplayName</key>
  <string>{{projectName}}</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleShortVersionString</key>
  <string>0.1.0</string>
  <key>CFBundleVersion</key>
  <string>0.1.0</string>
  <key>CFBundleIconFile</key>
  <string>{{MacAppIconFileName}}</string>
  <key>LSMinimumSystemVersion</key>
  <string>12.0</string>
  <key>NSHighResolutionCapable</key>
  <true/>
</dict>
</plist>
""";
    }

    private static FileInfo? FindBestApk(string outputDirectory, string? projectName = null, string? packageId = null)
    {
        var directory = new DirectoryInfo(outputDirectory);
        if (!directory.Exists)
        {
            return null;
        }

        return directory
            .EnumerateFiles("*.apk", SearchOption.AllDirectories)
            .OrderByDescending(file => GetApkScore(file, projectName, packageId))
            .ThenByDescending(file => file.LastWriteTimeUtc)
            .ThenBy(file => file.FullName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static int GetApkScore(FileInfo apk, string? projectName, string? packageId)
    {
        var score = 0;
        var fileName = apk.Name;

        if (fileName.EndsWith("-Signed.apk", StringComparison.OrdinalIgnoreCase))
        {
            score += 100;
        }

        if (!string.IsNullOrWhiteSpace(projectName) &&
            fileName.Contains(projectName.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            score += 50;
        }

        if (!string.IsNullOrWhiteSpace(packageId) &&
            fileName.Contains(packageId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            score += 40;
        }

        if (fileName.EndsWith("-debug.apk", StringComparison.OrdinalIgnoreCase))
        {
            score -= 10;
        }

        return score;
    }

    private static async Task<int> RunProcessAsync(IReadOnlyList<string> arguments, Action<string> writeOutput, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = arguments[0],
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments.Skip(1))
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var standardOutputTask = PumpReaderAsync(process.StandardOutput, writeOutput, cancellationToken);
        var standardErrorTask = PumpReaderAsync(process.StandardError, writeOutput, cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            writeOutput(Environment.NewLine + "Cancelling running process..." + Environment.NewLine);
            KillProcessTree(process);
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(standardOutputTask, standardErrorTask);
            throw;
        }

        await Task.WhenAll(standardOutputTask, standardErrorTask);
        return process.ExitCode;
    }

    private static async Task PumpReaderAsync(StreamReader reader, Action<string> writeOutput, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            writeOutput(line + Environment.NewLine);
        }
    }

    private static void PlayCompletionSound(bool success)
    {
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                var soundName = success ? "Glass" : "Basso";
                Process.Start(new ProcessStartInfo("afplay", $"/System/Library/Sounds/{soundName}.aiff")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                return;
            }

            if (OperatingSystem.IsWindows())
            {
                Console.Beep(success ? 880 : 220, 180);
                return;
            }

            Console.Write("\a");
        }
        catch
        {
            // Best-effort only.
        }
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (process.HasExited)
            {
                return;
            }

            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best effort.
        }
    }
}
