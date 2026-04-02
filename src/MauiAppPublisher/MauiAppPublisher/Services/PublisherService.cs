using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia.Controls;
using MauiAppPublisher.Models;

namespace MauiAppPublisher.Services;

public sealed class PublisherService
{
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

    public string DotnetStatusText => DotnetPath is null ? "dotnet not found" : $"dotnet: {DotnetPath}";

    public string AdbStatusText => AdbPath is null ? "adb not found" : $"adb: {AdbPath}";

    public string EmulatorStatusText => EmulatorPath is null ? "emulator not found" : $"emulator: {EmulatorPath}";

    public ProjectMetadata LoadProjectMetadata(string projectDirectory, string configuration, string targetFramework, string runtimeIdentifier)
    {
        var projectDirectoryPath = CreateProjectDirectory(projectDirectory);
        var projectFile = FindProjectFile(projectDirectoryPath)
            ?? throw new InvalidOperationException($"No .csproj found in {projectDirectoryPath}.");

        return new ProjectMetadata(
            projectFile.FullName,
            GetDefaultOutputDirectory(projectDirectoryPath, configuration, targetFramework, runtimeIdentifier),
            ReadProperty(projectFile, "ApplicationId", "PackageName", "ApplicationIdentifier"),
            ReadTargetFramework(projectFile));
    }

    public PublishCommandBundle BuildPublishCommandBundle(PublishConfiguration configuration)
    {
        var projectDirectory = CreateProjectDirectory(configuration.ProjectDirectory);
        var projectFile = FindProjectFile(projectDirectory)
            ?? throw new InvalidOperationException($"No .csproj found in {projectDirectory}.");

        var formats = GetSelectedFormats(configuration);
        if (formats.Count == 0)
        {
            throw new InvalidOperationException("Select at least one package format: APK, AAB, or both.");
        }

        if (DotnetPath is null)
        {
            throw new InvalidOperationException("`dotnet` was not found. Install the .NET SDK or add dotnet to PATH.");
        }

        var outputDirectory = string.IsNullOrWhiteSpace(configuration.OutputDirectory)
            ? GetDefaultOutputDirectory(projectDirectory, configuration.Configuration, configuration.TargetFramework, configuration.RuntimeIdentifier)
            : configuration.OutputDirectory.Trim();

        var customTrimProperty = DetectCustomTrimProperty(projectFile);
        var command = BuildBaseCommand(configuration, projectFile.FullName, customTrimProperty);
        command.Add($"-p:AndroidPackageFormats={string.Join("%3B", formats)}");
        command.Add("-o");
        command.Add(outputDirectory);

        return new PublishCommandBundle(
            command,
            MaskCommand(command),
            MaskCommand(BuildVerifiedApkCommand(configuration, projectFile.FullName, outputDirectory, customTrimProperty)),
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
        writeOutput("Known-good APK-only command:" + Environment.NewLine);
        writeOutput(bundle.VerifiedApkPreviewText + Environment.NewLine + Environment.NewLine);

        if (outputDirectory.Exists)
        {
            writeOutput($"Clearing output folder {outputDirectory.FullName}{Environment.NewLine}");
            outputDirectory.Delete(recursive: true);
        }

        if (configuration.DeleteBin)
        {
            DeleteDirectoryIfPresent(Path.Combine(projectDirectory.FullName, "bin"), writeOutput);
        }

        if (configuration.DeleteObj)
        {
            DeleteDirectoryIfPresent(Path.Combine(projectDirectory.FullName, "obj"), writeOutput);
        }

        var staleAndroidDirectory = Path.Combine(
            projectDirectory.FullName,
            "obj",
            configuration.Configuration.Trim(),
            configuration.TargetFramework.Trim(),
            configuration.RuntimeIdentifier.Trim(),
            "android");

        if (Directory.Exists(staleAndroidDirectory))
        {
            Directory.Delete(staleAndroidDirectory, recursive: true);
        }

        writeOutput($"--- Running: {bundle.PreviewText} ---{Environment.NewLine}");
        var exitCode = await RunProcessAsync(bundle.CommandArguments, writeOutput, cancellationToken);

        if (exitCode == 0)
        {
            writeOutput(Environment.NewLine + "=== Publish completed successfully ===" + Environment.NewLine);
            PlayCompletionSound(success: true);
            return true;
        }

        writeOutput(Environment.NewLine + $"=== Publish failed (exit code {exitCode}) ===" + Environment.NewLine);
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

    public async Task<string> InstallLatestApkAsync(string outputDirectory, CancellationToken cancellationToken)
    {
        var adbPath = AdbPath ?? throw new InvalidOperationException("`adb` was not found. Install Android platform-tools or add adb to PATH.");
        var apk = FindBestApk(outputDirectory)
            ?? throw new InvalidOperationException($"No APK found in {outputDirectory}.");

        var output = new StringBuilder();
        var exitCode = await RunProcessAsync(
            [adbPath, "-s", "emulator-5554", "install", "-r", apk.FullName],
            text => output.Append(text),
            cancellationToken);

        return exitCode == 0
            ? $"Installed {apk.Name} on emulator-5554.{Environment.NewLine}{output}"
            : $"APK install failed for {apk.Name}.{Environment.NewLine}{output}";
    }

    public async Task<string> UninstallAsync(string packageId, CancellationToken cancellationToken)
    {
        var adbPath = AdbPath ?? throw new InvalidOperationException("`adb` was not found. Install Android platform-tools or add adb to PATH.");
        if (string.IsNullOrWhiteSpace(packageId))
        {
            throw new InvalidOperationException("Package id is required to uninstall the app.");
        }

        var output = new StringBuilder();
        var exitCode = await RunProcessAsync(
            [adbPath, "-s", "emulator-5554", "uninstall", packageId.Trim()],
            text => output.Append(text),
            cancellationToken);

        return exitCode == 0
            ? $"Uninstall requested for {packageId}.{Environment.NewLine}{output}"
            : $"Uninstall failed for {packageId}.{Environment.NewLine}{output}";
    }

    public async Task<string> LaunchAsync(string packageId, CancellationToken cancellationToken)
    {
        var adbPath = AdbPath ?? throw new InvalidOperationException("`adb` was not found. Install Android platform-tools or add adb to PATH.");
        if (string.IsNullOrWhiteSpace(packageId))
        {
            throw new InvalidOperationException("Package id is required to launch the app.");
        }

        var output = new StringBuilder();
        var exitCode = await RunProcessAsync(
            [adbPath, "-s", "emulator-5554", "shell", "monkey", "-p", packageId.Trim(), "-c", "android.intent.category.LAUNCHER", "1"],
            text => output.Append(text),
            cancellationToken);

        return exitCode == 0
            ? $"Launch requested for {packageId}.{Environment.NewLine}{output}"
            : $"Launch failed for {packageId}.{Environment.NewLine}{output}";
    }

    public async Task<string> CopyWindowScreenshotToClipboardAsync(Window window, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return "Window screenshot-to-clipboard is currently implemented for macOS only.";
        }

        var scaling = window.DesktopScaling;
        var bounds = window.Bounds;
        var position = window.Position;
        var x = (int)Math.Max(0, Math.Round(position.X * scaling));
        var y = (int)Math.Max(0, Math.Round(position.Y * scaling));
        var width = (int)Math.Max(1, Math.Round(bounds.Width * scaling));
        var height = (int)Math.Max(1, Math.Round(bounds.Height * scaling));
        var region = $"{x},{y},{width},{height}";

        var exitCode = await RunProcessAsync(
            ["screencapture", "-x", "-c", "-R", region],
            _ => { },
            cancellationToken);

        if (exitCode == 0)
        {
            return "Window screenshot copied to the clipboard.";
        }

        exitCode = await RunProcessAsync(["screencapture", "-x", "-c"], _ => { }, cancellationToken);
        return exitCode == 0
            ? "Window region capture failed, so a full-screen screenshot was copied instead."
            : "Screenshot capture failed. Check macOS Screen Recording permissions for the app.";
    }

    public async Task<string> SaveWindowScreenshotToDiskAsync(Window window, string outputDirectory, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return "Window screenshot-to-disk is currently implemented for macOS only.";
        }

        var targetDirectory = ResolveScreenshotDirectory(outputDirectory);
        Directory.CreateDirectory(targetDirectory);

        var filePath = Path.Combine(
            targetDirectory,
            $"MauiAppPublisher-screenshot-{DateTime.Now:yyyyMMdd-HHmmss}.png");

        var scaling = window.DesktopScaling;
        var bounds = window.Bounds;
        var position = window.Position;
        var x = (int)Math.Max(0, Math.Round(position.X * scaling));
        var y = (int)Math.Max(0, Math.Round(position.Y * scaling));
        var width = (int)Math.Max(1, Math.Round(bounds.Width * scaling));
        var height = (int)Math.Max(1, Math.Round(bounds.Height * scaling));
        var region = $"{x},{y},{width},{height}";

        var exitCode = await RunProcessAsync(
            ["screencapture", "-x", "-R", region, filePath],
            _ => { },
            cancellationToken);

        if (exitCode != 0)
        {
            return "Screenshot-to-disk failed. Check macOS Screen Recording permissions for the app.";
        }

        return $"Window screenshot saved to {filePath}.";
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
            var document = XDocument.Load(projectFile.FullName);
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
        catch
        {
            return null;
        }

        return null;
    }

    private static string? ReadTargetFramework(FileInfo projectFile)
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
            .FirstOrDefault(framework => framework.Contains("android", StringComparison.OrdinalIgnoreCase))
            ?? multiTarget.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
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

    private List<string> BuildBaseCommand(PublishConfiguration configuration, string projectFilePath, string? customTrimProperty)
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
            command.Insert(8, "-p:PublishTrimmed=false");
        }
        else
        {
            command.Insert(8, $"-p:{customTrimProperty}=false");
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

    private static void DeleteDirectoryIfPresent(string path, Action<string> writeOutput)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        writeOutput($"Deleting {path}{Environment.NewLine}");
        Directory.Delete(path, recursive: true);
    }

    private static FileInfo? FindBestApk(string outputDirectory)
    {
        var directory = new DirectoryInfo(outputDirectory);
        if (!directory.Exists)
        {
            return null;
        }

        static IEnumerable<FileInfo> OrderNewest(IEnumerable<FileInfo> files)
        {
            return files.OrderByDescending(file => file.LastWriteTimeUtc);
        }

        var signedRecursive = OrderNewest(directory.EnumerateFiles("*-Signed.apk", SearchOption.AllDirectories)).FirstOrDefault();
        if (signedRecursive is not null)
        {
            return signedRecursive;
        }

        var anyRecursive = OrderNewest(directory.EnumerateFiles("*.apk", SearchOption.AllDirectories)).FirstOrDefault();
        return anyRecursive;
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
        await process.WaitForExitAsync(cancellationToken);
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
}
