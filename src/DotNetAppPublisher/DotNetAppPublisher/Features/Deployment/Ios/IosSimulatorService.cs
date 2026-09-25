using System.Text;
using System.Text.Json;
using DotNetAppPublisher.Features.Publishing.Process;

namespace DotNetAppPublisher.Features.Deployment.Ios;

public sealed class IosSimulatorService
{
    private readonly string? _xcrunPath;
    private readonly Func<string, DirectoryInfo?> _findNewestAppBundle;

    public IosSimulatorService(string? xcrunPath, Func<string, DirectoryInfo?> findNewestAppBundle)
    {
        _xcrunPath = xcrunPath;
        _findNewestAppBundle = findNewestAppBundle;
    }

    public async Task<IReadOnlyList<string>> DiscoverAsync(CancellationToken cancellationToken)
    {
        var xcrunPath = RequireXcrun();
        var output = new StringBuilder();
        var exitCode = await ProcessRunner.RunAsync(
            [xcrunPath, "simctl", "list", "devices", "available", "--json"],
            text => output.Append(text),
            cancellationToken);
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
            foreach (var device in runtimeDevices.Value.EnumerateArray())
            {
                if (!device.TryGetProperty("isAvailable", out var isAvailableElement) || !isAvailableElement.GetBoolean())
                {
                    continue;
                }

                var name = device.GetProperty("name").GetString();
                var udid = device.GetProperty("udid").GetString();
                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(udid))
                {
                    simulators.Add($"{name} | {udid}");
                }
            }
        }

        return simulators
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<string> LaunchAsync(string simulator, CancellationToken cancellationToken)
    {
        var xcrunPath = RequireXcrun();
        var simulatorId = RequireSimulator(simulator);
        await EnsureBootedAsync(xcrunPath, simulatorId, cancellationToken);
        return $"Launched iOS simulator `{simulator}`.";
    }

    public async Task<string> InstallAppAsync(string outputDirectory, string simulator, CancellationToken cancellationToken)
    {
        var xcrunPath = RequireXcrun();
        var simulatorId = RequireSimulator(simulator);
        var appBundle = _findNewestAppBundle(outputDirectory)
            ?? throw new InvalidOperationException($"No .app bundle found in {outputDirectory}.");
        await EnsureBootedAsync(xcrunPath, simulatorId, cancellationToken);

        var output = new StringBuilder();
        var exitCode = await ProcessRunner.RunAsync(
            [xcrunPath, "simctl", "install", simulatorId, appBundle.FullName],
            text => output.Append(text),
            cancellationToken);
        return exitCode == 0
            ? $"Installed {appBundle.Name} on {simulator}.{Environment.NewLine}{output}"
            : $"iOS app install failed for {appBundle.Name}.{Environment.NewLine}{output}";
    }

    public async Task<string> UninstallAppAsync(string packageId, string simulator, CancellationToken cancellationToken)
    {
        var xcrunPath = RequireXcrun();
        if (string.IsNullOrWhiteSpace(packageId))
        {
            throw new InvalidOperationException("Bundle id is required to uninstall the app.");
        }

        var simulatorId = RequireSimulator(simulator);
        await EnsureBootedAsync(xcrunPath, simulatorId, cancellationToken);
        var output = new StringBuilder();
        var exitCode = await ProcessRunner.RunAsync(
            [xcrunPath, "simctl", "uninstall", simulatorId, packageId.Trim()],
            text => output.Append(text),
            cancellationToken);
        return exitCode == 0
            ? $"Uninstall requested for {packageId} on {simulator}.{Environment.NewLine}{output}"
            : $"iOS uninstall failed for {packageId}.{Environment.NewLine}{output}";
    }

    public async Task<string> LaunchAppAsync(string packageId, string simulator, CancellationToken cancellationToken)
    {
        var xcrunPath = RequireXcrun();
        if (string.IsNullOrWhiteSpace(packageId))
        {
            throw new InvalidOperationException("Bundle id is required to launch the app.");
        }

        var simulatorId = RequireSimulator(simulator);
        await EnsureBootedAsync(xcrunPath, simulatorId, cancellationToken);
        var output = new StringBuilder();
        var exitCode = await ProcessRunner.RunAsync(
            [xcrunPath, "simctl", "launch", simulatorId, packageId.Trim()],
            text => output.Append(text),
            cancellationToken);
        return exitCode == 0
            ? $"Launch requested for {packageId} on {simulator}.{Environment.NewLine}{output}"
            : $"iOS launch failed for {packageId}.{Environment.NewLine}{output}";
    }

    public async Task<string> PushFileAsync(string localFilePath, string simulator, CancellationToken cancellationToken)
    {
        var xcrunPath = RequireXcrun();
        if (string.IsNullOrWhiteSpace(localFilePath))
        {
            throw new InvalidOperationException("File path is required.");
        }

        if (!File.Exists(localFilePath))
        {
            throw new InvalidOperationException($"File not found: {localFilePath}");
        }

        var simulatorId = RequireSimulator(simulator);
        await EnsureBootedAsync(xcrunPath, simulatorId, cancellationToken);
        var output = new StringBuilder();
        var exitCode = await ProcessRunner.RunAsync(
            [xcrunPath, "simctl", "listapps", simulatorId],
            text => output.Append(text),
            cancellationToken);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"Failed to query simulator apps: {output}");
        }

        var fileProviderPath = ExtractFileProviderPath(output.ToString());
        if (string.IsNullOrWhiteSpace(fileProviderPath))
        {
            throw new InvalidOperationException("Could not find File Provider storage path. Make sure the iOS Simulator is running.");
        }

        var storageDir = Path.Combine(fileProviderPath, "File Provider Storage");
        Directory.CreateDirectory(storageDir);
        var targetPath = Path.Combine(storageDir, Path.GetFileName(localFilePath));
        File.Copy(localFilePath, targetPath, true);
        return $"Pushed {Path.GetFileName(localFilePath)} to Files app on {simulator}.{Environment.NewLine}Location: On My iPhone";
    }

    public static string ExtractSimulatorId(string simulator)
    {
        var separatorIndex = simulator.LastIndexOf('|');
        return separatorIndex < 0
            ? simulator.Trim()
            : simulator[(separatorIndex + 1)..].Trim();
    }

    private string RequireXcrun() => _xcrunPath ?? throw new InvalidOperationException(
        "`xcrun` was not found. Install Xcode command line tools to use iOS simulator actions.");

    private static string RequireSimulator(string simulator)
    {
        if (string.IsNullOrWhiteSpace(simulator))
        {
            throw new InvalidOperationException("Select an iOS simulator first.");
        }

        return ExtractSimulatorId(simulator);
    }

    private static async Task EnsureBootedAsync(string xcrunPath, string simulatorId, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsMacOS())
        {
            _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("open", "-a Simulator")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }

        var bootOutput = new StringBuilder();
        var bootExitCode = await ProcessRunner.RunAsync(
            [xcrunPath, "simctl", "boot", simulatorId],
            text => bootOutput.Append(text),
            cancellationToken);
        if (bootExitCode != 0 && !bootOutput.ToString().Contains("Booted", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"iOS simulator boot command returned {bootExitCode}.{Environment.NewLine}{bootOutput}");
        }

        var statusOutput = new StringBuilder();
        var statusExitCode = await ProcessRunner.RunAsync(
            [xcrunPath, "simctl", "bootstatus", simulatorId, "-b"],
            text => statusOutput.Append(text),
            cancellationToken);
        if (statusExitCode != 0)
        {
            throw new InvalidOperationException($"iOS simulator boot status check failed ({statusExitCode}).{Environment.NewLine}{statusOutput}");
        }
    }

    private static string? ExtractFileProviderPath(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            foreach (var app in document.RootElement.EnumerateObject())
            {
                if (app.Value.TryGetProperty("CFBundleIdentifier", out var bundleId)
                    && bundleId.GetString() == "com.apple.FileProvider"
                    && app.Value.TryGetProperty("GroupContainers", out var groupContainers)
                    && groupContainers.TryGetProperty("group.com.apple.FileProvider.LocalStorage", out var localStorage))
                {
                    var path = localStorage.GetString();
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        return path.Replace("file://", string.Empty);
                    }
                }
            }
        }
        catch
        {
        }

        return null;
    }
}
