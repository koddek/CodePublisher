using System.Text;
using CodePublisher.Features.Publishing.Process;

namespace CodePublisher.Features.Deployment.Ios;

/// <summary>
/// Deploys a built app bundle to a real iPhone or iPad through CoreDevice (`xcrun devicectl`).
/// Pairing, trust, Developer Mode, and code signing stay user-owned prerequisites: this service
/// reports exactly which one is missing instead of pretending the device is usable.
/// </summary>
public sealed class IosDeviceService
{
    private readonly string? _xcrunPath;
    private readonly Func<string, DirectoryInfo?> _findNewestAppBundle;

    public IosDeviceService(string? xcrunPath, Func<string, DirectoryInfo?> findNewestAppBundle)
    {
        _xcrunPath = xcrunPath;
        _findNewestAppBundle = findNewestAppBundle;
    }

    /// <summary>
    /// Lists real devices. CoreDevice is the primary source; `xctrace` covers devices that CoreDevice
    /// does not know about.
    /// </summary>
    public async Task<IReadOnlyList<IosDeviceInfo>> DiscoverAsync(CancellationToken cancellationToken)
    {
        var xcrunPath = RequireXcrun();
        var jsonPath = Path.Combine(Path.GetTempPath(), $"devicectl-devices-{Guid.NewGuid():N}.json");
        try
        {
            var output = new StringBuilder();
            var exitCode = await ProcessRunner.RunAsync(
                [xcrunPath, "devicectl", "list", "devices", "--json-output", jsonPath],
                text => output.Append(text),
                cancellationToken);

            var devices = new List<IosDeviceInfo>();
            if (exitCode == 0 && File.Exists(jsonPath))
            {
                devices.AddRange(IosDeviceListParser.ParseDevicectlJson(await File.ReadAllTextAsync(jsonPath, cancellationToken)));
            }

            if (devices.Count > 0)
            {
                return devices
                    .OrderBy(IosDeviceInfo.Rank)
                    .ThenBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }

            var fallbackOutput = new StringBuilder();
            var fallbackExitCode = await ProcessRunner.RunAsync(
                [xcrunPath, "xctrace", "list", "devices"],
                text => fallbackOutput.Append(text),
                cancellationToken);
            if (fallbackExitCode == 0)
            {
                devices.AddRange(IosDeviceListParser.ParseXctraceList(fallbackOutput.ToString()));
            }

            if (devices.Count == 0 && exitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Failed to list iOS devices with devicectl (exit code {exitCode}). Install Xcode 15 or newer and select it with `xcode-select`." +
                    $"{Environment.NewLine}{output}");
            }

            return devices
                .OrderBy(IosDeviceInfo.Rank)
                .ThenBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        finally
        {
            TryDelete(jsonPath);
        }
    }

    /// <summary>Installs the newest built .app bundle on a real device. The app must already be signed for it.</summary>
    public async Task<string> InstallAppAsync(string outputDirectory, string udid, CancellationToken cancellationToken)
    {
        var xcrunPath = RequireXcrun();
        var device = RequireDevice(udid);
        var appBundle = _findNewestAppBundle(outputDirectory)
            ?? throw new InvalidOperationException($"No .app bundle found in {outputDirectory}.");

        var output = new StringBuilder();
        var exitCode = await ProcessRunner.RunAsync(
            [xcrunPath, "devicectl", "device", "install", "app", "--device", device, appBundle.FullName],
            text => output.Append(text),
            cancellationToken);

        if (exitCode == 0)
        {
            return $"Installed {appBundle.Name} on iOS device {device}.{Environment.NewLine}{output}";
        }

        throw new InvalidOperationException(
            $"Installing {appBundle.Name} on iOS device {device} failed.{Environment.NewLine}" +
            "A real device only accepts an app signed with a development profile that includes this device " +
            "(and Developer Mode must be on). For a simulator build, use the simulator actions instead." +
            $"{Environment.NewLine}{output}");
    }

    public async Task<string> LaunchAppAsync(string bundleId, string udid, CancellationToken cancellationToken)
    {
        var xcrunPath = RequireXcrun();
        if (string.IsNullOrWhiteSpace(bundleId))
        {
            throw new InvalidOperationException("Bundle id is required to launch the app.");
        }

        var device = RequireDevice(udid);
        var output = new StringBuilder();
        var exitCode = await ProcessRunner.RunAsync(
            [xcrunPath, "devicectl", "device", "process", "launch", "--device", device, bundleId.Trim()],
            text => output.Append(text),
            cancellationToken);

        return exitCode == 0
            ? $"Launch requested for {bundleId} on iOS device {device}.{Environment.NewLine}{output}"
            : $"iOS launch failed for {bundleId} on {device}.{Environment.NewLine}{output}";
    }

    /// <summary>
    /// Force-quits the app on a real device. CoreDevice has no "terminate by bundle id" command, so
    /// this resolves the installed bundle path, finds the running processes whose executable lives
    /// inside it, and signals each with SIGKILL. The installed app and its data are kept; an app that
    /// is not running is reported as "nothing to stop" instead of an error.
    /// </summary>
    public async Task<string> TerminateAppAsync(string bundleId, string udid, CancellationToken cancellationToken)
    {
        var xcrunPath = RequireXcrun();
        if (string.IsNullOrWhiteSpace(bundleId))
        {
            throw new InvalidOperationException("Bundle id is required to stop the app.");
        }

        var device = RequireDevice(udid);
        var package = bundleId.Trim();

        var appsJson = await ReadInfoJsonAsync(xcrunPath, device, "apps", cancellationToken);
        var bundlePath = IosStopParsers.FindAppBundlePath(appsJson, package);
        if (string.IsNullOrWhiteSpace(bundlePath))
        {
            throw new InvalidOperationException(
                $"{package} is not installed on iOS device {device}, so there is nothing to stop. Install it first.");
        }

        var processesJson = await ReadInfoJsonAsync(xcrunPath, device, "processes", cancellationToken);
        var pids = IosStopParsers.FindRunningPids(processesJson, bundlePath);
        if (pids.Count == 0)
        {
            return $"{package} is not running on iOS device {device} (nothing to stop).";
        }

        var log = new StringBuilder();
        foreach (var pid in pids)
        {
            var signalOutput = new StringBuilder();
            var signalExitCode = await ProcessRunner.RunAsync(
                [xcrunPath, "devicectl", "device", "process", "signal", "--device", device,
                    "--pid", pid.ToString(System.Globalization.CultureInfo.InvariantCulture), "--signal", "SIGKILL"],
                text => signalOutput.Append(text),
                cancellationToken);
            log.Append($"SIGKILL to pid {pid}: exit code {signalExitCode}.{Environment.NewLine}{signalOutput}");
            if (signalExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Failed to stop {package} on iOS device {device} (signalling pid {pid} failed). " +
                    $"Check that the device is unlocked and still connected.{Environment.NewLine}{log}");
            }
        }

        return $"Stopped {package} on iOS device {device} ({pids.Count} process(es) terminated). " +
            $"The app and its data are kept.{Environment.NewLine}{log}";
    }

    /// <summary>Runs `devicectl device info <kind>` with JSON output into a temp file and returns it.</summary>
    private async Task<string> ReadInfoJsonAsync(string xcrunPath, string device, string kind, CancellationToken cancellationToken)
    {
        var jsonPath = Path.Combine(Path.GetTempPath(), $"devicectl-{kind}-{Guid.NewGuid():N}.json");
        try
        {
            var output = new StringBuilder();
            var exitCode = await ProcessRunner.RunAsync(
                [xcrunPath, "devicectl", "device", "info", kind, "--device", device, "--json-output", jsonPath],
                text => output.Append(text),
                cancellationToken);
            if (exitCode != 0 || !File.Exists(jsonPath))
            {
                throw new InvalidOperationException(
                    $"Could not read {kind} from iOS device {device} (exit code {exitCode}). " +
                    $"Check that the device is paired, unlocked, and reachable.{Environment.NewLine}{output}");
            }

            return await File.ReadAllTextAsync(jsonPath, cancellationToken);
        }
        finally
        {
            TryDelete(jsonPath);
        }
    }

    private string RequireXcrun() => _xcrunPath ?? throw new InvalidOperationException(
        "`xcrun` was not found. Install Xcode command line tools to use iOS device actions.");

    private static string RequireDevice(string udid) => string.IsNullOrWhiteSpace(udid)
        ? throw new InvalidOperationException("Select an iOS device first.")
        : udid.Trim();

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}
