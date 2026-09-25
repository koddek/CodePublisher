using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using DotNetAppPublisher.Features.Deployment.Android;
using DotNetAppPublisher.Features.Deployment.Ios;
using DotNetAppPublisher.Features.Publishing.Artifacts;
using DotNetAppPublisher.Features.Publishing.Capture;
using DotNetAppPublisher.Features.Publishing.Build;
using DotNetAppPublisher.Features.Publishing.Configure;
using DotNetAppPublisher.Features.Publishing.Inspect;
using DotNetAppPublisher.Features.Publishing.Process;
using DotNetAppPublisher.Models;

namespace DotNetAppPublisher.Services;

public sealed class PublisherService
{
    public const string AndroidPlatform = PublishPlatforms.Android;
    public const string MacOsPlatform = PublishPlatforms.MacOs;
    public const string WindowsPlatform = PublishPlatforms.Windows;
    public const string IosPlatform = PublishPlatforms.Ios;
    public const string LinuxPlatform = PublishPlatforms.Linux;

    private const string MacAppIconFileName = "dotnet-app-publisher";
    private const string MacAppIconAssetPath = "avares://DotNetAppPublisher/Assets/dotnet-app-publisher.icns";

    private static readonly string[] DotnetCandidates =
    [
        "/usr/local/share/dotnet/dotnet",
        "/opt/homebrew/bin/dotnet",
        "/usr/local/bin/dotnet"
    ];

    private readonly AndroidDeploymentService _androidDeploymentService;
    private readonly IosSimulatorService _iosSimulatorService;

    public PublisherService()
    {
        DotnetPath = ResolveExecutable("dotnet", DotnetCandidates);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var androidHome = GetAndroidHome();
        var androidCandidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(androidHome))
        {
            androidCandidates.Add(Path.Combine(androidHome, "platform-tools/adb"));
            androidCandidates.Add(Path.Combine(androidHome, "emulator/emulator"));
        }

        AdbPath = ResolveExecutable("adb",
        [
            Path.Combine(home, "Library/Android/sdk/platform-tools/adb"),
            Path.Combine(home, "Android/Sdk/platform-tools/adb"),
            "/opt/homebrew/bin/adb",
            "/usr/local/bin/adb",
            .. androidCandidates.Where(p => p.EndsWith("adb", StringComparison.OrdinalIgnoreCase))
        ]);

        EmulatorPath = ResolveExecutable("emulator",
        [
            Path.Combine(home, "Library/Android/sdk/emulator/emulator"),
            Path.Combine(home, "Android/Sdk/emulator/emulator"),
            "/opt/homebrew/bin/emulator",
            "/usr/local/bin/emulator",
            .. androidCandidates.Where(p => p.EndsWith("emulator", StringComparison.OrdinalIgnoreCase))
        ]);

        _androidDeploymentService = new AndroidDeploymentService(
            AdbPath,
            EmulatorPath,
            () => ApkSignerPath,
            FindBestApk);
        _iosSimulatorService = new IosSimulatorService(
            ResolveExecutable("xcrun", ["/usr/bin/xcrun"]),
            FindNewestAppBundle);
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
        return LoadProjectMetadataAsync(projectDirectory, configuration, targetFramework, runtimeIdentifier, publishPlatform)
            .GetAwaiter()
            .GetResult();
    }

    public async Task<ProjectMetadata> LoadProjectMetadataAsync(string projectDirectory, string configuration, string targetFramework, string runtimeIdentifier, string publishPlatform)
    {
        var projectDirectoryPath = ProjectInspector.CreateProjectDirectory(projectDirectory);
        var projectFile = ProjectInspector.FindProjectFile(projectDirectoryPath)
            ?? throw new InvalidOperationException($"No .csproj found in {projectDirectoryPath}.");
        var projectName = Path.GetFileNameWithoutExtension(projectFile.Name);
        var detectedTargetFramework = ProjectInspector.ReadTargetFramework(projectFile, publishPlatform);
        var effectiveTargetFramework = detectedTargetFramework ?? targetFramework;
        var outputLayout = await ResolveProjectOutputLayoutAsync(
            projectFile,
            configuration,
            effectiveTargetFramework,
            runtimeIdentifier,
            CancellationToken.None);

        return new ProjectMetadata(
            projectFile.FullName,
            outputLayout.DefaultOutputDirectory,
            GetProjectIdentifier(projectFile, projectName, publishPlatform),
            detectedTargetFramework,
            ProjectInspector.ReadVersion(projectFile, publishPlatform, isDisplay: true),
            ProjectInspector.ReadVersion(projectFile, publishPlatform, isDisplay: false),
            ProjectInspector.SupportsInternalVersion(publishPlatform),
            outputLayout);
    }

    public PublishCommandBundle BuildPublishCommandBundle(PublishConfiguration configuration)
    {
        var projectDirectory = ProjectInspector.CreateProjectDirectory(configuration.ProjectDirectory);
        var projectFile = ProjectInspector.FindProjectFile(projectDirectory)
            ?? throw new InvalidOperationException($"No .csproj found in {projectDirectory}.");

        ProjectInspector.ValidateProjectTargetFramework(projectFile, configuration.TargetFramework);

        ProjectInspector.ValidateRuntimeForPlatform(configuration);

        var dotnetPath = DotnetPath
            ?? throw new InvalidOperationException("`dotnet` was not found. Install the .NET SDK or add dotnet to PATH.");

        var outputDirectory = string.IsNullOrWhiteSpace(configuration.OutputDirectory)
            ? ProjectInspector.GetDefaultOutputDirectory(projectDirectory, configuration.Configuration, configuration.TargetFramework, configuration.RuntimeIdentifier)
            : configuration.OutputDirectory.Trim();

        var customTrimProperty = ProjectInspector.DetectCustomTrimProperty(projectFile);
        var command = PublishCommandBuilder.Build(
            dotnetPath,
            configuration,
            projectFile.FullName,
            outputDirectory,
            customTrimProperty);
        var baseline = PublishCommandBuilder.BuildBaseline(
            dotnetPath,
            configuration,
            projectFile.FullName,
            outputDirectory,
            customTrimProperty);

        return new PublishCommandBundle(
            command,
            PublishCommandBuilder.Mask(command),
            PublishCommandBuilder.Mask(baseline),
            projectFile.FullName,
            outputDirectory);
    }

    public async Task<bool> PublishAsync(PublishConfiguration configuration, Action<string> writeOutput, CancellationToken cancellationToken)
    {
        return await PublishAsync(configuration, writeOutput, cancellationToken, confirmAsync: null);
    }

    public async Task<bool> PublishAsync(
        PublishConfiguration configuration,
        Action<string> writeOutput,
        CancellationToken cancellationToken,
        Func<string, Task<bool>>? confirmAsync)
    {
        var bundle = BuildPublishCommandBundle(configuration);
        var projectDirectory = ProjectInspector.CreateProjectDirectory(configuration.ProjectDirectory);
        var outputDirectory = new DirectoryInfo(bundle.OutputDirectory);

        writeOutput(Environment.NewLine + "=== Publish started ===" + Environment.NewLine);
        writeOutput(bundle.PreviewText + Environment.NewLine + Environment.NewLine);
        if (!string.IsNullOrWhiteSpace(bundle.BaselinePreviewText))
        {
            writeOutput("Known-good baseline command:" + Environment.NewLine);
            writeOutput(bundle.BaselinePreviewText + Environment.NewLine + Environment.NewLine);
        }

        await PrepareOutputFoldersAsync(configuration, projectDirectory, outputDirectory, writeOutput);

        var (exitCode, publishOutput) = await RunPublishAsync(bundle, configuration, writeOutput, cancellationToken);

        // Credentials only when absolutely needed: a missing mobile workload fails with
        // NETSDK1147 / "workloads must be installed". Ask, elevate once, retry once.
        if (exitCode != 0
            && RequiresMobileWorkload(configuration.TargetFramework)
            && IsMissingWorkloadError(publishOutput.ToString()))
        {
            var restored = await TryRestoreWorkloadsAfterFailureAsync(bundle, writeOutput, confirmAsync, cancellationToken);
            if (restored)
            {
                writeOutput(Environment.NewLine + "--- Retrying publish after workload restore ---" + Environment.NewLine);
                (exitCode, publishOutput) = await RunPublishAsync(bundle, configuration, writeOutput, cancellationToken);
            }
        }

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

        writeOutput(Environment.NewLine + $"=== Publish failed (exit code {exitCode}) ===" + Environment.NewLine);
        PlayCompletionSound(success: false);
        return false;
    }

    private async Task PrepareOutputFoldersAsync(
        PublishConfiguration configuration,
        DirectoryInfo projectDirectory,
        DirectoryInfo outputDirectory,
        Action<string> writeOutput)
    {
        if (outputDirectory.Exists)
        {
            writeOutput($"Clearing output folder {outputDirectory.FullName}{Environment.NewLine}");
            await TryDeleteDirectorySafelyAsync(outputDirectory.FullName, writeOutput);
        }

        var outputLayout = configuration.OutputLayout;
        var usesCentralizedArtifacts = outputLayout?.UsesCentralizedArtifacts == true;

        if (configuration.DeleteObj && !usesCentralizedArtifacts)
        {
            await CleanStalePlatformDirectoriesAsync(projectDirectory, configuration.Configuration, configuration.TargetFramework, configuration.RuntimeIdentifier, configuration.PublishPlatform, writeOutput);
        }

        if (configuration.DeleteBin)
        {
            var buildOutputDirectory = usesCentralizedArtifacts
                ? outputLayout!.BuildOutputDirectory
                : Path.Combine(projectDirectory.FullName, "bin");

            if (!string.IsNullOrWhiteSpace(buildOutputDirectory))
            {
                await DeleteDirectoryIfPresentSafelyAsync(buildOutputDirectory, writeOutput);
            }
        }

        if (configuration.DeleteObj)
        {
            var intermediateOutputDirectory = usesCentralizedArtifacts
                ? outputLayout!.IntermediateOutputDirectory
                : Path.Combine(projectDirectory.FullName, "obj");

            if (!string.IsNullOrWhiteSpace(intermediateOutputDirectory))
            {
                await DeleteDirectoryIfPresentSafelyAsync(intermediateOutputDirectory, writeOutput);
            }
        }
    }

    private async Task<(int ExitCode, StringBuilder Output)> RunPublishAsync(
        PublishCommandBundle bundle,
        PublishConfiguration configuration,
        Action<string> writeOutput,
        CancellationToken cancellationToken)
    {
        writeOutput($"--- Running: {bundle.PreviewText} ---{Environment.NewLine}");
        var output = new StringBuilder();
        var exitCode = await ProcessRunner.RunAsync(
            bundle.CommandArguments,
            text =>
            {
                output.Append(text);
                writeOutput(text);
            },
            cancellationToken,
            Path.GetDirectoryName(bundle.ProjectFilePath),
            CreateSigningEnvironment(configuration));
        return (exitCode, output);
    }

    private static IReadOnlyDictionary<string, string>? CreateSigningEnvironment(PublishConfiguration configuration)
    {
        if (!string.Equals(configuration.SignMode.Trim(), "Sign", StringComparison.Ordinal))
        {
            return null;
        }

        return new Dictionary<string, string>
        {
            ["DOTNET_APP_PUBLISHER_KEYSTORE_PASSWORD"] = configuration.KeystorePassword,
            ["DOTNET_APP_PUBLISHER_KEY_PASSWORD"] = configuration.KeyPassword
        };
    }

    private static bool IsMissingWorkloadError(string publishOutput)
    {
        return publishOutput.Contains("NETSDK1147", StringComparison.OrdinalIgnoreCase)
            || publishOutput.Contains("workloads must be installed", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> TryRestoreWorkloadsAfterFailureAsync(
        PublishCommandBundle bundle,
        Action<string> writeOutput,
        Func<string, Task<bool>>? confirmAsync,
        CancellationToken cancellationToken)
    {
        var dotnetRoot = Path.GetDirectoryName(DotnetPath!) ?? string.Empty;

        var canWrite = ElevatedProcessRunner.CanWriteToWorkloadLocation(DotnetPath!);
        var requiresAdmin = !canWrite;
        var question = requiresAdmin
            ? "This project needs mobile .NET workloads that are not installed yet." + Environment.NewLine +
              $"Installing them requires administrator permission to write to {dotnetRoot}.{Environment.NewLine}{Environment.NewLine}" +
              "Continue and enter your admin credentials now?"
            : "This project needs mobile .NET workloads that are not installed yet." + Environment.NewLine +
              "Install them now?";

        if (confirmAsync is not null)
        {
            var confirmed = await confirmAsync(question);
            if (!confirmed)
            {
                writeOutput(
                    $"Workload restore declined by user.{Environment.NewLine}" +
                    $"Run in Terminal: sudo dotnet workload restore \"{bundle.ProjectFilePath}\"{Environment.NewLine}" +
                    $"Then publish again, or use the Known-Good Baseline command shown above (no AOT — no workload needed).{Environment.NewLine}");
                return false;
            }
        }

        void WriteWorkloadOutput(string text) => writeOutput(text);

        int workloadExitCode;
        if (requiresAdmin)
        {
            writeOutput($"--- Restoring workloads (administrator approval required) ---{Environment.NewLine}");
            var env = new Dictionary<string, string>
            {
                ["DOTNET_ROOT"] = dotnetRoot,
                ["DOTNET_MULTILEVEL_LOOKUP"] = "0"
            };
            workloadExitCode = await ElevatedProcessRunner.RunElevatedAsync(
                DotnetPath!,
                ["workload", "restore", bundle.ProjectFilePath],
                Path.GetDirectoryName(bundle.ProjectFilePath),
                env,
                WriteWorkloadOutput,
                cancellationToken);
        }
        else
        {
            writeOutput($"--- Restoring workloads (dotnet workload restore) ---{Environment.NewLine}");
            workloadExitCode = await ProcessRunner.RunAsync(
                [DotnetPath!, "workload", "restore", bundle.ProjectFilePath],
                WriteWorkloadOutput,
                cancellationToken,
                Path.GetDirectoryName(bundle.ProjectFilePath));
        }

        if (workloadExitCode == -128)
        {
            writeOutput($"Workload restore cancelled by user.{Environment.NewLine}");
            return false;
        }

        if (workloadExitCode != 0)
        {
            writeOutput(
                $"Workload restore failed (exit code {workloadExitCode}).{Environment.NewLine}" +
                $"Run in Terminal: sudo dotnet workload restore \"{bundle.ProjectFilePath}\"{Environment.NewLine}" +
                $"Then publish again, or use the Known-Good Baseline command shown above (no AOT — no workload needed).{Environment.NewLine}");
            return false;
        }

        writeOutput($"Workloads ready.{Environment.NewLine}");
        return true;
    }

    public void OpenPublishFolder(string outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
        {
            throw new InvalidOperationException($"Publish folder not found: {outputDirectory}");
        }

        if (OperatingSystem.IsMacOS())
        {
            var openStartInfo = new ProcessStartInfo("open")
            {
                UseShellExecute = true
            };
            openStartInfo.ArgumentList.Add(outputDirectory);
            Process.Start(openStartInfo);
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

        var xdgStartInfo = new ProcessStartInfo("xdg-open")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        xdgStartInfo.ArgumentList.Add(outputDirectory);
        Process.Start(xdgStartInfo);
    }

    public Task<string> InstallLatestApkAsync(
        PublishConfiguration configuration,
        string? deviceSerial,
        CancellationToken cancellationToken) =>
        _androidDeploymentService.InstallLatestApkAsync(configuration, deviceSerial, cancellationToken);

    public Task<string> UninstallAsync(string packageId, string? deviceSerial, CancellationToken cancellationToken) =>
        _androidDeploymentService.UninstallAsync(packageId, deviceSerial, cancellationToken);

    public Task<string> LaunchAsync(string packageId, string? deviceSerial, CancellationToken cancellationToken) =>
        _androidDeploymentService.LaunchAsync(packageId, deviceSerial, cancellationToken);

    public Task<string> PushFileToDownloadsAsync(string localFilePath, string? deviceSerial, CancellationToken cancellationToken) =>
        _androidDeploymentService.PushFileToDownloadsAsync(localFilePath, deviceSerial, cancellationToken);

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

    public async Task<string> CopyElementScreenshotToClipboardAsync(Window window, Control element, CancellationToken cancellationToken)
    {
        return await _screenshotService.CaptureElementToClipboardAsync(window, element, cancellationToken);
    }

    public async Task<string> SaveElementScreenshotToDiskAsync(Window window, Control element, string outputDirectory, CancellationToken cancellationToken)
    {
        return await _screenshotService.CaptureElementToDiskAsync(window, element, outputDirectory, cancellationToken);
    }

    public async Task<string> CopyDetailScreenshotToClipboardAsync(Window window, ScrollViewer detailScrollViewer, CancellationToken cancellationToken)
    {
        return await _screenshotService.CaptureDetailToClipboardAsync(window, detailScrollViewer, cancellationToken);
    }

    public async Task<string> SaveDetailScreenshotToDiskAsync(Window window, ScrollViewer detailScrollViewer, string outputDirectory, CancellationToken cancellationToken)
    {
        return await _screenshotService.CaptureDetailToDiskAsync(window, detailScrollViewer, outputDirectory, cancellationToken);
    }

    public Task<IReadOnlyList<string>> DiscoverEmulatorsAsync(CancellationToken cancellationToken) =>
        _androidDeploymentService.DiscoverEmulatorsAsync(cancellationToken);

    public Task<IReadOnlyList<AndroidDeviceInfo>> DiscoverAndroidDevicesAsync(CancellationToken cancellationToken) =>
        _androidDeploymentService.DiscoverAndroidDevicesAsync(cancellationToken);

    public static string? GetFirstRunningDeviceSerial(IReadOnlyList<AndroidDeviceInfo> devices) =>
        AndroidDeploymentService.GetFirstRunningDeviceSerial(devices);

    public Task<IReadOnlyList<string>> DiscoverIosSimulatorsAsync(CancellationToken cancellationToken) =>
        _iosSimulatorService.DiscoverAsync(cancellationToken);

    public Task<string> LaunchEmulatorAsync(string emulatorName, CancellationToken cancellationToken) =>
        _androidDeploymentService.LaunchEmulatorAsync(emulatorName, cancellationToken);

    public Task<string> LaunchIosSimulatorAsync(string simulator, CancellationToken cancellationToken) =>
        _iosSimulatorService.LaunchAsync(simulator, cancellationToken);

    public Task<string> InstallIosAppAsync(string outputDirectory, string simulator, CancellationToken cancellationToken) =>
        _iosSimulatorService.InstallAppAsync(outputDirectory, simulator, cancellationToken);

    public Task<string> UninstallIosAppAsync(string packageId, string simulator, CancellationToken cancellationToken) =>
        _iosSimulatorService.UninstallAppAsync(packageId, simulator, cancellationToken);

    public Task<string> LaunchIosAppAsync(string packageId, string simulator, CancellationToken cancellationToken) =>
        _iosSimulatorService.LaunchAppAsync(packageId, simulator, cancellationToken);

    public Task<string> PushFileToSimulatorAsync(string localFilePath, string simulator, CancellationToken cancellationToken) =>
        _iosSimulatorService.PushFileAsync(localFilePath, simulator, cancellationToken);

    private static string? ResolveExecutable(string name, IReadOnlyList<string> candidates)
    {
        var executableNames = GetExecutableNames(name);
        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathVariable))
        {
            foreach (var path in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (var executableName in executableNames)
                {
                    var candidate = Path.Combine(path, executableName);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
        }

        foreach (var candidate in candidates)
        {
            foreach (var executableName in GetExecutableNames(Path.GetFileName(candidate)))
            {
                var resolvedCandidate = Path.Combine(
                    Path.GetDirectoryName(candidate) ?? string.Empty,
                    executableName);
                if (File.Exists(resolvedCandidate))
                {
                    return resolvedCandidate;
                }
            }
        }

        return null;
    }

    private static IReadOnlyList<string> GetExecutableNames(string name)
    {
        if (!OperatingSystem.IsWindows() || !string.IsNullOrEmpty(Path.GetExtension(name)))
        {
            return [name];
        }

        return [name + ".exe", name + ".cmd", name];
    }

    private static string GetAndroidHome()
    {
        var androidHome = Environment.GetEnvironmentVariable("ANDROID_HOME");
        return !string.IsNullOrWhiteSpace(androidHome)
            ? androidHome
            : Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT") ?? string.Empty;
    }

    private string? ResolveApkSignerPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var androidHome = GetAndroidHome();
        var candidates = new List<string>
        {
            Path.Combine(home, "Library/Android/sdk/build-tools"),
            Path.Combine(home, "Android/Sdk/build-tools"),
            "/opt/homebrew/share/android-commandlinetools/build-tools",
            "/usr/local/share/android-commandlinetools/build-tools"
        };
        if (!string.IsNullOrWhiteSpace(androidHome))
        {
            candidates.Insert(0, Path.Combine(androidHome, "build-tools"));
        }

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

    private async Task<ProjectOutputLayout> ResolveProjectOutputLayoutAsync(
        FileInfo projectFile,
        string configuration,
        string targetFramework,
        string runtimeIdentifier,
        CancellationToken cancellationToken)
    {
        var dotnetPath = DotnetPath;
        if (dotnetPath is null)
        {
            return ProjectInspector.CreateUnavailableOutputLayout(
                ProjectInspector.GetDefaultOutputDirectory(projectFile.Directory!, configuration, targetFramework, runtimeIdentifier),
                "dotnet was not found, so the project output layout could not be detected.");
        }

        return await ProjectOutputLayoutResolver.ResolveAsync(
            dotnetPath,
            projectFile,
            configuration,
            targetFramework,
            runtimeIdentifier,
            async (arguments, token) =>
            {
                var output = new StringBuilder();
                var exitCode = await ProcessRunner.RunAsync(
                    arguments,
                    text => output.Append(text),
                    token,
                    projectFile.DirectoryName);
                return (exitCode, output.ToString());
            },
            cancellationToken);
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

    private static bool IsLinuxPlatform(string publishPlatform)
    {
        return string.Equals(publishPlatform, LinuxPlatform, StringComparison.Ordinal);
    }

    private static bool RequiresMobileWorkload(string targetFramework)
    {
        return targetFramework.Contains("android", StringComparison.OrdinalIgnoreCase)
            || targetFramework.Contains("ios", StringComparison.OrdinalIgnoreCase)
            || targetFramework.Contains("maccatalyst", StringComparison.OrdinalIgnoreCase);
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
        else if (IsLinuxPlatform(publishPlatform))
        {
            prefixes.Add("linux-");
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
        var fullDirectoryPath = Path.GetFullPath(directoryPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        foreach (var candidate in GetProcessLocationCandidates())
        {
            if (candidate.StartsWith(fullDirectoryPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> GetProcessLocationCandidates()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            yield return Path.GetFullPath(processPath);
        }

        string? currentDirectory = null;
        try
        {
            currentDirectory = Environment.CurrentDirectory;
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        if (!string.IsNullOrWhiteSpace(currentDirectory))
        {
            yield return Path.GetFullPath(currentDirectory);
        }
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
        var identifier = ProjectInspector.ReadProperty(
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

        if (IsWindowsPlatform(publishPlatform) || IsLinuxPlatform(publishPlatform))
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
  <key>CFBundleIconName</key>
  <string>{{MacAppIconFileName}}</string>
  <key>LSMinimumSystemVersion</key>
  <string>12.0</string>
  <key>NSHighResolutionCapable</key>
  <true/>
</dict>
</plist>
""";
    }

    public FileInfo? FindBestApk(string outputDirectory, string? projectName = null, string? packageId = null) =>
        ArtifactLocator.FindBestApk(outputDirectory, projectName, packageId);

    public FileInfo? FindBestAab(string outputDirectory, string? projectName = null, string? packageId = null) =>
        ArtifactLocator.FindBestAab(outputDirectory, projectName, packageId);

    private static void PlayCompletionSound(bool success)
    {
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                var soundName = success ? "Glass" : "Basso";
                var startInfo = new ProcessStartInfo("afplay")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                startInfo.ArgumentList.Add($"/System/Library/Sounds/{soundName}.aiff");
                Process.Start(startInfo);
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
