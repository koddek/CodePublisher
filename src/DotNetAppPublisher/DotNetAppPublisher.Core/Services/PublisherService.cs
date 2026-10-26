using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DotNetAppPublisher.Features.Deployment.Android;
using DotNetAppPublisher.Features.Deployment.CrashLogs;
using DotNetAppPublisher.Features.Deployment.Ios;
using DotNetAppPublisher.Features.Publishing.Artifacts;
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

    private static readonly string[] DotnetCandidates =
    [
        "/usr/local/share/dotnet/dotnet",
        "/opt/homebrew/bin/dotnet",
        "/usr/local/bin/dotnet"
    ];

    private readonly AndroidDeploymentService _androidDeploymentService;
    private readonly IosSimulatorService _iosSimulatorService;
    private readonly IosDeviceService _iosDeviceService;
    private readonly AndroidCrashLogService _androidCrashLogService;
    private readonly IosCrashLogService _iosCrashLogService;
    private readonly DesktopCrashLogService _desktopCrashLogService;
    private readonly IAppIconProvider? _appIconProvider;

    public PublisherService(IAppIconProvider? appIconProvider = null)
    {
        _appIconProvider = appIconProvider;
        DotnetPath = ResolveExecutable("dotnet", DotnetCandidates);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var androidHome = GetAndroidHome();
        var androidCandidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(androidHome))
        {
            androidCandidates.Add(Path.Combine(androidHome, "platform-tools/adb"));
            androidCandidates.Add(Path.Combine(androidHome, "emulator/emulator"));
        }

        // The most outdated copy on the machine wins a "first found" search, which is how a host with
        // the SDK's 37.x and a package manager's 36.x ends up pairing with the older one: the phone's
        // pairing service speaks a newer protocol and the pair fails with a protocol fault. The
        // candidates are therefore versioned and the newest one is used.
        var adbCandidatePaths = new List<string>
        {
            Path.Combine(home, "Library/Android/sdk/platform-tools/adb"),
            Path.Combine(home, "Android/Sdk/platform-tools/adb"),
            Path.Combine(home, "Library/Developer/Xamarin/android-sdk-macosx/platform-tools/adb"),
            Path.Combine(home, "AppData/Local/Android/Sdk/platform-tools/adb.exe"),
            "/opt/homebrew/bin/adb",
            "/usr/local/bin/adb",
            "/usr/bin/adb",
            "/opt/homebrew/share/android-commandlinetools/platform-tools/adb"
        };
        const string caskroom = "/opt/homebrew/Caskroom/android-platform-tools";
        if (Directory.Exists(caskroom))
        {
            try
            {
                adbCandidatePaths.AddRange(
                    Directory.EnumerateFiles(caskroom, "adb", SearchOption.AllDirectories));
            }
            catch
            {
                // Best effort.
            }
        }
        adbCandidatePaths.AddRange(
            androidCandidates.Where(p => p.EndsWith("adb", StringComparison.OrdinalIgnoreCase)));
        var adbCandidates = ResolveAdbCandidates(adbCandidatePaths.ToArray());

        AdbPath = adbCandidates.NewestPath;
        AdbVersion = adbCandidates.Detected;
        AdbInstallations = adbCandidates.Installations;

        EmulatorPath = ResolveExecutable("emulator",
        [
            Path.Combine(home, "Library/Android/sdk/emulator/emulator"),
            Path.Combine(home, "Android/Sdk/emulator/emulator"),
            "/opt/homebrew/bin/emulator",
            "/usr/local/bin/emulator",
            .. androidCandidates.Where(p => p.EndsWith("emulator", StringComparison.OrdinalIgnoreCase))
        ]);

        var xcrunPath = ResolveExecutable("xcrun", ["/usr/bin/xcrun"]);
        _androidDeploymentService = new AndroidDeploymentService(
            AdbPath,
            EmulatorPath,
            () => ApkSignerPath,
            FindBestApk);
        _iosSimulatorService = new IosSimulatorService(xcrunPath, FindNewestAppBundle);
        _iosDeviceService = new IosDeviceService(xcrunPath, FindNewestAppBundle);
        _androidCrashLogService = new AndroidCrashLogService(AdbPath);
        _iosCrashLogService = new IosCrashLogService(xcrunPath);
        _desktopCrashLogService = new DesktopCrashLogService();
    }

    public string? DotnetPath { get; }

    public string? AdbPath { get; }

    /// <summary>The adb build the app runs, as <c>adb version</c> reports it.</summary>
    public AndroidAdbVersion? AdbVersion { get; }

    /// <summary>
    /// Every adb this computer has, newest first, with the one the app runs marked. Older copies are
    /// never used and never removed by the app: a package manager or an IDE owns those files, and
    /// removing a tool somebody else installed is not this app's call.
    /// </summary>
    public IReadOnlyList<AndroidAdbInstallation> AdbInstallations { get; } = [];

    public string? EmulatorPath { get; }

    /// <summary>
    /// The one alert-sound player for the app: publishing, deploy actions, device file transfers, and
    /// clipboard copies all report through this instance, so the mute toggle silences everything.
    /// </summary>
    public AlertSoundService AlertSounds { get; } = new();

    public string? ApkSignerPath => ResolveApkSignerPath();

    public string? XcrunPath => ResolveExecutable("xcrun", ["/usr/bin/xcrun"]);

    public string DotnetStatusText => DotnetPath is null ? "dotnet not found" : $"dotnet: {DotnetPath}";

    public string AdbStatusText => AdbPath is null
        ? "adb not found"
        : AdbVersion is null ? $"adb: {AdbPath}" : $"adb {AdbVersion.Display}: {AdbPath}";

    /// <summary>
    /// The older adb copies this computer has, with the command that removes each. The app names them
    /// and leaves them in place: a Homebrew cask or an IDE owns those files, and an app that deleted
    /// another installer's files would break that installer's own records.
    /// </summary>
    public IReadOnlyList<string> UnusedAdbNotes =>
        AdbInstallations
            .Where(installation => !installation.IsUsed)
            .Select(installation => $"{installation.Path} ({installation.Version.Display}) — remove it yourself with: " +
                (installation.Path.Contains("/opt/homebrew", StringComparison.Ordinal)
                    ? "brew uninstall --cask android-platform-tools"
                    : installation.Path.Contains("/usr/local", StringComparison.Ordinal)
                        ? "brew uninstall --cask android-platform-tools"
                        : "delete that folder, or keep it and ignore this note"))
            .ToArray();

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
        // Effective version: MSBuild sees csproj, Directory.Build.props, and any imported props;
        // the csproj XML scan stays as the fallback when dotnet evaluation is unavailable.
        var evaluatedVersions = await ResolveProjectVersionsAsync(
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
            ProjectInspector.ReadVersion(projectFile, publishPlatform, isDisplay: true, evaluatedVersions),
            ProjectInspector.ReadVersion(projectFile, publishPlatform, isDisplay: false, evaluatedVersions),
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
            if (IsAndroidPlatform(configuration.PublishPlatform) && configuration.IncludeAab)
            {
                CopyUniversalApkNextToAab(configuration, projectDirectory, bundle.OutputDirectory, writeOutput);
            }

            if (IsMacOsPlatform(configuration.PublishPlatform) && configuration.CreateMacAppBundle)
            {
                var createdBundlePath = await EnsureMacAppBundleAsync(
                    bundle.OutputDirectory,
                    bundle.ProjectFilePath,
                    configuration.PackageId,
                    _appIconProvider?.GetIconBytes(),
                    writeOutput,
                    cancellationToken);

                if (!string.IsNullOrWhiteSpace(createdBundlePath))
                {
                    writeOutput($"macOS app bundle ready: {createdBundlePath}{Environment.NewLine}");
                }
            }

            writeOutput(Environment.NewLine + "=== Publish completed successfully ===" + Environment.NewLine);
            AlertSounds.Play(AlertTone.Success);
            return true;
        }

        writeOutput(Environment.NewLine + $"=== Publish failed (exit code {exitCode}) ===" + Environment.NewLine);
        AlertSounds.Play(AlertTone.Failure);
        return false;
    }

    /// <summary>
    /// When the AAB is selected the Android SDK builds the store bundle as the primary artifact and
    /// derives the universal APK from it, but leaves that APK in the build output instead of copying
    /// it to the publish folder. Copy the best built universal APK next to the published AAB so one
    /// publish yields both packages side by side, as the Generate AAB toggle promises.
    /// </summary>
    private static void CopyUniversalApkNextToAab(
        PublishConfiguration configuration,
        DirectoryInfo projectDirectory,
        string publishDirectory,
        Action<string> writeOutput)
    {
        var aab = ArtifactLocator.FindBestAab(publishDirectory, configuration.ProjectName, configuration.PackageId);
        if (aab?.Directory is null)
        {
            return;
        }

        if (ArtifactLocator.FindBestApk(publishDirectory, configuration.ProjectName, configuration.PackageId) is not null)
        {
            return;
        }

        var usesCentralizedArtifacts = configuration.OutputLayout?.UsesCentralizedArtifacts == true;
        var buildOutputDirectory = usesCentralizedArtifacts
            ? configuration.OutputLayout!.BuildOutputDirectory
            : Path.Combine(projectDirectory.FullName, "bin");
        if (string.IsNullOrWhiteSpace(buildOutputDirectory))
        {
            return;
        }

        var universalApk = ArtifactLocator.FindBestUniversalApk(
            [buildOutputDirectory],
            configuration.ProjectName,
            configuration.PackageId);
        if (universalApk is null)
        {
            writeOutput($"No universal APK was built from the AAB; only the store bundle is in {aab.Directory.FullName}{Environment.NewLine}");
            return;
        }

        if (string.Equals(universalApk.Directory?.FullName, aab.Directory.FullName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var destination = Path.Combine(aab.Directory.FullName, universalApk.Name);
        File.Copy(universalApk.FullName, destination, overwrite: true);
        writeOutput($"Copied universal APK next to the store bundle: {universalApk.Name}{Environment.NewLine}");
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

    /// <summary>
    /// Reports what the device has installed for the package: who installed it, the version code
    /// it runs, and the certificate it was signed with. This is what tells the user whether a
    /// Play Store update can replace the install.
    /// </summary>
    public Task<AndroidInstalledPackageInfo?> ReadInstalledAndroidPackageAsync(
        string packageId,
        string? deviceSerial,
        CancellationToken cancellationToken) =>
        _androidDeploymentService.ReadInstalledPackageAsync(packageId, deviceSerial, cancellationToken);

    public Task<string> LaunchAsync(string packageId, string? deviceSerial, CancellationToken cancellationToken) =>
        _androidDeploymentService.LaunchAsync(packageId, deviceSerial, cancellationToken);

    public Task<string> CloseAsync(string packageId, string? deviceSerial, CancellationToken cancellationToken) =>
        _androidDeploymentService.CloseAppAsync(packageId, deviceSerial, cancellationToken);

    public Task<string> PushFileToDownloadsAsync(string localFilePath, string? deviceSerial, CancellationToken cancellationToken) =>
        _androidDeploymentService.PushFileToDownloadsAsync(localFilePath, deviceSerial, cancellationToken);

    public Task<IReadOnlyList<AndroidDeviceFileEntry>> ListAndroidFilesAsync(string? deviceSerial, string remotePath, CancellationToken cancellationToken) =>
        _androidDeploymentService.Files.ListAsync(deviceSerial ?? string.Empty, remotePath, cancellationToken);

    public Task<string> DeleteAndroidFileAsync(string? deviceSerial, string remotePath, bool isDirectory, CancellationToken cancellationToken) =>
        _androidDeploymentService.Files.DeleteAsync(deviceSerial ?? string.Empty, remotePath, isDirectory, cancellationToken);

    public Task<string> UploadToAndroidDeviceAsync(string? deviceSerial, IReadOnlyList<string> localPaths, string remoteDirectory, CancellationToken cancellationToken) =>
        _androidDeploymentService.Files.UploadAsync(deviceSerial ?? string.Empty, localPaths, remoteDirectory, cancellationToken);

    public Task<string> DownloadFromAndroidDeviceAsync(string? deviceSerial, string remotePath, string localDirectory, CancellationToken cancellationToken) =>
        _androidDeploymentService.Files.DownloadAsync(deviceSerial ?? string.Empty, remotePath, localDirectory, cancellationToken);

    /// <summary>`adb devices -l` alone: cheap enough to poll while the app watches for targets.</summary>
    public Task<IReadOnlyList<AndroidDeviceListEntry>> ProbeAndroidDevicesAsync(CancellationToken cancellationToken) =>
        _androidDeploymentService.ProbeAndroidDevicesAsync(cancellationToken);

    /// <summary>
    /// Every wireless debugging service the computer can see, from the adb server's mDNS list and the
    /// operating system's own resolver. The adb backend can be blind on a host where the resolver is
    /// not, so a scan that reads only adb loses the phone.
    /// </summary>
    public Task<IReadOnlyList<AndroidWirelessService>> DiscoverAndroidWirelessServicesAsync(CancellationToken cancellationToken) =>
        _androidDeploymentService.DiscoverAllWirelessServicesAsync(cancellationToken);

    /// <summary>
    /// Fast dedicated search for wireless pairing services (`_adb-tls-pairing._tcp`), returning
    /// immediately once an endpoint is discovered without scanning connect services.
    /// </summary>
    public Task<IReadOnlyList<AndroidWirelessService>> DiscoverAndroidWirelessPairingServicesAsync(CancellationToken cancellationToken) =>
        _androidDeploymentService.DiscoverAllWirelessServicesAsync(cancellationToken, pairingOnly: true);

    /// <summary>Drops the Wi-Fi transports an earlier pairing attempt left behind.</summary>
    public Task<string> PrepareAndroidWirelessPairingAsync(string? previousHost, CancellationToken cancellationToken) =>
        _androidDeploymentService.PrepareWirelessPairingAsync(previousHost, cancellationToken);

    /// <summary>Drops every Wi-Fi transport and, with nothing else attached, restarts the adb server.</summary>
    public Task<AndroidWirelessReset> ResetAndroidWirelessStateAsync(
        string? host,
        bool restartServerWhenIdle,
        CancellationToken cancellationToken) =>
        _androidDeploymentService.ResetWirelessStateAsync(host, restartServerWhenIdle, cancellationToken);

    /// <summary>
    /// Reads the adb server's own report of how it is set up for device discovery.
    /// </summary>
    public Task<AndroidAdbServerStatus> ReadAndroidAdbServerStatusAsync(CancellationToken cancellationToken) =>
        _androidDeploymentService.ReadAdbServerStatusAsync(cancellationToken);

    /// <summary>
    /// Restarts the adb server with the discovery settings Google documents when the server that is
    /// running cannot find devices and nothing else is attached to it. The result says what changed
    /// and how many devices the server now sees on the network.
    /// </summary>
    public Task<AndroidWirelessRepair> RepairAndroidWirelessDiscoveryAsync(CancellationToken cancellationToken) =>
        _androidDeploymentService.RepairWirelessDiscoveryAsync(cancellationToken);

    public Task<string> PairAndroidWirelessAsync(string address, string pairingCode, CancellationToken cancellationToken) =>
        _androidDeploymentService.PairWirelessDeviceAsync(address, pairingCode, cancellationToken);

    /// <summary>
    /// Probes a wireless debugging address before adb is asked to use it. Returns what the socket
    /// said, because <c>adb pair</c> reports the same protocol fault whether the connection never
    /// opened or the phone answered and did not speak the protocol.
    /// </summary>
    public Task<AndroidEndpointProbe> ProbeAndroidEndpointAsync(string address, CancellationToken cancellationToken) =>
        AndroidDeploymentService.ProbeEndpointAsync(address, TimeSpan.FromSeconds(4), cancellationToken);

    /// <summary>
    /// Opens the phone's Developer options screen, where the paired computers are forgotten and the
    /// debugging authorizations are revoked. There is no adb command that does either, so the phone
    /// has to be put in front of the user with its screen already open.
    /// </summary>
    public Task<string> OpenAndroidDeveloperOptionsAsync(string? deviceSerial, CancellationToken cancellationToken) =>
        _androidDeploymentService.OpenDeveloperOptionsAsync(deviceSerial, cancellationToken);

    public Task<string> PairAndroidWirelessSecretAsync(string address, string secret, CancellationToken cancellationToken) =>
        _androidDeploymentService.PairWirelessSecretAsync(address, secret, cancellationToken);

    public Task<AndroidWirelessConnectResult> ConnectAndroidWirelessAsync(string address, CancellationToken cancellationToken) =>
        _androidDeploymentService.ConnectWirelessDeviceAsync(address, cancellationToken);

    /// <summary>
    /// Connects to whatever debugging port a host publishes now, falling back to a saved address.
    /// Every advertised endpoint for the host is tried, because a cached record and a live one can
    /// carry different ports.
    /// </summary>
    public Task<AndroidWirelessConnectResult> ConnectAndroidWirelessHostAsync(
        string host,
        string? fallbackAddress,
        CancellationToken cancellationToken) =>
        _androidDeploymentService.ConnectWirelessHostAsync(host, fallbackAddress, cancellationToken);

    public Task<string> DisconnectAndroidWirelessAsync(string address, CancellationToken cancellationToken) =>
        _androidDeploymentService.DisconnectWirelessDeviceAsync(address, cancellationToken);

    public Task<AndroidWirelessEnableResult> EnableAndroidWirelessDebuggingAsync(string? deviceSerial, int port, CancellationToken cancellationToken) =>
        _androidDeploymentService.EnableWirelessDebuggingAsync(deviceSerial ?? string.Empty, port, cancellationToken);

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

    public Task<IReadOnlyList<string>> DiscoverEmulatorsAsync(CancellationToken cancellationToken) =>
        _androidDeploymentService.DiscoverEmulatorsAsync(cancellationToken);

    public Task<IReadOnlyList<AndroidDeviceInfo>> DiscoverAndroidDevicesAsync(CancellationToken cancellationToken) =>
        _androidDeploymentService.DiscoverAndroidDevicesAsync(cancellationToken);

    public static string? GetFirstRunningDeviceSerial(IReadOnlyList<AndroidDeviceInfo> devices) =>
        AndroidDeploymentService.GetFirstRunningDeviceSerial(devices);

    public Task<IReadOnlyList<string>> DiscoverIosSimulatorsAsync(CancellationToken cancellationToken) =>
        _iosSimulatorService.DiscoverAsync(cancellationToken);

    public Task<IReadOnlyList<IosDeviceInfo>> DiscoverIosDevicesAsync(CancellationToken cancellationToken) =>
        _iosDeviceService.DiscoverAsync(cancellationToken);

    public Task<string> InstallIosDeviceAppAsync(string outputDirectory, string udid, CancellationToken cancellationToken) =>
        _iosDeviceService.InstallAppAsync(outputDirectory, udid, cancellationToken);

    public Task<string> LaunchIosDeviceAppAsync(string packageId, string udid, CancellationToken cancellationToken) =>
        _iosDeviceService.LaunchAppAsync(packageId, udid, cancellationToken);

    public Task<string> StopIosDeviceAppAsync(string packageId, string udid, CancellationToken cancellationToken) =>
        _iosDeviceService.TerminateAppAsync(packageId, udid, cancellationToken);

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

    public Task<string> StopIosAppAsync(string packageId, string simulator, CancellationToken cancellationToken) =>
        _iosSimulatorService.TerminateAppAsync(packageId, simulator, cancellationToken);

    public Task<string> PushFileToSimulatorAsync(string localFilePath, string simulator, CancellationToken cancellationToken) =>
        _iosSimulatorService.PushFileAsync(localFilePath, simulator, cancellationToken);

    /// <summary>
    /// Looks for the crash reports of the app on the selected Android device: the app's own data
    /// folder, Android's own crash store, and the device log.
    /// </summary>
    public Task<CrashLogScan> ScanAndroidCrashLogsAsync(
        string? deviceSerial,
        string packageId,
        CancellationToken cancellationToken) =>
        _androidCrashLogService.ScanAsync(deviceSerial ?? string.Empty, packageId, cancellationToken);

    /// <summary>Looks for the crash reports of the app on the selected real iOS device.</summary>
    public Task<CrashLogScan> ScanIosDeviceCrashLogsAsync(
        string? deviceUdid,
        string bundleId,
        CancellationToken cancellationToken) =>
        _iosCrashLogService.ScanDeviceAsync(deviceUdid ?? string.Empty, bundleId, cancellationToken);

    /// <summary>Looks for the crash reports of the app on the selected iOS simulator.</summary>
    public Task<CrashLogScan> ScanIosSimulatorCrashLogsAsync(
        string? simulatorId,
        string bundleId,
        CancellationToken cancellationToken) =>
        _iosCrashLogService.ScanSimulatorAsync(simulatorId ?? string.Empty, bundleId, cancellationToken);

    /// <summary>Looks for the crash reports of the app on this computer.</summary>
    public Task<CrashLogScan> ScanDesktopCrashLogsAsync(
        string? appNameHint,
        CancellationToken cancellationToken) =>
        _desktopCrashLogService.ScanAsync(appNameHint, cancellationToken);

    /// <summary>
    /// The name of the published desktop app, used to pick the crash reports that belong to it out
    /// of the ones macOS and Windows wrote for every app.
    /// </summary>
    public string? FindDesktopAppName(string outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
        {
            return null;
        }

        var appBundle = new DirectoryInfo(outputDirectory)
            .EnumerateDirectories("*.app", SearchOption.AllDirectories)
            .OrderByDescending(directory => directory.LastWriteTimeUtc)
            .FirstOrDefault();

        return appBundle?.Name;
    }

    /// <summary>
    /// The adb installations found on this machine, with their versions read once. The newest one is
    /// the one the app runs, because wireless debugging against a current phone needs adb 37.0.0 or
    /// newer and a machine often holds an older package-manager copy as well.
    /// </summary>
    private sealed record AdbCandidates(IReadOnlyList<(string Path, AndroidAdbVersion Version)> Found)
    {
        /// <summary>The newest adb on this machine, and its version; null when none was found.</summary>
        public (string? Path, AndroidAdbVersion? Version) Newest =>
            Found
                .Select((candidate, index) => (candidate, index))
                .OrderByDescending(entry => entry.candidate.Version)
                .ThenBy(entry => entry.index)
                .Select(entry => ((string?)entry.candidate.Path, (AndroidAdbVersion?)entry.candidate.Version))
                .FirstOrDefault();

        public string? NewestPath => Newest.Path;

        public AndroidAdbVersion? Detected => Newest.Version;

        /// <summary>Newest first, with the app's own choice flagged on the first entry.</summary>
        public IReadOnlyList<AndroidAdbInstallation> Installations =>
            Found
                .OrderByDescending(candidate => candidate.Version)
                .Select((candidate, index) => new AndroidAdbInstallation(candidate.Path, candidate.Version, index == 0))
                .ToArray();
    }

    private static AdbCandidates ResolveAdbCandidates(params string[] known)
    {
        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in known)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            foreach (var executableName in GetExecutableNames(Path.GetFileName(candidate)))
            {
                var resolved = Path.Combine(Path.GetDirectoryName(candidate) ?? string.Empty, executableName);
                if (File.Exists(resolved) && seen.Add(resolved))
                {
                    paths.Add(resolved);
                }
            }
        }

        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathVariable))
        {
            foreach (var directory in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (var executableName in GetExecutableNames("adb"))
                {
                    var resolved = Path.Combine(directory, executableName);
                    if (File.Exists(resolved) && seen.Add(resolved))
                    {
                        paths.Add(resolved);
                    }
                }
            }
        }

        var found = new List<(string, AndroidAdbVersion)>();
        foreach (var path in paths)
        {
            found.Add((path, ReadAdbVersion(path)));
        }

        return new AdbCandidates(found);
    }

    private static AndroidAdbVersion ReadAdbVersion(string adbPath)
    {
        // This runs on the application's start-up path, which is the UI thread inside
        // OnFrameworkInitializationCompleted. ProcessRunner.RunAsync awaits
        // Process.WaitForExitAsync, and that continuation is posted to the SynchronizationContext
        // the caller captured (the Avalonia dispatcher), so blocking on the task from the UI
        // thread deadlocks: the task needs the very thread that is waiting for it, the
        // constructor never returns, no window is created, and the Dock icon bounces forever.
        // A thread-pool thread has no SynchronizationContext to capture, so the wait completes.
        try
        {
            return Task.Run(() => ReadAdbVersionAsync(adbPath)).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // An adb that cannot report a version stays at 0, so it never wins over one that can.
        }

        return AndroidAdbVersion.Parse(null);
    }

    private static async Task<AndroidAdbVersion> ReadAdbVersionAsync(string adbPath)
    {
        var output = new StringBuilder();
        await ProcessRunner.RunAsync([adbPath, "version"], text => output.Append(text), CancellationToken.None)
            .ConfigureAwait(false);
        return AndroidAdbVersion.Parse(output.ToString());
    }

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
            (arguments, token) => EvaluateMsBuildAsync(projectFile, arguments, token),
            cancellationToken);
    }

    /// <summary>
    /// Evaluated version properties for the opened project, or null when dotnet is unavailable —
    /// the caller then relies on the csproj XML scan inside <see cref="ProjectInspector.ReadVersion"/>.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, string>?> ResolveProjectVersionsAsync(
        FileInfo projectFile,
        string configuration,
        string targetFramework,
        string runtimeIdentifier,
        CancellationToken cancellationToken)
    {
        var dotnetPath = DotnetPath;
        if (dotnetPath is null)
        {
            return null;
        }

        return await ProjectVersionResolver.ResolveAsync(
            dotnetPath,
            projectFile,
            configuration,
            targetFramework,
            runtimeIdentifier,
            (arguments, token) => EvaluateMsBuildAsync(projectFile, arguments, token),
            cancellationToken);
    }

    private static async Task<(int ExitCode, string Output)> EvaluateMsBuildAsync(
        FileInfo projectFile,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        var exitCode = await ProcessRunner.RunAsync(
            arguments,
            text => output.Append(text),
            cancellationToken,
            projectFile.DirectoryName);
        return (exitCode, output.ToString());
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
        byte[]? iconBytes,
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
        CopyMacAppIcon(resourcesPath, iconBytes);

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
        byte[]? iconBytes,
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
            CopyMacAppIcon(resourcesPath, iconBytes);

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

    private static void CopyMacAppIcon(string resourcesPath, byte[]? iconBytes)
    {
        if (iconBytes is null || iconBytes.Length == 0)
        {
            return;
        }

        var iconPath = Path.Combine(resourcesPath, $"{MacAppIconFileName}.icns");
        File.WriteAllBytes(iconPath, iconBytes);
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

        return $"com.codepublisher";
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
