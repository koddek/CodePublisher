using CodePublisher.Features.Publishing.Inspect;

namespace CodePublisher.Features.Publishing.Bundle;

/// <summary>
/// Creates the macOS application bundle for a published build: copies the publish payload into
/// <c>&lt;output&gt;/&lt;project&gt;.app/Contents</c>, marks the launcher executable, writes an
/// Info.plist, and drops the icon when one is provided. Lives in the macOS library so the bundle
/// format's rules are nowhere else.
/// </summary>
public static class MacAppBundleService
{
    private const string MacAppIconFileName = "codepublisher";

    /// <summary>
    /// Returns the path of the ensured .app bundle, or null when this machine is not a macOS host
    /// or the output directory does not exist. The publish output is left alone when a bundle
    /// already exists or the output is already the MacOS folder of one.
    /// </summary>
    public static async Task<string?> EnsureAsync(
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

            var bundleIdentifier = ProjectIdentifier.BuildBundleIdentifier(packageId, projectName);
            var plistPath = Path.Combine(contentsPath, "Info.plist");
            File.WriteAllText(
                plistPath,
                BuildInfoPlist(projectName, executableName ?? projectName, bundleIdentifier));

            writeOutput($"Created .app bundle at {bundlePath}{Environment.NewLine}");
            return bundlePath;
        }, cancellationToken);
    }

    /// <summary>The newest <c>*.app</c> under the output directory, or null when none exists.</summary>
    public static DirectoryInfo? FindNewestAppBundle(string outputDirectory)
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

    private static void CopyMacAppIcon(string resourcesPath, byte[]? iconBytes)
    {
        if (iconBytes is null || iconBytes.Length == 0)
        {
            return;
        }

        var iconPath = Path.Combine(resourcesPath, $"{MacAppIconFileName}.icns");
        File.WriteAllBytes(iconPath, iconBytes);
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
}
