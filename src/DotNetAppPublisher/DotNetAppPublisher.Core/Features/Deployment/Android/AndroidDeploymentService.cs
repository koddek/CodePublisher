using System.Globalization;
using System.Net.Sockets;
using System.Text;
using DotNetAppPublisher.Features.Publishing.Inspect;
using DotNetAppPublisher.Features.Publishing.Process;
using DotNetAppPublisher.Models;

namespace DotNetAppPublisher.Features.Deployment.Android;

/// <summary>
/// Everything adb does for a real device or emulator: discovery, Wireless debugging setup,
/// install/update, launch, and file transfer. SDK paths and process execution are injected.
/// </summary>
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
        Func<string, string?, string?, FileInfo?> findApk,
        AndroidDeviceFileService? fileService = null)
    {
        _adbPath = adbPath;
        _emulatorPath = emulatorPath;
        _apkSignerPath = apkSignerPath;
        _findApk = findApk;
        Files = fileService ?? new AndroidDeviceFileService(adbPath);
    }

    /// <summary>File browsing, transfer, and delete operations for the selected device.</summary>
    public AndroidDeviceFileService Files { get; }

    /// <summary>
    /// Installs the newest APK from the publish folder. The default install keeps the existing app and
    /// its data (`adb install -r`): the app is never uninstalled automatically, so a signature clash
    /// fails with an explanation instead of silently wiping app data. Before anything runs, the APK's
    /// signing certificate is compared with the copy already on the device, so a build that could
    /// break Google Play updates is stopped with guidance instead of being installed.
    /// </summary>
    /// <summary>
    /// Reports what the device has installed for a package: who installed it, which certificates
    /// it was signed with, and which version code it runs. This is what tells the user whether a
    /// Play Store update can replace the install, and which release line the device is on.
    /// </summary>
    public async Task<AndroidInstalledPackageInfo?> ReadInstalledPackageAsync(
        string packageId,
        string? deviceSerial,
        CancellationToken cancellationToken)
    {
        var adbPath = _adbPath ?? throw new InvalidOperationException(MissingAdb);
        var targetDevice = !string.IsNullOrWhiteSpace(deviceSerial)
            ? deviceSerial!.Trim()
            : throw new InvalidOperationException("No device selected. Select a device first.");

        if (string.IsNullOrWhiteSpace(packageId))
        {
            throw new InvalidOperationException("A package id is required to inspect the installed app.");
        }

        return await ReadInstalledPackageAsync(adbPath, targetDevice, packageId.Trim(), cancellationToken);
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

        var adbPath = _adbPath ?? throw new InvalidOperationException(MissingAdb);
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
        var apkDigests = await ReadApkCertificateDigestsAsync(apk.FullName, cancellationToken);

        // Install preflight: compare certificates with the copy already on the device, so a signature
        // clash is explained (with its Google Play impact) before any command touches the phone.
        AndroidInstalledPackageInfo? installedPackage = null;
        var packageId = configuration.PackageId.Trim();
        if (apkDigests is not null && packageId.Length > 0)
        {
            installedPackage = await ReadInstalledPackageAsync(adbPath, targetDevice, packageId, cancellationToken);
            if (installedPackage is { CertificateDigests.Count: > 0 }
                && !AndroidSigningInspector.CertificatesMatch(apkDigests, installedPackage.CertificateDigests))
            {
                throw new InvalidOperationException(
                    $"Not installing {apk.Name} on {targetDevice}: it is signed with a different key than " +
                    $"{packageId} already installed there.{Environment.NewLine}" +
                    AndroidInstallDiagnostics.DescribeSignatureClash(
                        apkDigests,
                        installedPackage.CertificateDigests,
                        installedPackage.InstallerPackage));
            }
        }

        var log = new StringBuilder();
        var wroteHeader = false;
        void Append(string text)
        {
            if (!wroteHeader)
            {
                log.Append($"Updating the installed app on {targetDevice} (existing app data is kept).{Environment.NewLine}");
                wroteHeader = true;
            }

            log.Append(text);
        }

        var arguments = new List<string> { adbPath, "-s", targetDevice, "install", "-r", apk.FullName };
        var exitCode = await RunAdbAsync(arguments, Append, cancellationToken);

        // Older devices reject streaming installs; retry once with the APK copied to the device first.
        if (exitCode != 0 && AndroidInstallDiagnostics.IsStreamingFailure(log.ToString()))
        {
            Append($"Streaming install is not supported on this device. Retrying with --no-streaming...{Environment.NewLine}");
            arguments.Insert(arguments.Count - 1, "--no-streaming");
            exitCode = await RunAdbAsync(arguments, Append, cancellationToken);
        }

        if (exitCode == 0)
        {
            var signatureNote = BuildInstallSignatureNote(apkDigests, installedPackage);
            return $"Installed {apk.Name} on {targetDevice}.{Environment.NewLine}" +
                (signatureNote is null ? string.Empty : signatureNote + Environment.NewLine) +
                log;
        }

        var guidance = AndroidInstallDiagnostics.DescribeFailure(log.ToString());
        throw new InvalidOperationException(
            $"APK install failed for {apk.Name} on {targetDevice}." +
            (guidance is null ? string.Empty : $"{Environment.NewLine}{guidance}") +
            $"{Environment.NewLine}{log}");
    }

    public async Task<string> UninstallAsync(string packageId, string? deviceSerial, CancellationToken cancellationToken)
    {
        var adbPath = _adbPath ?? throw new InvalidOperationException(MissingAdb);
        if (string.IsNullOrWhiteSpace(packageId))
        {
            throw new InvalidOperationException("Package id is required to uninstall the app.");
        }

        var targetDevice = !string.IsNullOrWhiteSpace(deviceSerial)
            ? deviceSerial!.Trim()
            : throw new InvalidOperationException("No device selected. Select a device first.");
        var output = new StringBuilder();
        var exitCode = await RunAdbAsync(
            [adbPath, "-s", targetDevice, "uninstall", packageId.Trim()],
            text => output.Append(text),
            cancellationToken);

        if (exitCode == 0)
        {
            return $"Uninstalled {packageId} on {targetDevice}. App data for this package is gone; a later Install starts fresh.{Environment.NewLine}{output}";
        }

        throw new InvalidOperationException($"Uninstall failed for {packageId} on {targetDevice}.{Environment.NewLine}{output}");
    }

    /// <summary>
    /// Starts the app on the device. The launcher activity is resolved from the package first, because
    /// `monkey` (the legacy launcher) is restricted on some OEM builds.
    /// </summary>
    public async Task<string> LaunchAsync(string packageId, string? deviceSerial, CancellationToken cancellationToken)
    {
        var adbPath = _adbPath ?? throw new InvalidOperationException(MissingAdb);
        if (string.IsNullOrWhiteSpace(packageId))
        {
            throw new InvalidOperationException("Package id is required to launch the app.");
        }

        var targetDevice = !string.IsNullOrWhiteSpace(deviceSerial)
            ? deviceSerial!.Trim()
            : throw new InvalidOperationException("No device selected. Select a device first.");
        var package = packageId.Trim();

        var resolveOutput = new StringBuilder();
        await RunAdbAsync(
            [adbPath, "-s", targetDevice, "shell", "cmd", "package", "resolve-activity", "--brief", "-c", "android.intent.category.LAUNCHER", package],
            text => resolveOutput.Append(text),
            cancellationToken);

        var component = ResolveLauncherComponent(resolveOutput.ToString());
        var output = new StringBuilder();
        var exitCode = component is null
            ? await RunAdbAsync(
                [adbPath, "-s", targetDevice, "shell", "monkey", "-p", package, "-c", "android.intent.category.LAUNCHER", "1"],
                text => output.Append(text),
                cancellationToken)
            : await RunAdbAsync(
                [adbPath, "-s", targetDevice, "shell", "am", "start", "-n", component],
                text => output.Append(text),
                cancellationToken);

        if (exitCode == 0)
        {
            return $"Launch requested for {package} on {targetDevice}.{Environment.NewLine}{output}";
        }

        throw new InvalidOperationException(
            $"Launch failed for {package} on {targetDevice}. Check the Package ID, and make sure the app is installed." +
            $"{Environment.NewLine}{output}");
    }

    /// <summary>
    /// Closes the app completely on a device or emulator. Two steps, in order: every process of the
    /// package is stopped (`am force-stop`), then the app's card is dismissed from Recents / the app
    /// switcher (`am stack remove` on the task read back from `dumpsys activity recents`). Stopping
    /// alone leaves the card behind, which is why this exists; the installed app and its data are kept,
    /// and only tasks the device reports as belonging to this package are dismissed, so no other app is
    /// ever removed from Recents.
    /// </summary>
    public async Task<string> CloseAppAsync(string packageId, string? deviceSerial, CancellationToken cancellationToken)
    {
        var adbPath = _adbPath ?? throw new InvalidOperationException(MissingAdb);
        if (string.IsNullOrWhiteSpace(packageId))
        {
            throw new InvalidOperationException("Package id is required to close the app.");
        }

        var targetDevice = !string.IsNullOrWhiteSpace(deviceSerial)
            ? deviceSerial!.Trim()
            : throw new InvalidOperationException("No device selected. Select a device first.");
        var package = packageId.Trim();
        var stopped = await ForceStopAsync(adbPath, targetDevice, package, cancellationToken);

        string recentsDump;
        try
        {
            recentsDump = await ReadRecentTasksAsync(adbPath, targetDevice, cancellationToken);
        }
        catch (Exception ex)
        {
            return $"Stopped every process of {package} on {targetDevice}, but its Recents card could not be removed: " +
                $"{ex.Message}{Environment.NewLine}Swipe the card away in the app switcher. The installed app and its data are kept.";
        }

        var tasks = AndroidRecentTaskParser.FindDismissable(AndroidRecentTaskParser.Parse(recentsDump), package);
        if (tasks.Count == 0)
        {
            return $"Closed {package} on {targetDevice}: every process was stopped, and it had no card in Recents." +
                $"{Environment.NewLine}{stopped}The installed app and its data are kept.";
        }

        var commandFailures = new List<string>();
        foreach (var task in tasks)
        {
            var removeOutput = new StringBuilder();
            var exitCode = await RunAdbAsync(
                [adbPath, "-s", targetDevice, "shell", "am", "stack", "remove", task.RootTaskId.ToString(CultureInfo.InvariantCulture)],
                text => removeOutput.Append(text),
                cancellationToken);

            if (exitCode != 0 || removeOutput.ToString().Trim().Length > 0)
            {
                var detail = removeOutput.ToString().Trim();
                commandFailures.Add(detail.Length > 0
                    ? $"task {task.RootTaskId} (exit code {exitCode}): {detail}"
                    : $"task {task.RootTaskId} (exit code {exitCode})");
            }
        }

        // Verify against the device instead of trusting the exit code: `am stack remove` also exits 0 for
        // an id that does not exist, so the card list has to be read back.
        IReadOnlyList<AndroidRecentTask>? remaining;
        string? verificationError = null;
        try
        {
            remaining = AndroidRecentTaskParser.FindDismissable(
                AndroidRecentTaskParser.Parse(await ReadRecentTasksAsync(adbPath, targetDevice, cancellationToken)),
                package);
        }
        catch (Exception ex)
        {
            remaining = null;
            verificationError = ex.Message;
        }

        var removed = remaining is null
            ? []
            : tasks.Where(task => remaining.All(entry => entry.RootTaskId != task.RootTaskId)).ToArray();
        var stillListed = remaining is null
            ? tasks
            : tasks.Where(task => remaining.Any(entry => entry.RootTaskId == task.RootTaskId)).ToArray();

        if (stillListed.Count == 0 && commandFailures.Count == 0)
        {
            var removedLabel = string.Join(", ", removed.Select(task => $"task {task.RootTaskId} ({task.DisplayLabel})"));
            return $"Closed {package} on {targetDevice}: every process was stopped and its Recents card was dismissed " +
                $"({removedLabel}). The installed app and its data are kept." +
                $"{Environment.NewLine}{stopped}";
        }

        var problems = new List<string>(commandFailures);
        problems.AddRange(stillListed.Select(task => $"task {task.RootTaskId} ({task.DisplayLabel}) is still listed"));
        if (verificationError is not null)
        {
            problems.Add($"the Recents list could not be read back ({verificationError})");
        }

        return $"Stopped every process of {package} on {targetDevice}, but removing its Recents card failed " +
            $"({string.Join("; ", problems)}). Swipe the card away in the app switcher, or update platform-tools. " +
            $"The installed app and its data are kept." +
            $"{Environment.NewLine}{stopped}";
    }

    /// <summary>Stops every process of the package (`am force-stop`) and returns the device output.</summary>
    private static async Task<string> ForceStopAsync(string adbPath, string targetDevice, string package, CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        var exitCode = await RunAdbAsync(
            [adbPath, "-s", targetDevice, "shell", "am", "force-stop", package],
            text => output.Append(text),
            cancellationToken);

        if (exitCode == 0)
        {
            return $"Stopped every process of {package} on {targetDevice}.{Environment.NewLine}{output}";
        }

        throw new InvalidOperationException(
            $"Close failed for {package} on {targetDevice}: the app could not be stopped. Make sure the app is installed and the device is ready." +
            $"{Environment.NewLine}{output}");
    }

    /// <summary>
    /// Reads `dumpsys activity recents`, the list that backs the device's app switcher. Its `taskId`
    /// and `rootTaskId` fields are what `am stack remove` needs, and its `type`/component fields say
    /// which package owns each card.
    /// </summary>
    private static async Task<string> ReadRecentTasksAsync(string adbPath, string targetDevice, CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        var exitCode = await RunAdbAsync(
            [adbPath, "-s", targetDevice, "shell", "dumpsys", "activity", "recents"],
            text => output.Append(text),
            cancellationToken);
        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"the device did not return its Recents list (exit code {exitCode})." +
                $"{Environment.NewLine}{output}");
        }

        return output.ToString();
    }

    /// <summary>Sends one file to the device Downloads folder (kept for the Deploy tab shortcut).</summary>
    public Task<string> PushFileToDownloadsAsync(string localFilePath, string? deviceSerial, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(localFilePath))
        {
            throw new InvalidOperationException("File path is required.");
        }

        if (!File.Exists(localFilePath))
        {
            throw new InvalidOperationException($"File not found: {localFilePath}");
        }

        return Files.UploadAsync(deviceSerial ?? string.Empty, [localFilePath], AndroidStorageLocations.Downloads, cancellationToken);
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

    /// <summary>
    /// Lists every adb target: physical devices on USB or Wi-Fi (including unauthorized and offline
    /// ones, so the app can explain what to do) plus running and available emulators.
    /// </summary>
    public async Task<IReadOnlyList<AndroidDeviceInfo>> DiscoverAndroidDevicesAsync(CancellationToken cancellationToken)
    {
        var devices = new List<AndroidDeviceInfo>();
        var runningSerials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_adbPath is not null)
        {
            var adbOutput = new StringBuilder();
            var adbExitCode = await RunAdbAsync([_adbPath, "devices", "-l"], text => adbOutput.Append(text), cancellationToken);
            if (adbExitCode != 0)
            {
                throw new InvalidOperationException($"Failed to query Android devices with adb (exit code {adbExitCode}).{Environment.NewLine}{adbOutput}");
            }

            foreach (var entry in AndroidDeviceListParser.Parse(adbOutput.ToString()))
            {
                runningSerials.Add(entry.Serial);
                var isReady = string.Equals(entry.State, AndroidDeviceState.Ready, StringComparison.Ordinal);
                var properties = isReady ? await ReadDevicePropertiesAsync(entry.Serial, cancellationToken) : null;

                var isEmulator = entry.IsEmulatorSerial || properties?.IsEmulator == true;
                var connection = isEmulator
                    ? AndroidDeviceConnection.Emulator
                    : entry.IsWirelessSerial ? AndroidDeviceConnection.Wireless : AndroidDeviceConnection.Usb;

                var model = properties?.Model ?? entry.Model;
                var name = isEmulator
                    ? await GetAvdNameAsync(entry.Serial, cancellationToken) ?? model ?? entry.Serial
                    : model ?? entry.Serial;

                devices.Add(new AndroidDeviceInfo(
                    entry.Serial,
                    name,
                    isEmulator ? "emulator" : "device",
                    isReady,
                    entry.State,
                    connection,
                    model ?? string.Empty,
                    properties?.AndroidVersion ?? string.Empty,
                    properties?.ApiLevel ?? string.Empty,
                    properties?.Abi ?? string.Empty));
            }
        }

        var runningAvdNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var serial in runningSerials.Where(serial => serial.StartsWith("emulator-", StringComparison.OrdinalIgnoreCase)))
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
                    devices.Add(new AndroidDeviceInfo(
                        string.Empty,
                        avdName,
                        "emulator",
                        false,
                        AndroidDeviceState.Offline,
                        AndroidDeviceConnection.Emulator));
                }
            }
        }

        return devices
            .OrderBy(AndroidDeviceInfo.Rank)
            .ThenBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
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

    // --- Wireless debugging (Android 11+ pairing, plus the legacy adb tcpip flow) ---

    /// <summary>
    /// `adb devices -l` on its own: no per-device probing, so it is cheap enough to poll while the app
    /// watches for a phone to be plugged in, authorized, or an emulator to finish booting.
    /// </summary>
    public async Task<IReadOnlyList<AndroidDeviceListEntry>> ProbeAndroidDevicesAsync(CancellationToken cancellationToken)
    {
        var adbPath = _adbPath ?? throw new InvalidOperationException(MissingAdb);
        var output = new StringBuilder();
        var exitCode = await RunAdbAsync([adbPath, "devices", "-l"], text => output.Append(text), cancellationToken);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"Failed to query Android devices with adb (exit code {exitCode}).{Environment.NewLine}{output}");
        }

        return AndroidDeviceListParser.Parse(output.ToString());
    }

    /// <summary>Lists the wireless debugging services the devices advertise over mDNS.</summary>
    public async Task<IReadOnlyList<AndroidWirelessService>> DiscoverWirelessServicesAsync(CancellationToken cancellationToken)
    {
        var adbPath = _adbPath ?? throw new InvalidOperationException(MissingAdb);
        var output = new StringBuilder();
        var exitCode = await RunAdbAsync([adbPath, "mdns", "services"], text => output.Append(text), cancellationToken);
        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"Failed to scan for wireless debugging services (exit code {exitCode}). " +
                "Wireless debugging must be enabled on the device, both devices must be on the same network, " +
                "and the network must allow mDNS discovery." +
                $"{Environment.NewLine}{output}");
        }

        return AndroidWirelessDebug.ParseMdnsServices(output.ToString());
    }

    /// <summary>
    /// Every wireless debugging service this computer can see, from the adb server's mDNS list and
    /// from the operating system's own resolver.
    ///
    /// Both are needed: on macOS 26 with platform-tools 37.0.1 the adb server reports
    /// `mdns_backend: LIBADBMDNS` and `mdns_enabled: true` while `adb mdns services` stays empty, and
    /// the same phone answers `dns-sd` on the same machine at the same moment. Reading only the adb
    /// list therefore loses the phone for every step that depends on discovery, including the
    /// debugging port that has to be connected to after pairing.
    /// </summary>
    /// <summary>
    /// Every wireless debugging service this computer can see, from the adb server's mDNS list and
    /// from the operating system's own resolver.
    ///
    /// When <paramref name="pairingOnly"/> is true, only pairing services (`_adb-tls-pairing._tcp`)
    /// are scanned and returned, and an adb hit returns immediately to keep QR code discovery fast.
    /// </summary>
    public async Task<IReadOnlyList<AndroidWirelessService>> DiscoverAllWirelessServicesAsync(
        CancellationToken cancellationToken,
        bool pairingOnly = false)
    {
        var fromAdb = Array.Empty<AndroidWirelessService>();
        try
        {
            fromAdb = (await DiscoverWirelessServicesAsync(cancellationToken)).ToArray();
            if (pairingOnly && fromAdb.Any(service => service.IsPairingService))
            {
                return fromAdb.Where(service => service.IsPairingService).ToArray();
            }
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // A failing adb mDNS backend is exactly what the resolver path is for.
        }

        var fromResolver = new List<AndroidWirelessService>();
        var serviceTypes = pairingOnly ? [true] : new[] { true, false };
        foreach (var pairingService in serviceTypes)
        {
            var advertised = await BrowseNativeMdnsAsync(pairingService, cancellationToken);
            foreach (var instance in advertised)
            {
                var resolved = await ResolveNativeMdnsServiceAsync(instance, pairingService, cancellationToken);
                if (resolved is not null)
                {
                    fromResolver.Add(resolved);
                }
            }
        }

        var merged = AndroidWirelessDebug.MergeServices(fromAdb, fromResolver);
        return pairingOnly
            ? merged.Where(service => service.IsPairingService).ToArray()
            : merged;
    }

    /// <summary>
    /// The mDNS resolver that does not depend on the adb server. <c>dns-sd</c> ships with macOS and
    /// with Apple's Bonjour on Windows; Linux distributions ship <c>avahi-browse</c> instead, which
    /// speaks a different syntax, so it is not used here. Returns null when the tool is not on PATH,
    /// because a missing optional tool must degrade to the adb path rather than raise an error.
    /// </summary>
    public static string? ResolveNativeMdnsTool()
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsWindows())
        {
            return null;
        }

        return IsOnPath(AndroidMdnsResolve.DnsSd) ? AndroidMdnsResolve.DnsSd : null;
    }

    /// <summary>True when an executable resolves through <c>PATH</c> or an absolute location.</summary>
    private static bool IsOnPath(string executable)
    {
        if (Path.IsPathRooted(executable))
        {
            return File.Exists(executable);
        }

        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathVariable))
        {
            return false;
        }

        return pathVariable
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(directory => Path.Combine(directory, executable))
            .Any(File.Exists);
    }

    /// <summary>
    /// Drops the Wi-Fi transports an earlier attempt left behind, so a new pairing starts from a
    /// clean slate: a half-open transport is what makes a phone sit on its connecting spinner while
    /// the host reports nothing it can use.
    /// </summary>
    public async Task<string> PrepareWirelessPairingAsync(string? previousHost, CancellationToken cancellationToken)
    {
        var adbPath = _adbPath ?? throw new InvalidOperationException(MissingAdb);
        var entries = await ProbeAndroidDevicesAsync(cancellationToken);
        var dropped = new List<string>();

        // Drops all TCP/IP transports on the adb daemon to clear any half-open or stale connections
        await TryDropWirelessTransportAsync([adbPath, "disconnect"], cancellationToken);

        foreach (var entry in entries.Where(entry => entry.IsWirelessSerial))
        {
            await TryDropWirelessTransportAsync([adbPath, "disconnect", entry.Serial], cancellationToken);
            dropped.Add(entry.Serial);
        }

        return dropped.Count == 0
            ? "Cleared earlier Wi-Fi connections and daemon state."
            : $"Cleared {dropped.Count} earlier Wi-Fi connection(s) first: {string.Join(", ", dropped)}.";
    }

    /// <summary>
    /// Clears this computer's wireless debugging state: every Wi-Fi transport is dropped, and with
    /// nothing else attached the adb server is restarted, which is also what empties its mDNS cache.
    /// Pairing keys are not touched — they are shared with USB and the phone owns the other half of
    /// each one — so a phone that no longer recognises this computer still needs Forget or Revoke on
    /// the phone, which <see cref="OpenDeveloperOptionsAsync"/> puts one tap away.
    /// </summary>
    public async Task<AndroidWirelessReset> ResetWirelessStateAsync(
        string? host,
        bool restartServerWhenIdle,
        CancellationToken cancellationToken)
    {
        var adbPath = _adbPath ?? throw new InvalidOperationException(MissingAdb);
        var entries = await ProbeAndroidDevicesAsync(cancellationToken);
        var targets = entries.Where(entry => entry.IsWirelessSerial)
            .Where(entry => string.IsNullOrWhiteSpace(host)
                || (AndroidWirelessDebug.TryParseEndpoint(entry.Serial, out var entryHost, out _)
                    && string.Equals(entryHost, host.Trim(), StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        var dropped = new List<string>();
        foreach (var target in targets)
        {
            await TryDropWirelessTransportAsync([adbPath, "disconnect", target.Serial], cancellationToken);
            dropped.Add(target.Serial);
        }

        var restarted = false;
        var attachedNonWireless = entries.Any(entry => !entry.IsWirelessSerial);
        if (restartServerWhenIdle && !attachedNonWireless)
        {
            await TryDropWirelessTransportAsync([adbPath, "kill-server"], cancellationToken);
            await StartDiscoveryAdbAsync([adbPath, "start-server"], cancellationToken);
            await ReadAdbServerStatusAsync(cancellationToken);
            restarted = true;
        }

        var cleared = dropped.Count == 0
            ? "No Wi-Fi connection was attached."
            : $"Dropped {dropped.Count} Wi-Fi connection(s): {string.Join(", ", dropped)}.";
        var server = restarted
            ? " The adb server was restarted, so its cached mDNS list is empty again."
            : attachedNonWireless
                ? " The adb server was left running because a USB device or emulator is attached to it."
                : string.Empty;
        return new AndroidWirelessReset(dropped, restarted, cleared + server);
    }

    /// <summary>Resolves one advertised service using the default lookup window.</summary>
    public static Task<AndroidWirelessService?> ResolveNativeMdnsServiceAsync(
        string instanceName,
        bool pairingService,
        CancellationToken cancellationToken) =>
        ResolveNativeMdnsServiceAsync(instanceName, pairingService, NativeLookupTimeout, cancellationToken);

    /// <summary>
    /// Resolves one advertised service with the operating system's own mDNS resolver. Returns null
    /// when the resolver is unavailable, the advertisement has not appeared yet, or the host has no
    /// reachable IPv4 address.
    /// </summary>
    public static async Task<AndroidWirelessService?> ResolveNativeMdnsServiceAsync(
        string instanceName,
        bool pairingService,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var tool = ResolveNativeMdnsTool();
        if (tool is null || string.IsNullOrWhiteSpace(instanceName))
        {
            return null;
        }

        var serviceType = pairingService
            ? AndroidMdnsResolve.PairingServiceType
            : AndroidMdnsResolve.ConnectServiceType;

        var resolved = await RunResolverAsync(
            tool,
            ["-L", instanceName.Trim(), serviceType, "local"],
            timeout,
            cancellationToken);
        if (resolved is not (var host, var port))
        {
            return null;
        }

        // The resolver reports a .local hostname; a plain address is what `adb pair` wants, because
        // `.local` resolution depends on nss-mdns being configured on the host.
        var address = await ResolveNativeHostAsync(tool, host, timeout, cancellationToken);
        AndroidMdnsResolve.TryTrimVersionMarker(instanceName, out var serviceName, out var serviceVersion);
        return new AndroidWirelessService(
            serviceName.Length > 0 ? serviceName : instanceName.Trim(),
            address is null ? $"{host}:{port}" : $"{address}:{port}",
            pairingService)
        {
            ServiceVersion = serviceVersion
        };
    }

    private static readonly TimeSpan NativeLookupTimeout = TimeSpan.FromMilliseconds(1500);

    /// <summary>
    /// The instance names currently advertised for one service type, from the operating system's own
    /// resolver. Returns an empty list when the resolver is unavailable or nothing is advertised.
    /// </summary>
    public static async Task<IReadOnlyList<string>> BrowseNativeMdnsAsync(
        bool pairingService,
        CancellationToken cancellationToken)
    {
        var tool = ResolveNativeMdnsTool();
        if (tool is null)
        {
            return [];
        }

        var serviceType = pairingService
            ? AndroidMdnsResolve.PairingServiceType
            : AndroidMdnsResolve.ConnectServiceType;

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stop.CancelAfter(NativeLookupTimeout);

        var instances = new List<string>();
        try
        {
            await ProcessRunner.RunAsync(
                [tool, "-B", serviceType],
                line =>
                {
                    if (AndroidMdnsResolve.TryParseBrowseLine(line, out _, out var instance))
                    {
                        instances.Add(instance);
                    }

                    // A short browse is enough: the phone publishes its connect service before
                    // pairing even begins, so it is visible within a second or two.
                    stop.CancelAfter(NativeLookupTimeout / 4);
                },
                stop.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Stopping the browse is how it ends.
        }

        return instances
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Resolves a <c>.local</c> hostname to the IPv4 address the host can actually reach.
    ///
    /// A host can answer with more than one address, and the first one is not always usable: this
    /// Mac answers `MacBook-Air.local` with 127.0.0.1 on the loopback interface before its LAN
    /// address. Stopping at the first answer and then rejecting it as loopback loses the name
    /// entirely, so the lookup keeps listening until an address passes
    /// <see cref="AndroidMdnsResolve.PickBestIpv4"/> or the window closes.
    /// </summary>
    private static async Task<string?> ResolveNativeHostAsync(
        string tool,
        string host,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stop.CancelAfter(timeout);

        var addresses = new List<string>();
        try
        {
            await ProcessRunner.RunAsync(
                [tool, "-G", "v4", host],
                line =>
                {
                    if (!AndroidMdnsResolve.TryParseAddressLine(line, out var address))
                    {
                        return;
                    }

                    addresses.Add(address);
                    if (AndroidMdnsResolve.PickBestIpv4(addresses) is not null)
                    {
                        // A usable address arrived, and the resolver would otherwise listen forever.
                        stop.Cancel();
                    }
                },
                stop.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Stopping the resolver is how this lookup ends.
        }

        return AndroidMdnsResolve.PickBestIpv4(addresses);
    }

    /// <summary>
    /// Runs a resolver command that never exits on its own (<c>dns-sd -L</c> sits and listens) and
    /// stops it as soon as a line carries the address, or when the timeout expires. The resolver is
    /// killed either way, which is the intended stop.
    /// </summary>
    private static async Task<(string Host, int Port)?> RunResolverAsync(
        string tool,
        string[] arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stop.CancelAfter(timeout);

        string host = string.Empty;
        var port = 0;
        var done = false;
        try
        {
            await ProcessRunner.RunAsync(
                [tool, .. arguments],
                line =>
                {
                    if (done)
                    {
                        return;
                    }

                    if (AndroidMdnsResolve.TryParseResolveLine(line, out var parsedHost, out var parsedPort))
                    {
                        host = parsedHost;
                        port = parsedPort;
                        done = true;
                        stop.Cancel();
                    }
                },
                stop.Token);
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            // Either the address arrived or the timeout ran out; both end the resolver here.
        }

        return done ? (host, port) : null;
    }

    /// <summary>
    /// The environment values Google documents for an adb server that can discover devices on the
    /// network. They only take effect when the server <em>starts</em>, so they are passed on the
    /// commands that start one, and the app never guesses about a server somebody else started.
    /// </summary>
    /// <summary>
    /// The environment values Google documents for an adb server whose mDNS backend is not the
    /// current one. They only take effect when a server <em>starts</em>, so they are passed on the
    /// restart inside <see cref="RepairWirelessDiscoveryAsync"/>, and no other adb command changes
    /// what a server somebody else started is doing.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> AdbDiscoveryEnvironment = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["ADB_MDNS"] = "1",
        ["ADB_MDNS_OPENSCREEN"] = "0"
    };

    /// <summary>How many times the advertised list is read after the adb server restarts.</summary>
    private const int DiscoverySettleAttempts = 8;

    private static readonly TimeSpan DiscoverySettleDelay = TimeSpan.FromSeconds(1.5);

    /// <summary>Starts an adb server with the discovery settings Google documents.</summary>
    private static Task<int> StartDiscoveryAdbAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        ProcessRunner.RunAsync(arguments, _ => { }, cancellationToken, environment: AdbDiscoveryEnvironment);

    private static Task<int> RunAdbAsync(IReadOnlyList<string> arguments, Action<string> writeOutput, CancellationToken cancellationToken) =>
        ProcessRunner.RunAsync(arguments, writeOutput, cancellationToken);

    /// <summary>
    /// Reads the adb server's own report of how it is set up for device discovery
    /// (<c>adb server-status</c>). Starting this command also starts the server when it is down.
    /// A server that has just been restarted does not answer its first read, so the read is retried
    /// briefly rather than reported as "unknown".
    /// </summary>
    public async Task<AndroidAdbServerStatus> ReadAdbServerStatusAsync(CancellationToken cancellationToken)
    {
        var adbPath = _adbPath ?? throw new InvalidOperationException(MissingAdb);
        AndroidAdbServerStatus status = new(null, null, false, false);
        for (var attempt = 1; attempt <= DiscoverySettleAttempts / 4; attempt++)
        {
            var output = new StringBuilder();
            await RunAdbAsync([adbPath, "server-status"], text => output.Append(text), cancellationToken);
            status = AndroidAdbServerStatus.Parse(output.ToString());
            if (status.IsKnown)
            {
                return status;
            }

            await Task.Delay(DiscoverySettleDelay, cancellationToken);
        }

        return status;
    }

    /// <summary>
    /// Applies Google's documented fix for an adb server that is not set up to discover devices.
    ///
    /// <c>adb server-status</c> reports the version, whether mDNS is enabled, and which mDNS backend
    /// the server runs, and Google's wireless debugging troubleshooting names the remedy for each
    /// one: update platform-tools to 37.0.0, set <c>ADB_MDNS=1</c>, and set <c>ADB_MDNS_OPENSCREEN=0</c>,
    /// then restart the server. The restart is the only part this method can perform, and it first
    /// checks the target list, because a restart drops every session attached to the running server.
    /// The result reports what was applied and how many devices the restarted server now sees, so a
    /// host that still needs the platform-tools update says so instead of pretending to be fixed.
    /// </summary>
    public async Task<AndroidWirelessRepair> RepairWirelessDiscoveryAsync(CancellationToken cancellationToken)
    {
        var adbPath = _adbPath ?? throw new InvalidOperationException(MissingAdb);
        var before = await ReadAdbServerStatusAsync(cancellationToken);
        if (before.IsHealthy)
        {
            return new AndroidWirelessRepair(false, before, 0,
                $"The adb server is {before.Version} with the {before.MdnsBackend} backend, so discovery is already set up correctly.");
        }

        var attached = await CountAttachedTargetsAsync(adbPath, cancellationToken);
        if (attached > 0)
        {
            return new AndroidWirelessRepair(false, before, 0,
                $"The adb server needs a restart before it can find devices, and {attached} target(s) are attached to it, " +
                $"so the app left them alone. Do the restart yourself: adb kill-server, then adb start-server. " +
                $"{before.FixCommands}");
        }

        await TryDropWirelessTransportAsync([adbPath, "kill-server"], cancellationToken);

        // The restart only matters because this command carries the documented environment: the
        // server reads it on start-up and keeps it for as long as it runs. The list is read
        // afterwards rather than assumed, because a backend that is only offered from adb 37.0.0
        // does not arrive on an older server.
        await StartDiscoveryAdbAsync([adbPath, "start-server"], cancellationToken);
        var after = await ReadAdbServerStatusAsync(cancellationToken);
        var advertised = await WaitForAdvertisedServicesAsync(adbPath, cancellationToken);

        var message = advertised.Count > 0
            ? $"The adb server has been restarted with the discovery settings Google documents and now sees " +
              $"{advertised.Count} wireless debugging service(s) on this network."
            : $"The adb server has been restarted with the discovery settings Google documents, but no device is " +
              $"advertising right now. Open Developer options > Wireless debugging on the phone and keep its screen on, " +
              $"and update Android platform-tools to 37.0.0 or newer, because this server is {after.Version} with the " +
              $"{after.MdnsBackend} backend. {after.FixCommands}";
        return new AndroidWirelessRepair(true, after, advertised.Count, message);
    }

    /// <summary>
    /// Reads the advertised service list until something shows up. A freshly started adb server
    /// answers its first <c>adb mdns services</c> with an empty list and fills it on the next mDNS
    /// cycle, so the wait is what makes the result mean something.
    /// </summary>
    private static async Task<IReadOnlyList<AndroidWirelessService>> WaitForAdvertisedServicesAsync(
        string adbPath,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<AndroidWirelessService> services = [];
        for (var attempt = 1; attempt <= DiscoverySettleAttempts; attempt++)
        {
            var output = new StringBuilder();
            try
            {
                var exitCode = await RunAdbAsync(
                    [adbPath, "mdns", "services"],
                    text => output.Append(text),
                    cancellationToken);
                services = exitCode == 0 ? AndroidWirelessDebug.ParseMdnsServices(output.ToString()) : [];
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // A server still starting up refuses a command; the next attempt reads it.
            }

            if (services.Count > 0)
            {
                return services;
            }

            await Task.Delay(DiscoverySettleDelay, cancellationToken);
        }

        return services;
    }

    /// <summary>How many targets the adb server currently holds, counting devices and emulators.</summary>
    private static async Task<int> CountAttachedTargetsAsync(string adbPath, CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        try
        {
            await RunAdbAsync([adbPath, "devices", "-l"], text => output.Append(text), cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return 0;
        }

        return output
            .ToString()
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Count(line => line.Length > 0 && !line.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase)
                           && !line.StartsWith('*'));
    }

    private const string MissingAdb = "`adb` was not found. Install Android platform-tools or add adb to PATH.";

    /// <summary>Runs adb and treats any failure as "nothing happened", for the cleanup steps.</summary>
    private async Task TryDropWirelessTransportAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        try
        {
            await RunAdbAsync(arguments, _ => { }, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>
    /// Tries to open a TCP connection to a wireless debugging address before adb is asked to use it.
    /// A pairing address found through mDNS is not proof that the phone still answers it: the record
    /// outlives the phone leaving the network or closing its dialog, and <c>adb pair</c> reports the
    /// same protocol fault for a connection that never opened as for one the phone answered and
    /// dropped. The socket error is the only honest answer, so it is collected here.
    /// </summary>
    public static async Task<AndroidEndpointProbe> ProbeEndpointAsync(
        string address,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (!AndroidWirelessDebug.TryParseEndpoint(address, out var host, out var port))
        {
            return new AndroidEndpointProbe(false, "not an address with a port");
        }

        using var client = new TcpClient();
        try
        {
            // ConnectAsync accepts a token, and the timeout is a second bound on the same operation,
            // because the socket itself can sit for minutes on an address that is silently dropping.
            await client.ConnectAsync(host, port, cancellationToken).AsTask().WaitAsync(timeout, cancellationToken);
            return new AndroidEndpointProbe(true, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // The message carries the socket error: "No route to host", "Connection refused",
            // "timed out". The wording differs per platform, which is why it is matched, not named.
            return new AndroidEndpointProbe(false, exception.Message);
        }
    }

    private static readonly TimeSpan EndpointProbeTimeout = TimeSpan.FromSeconds(4);

    /// <summary>How long one <c>adb pair</c> may take before it is called a dead pairing attempt.</summary>
    private static readonly TimeSpan PairingAttemptTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Opens the phone's Developer options screen, where the list of paired computers and "Revoke adb
    /// debugging authorizations" live. The phone owns both actions and no adb command performs them,
    /// so the app cannot forget a computer or revoke a key on its own; it can put the user one tap
    /// from the buttons. Requires an attached target (USB or a live Wi-Fi session).
    /// </summary>
    public async Task<string> OpenDeveloperOptionsAsync(string? deviceSerial, CancellationToken cancellationToken)
    {
        var adbPath = _adbPath ?? throw new InvalidOperationException(MissingAdb);
        if (string.IsNullOrWhiteSpace(deviceSerial))
        {
            throw new InvalidOperationException(
                "Connect the phone first, then use this. Without a connection there is no way to open a screen on it.");
        }

        var serial = deviceSerial.Trim();
        var output = new StringBuilder();
        var exitCode = await RunAdbAsync(
            [adbPath, "-s", serial, "shell", "am", "start", "-a", "android.settings.APPLICATION_DEVELOPMENT_SETTINGS"],
            text => output.Append(text),
            cancellationToken);
        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"Could not open Developer options on {serial}.{Environment.NewLine}{output}");
        }

        return $"Developer options are open on {serial}. Under Wireless debugging: tap this computer and choose " +
            $"Forget, or press Revoke adb debugging authorizations to start over with every computer. If the entry " +
            $"stays on screen after Forget, that is the phone's list not refreshing: press Revoke, then close and " +
            $"reopen this screen (or restart the phone).{Environment.NewLine}{output}";
    }

    /// <summary>Pairs with a device using the code shown under Wireless debugging.</summary>
    public async Task<string> PairWirelessDeviceAsync(string address, string pairingCode, CancellationToken cancellationToken)
    {
        if (!AndroidWirelessDebug.TryNormalizePairingCode(pairingCode, out var code, out var codeError))
        {
            throw new InvalidOperationException(codeError);
        }

        return await PairWirelessAsync(address, code, "the pairing code shown on the device", cancellationToken);
    }

    /// <summary>Pairs with a device using the password this computer put in a pairing QR code.</summary>
    public async Task<string> PairWirelessSecretAsync(string address, string secret, CancellationToken cancellationToken)
    {
        if (!AndroidQrPairing.TryNormalizeSecret(secret, out var normalized, out var secretError))
        {
            throw new InvalidOperationException(secretError);
        }

        return await PairWirelessAsync(address, normalized, "the QR pairing password", cancellationToken);
    }

    private async Task<string> PairWirelessAsync(string address, string secret, string secretDescription, CancellationToken cancellationToken)
    {
        var adbPath = _adbPath ?? throw new InvalidOperationException(MissingAdb);
        if (!AndroidWirelessDebug.TryNormalizeAddress(address, out var endpoint, out var addressError))
        {
            throw new InvalidOperationException(addressError);
        }

        // We run `adb pair` directly without a preliminary raw socket connect.
        // A raw TCP connect followed by immediate disconnect disrupts the TLS SPAKE2 pairing
        // server on Android devices and can trigger connection reset or protocol fault.
        var output = new StringBuilder();
        int exitCode;
        using var pairStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        pairStop.CancelAfter(PairingAttemptTimeout);
        try
        {
            exitCode = await RunAdbAsync([adbPath, "pair", endpoint, secret], text => output.Append(text), pairStop.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                $"Pairing with {endpoint} did not finish within {PairingAttemptTimeout.TotalSeconds:0} seconds. The phone " +
                $"probably closed its pairing dialog or stopped answering. Press Connect with QR code for a fresh code " +
                $"and leave the phone's dialog open until it reports the pairing.{Environment.NewLine}{output}");
        }

        if (exitCode == 0)
        {
            return $"Paired with {endpoint}. Now connect using the debugging port shown on the device.{Environment.NewLine}{output}";
        }

        var text = output.ToString();
        var failure = AndroidWirelessDebug.ReadPairingFailure(text);
        var guidance = failure is null
            ? string.Empty
            : $"{failure.Cause} {failure.Fix}{Environment.NewLine}";
        throw new InvalidOperationException(
            $"Pairing with {endpoint} failed.{Environment.NewLine}" +
            $"{guidance}" +
            $"Check that {secretDescription} is the one the device is expecting, that the pairing port is still open " +
            "(it closes when the dialog closes or the pairing times out), and that adb is platform-tools 30.0.0 or newer. " +
            "An address copied from an earlier scan goes stale when wireless debugging restarts on the phone, so " +
            "scan again before pairing." +
            $"{Environment.NewLine}{text}");
    }

    /// <summary>
    /// Connects to a device that is already paired (or to a legacy adb tcpip port) and reports whether
    /// the transport actually became usable.
    ///
    /// <c>adb connect</c> is not a success signal on its own: it exits 0 and prints `connected to`,
    /// `already connected to`, `failed to connect`, or `failed to authenticate` while the outcome is
    /// decided by what the device list afterwards says. The state of that row (`device`, `offline`,
    /// `unauthorized`) is therefore read after every attempt, and only `device` counts as connected.
    /// </summary>
    /// <summary>
    /// Connects to the debugging port a host publishes, trying every endpoint advertised for that host
    /// and then <paramref name="fallbackAddress"/>.
    ///
    /// One host can be listed twice with different ports: the adb server's mDNS cache can hold the
    /// port from before wireless debugging restarted while the system resolver holds the current one,
    /// and there is no way to tell a cached record from a live one without trying. The first endpoint
    /// that becomes a usable target wins, so a stale record costs one attempt instead of the whole
    /// connection.
    /// </summary>
    public async Task<AndroidWirelessConnectResult> ConnectWirelessHostAsync(
        string host,
        string? fallbackAddress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new InvalidOperationException(
                "A Wi-Fi device is identified by its address, so the host part is required. Find it under Wireless debugging on the phone.");
        }

        IReadOnlyList<AndroidWirelessService> services = [];
        try
        {
            services = await DiscoverAllWirelessServicesAsync(cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Discovery failing still leaves the caller's address to try.
        }

        var addresses = AndroidWirelessDebug.ConnectCandidates(services, host, fallbackAddress);
        if (addresses.Count == 0)
        {
            return new AndroidWirelessConnectResult(host, false, "not listed",
                $"The phone at {host} is not publishing a debugging port right now. Open Developer options > " +
                "Wireless debugging on the phone, keep its screen on, then connect again.");
        }

        AndroidWirelessConnectResult? last = null;
        foreach (var address in addresses)
        {
            var result = await ConnectWirelessDeviceAsync(address, cancellationToken);
            if (result.Connected || result.NeedsApproval)
            {
                return result;
            }

            last = result;
        }

        return last!;
    }

    public async Task<AndroidWirelessConnectResult> ConnectWirelessDeviceAsync(string address, CancellationToken cancellationToken)
    {
        var adbPath = _adbPath ?? throw new InvalidOperationException(MissingAdb);
        if (!AndroidWirelessDebug.TryNormalizeAddress(address, out var endpoint, out var addressError))
        {
            throw new InvalidOperationException(addressError);
        }

        // A half-open transport for this endpoint is what makes a phone sit on its "connecting"
        // spinner: the phone believes a session is starting while the host reports nothing it can
        // use, and a fresh `adb connect` against that endpoint is refused until it is dropped. The
        // documented reset is to disconnect the endpoint, and it costs nothing when there is none.
        await TryDropWirelessTransportAsync([adbPath, "disconnect", endpoint], cancellationToken);

        // Right after pairing, the phone's TLS server can take a moment to accept connections, and
        // the row adb creates for it starts as `offline` before it settles. Retry briefly.
        const int attempts = 6;
        string? lastText = null;
        string? lastState = null;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            var output = new StringBuilder();
            await RunAdbAsync([adbPath, "connect", endpoint], text => output.Append(text), cancellationToken);
            lastText = output.ToString();

            var entries = await ProbeAndroidDevicesAsync(cancellationToken);
            lastState = AndroidWirelessDebug.FindWirelessTargetState(entries, endpoint);
            if (string.Equals(lastState, AndroidDeviceState.Ready, StringComparison.Ordinal))
            {
                return new AndroidWirelessConnectResult(endpoint, true, AndroidDeviceState.Ready,
                    $"Connected to {endpoint}. The phone accepts adb commands now.{Environment.NewLine}{lastText}");
            }

            // The phone is reachable and wants this computer to approve its key, so another attempt
            // would produce the same row. Say what the phone has to show instead of retrying.
            if (string.Equals(lastState, AndroidDeviceState.Unauthorized, StringComparison.Ordinal))
            {
                return new AndroidWirelessConnectResult(endpoint, false, AndroidDeviceState.Unauthorized,
                    $"The phone at {endpoint} answered but has not approved this computer. Unlock the phone and " +
                    $"accept the 'Allow USB debugging?' prompt on the phone, or pair again (Quick settings > Wireless debugging > " +
                    $"pair this computer).{Environment.NewLine}{lastText}");
            }

            if (attempt < attempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }

        var failure = AndroidWirelessDebug.ReadConnectFailure(lastText);
        if (failure is null)
        {
            // adb printed nothing specific enough, and the endpoint is listed but not usable. A probe is
            // what separates a port that answers (the phone refuses this computer's key) from one that
            // does not (the address or the port is stale), and the two need different actions.
            var probe = await ProbeEndpointAsync(endpoint, EndpointProbeTimeout, cancellationToken);
            failure = AndroidWirelessDebug.ReadStalledTransport(lastState, probe.Reachable);
        }

        var guidance = failure is null
            ? "Use the debugging port from Wireless debugging (it differs from the pairing port), keep the device " +
              "awake on the same network, and pair first for a new computer."
            : $"{failure.Cause} {failure.Fix}";
        var stateNote = lastState is null
            ? "The phone is not in the target list at all."
            : $"adb lists it as `{lastState}`.";
        return new AndroidWirelessConnectResult(endpoint, false, lastState ?? "not listed",
            $"Could not connect to {endpoint}. {stateNote} {guidance}{Environment.NewLine}{lastText}");
    }

    public async Task<string> DisconnectWirelessDeviceAsync(string address, CancellationToken cancellationToken)
    {
        var adbPath = _adbPath ?? throw new InvalidOperationException(MissingAdb);
        if (!AndroidWirelessDebug.TryNormalizeAddress(address, out var endpoint, out var addressError))
        {
            throw new InvalidOperationException(addressError);
        }

        var output = new StringBuilder();
        var exitCode = await RunAdbAsync([adbPath, "disconnect", endpoint], text => output.Append(text), cancellationToken);
        return exitCode == 0
            ? $"Disconnected from {endpoint}.{Environment.NewLine}{output}"
            : $"Failed to disconnect from {endpoint}.{Environment.NewLine}{output}";
    }

    /// <summary>
    /// Enables TCP/IP debugging on a USB-connected device (Android 10 and older need this step) and
    /// reports the address to connect to.
    /// </summary>
    public async Task<AndroidWirelessEnableResult> EnableWirelessDebuggingAsync(string deviceSerial, int port, CancellationToken cancellationToken)
    {
        var adbPath = _adbPath ?? throw new InvalidOperationException(MissingAdb);
        if (string.IsNullOrWhiteSpace(deviceSerial))
        {
            throw new InvalidOperationException("Connect the device over USB first, then select it.");
        }

        var serial = deviceSerial.Trim();
        var output = new StringBuilder();
        var exitCode = await RunAdbAsync([adbPath, "-s", serial, "tcpip", port.ToString()], text => output.Append(text), cancellationToken);
        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"Could not switch {serial} to TCP/IP port {port}. On Android 11 and newer use Wireless debugging " +
                $"and Pair instead.{Environment.NewLine}{output}");
        }

        var wifiIp = await ReadWifiIpAddressAsync(adbPath, serial, cancellationToken);
        var address = wifiIp is null ? null : $"{wifiIp}:{port}";
        var message = $"Wireless debugging is enabled on {serial}. Disconnect USB, then connect to {address ?? $"<device-ip>:{port}"}." +
            (wifiIp is null
                ? " The Wi-Fi address could not be read (the device may be on mobile data only); check Settings > About phone > Status for the IP address."
                : string.Empty) +
            $"{Environment.NewLine}{output}";
        return new AndroidWirelessEnableResult(address, message);
    }

    private async Task<string?> ReadWifiIpAddressAsync(string adbPath, string serial, CancellationToken cancellationToken)
    {
        try
        {
            var routeOutput = new StringBuilder();
            await RunAdbAsync([adbPath, "-s", serial, "shell", "ip", "route"], text => routeOutput.Append(text), cancellationToken);
            var address = AndroidWirelessDebug.ExtractWifiIpAddress(routeOutput.ToString());
            if (address is not null)
            {
                return address;
            }

            var addrOutput = new StringBuilder();
            await RunAdbAsync([adbPath, "-s", serial, "shell", "ip", "-f", "inet", "addr", "show"], text => addrOutput.Append(text), cancellationToken);
            var addrAddress = AndroidWirelessDebug.ExtractWifiIpAddressFromIpAddr(addrOutput.ToString());
            if (addrAddress is not null)
            {
                return addrAddress;
            }

            var propOutput = new StringBuilder();
            await RunAdbAsync([adbPath, "-s", serial, "shell", "getprop"], text => propOutput.Append(text), cancellationToken);
            return AndroidWirelessDebug.ExtractWifiIpAddressFromProperties(propOutput.ToString());
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Reads model and Android version for one target with a single `getprop` call.</summary>
    private async Task<AndroidDeviceProperties?> ReadDevicePropertiesAsync(string serial, CancellationToken cancellationToken)
    {
        if (_adbPath is null)
        {
            return null;
        }

        try
        {
            var output = new StringBuilder();
            var exitCode = await RunAdbAsync(
                [_adbPath, "-s", serial, "shell", "getprop"],
                text => output.Append(text),
                cancellationToken);
            return exitCode == 0 ? AndroidDeviceProperties.Parse(output.ToString()) : null;
        }
        catch
        {
            // A device can disappear mid-scan; discovery must still report the other targets.
            return null;
        }
    }

    /// <summary>
    /// Reads the launcher component (`package/activity`) from `cmd package resolve-activity --brief`.
    /// Returns null when the package has no launcher, so the caller can fall back to `monkey`.
    /// </summary>
    public static string? ResolveLauncherComponent(string? resolveActivityOutput)
    {
        if (string.IsNullOrWhiteSpace(resolveActivityOutput))
        {
            return null;
        }

        foreach (var rawLine in resolveActivityOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (rawLine.Contains('/') && !rawLine.Contains(' ') && rawLine.Contains('.', StringComparison.Ordinal))
            {
                return rawLine;
            }
        }

        return null;
    }

    /// <summary>
    /// Picks the target used when nothing is selected yet: a ready physical device, then a running
    /// emulator. Offline and unauthorized targets are never chosen.
    /// </summary>
    public static string? SelectPreferredSerial(IReadOnlyList<AndroidDeviceInfo> devices) =>
        devices
            .Where(device => device.IsReady && !string.IsNullOrEmpty(device.Serial))
            .OrderBy(AndroidDeviceInfo.Rank)
            .ThenBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
            .Select(device => device.Serial)
            .FirstOrDefault();

    public static string? GetFirstRunningDeviceSerial(IReadOnlyList<AndroidDeviceInfo> devices) =>
        SelectPreferredSerial(devices);

    private async Task<string?> GetAvdNameAsync(string serial, CancellationToken cancellationToken)
    {
        if (_adbPath is null)
        {
            return null;
        }

        try
        {
            var output = new StringBuilder();
            var exitCode = await RunAdbAsync([_adbPath, "-s", serial, "emu", "avd", "name"], text => output.Append(text), cancellationToken);
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

    /// <summary>
    /// Verifies the APK signature and returns its certificate SHA-256 digests. Returns null when
    /// apksigner is unavailable (install then relies on Android's own checks, as before).
    /// </summary>
    private async Task<IReadOnlyList<string>?> ReadApkCertificateDigestsAsync(string apkPath, CancellationToken cancellationToken)
    {
        var apkSignerPath = _apkSignerPath();
        if (string.IsNullOrWhiteSpace(apkSignerPath))
        {
            return null;
        }

        var output = new StringBuilder();
        var exitCode = await ProcessRunner.RunAsync(
            [apkSignerPath, "verify", "--print-certs", apkPath],
            text => output.Append(text),
            cancellationToken);
        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"APK signature verification failed for `{Path.GetFileName(apkPath)}`. " +
                "The APK appears unsigned or signed incorrectly. " +
                "Publish with proper signing (or use a debug-signed APK) and retry." +
                $"{Environment.NewLine}{output}");
        }

        var digests = AndroidSigningInspector.ParseApkSignerDigests(output.ToString());
        return digests.Count > 0 ? digests : null;
    }

    /// <summary>
    /// Reads what the device says about an already-installed package: the installer of record
    /// (Play Store is `com.android.vending`) and the signing certificates of its installed base APK.
    /// Returns null when the package is not installed; certificate digests stay empty when the
    /// installed APK cannot be pulled or inspected, in which case the caller falls back to adb's own
    /// signature check during install.
    /// </summary>
    private async Task<AndroidInstalledPackageInfo?> ReadInstalledPackageAsync(
        string adbPath,
        string targetDevice,
        string packageId,
        CancellationToken cancellationToken)
    {
        var pathOutput = new StringBuilder();
        await RunAdbAsync(
            [adbPath, "-s", targetDevice, "shell", "pm", "path", packageId],
            text => pathOutput.Append(text),
            cancellationToken);
        var installedApkPath = AndroidSigningInspector.ParseBaseApkPath(pathOutput.ToString());
        if (installedApkPath is null)
        {
            return null;
        }

        var installerOutput = new StringBuilder();
        await RunAdbAsync(
            [adbPath, "-s", targetDevice, "shell", "pm", "list", "packages", "-i", packageId, "--show-versioncode"],
            text => installerOutput.Append(text),
            cancellationToken);
        var installerPackage = AndroidSigningInspector.ParseInstallerPackage(installerOutput.ToString(), packageId);
        var versionCode = AndroidSigningInspector.ParseVersionCode(installerOutput.ToString());

        IReadOnlyList<string> certificateDigests = [];
        var apkSignerPath = _apkSignerPath();
        var temporaryCopy = Path.Combine(Path.GetTempPath(), $"dnap-installed-{Guid.NewGuid():N}.apk");
        try
        {
            if (!string.IsNullOrWhiteSpace(apkSignerPath))
            {
                var pullOutput = new StringBuilder();
                var pullExitCode = await RunAdbAsync(
                    [adbPath, "-s", targetDevice, "pull", installedApkPath, temporaryCopy],
                    text => pullOutput.Append(text),
                    cancellationToken);
                if (pullExitCode == 0 && File.Exists(temporaryCopy))
                {
                    var certOutput = new StringBuilder();
                    var certExitCode = await ProcessRunner.RunAsync(
                        [apkSignerPath, "verify", "--print-certs", temporaryCopy],
                        text => certOutput.Append(text),
                        cancellationToken);
                    if (certExitCode == 0)
                    {
                        certificateDigests = AndroidSigningInspector.ParseApkSignerDigests(certOutput.ToString());
                    }
                }
            }
        }
        finally
        {
            try
            {
                File.Delete(temporaryCopy);
            }
            catch
            {
                // Temporary copy cleanup must never fail an install.
            }
        }

        return new AndroidInstalledPackageInfo(installerPackage, certificateDigests)
        {
            VersionCode = versionCode
        };
    }

    /// <summary>
    /// Success-message note stating the APK's signing fingerprint and what it means for future
    /// Google Play updates.
    ///
    /// Google Play signs every download with the app signing key, not the upload key. A build
    /// signed with the upload key is a different signer to the platform, so Android refuses to
    /// update it. The note says so plainly, because "the key I uploaded with" is the wrong key to
    /// think about here.
    /// </summary>
    private static string? BuildInstallSignatureNote(
        IReadOnlyList<string>? apkDigests,
        AndroidInstalledPackageInfo? installedPackage)
    {
        if (apkDigests is not { Count: > 0 })
        {
            return null;
        }

        var fingerprint = $"Signing certificate SHA-256: {string.Join(", ", apkDigests)}.";
        if (installedPackage is { CertificateDigests.Count: > 0 }
            && AndroidSigningInspector.IsGooglePlayInstaller(installedPackage.InstallerPackage))
        {
            return $"{fingerprint} It matches the copy installed by Google Play, so Play Store updates keep " +
                "working; keep every future version code higher than the last.";
        }

        return $"{fingerprint} Google Play signs its downloads with the app signing key, not the upload key. " +
            "If this build was signed with the upload key, a Play Store update cannot replace it. Sign it " +
            "with the app signing key, or install the signed universal APK from Play Console, and keep every " +
            "version code higher than the last.";
    }
}
