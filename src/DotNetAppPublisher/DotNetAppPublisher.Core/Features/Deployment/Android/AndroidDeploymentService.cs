using System.Globalization;
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
        var adbPath = _adbPath ?? throw new InvalidOperationException("`adb` was not found. Install Android platform-tools or add adb to PATH.");
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
        var exitCode = await ProcessRunner.RunAsync(arguments, Append, cancellationToken);

        // Older devices reject streaming installs; retry once with the APK copied to the device first.
        if (exitCode != 0 && AndroidInstallDiagnostics.IsStreamingFailure(log.ToString()))
        {
            Append($"Streaming install is not supported on this device. Retrying with --no-streaming...{Environment.NewLine}");
            arguments.Insert(arguments.Count - 1, "--no-streaming");
            exitCode = await ProcessRunner.RunAsync(arguments, Append, cancellationToken);
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
        var adbPath = _adbPath ?? throw new InvalidOperationException("`adb` was not found. Install Android platform-tools or add adb to PATH.");
        if (string.IsNullOrWhiteSpace(packageId))
        {
            throw new InvalidOperationException("Package id is required to launch the app.");
        }

        var targetDevice = !string.IsNullOrWhiteSpace(deviceSerial)
            ? deviceSerial!.Trim()
            : throw new InvalidOperationException("No device selected. Select a device first.");
        var package = packageId.Trim();

        var resolveOutput = new StringBuilder();
        await ProcessRunner.RunAsync(
            [adbPath, "-s", targetDevice, "shell", "cmd", "package", "resolve-activity", "--brief", "-c", "android.intent.category.LAUNCHER", package],
            text => resolveOutput.Append(text),
            cancellationToken);

        var component = ResolveLauncherComponent(resolveOutput.ToString());
        var output = new StringBuilder();
        var exitCode = component is null
            ? await ProcessRunner.RunAsync(
                [adbPath, "-s", targetDevice, "shell", "monkey", "-p", package, "-c", "android.intent.category.LAUNCHER", "1"],
                text => output.Append(text),
                cancellationToken)
            : await ProcessRunner.RunAsync(
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
        var adbPath = _adbPath ?? throw new InvalidOperationException("`adb` was not found. Install Android platform-tools or add adb to PATH.");
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
            var exitCode = await ProcessRunner.RunAsync(
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
        var exitCode = await ProcessRunner.RunAsync(
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
        var exitCode = await ProcessRunner.RunAsync(
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
            var adbExitCode = await ProcessRunner.RunAsync([_adbPath, "devices", "-l"], text => adbOutput.Append(text), cancellationToken);
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
        var adbPath = _adbPath ?? throw new InvalidOperationException("`adb` was not found. Install Android platform-tools or add adb to PATH.");
        var output = new StringBuilder();
        var exitCode = await ProcessRunner.RunAsync([adbPath, "devices", "-l"], text => output.Append(text), cancellationToken);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"Failed to query Android devices with adb (exit code {exitCode}).{Environment.NewLine}{output}");
        }

        return AndroidDeviceListParser.Parse(output.ToString());
    }

    /// <summary>Lists the wireless debugging services the devices advertise over mDNS.</summary>
    public async Task<IReadOnlyList<AndroidWirelessService>> DiscoverWirelessServicesAsync(CancellationToken cancellationToken)
    {
        var adbPath = _adbPath ?? throw new InvalidOperationException("`adb` was not found. Install Android platform-tools or add adb to PATH.");
        var output = new StringBuilder();
        var exitCode = await ProcessRunner.RunAsync([adbPath, "mdns", "services"], text => output.Append(text), cancellationToken);
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
        var adbPath = _adbPath ?? throw new InvalidOperationException("`adb` was not found. Install Android platform-tools or add adb to PATH.");
        if (!AndroidWirelessDebug.TryNormalizeAddress(address, out var endpoint, out var addressError))
        {
            throw new InvalidOperationException(addressError);
        }

        var output = new StringBuilder();
        var exitCode = await ProcessRunner.RunAsync([adbPath, "pair", endpoint, secret], text => output.Append(text), cancellationToken);
        if (exitCode == 0)
        {
            return $"Paired with {endpoint}. Now connect using the debugging port shown on the device.{Environment.NewLine}{output}";
        }

        throw new InvalidOperationException(
            $"Pairing with {endpoint} failed.{Environment.NewLine}" +
            $"Check that {secretDescription} is the one the device is expecting, that the pairing port is still open " +
            "(it closes when the dialog closes or the pairing times out), and that adb is platform-tools 30.0.0 or newer." +
            $"{Environment.NewLine}{output}");
    }

    /// <summary>Connects to a device that is already paired (or to a legacy adb tcpip port).</summary>
    public async Task<string> ConnectWirelessDeviceAsync(string address, CancellationToken cancellationToken)
    {
        var adbPath = _adbPath ?? throw new InvalidOperationException("`adb` was not found. Install Android platform-tools or add adb to PATH.");
        if (!AndroidWirelessDebug.TryNormalizeAddress(address, out var endpoint, out var addressError))
        {
            throw new InvalidOperationException(addressError);
        }

        // Right after pairing, the phone's debugging service can take a moment before it accepts
        // connections. Retry a few times with a short wait before giving up.
        const int attempts = 3;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            var output = new StringBuilder();
            var exitCode = await ProcessRunner.RunAsync([adbPath, "connect", endpoint], text => output.Append(text), cancellationToken);
            var text = output.ToString();
            if (exitCode == 0 && !text.Contains("failed", StringComparison.OrdinalIgnoreCase))
            {
                return $"Connected to {endpoint}.{Environment.NewLine}{text}";
            }

            if (attempt < attempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
            else
            {
                throw new InvalidOperationException(
                    $"Could not connect to {endpoint}.{Environment.NewLine}" +
                    "Use the debugging port from Wireless debugging (it differs from the pairing port), keep the device " +
                    "awake on the same network, and pair first for a new computer." +
                    $"{Environment.NewLine}{text}");
            }
        }

        throw new InvalidOperationException("Unexpected exit from the connect retry loop.");
    }

    public async Task<string> DisconnectWirelessDeviceAsync(string address, CancellationToken cancellationToken)
    {
        var adbPath = _adbPath ?? throw new InvalidOperationException("`adb` was not found. Install Android platform-tools or add adb to PATH.");
        if (!AndroidWirelessDebug.TryNormalizeAddress(address, out var endpoint, out var addressError))
        {
            throw new InvalidOperationException(addressError);
        }

        var output = new StringBuilder();
        var exitCode = await ProcessRunner.RunAsync([adbPath, "disconnect", endpoint], text => output.Append(text), cancellationToken);
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
        var adbPath = _adbPath ?? throw new InvalidOperationException("`adb` was not found. Install Android platform-tools or add adb to PATH.");
        if (string.IsNullOrWhiteSpace(deviceSerial))
        {
            throw new InvalidOperationException("Connect the device over USB first, then select it.");
        }

        var serial = deviceSerial.Trim();
        var output = new StringBuilder();
        var exitCode = await ProcessRunner.RunAsync([adbPath, "-s", serial, "tcpip", port.ToString()], text => output.Append(text), cancellationToken);
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
            await ProcessRunner.RunAsync([adbPath, "-s", serial, "shell", "ip", "route"], text => routeOutput.Append(text), cancellationToken);
            var address = AndroidWirelessDebug.ExtractWifiIpAddress(routeOutput.ToString());
            if (address is not null)
            {
                return address;
            }

            var propOutput = new StringBuilder();
            await ProcessRunner.RunAsync([adbPath, "-s", serial, "shell", "getprop"], text => propOutput.Append(text), cancellationToken);
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
            var exitCode = await ProcessRunner.RunAsync(
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
        await ProcessRunner.RunAsync(
            [adbPath, "-s", targetDevice, "shell", "pm", "path", packageId],
            text => pathOutput.Append(text),
            cancellationToken);
        var installedApkPath = AndroidSigningInspector.ParseBaseApkPath(pathOutput.ToString());
        if (installedApkPath is null)
        {
            return null;
        }

        var installerOutput = new StringBuilder();
        await ProcessRunner.RunAsync(
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
                var pullExitCode = await ProcessRunner.RunAsync(
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
