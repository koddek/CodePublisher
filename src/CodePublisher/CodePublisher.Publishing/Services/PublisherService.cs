using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CodePublisher.Features.Deployment.Android;
using CodePublisher.Features.Publishing.Bundle;
using CodePublisher.Features.Shared.Tools;
using CodePublisher.Features.Deployment.CrashLogs;
using CodePublisher.Features.Deployment.Ios;
using CodePublisher.Features.Publishing.Artifacts;
using CodePublisher.Features.Publishing.Build;
using CodePublisher.Features.Publishing.Configure;
using CodePublisher.Features.Publishing.Inspect;
using CodePublisher.Features.Publishing.Process;
using CodePublisher.Models;

namespace CodePublisher.Services;

public sealed class PublisherService
{
    public const string AndroidPlatform = PublishPlatforms.Android;
    public const string MacOsPlatform = PublishPlatforms.MacOs;
    public const string WindowsPlatform = PublishPlatforms.Windows;
    public const string IosPlatform = PublishPlatforms.Ios;
    public const string LinuxPlatform = PublishPlatforms.Linux;
    public const string NuGetPlatform = PublishPlatforms.NuGet;

    // Space-free roots first. dotnetup defaults to "~/Library/Application Support/dotnetup", whose
    // path contains a space, and the Mono AOT cross-assembler cannot run from a path with spaces
    // (error: "aarch64-linux-android-as: line 5: /Library/as: No such file or directory"). When
    // both exist, the space-free root wins even if PATH is not set (Dock-launched app).
    private static readonly string[] DotnetCandidates = BuildDotnetCandidates();

    private static string[] BuildDotnetCandidates()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return
        [
            Path.Combine(home, "dotnet", "dotnet"),
            Path.Combine(home, ".dotnet", "dotnet"),
            "/opt/homebrew/bin/dotnet",
            "/usr/local/bin/dotnet",
            "/usr/local/share/dotnet/dotnet",
            Path.Combine(home, "Library", "Application Support", "dotnetup", "dotnet", "dotnet")
        ];
    }

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
        DotnetPath = HostExecutableResolver.Resolve("dotnet", DotnetCandidates);

        var android = AndroidTooling.Discover();
        AdbPath = android.AdbPath;
        AdbVersion = android.AdbVersion;
        AdbInstallations = android.AdbInstallations;
        EmulatorPath = android.EmulatorPath;

        var xcrunPath = HostExecutableResolver.Resolve("xcrun", ["/usr/bin/xcrun"]);
        _androidDeploymentService = new AndroidDeploymentService(
            AdbPath,
            EmulatorPath,
            () => AndroidTooling.ResolveApkSignerPath(),
            FindBestApk);
        _iosSimulatorService = new IosSimulatorService(xcrunPath, MacAppBundleService.FindNewestAppBundle);
        _iosDeviceService = new IosDeviceService(xcrunPath, MacAppBundleService.FindNewestAppBundle);
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

    public string? ApkSignerPath => AndroidTooling.ResolveApkSignerPath();

    public string? XcrunPath => HostExecutableResolver.Resolve("xcrun", ["/usr/bin/xcrun"]);

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

    public ProjectMetadata LoadProjectMetadata(string projectDirectory, string configuration, string targetFramework, string runtimeIdentifier, string publishPlatform, string? projectFilePath = null)
    {
        return LoadProjectMetadataAsync(projectDirectory, configuration, targetFramework, runtimeIdentifier, publishPlatform, projectFilePath)
            .GetAwaiter()
            .GetResult();
    }

    public async Task<ProjectMetadata> LoadProjectMetadataAsync(string projectDirectory, string configuration, string targetFramework, string runtimeIdentifier, string publishPlatform, string? projectFilePath = null)
    {
        var projectDirectoryPath = ProjectInspector.CreateProjectDirectory(projectDirectory);
        var projectFile = !string.IsNullOrWhiteSpace(projectFilePath) && File.Exists(projectFilePath)
            ? new FileInfo(projectFilePath)
            : ProjectInspector.FindProjectFile(projectDirectoryPath)
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
            ProjectIdentifierResolver.Resolve(projectFile, projectName, publishPlatform, evaluatedVersions),
            detectedTargetFramework,
            ProjectInspector.ReadVersion(projectFile, publishPlatform, isDisplay: true, evaluatedVersions),
            ProjectInspector.ReadVersion(projectFile, publishPlatform, isDisplay: false, evaluatedVersions),
            ProjectInspector.SupportsInternalVersion(publishPlatform),
            outputLayout);
    }

    public PublishCommandBundle BuildPublishCommandBundle(PublishConfiguration configuration, string? projectFilePath = null)
    {
        var projectDirectory = ProjectInspector.CreateProjectDirectory(configuration.ProjectDirectory);
        // The configuration's picked project wins, then the explicit argument, then top-level lookup.
        var explicitPath = !string.IsNullOrWhiteSpace(configuration.ProjectFilePath)
            ? configuration.ProjectFilePath
            : projectFilePath;
        var projectFile = !string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath)
            ? new FileInfo(explicitPath)
            : ProjectInspector.FindProjectFile(projectDirectory)
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
                var createdBundlePath = await MacAppBundleService.EnsureAsync(
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

    private static bool IsNuGetPlatform(string publishPlatform)
    {
        return string.Equals(publishPlatform, NuGetPlatform, StringComparison.Ordinal);
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
