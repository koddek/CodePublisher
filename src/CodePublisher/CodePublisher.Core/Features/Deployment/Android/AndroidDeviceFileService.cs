using System.Text;
using CodePublisher.Features.Publishing.Process;

namespace CodePublisher.Features.Deployment.Android;

/// <summary>
/// Reads and changes files in the public folders of a real device or emulator through adb. Every
/// operation needs an explicit device serial and stays inside shared storage (/sdcard).
/// </summary>
public sealed class AndroidDeviceFileService
{
    private readonly string? _adbPath;

    public AndroidDeviceFileService(string? adbPath)
    {
        _adbPath = adbPath;
    }

    public async Task<IReadOnlyList<AndroidDeviceFileEntry>> ListAsync(
        string serial,
        string remotePath,
        CancellationToken cancellationToken)
    {
        var adbPath = RequireAdb();
        var target = RequireSerial(serial);
        var directory = AndroidStorageLocations.NormalizeRemotePath(remotePath);

        var output = new StringBuilder();
        var exitCode = await ProcessRunner.RunAsync(
            [adbPath, "-s", target, "shell", $"ls -la {AndroidStorageLocations.QuoteForRemoteShell(directory)}"],
            text => output.Append(text),
            cancellationToken);
        var text = output.ToString();

        if (text.Contains("unknown option", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"This device's Android version does not support `ls -la`, so the file browser cannot list {directory}. " +
                "Browsing needs Android 7 or newer.");
        }

        if (exitCode != 0)
        {
            throw new InvalidOperationException(DescribeListingFailure(directory, text));
        }

        return AndroidDeviceFileListParser.Parse(text, directory);
    }

    public async Task<string> DeleteAsync(
        string serial,
        string remotePath,
        bool isDirectory,
        CancellationToken cancellationToken)
    {
        var adbPath = RequireAdb();
        var target = RequireSerial(serial);
        var path = RequireChangeablePath(remotePath);

        var command = isDirectory
            ? $"rm -rf {AndroidStorageLocations.QuoteForRemoteShell(path)}"
            : $"rm -f {AndroidStorageLocations.QuoteForRemoteShell(path)}";

        var output = new StringBuilder();
        var exitCode = await ProcessRunner.RunAsync(
            [adbPath, "-s", target, "shell", command],
            text => output.Append(text),
            cancellationToken);
        var text = output.ToString().Trim();

        // `rm` is silent on success, and older adb builds do not always propagate the remote exit code,
        // so a named error in the output is treated as a failure too.
        if (exitCode != 0 || HasDeleteError(text))
        {
            throw new InvalidOperationException($"Could not delete {path} on {target}.{Environment.NewLine}{text}");
        }

        return $"Deleted {(isDirectory ? "folder" : "file")} {path} from {target}.";
    }

    /// <summary>Copies local files or folders into a device folder, creating the folder when needed.</summary>
    public async Task<string> UploadAsync(
        string serial,
        IReadOnlyList<string> localPaths,
        string remoteDirectory,
        CancellationToken cancellationToken)
    {
        var adbPath = RequireAdb();
        var target = RequireSerial(serial);
        var directory = RequireChangeablePath(remoteDirectory);

        if (localPaths.Count == 0)
        {
            throw new InvalidOperationException("Choose at least one file or folder to send to the device.");
        }

        foreach (var localPath in localPaths)
        {
            if (!File.Exists(localPath) && !Directory.Exists(localPath))
            {
                throw new InvalidOperationException($"Not found on this computer: {localPath}");
            }
        }

        await EnsureRemoteDirectoryAsync(adbPath, target, directory, cancellationToken);

        var log = new StringBuilder();
        var destination = AndroidStorageLocations.AsRemoteDirectory(directory);
        foreach (var localPath in localPaths)
        {
            var output = new StringBuilder();
            var exitCode = await ProcessRunner.RunAsync(
                [adbPath, "-s", target, "push", localPath, destination],
                text => output.Append(text),
                cancellationToken);
            log.Append(output);
            if (exitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Failed to send {Path.GetFileName(localPath)} to {directory} on {target}.{Environment.NewLine}{output}");
            }
        }

        var count = localPaths.Count;
        return $"Sent {count} item{(count == 1 ? string.Empty : "s")} to {directory} on {target}.{Environment.NewLine}{log}";
    }

    /// <summary>Copies a device file or folder to a folder on this computer.</summary>
    public async Task<string> DownloadAsync(
        string serial,
        string remotePath,
        string localDirectory,
        CancellationToken cancellationToken)
    {
        var adbPath = RequireAdb();
        var target = RequireSerial(serial);
        var path = AndroidStorageLocations.NormalizeRemotePath(remotePath);

        if (string.IsNullOrWhiteSpace(localDirectory) || !Directory.Exists(localDirectory))
        {
            throw new InvalidOperationException($"Folder not found on this computer: {localDirectory}");
        }

        var output = new StringBuilder();
        var exitCode = await ProcessRunner.RunAsync(
            [adbPath, "-s", target, "pull", path, localDirectory],
            text => output.Append(text),
            cancellationToken);

        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"Failed to copy {path} from {target} to {localDirectory}.{Environment.NewLine}{output}");
        }

        return $"Saved {Path.GetFileName(path)} from {target} to {localDirectory}.{Environment.NewLine}{output}";
    }

    private static async Task EnsureRemoteDirectoryAsync(
        string adbPath,
        string target,
        string directory,
        CancellationToken cancellationToken)
    {
        if (string.Equals(directory, AndroidStorageLocations.InternalStorage, StringComparison.Ordinal))
        {
            return;
        }

        var output = new StringBuilder();
        var exitCode = await ProcessRunner.RunAsync(
            [adbPath, "-s", target, "shell", $"mkdir -p {AndroidStorageLocations.QuoteForRemoteShell(directory)}"],
            text => output.Append(text),
            cancellationToken);

        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"Could not create {directory} on {target}.{Environment.NewLine}{output}");
        }
    }

    private static bool HasDeleteError(string output) =>
        output.Contains("No such file", StringComparison.OrdinalIgnoreCase)
        || output.Contains("Permission denied", StringComparison.OrdinalIgnoreCase)
        || output.Contains("Read-only file system", StringComparison.OrdinalIgnoreCase)
        || output.Contains("Operation not permitted", StringComparison.OrdinalIgnoreCase);

    private static string DescribeListingFailure(string directory, string output)
    {
        var detail = output.Trim();
        if (output.Contains("No such file", StringComparison.OrdinalIgnoreCase))
        {
            return $"Folder not found on the device: {directory}. The device may not have this folder yet (for example DCIM before the camera has saved anything).{Environment.NewLine}{detail}";
        }

        if (output.Contains("Permission denied", StringComparison.OrdinalIgnoreCase))
        {
            return $"The device denies adb access to {directory}. Android/data and Android/obb are restricted on " +
                $"Android 11 and newer, and other users' storage is not reachable.{Environment.NewLine}{detail}";
        }

        return $"Failed to list {directory}.{Environment.NewLine}{detail}";
    }

    private static string RequireChangeablePath(string remotePath)
    {
        var path = AndroidStorageLocations.NormalizeRemotePath(remotePath);
        if (!AndroidStorageLocations.IsWithinInternalStorage(path))
        {
            throw new InvalidOperationException($"Only folders inside {AndroidStorageLocations.InternalStorage} can be changed from this app: {path}");
        }

        if (string.Equals(path, AndroidStorageLocations.InternalStorage, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Refusing to change the internal storage root itself. Choose a folder inside it.");
        }

        return path;
    }

    private string RequireAdb() => _adbPath
        ?? throw new InvalidOperationException("`adb` was not found. Install Android platform-tools or add adb to PATH.");

    private static string RequireSerial(string serial) => string.IsNullOrWhiteSpace(serial)
        ? throw new InvalidOperationException("No device selected. Select a device first.")
        : serial.Trim();
}
