using System.Text.RegularExpressions;
using System.Xml.Linq;
using CodePublisher.Features.Publishing.Configure;
using CodePublisher.Models;

namespace CodePublisher.Features.Publishing.Inspect;

public static class ProjectInspector
{
    public static DirectoryInfo CreateProjectDirectory(string projectDirectory)
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

    public static FileInfo? FindProjectFile(DirectoryInfo projectDirectory)
    {
        return projectDirectory
            .GetFiles("*.csproj", SearchOption.TopDirectoryOnly)
            .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static readonly HashSet<string> SkippedDiscoveryFolders =
        [with(StringComparer.OrdinalIgnoreCase), "bin", "obj", "node_modules", ".git", ".idea", ".vs", "artifacts", "packages"];

    private static readonly HashSet<string> AndroidRuntimes10 = [with(StringComparer.OrdinalIgnoreCase), "android-arm", "android-arm64", "android-x64", "android-x86"];
    private static readonly HashSet<string> AndroidRuntimes11 = [with(StringComparer.OrdinalIgnoreCase), "android-arm", "android-arm64", "android-x64"];
    private static readonly HashSet<string> AppleRuntimes = [with(StringComparer.OrdinalIgnoreCase), "ios-arm64", "iossimulator-arm64", "iossimulator-x64", "maccatalyst-arm64", "maccatalyst-x64"];
    private static readonly HashSet<string> MacRuntimes = [with(StringComparer.OrdinalIgnoreCase), "osx-arm64", "osx-x64"];
    private static readonly HashSet<string> WindowsRuntimes = [with(StringComparer.OrdinalIgnoreCase), "win-x64", "win-x86", "win-arm64"];
    private static readonly HashSet<string> LinuxRuntimes = [with(StringComparer.OrdinalIgnoreCase), "linux-x64", "linux-arm", "linux-arm64", "linux-musl-x64", "linux-musl-arm", "linux-musl-arm64", "linux-bionic-x64", "linux-bionic-arm64", "linux-loongarch64"];

    /// <summary>
    /// Every .csproj under the directory, recursively, skipping build output folders. A source root
    /// that holds several projects (a main library plus a source generator, tests, samples) yields
    /// them all, so the UI can offer a picker instead of silently using the first top-level project.
    /// </summary>
    public static IReadOnlyList<FileInfo> DiscoverProjectFiles(DirectoryInfo projectDirectory, int maxDepth = 5)
    {
        var found = new List<FileInfo>();
        CollectProjectFiles(projectDirectory, found, depth: 0, maxDepth);
        return found
            .OrderBy(file => file.FullName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void CollectProjectFiles(DirectoryInfo directory, List<FileInfo> found, int depth, int maxDepth)
    {
        if (depth > maxDepth || !directory.Exists)
        {
            return;
        }

        try
        {
            found.AddRange(directory.GetFiles("*.csproj", SearchOption.TopDirectoryOnly));
        }
        catch
        {
            // Unreadable folder: skip it, the rest of the tree still matters.
        }

        if (depth == maxDepth)
        {
            return;
        }

        try
        {
            foreach (var subdirectory in directory.GetDirectories())
            {
                if (SkippedDiscoveryFolders.Contains(subdirectory.Name))
                {
                    continue;
                }

                CollectProjectFiles(subdirectory, found, depth + 1, maxDepth);
            }
        }
        catch
        {
            // Unreadable folder: skip it.
        }
    }

    public static string GetDefaultOutputDirectory(
        DirectoryInfo projectDirectory,
        string configuration,
        string targetFramework,
        string runtimeIdentifier)
    {
        // The SDK's own default publish layout is bin/<configuration>/<tfm>/[<rid>/]publish. The
        // APK/AAB/IPA the toolchain copies into $(PublishDir) lands there, not in the RID build
        // folder above it; pointing the default at the RID folder puts the published packages next
        // to the build intermediates and diverges from every other .NET publish.
        if (!string.IsNullOrWhiteSpace(runtimeIdentifier))
        {
            return Path.Combine(
                projectDirectory.FullName,
                "bin",
                configuration.Trim(),
                targetFramework.Trim(),
                runtimeIdentifier.Trim(),
                "publish");
        }

        return Path.Combine(
            projectDirectory.FullName,
            "bin",
            configuration.Trim(),
            targetFramework.Trim(),
            "publish");
    }

    public static void ValidateProjectTargetFramework(FileInfo projectFile, string targetFramework)
    {
        var selectedFramework = targetFramework.Trim();
        if (string.IsNullOrWhiteSpace(selectedFramework))
        {
            throw new InvalidOperationException("Target framework is required.");
        }

        var singleTarget = ReadProperty(projectFile, "TargetFramework");
        if (!string.IsNullOrWhiteSpace(singleTarget))
        {
            if (!string.Equals(singleTarget.Trim(), selectedFramework, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Target framework `{selectedFramework}` is not declared by {projectFile.Name}. Use `{singleTarget.Trim()}` or switch to a compatible project.");
            }

            return;
        }

        var multiTarget = ReadProperty(projectFile, "TargetFrameworks");
        if (string.IsNullOrWhiteSpace(multiTarget))
        {
            return;
        }

        var frameworks = multiTarget
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (!frameworks.Any(framework => string.Equals(framework, selectedFramework, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Target framework `{selectedFramework}` is not in `{projectFile.Name}` target frameworks: {string.Join(", ", frameworks)}.");
        }
    }

    private static void ValidateTargetFrameworkForPlatform(PublishConfiguration configuration)
    {
        var framework = configuration.TargetFramework.Trim();
        var separator = framework.IndexOf('-');
        var suffix = separator < 0 ? string.Empty : framework[(separator + 1)..];
        var isAndroid = suffix.StartsWith("android", StringComparison.OrdinalIgnoreCase);
        var isIos = suffix.StartsWith("ios", StringComparison.OrdinalIgnoreCase);
        var isCatalyst = suffix.StartsWith("maccatalyst", StringComparison.OrdinalIgnoreCase);
        var isMac = suffix.StartsWith("macos", StringComparison.OrdinalIgnoreCase);
        var isWindows = suffix.StartsWith("windows", StringComparison.OrdinalIgnoreCase);
        var isPlain = suffix.Length == 0;

        var valid = configuration.PublishPlatform switch
        {
            PublishPlatforms.Android => isAndroid,
            PublishPlatforms.Ios => isIos,
            PublishPlatforms.MacOs => isCatalyst || isMac || isPlain,
            PublishPlatforms.Windows => isWindows || isPlain,
            PublishPlatforms.Linux => isPlain,
            PublishPlatforms.NuGet => true,
            _ => false
        };
        if (!valid)
        {
            throw new InvalidOperationException($"Target framework `{framework}` does not match the selected {configuration.PublishPlatform} platform.");
        }

        var runtimeIsCatalyst = configuration.RuntimeIdentifier.StartsWith("maccatalyst-", StringComparison.OrdinalIgnoreCase);
        if (isCatalyst != runtimeIsCatalyst && configuration.PublishPlatform == PublishPlatforms.MacOs)
        {
            throw new InvalidOperationException("Mac Catalyst requires a `net*-maccatalyst` framework and a `maccatalyst-*` runtime identifier together.");
        }
    }

    public static void ValidateRuntimeForPlatform(PublishConfiguration configuration)
    {
        // NuGet packages are platform-neutral: dotnet pack needs no runtime identifier.
        if (string.Equals(configuration.PublishPlatform, PublishPlatforms.NuGet, StringComparison.Ordinal))
        {
            return;
        }

        ValidateTargetFrameworkForPlatform(configuration);

        var runtimeIdentifier = configuration.RuntimeIdentifier.Trim();
        if (string.IsNullOrWhiteSpace(runtimeIdentifier))
        {
            throw new InvalidOperationException("Runtime identifier is required.");
        }

        if (string.Equals(configuration.PublishPlatform, PublishPlatforms.Android, StringComparison.Ordinal))
        {
            var supported = GetTargetFrameworkVersion(configuration.TargetFramework) >= 11
                ? AndroidRuntimes11
                : AndroidRuntimes10;
            if (!supported.Contains(runtimeIdentifier))
            {
                throw new InvalidOperationException(GetTargetFrameworkVersion(configuration.TargetFramework) >= 11
                    ? ".NET 11 Android supports `android-arm`, `android-arm64`, and `android-x64`. The `android-x86` runtime was removed."
                    : "Android supports `android-arm`, `android-arm64`, `android-x64`, and `android-x86` on .NET 10 and earlier.");
            }

            return;
        }

        if (string.Equals(configuration.PublishPlatform, PublishPlatforms.MacOs, StringComparison.Ordinal))
        {
            var isMacRuntime = MacRuntimes.Contains(runtimeIdentifier)
                || runtimeIdentifier.StartsWith("maccatalyst-", StringComparison.OrdinalIgnoreCase) && AppleRuntimes.Contains(runtimeIdentifier);
            if (!isMacRuntime)
            {
                throw new InvalidOperationException("macOS supports `osx-arm64`, `osx-x64`, `maccatalyst-arm64`, and `maccatalyst-x64`.");
            }

            return;
        }

        if (string.Equals(configuration.PublishPlatform, PublishPlatforms.Windows, StringComparison.Ordinal))
        {
            if (!WindowsRuntimes.Contains(runtimeIdentifier))
            {
                throw new InvalidOperationException("Windows supports `win-x64`, `win-x86`, and `win-arm64`.");
            }

            return;
        }

        if (string.Equals(configuration.PublishPlatform, PublishPlatforms.Ios, StringComparison.Ordinal))
        {
            if (!runtimeIdentifier.StartsWith("ios", StringComparison.OrdinalIgnoreCase)
                || !AppleRuntimes.Contains(runtimeIdentifier))
            {
                throw new InvalidOperationException("iOS supports `ios-arm64`, `iossimulator-arm64`, and `iossimulator-x64`.");
            }

            return;
        }

        if (string.Equals(configuration.PublishPlatform, PublishPlatforms.Linux, StringComparison.Ordinal)
            && !LinuxRuntimes.Contains(runtimeIdentifier))
        {
            throw new InvalidOperationException("Use a supported portable Linux RID such as `linux-x64`, `linux-arm64`, `linux-musl-x64`, or `linux-musl-arm64`.");
        }
    }

    public static string? ReadVersion(FileInfo projectFile, string publishPlatform, bool isDisplay) =>
        ReadVersion(projectFile, publishPlatform, isDisplay, evaluated: null);

    /// <summary>
    /// Reads the project version for the platform. When <paramref name="evaluated"/> is provided
    /// (from <see cref="ProjectVersionResolver"/>), the effective MSBuild value wins — that map
    /// already includes values set in the csproj itself, plus any imported props file. The scanned
    /// csproj stays as the fallback for each missing candidate.
    /// </summary>
    public static string? ReadVersion(
        FileInfo projectFile,
        string publishPlatform,
        bool isDisplay,
        IReadOnlyDictionary<string, string>? evaluated)
    {
        string? Choose(params string[] names)
        {
            if (evaluated is not null)
            {
                foreach (var name in names)
                {
                    if (evaluated.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }
            }

            return ReadProperty(projectFile, names);
        }

        if (isDisplay)
        {
            return string.Equals(publishPlatform, PublishPlatforms.Android, StringComparison.Ordinal)
                || string.Equals(publishPlatform, PublishPlatforms.Ios, StringComparison.Ordinal)
                ? Choose("ApplicationDisplayVersion", "Version", "InformationalVersion")
                : Choose("Version", "InformationalVersion");
        }

        if (string.Equals(publishPlatform, PublishPlatforms.Android, StringComparison.Ordinal)
            || string.Equals(publishPlatform, PublishPlatforms.Ios, StringComparison.Ordinal))
        {
            return Choose("ApplicationVersion", "FileVersion");
        }

        if (string.Equals(publishPlatform, PublishPlatforms.MacOs, StringComparison.Ordinal))
        {
            return Choose("FileVersion", "Version");
        }

        if (string.Equals(publishPlatform, PublishPlatforms.Windows, StringComparison.Ordinal)
            || string.Equals(publishPlatform, PublishPlatforms.Linux, StringComparison.Ordinal))
        {
            return null;
        }

        return Choose("FileVersion", "Version");
    }

    public static bool SupportsInternalVersion(string publishPlatform) =>
        string.Equals(publishPlatform, PublishPlatforms.Android, StringComparison.Ordinal)
        || string.Equals(publishPlatform, PublishPlatforms.Ios, StringComparison.Ordinal)
        || string.Equals(publishPlatform, PublishPlatforms.MacOs, StringComparison.Ordinal);

    public static string? ReadProperty(FileInfo projectFile, params string[] propertyNames)
    {
        try
        {
            foreach (var document in LoadProjectPropertyDocuments(projectFile))
            {
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
        }
        catch
        {
            return null;
        }

        return null;
    }

    /// <summary>
    /// Reads package="..." from an AndroidManifest.xml located at Platforms/Android, Properties, or project root.
    /// </summary>
    public static string? ReadAndroidManifestPackage(FileInfo projectFile)
    {
        try
        {
            var directory = projectFile.Directory;
            if (directory is null || !directory.Exists)
            {
                return null;
            }

            string[] candidateRelativePaths =
            [
                Path.Combine("Platforms", "Android", "AndroidManifest.xml"),
                Path.Combine("Properties", "AndroidManifest.xml"),
                "AndroidManifest.xml"
            ];

            foreach (var relPath in candidateRelativePaths)
            {
                var fullPath = Path.Combine(directory.FullName, relPath);
                if (File.Exists(fullPath))
                {
                    var doc = XDocument.Load(fullPath);
                    var packageAttr = doc.Root?.Attribute("package")?.Value;
                    if (!string.IsNullOrWhiteSpace(packageAttr))
                    {
                        return packageAttr.Trim();
                    }
                }
            }
        }
        catch
        {
        }

        return null;
    }

    /// <summary>
    /// Reads CFBundleIdentifier from an Info.plist located at Platforms/iOS or project root.
    /// </summary>
    public static string? ReadIosBundleIdentifier(FileInfo projectFile)
    {
        try
        {
            var directory = projectFile.Directory;
            if (directory is null || !directory.Exists)
            {
                return null;
            }

            string[] candidateRelativePaths =
            [
                Path.Combine("Platforms", "iOS", "Info.plist"),
                "Info.plist"
            ];

            foreach (var relPath in candidateRelativePaths)
            {
                var fullPath = Path.Combine(directory.FullName, relPath);
                if (File.Exists(fullPath))
                {
                    var doc = XDocument.Load(fullPath);
                    var keyElem = doc.Descendants("key").FirstOrDefault(e => string.Equals(e.Value, "CFBundleIdentifier", StringComparison.Ordinal));
                    var stringElem = keyElem?.ElementsAfterSelf("string").FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(stringElem?.Value))
                    {
                        return stringElem.Value.Trim();
                    }
                }
            }
        }
        catch
        {
        }

        return null;
    }

    public static string? ReadTargetFramework(FileInfo projectFile, string publishPlatform)
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

        var frameworks = multiTarget.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return frameworks.FirstOrDefault(framework => IsTargetFrameworkForPlatform(framework, publishPlatform))
            ?? frameworks.FirstOrDefault();
    }

    public static string? DetectCustomTrimProperty(FileInfo projectFile)
    {
        try
        {
            var projectText = File.ReadAllText(projectFile.FullName);
            var matches = Regex.Matches(projectText, @"\$\((?<name>[A-Za-z0-9_.-]*PublishTrimmed)\)");
            foreach (Match match in matches)
            {
                var propertyName = match.Groups["name"].Value;
                if (!string.IsNullOrWhiteSpace(propertyName)
                    && !string.Equals(propertyName, "PublishTrimmed", StringComparison.Ordinal))
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

    public static ProjectOutputLayout CreateUnavailableOutputLayout(string fallbackOutputDirectory, string description) =>
        new(false, fallbackOutputDirectory, null, null, description);

    public static string? GetMsBuildProperty(System.Text.Json.JsonElement properties, string name) =>
        properties.TryGetProperty(name, out var property) && property.ValueKind == System.Text.Json.JsonValueKind.String
            ? property.GetString()
            : null;

    public static string? NormalizeProjectPath(string? path, DirectoryInfo projectDirectory)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return Path.GetFullPath(Path.IsPathRooted(path)
            ? path
            : Path.Combine(projectDirectory.FullName, path));
    }

    private static IEnumerable<XDocument> LoadProjectPropertyDocuments(FileInfo projectFile)
    {
        yield return XDocument.Load(projectFile.FullName);

        for (var directory = projectFile.Directory; directory is not null; directory = directory.Parent)
        {
            var propsPath = Path.Combine(directory.FullName, "Directory.Build.props");
            if (!File.Exists(propsPath))
            {
                continue;
            }

            XDocument? document = null;
            try
            {
                document = XDocument.Load(propsPath);
            }
            catch
            {
            }

            if (document is not null)
            {
                yield return document;
            }
        }
    }

    /// <summary>
    /// The major version of a target framework moniker (`net11.0-android` → 11), or -1 when the
    /// moniker does not start with `netN`. Used to gate options that only exist on one major
    /// version, e.g. the Mono-only Android AOT knobs that .NET 11 removed.
    /// </summary>
    public static int GetTargetFrameworkVersion(string? targetFramework)
    {
        if (string.IsNullOrWhiteSpace(targetFramework))
        {
            return -1;
        }

        var match = Regex.Match(targetFramework.Trim(), @"^net(?<version>\d+)\.\d+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success && int.TryParse(match.Groups["version"].Value, out var major) ? major : -1;
    }

    private static bool IsTargetFrameworkForPlatform(string framework, string publishPlatform)
    {
        if (string.Equals(publishPlatform, PublishPlatforms.Android, StringComparison.Ordinal))
        {
            return framework.Contains("android", StringComparison.OrdinalIgnoreCase);
        }

        if (string.Equals(publishPlatform, PublishPlatforms.MacOs, StringComparison.Ordinal))
        {
            return IsBaseNetFramework(framework)
                || framework.Contains("maccatalyst", StringComparison.OrdinalIgnoreCase)
                || framework.Contains("macos", StringComparison.OrdinalIgnoreCase);
        }

        if (string.Equals(publishPlatform, PublishPlatforms.Windows, StringComparison.Ordinal))
        {
            return framework.Contains("windows", StringComparison.OrdinalIgnoreCase)
                || IsBaseNetFramework(framework);
        }

        if (string.Equals(publishPlatform, PublishPlatforms.Ios, StringComparison.Ordinal))
        {
            return framework.Contains("ios", StringComparison.OrdinalIgnoreCase);
        }

        if (string.Equals(publishPlatform, PublishPlatforms.Linux, StringComparison.Ordinal))
        {
            return IsBaseNetFramework(framework)
                || !framework.Contains("android", StringComparison.OrdinalIgnoreCase)
                    && !framework.Contains("ios", StringComparison.OrdinalIgnoreCase)
                    && !framework.Contains("windows", StringComparison.OrdinalIgnoreCase)
                    && !framework.Contains("maccatalyst", StringComparison.OrdinalIgnoreCase)
                    && !framework.Contains("macos", StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    private static bool IsBaseNetFramework(string framework)
        => Regex.IsMatch(
            framework.Trim(),
            @"^net\d+\.\d+$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
