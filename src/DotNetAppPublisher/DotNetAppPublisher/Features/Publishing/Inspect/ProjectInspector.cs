using System.Text.RegularExpressions;
using System.Xml.Linq;
using DotNetAppPublisher.Features.Publishing.Configure;
using DotNetAppPublisher.Models;

namespace DotNetAppPublisher.Features.Publishing.Inspect;

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

    public static string GetDefaultOutputDirectory(
        DirectoryInfo projectDirectory,
        string configuration,
        string targetFramework,
        string runtimeIdentifier)
    {
        return Path.Combine(
            projectDirectory.FullName,
            "bin",
            configuration.Trim(),
            targetFramework.Trim(),
            runtimeIdentifier.Trim());
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

    public static void ValidateRuntimeForPlatform(PublishConfiguration configuration)
    {
        var runtimeIdentifier = configuration.RuntimeIdentifier.Trim();
        if (string.IsNullOrWhiteSpace(runtimeIdentifier))
        {
            throw new InvalidOperationException("Runtime identifier is required.");
        }

        if (string.Equals(configuration.PublishPlatform, PublishPlatforms.Android, StringComparison.Ordinal))
        {
            if (!runtimeIdentifier.StartsWith("android-", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Android publishing requires an `android-*` runtime identifier.");
            }

            return;
        }

        if (string.Equals(configuration.PublishPlatform, PublishPlatforms.MacOs, StringComparison.Ordinal))
        {
            var isMacRuntime = runtimeIdentifier.StartsWith("osx-", StringComparison.OrdinalIgnoreCase)
                || runtimeIdentifier.StartsWith("maccatalyst-", StringComparison.OrdinalIgnoreCase);
            if (!isMacRuntime)
            {
                throw new InvalidOperationException("macOS publishing requires an `osx-*` or `maccatalyst-*` runtime identifier.");
            }

            return;
        }

        if (string.Equals(configuration.PublishPlatform, PublishPlatforms.Windows, StringComparison.Ordinal))
        {
            if (!runtimeIdentifier.StartsWith("win-", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Windows publishing requires a `win-*` runtime identifier.");
            }

            return;
        }

        if (string.Equals(configuration.PublishPlatform, PublishPlatforms.Ios, StringComparison.Ordinal))
        {
            if (!runtimeIdentifier.StartsWith("ios-", StringComparison.OrdinalIgnoreCase)
                && !runtimeIdentifier.StartsWith("iossimulator-", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("iOS publishing requires an `ios-*` or `iossimulator-*` runtime identifier.");
            }

            return;
        }

        if (string.Equals(configuration.PublishPlatform, PublishPlatforms.Linux, StringComparison.Ordinal)
            && !runtimeIdentifier.StartsWith("linux-", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Linux publishing requires a `linux-*` runtime identifier.");
        }
    }

    public static string? ReadVersion(FileInfo projectFile, string publishPlatform, bool isDisplay)
    {
        if (isDisplay)
        {
            return string.Equals(publishPlatform, PublishPlatforms.Android, StringComparison.Ordinal)
                || string.Equals(publishPlatform, PublishPlatforms.Ios, StringComparison.Ordinal)
                ? ReadProperty(projectFile, "ApplicationDisplayVersion", "Version", "InformationalVersion")
                : ReadProperty(projectFile, "Version", "InformationalVersion");
        }

        if (string.Equals(publishPlatform, PublishPlatforms.Android, StringComparison.Ordinal)
            || string.Equals(publishPlatform, PublishPlatforms.Ios, StringComparison.Ordinal))
        {
            return ReadProperty(projectFile, "ApplicationVersion", "FileVersion");
        }

        if (string.Equals(publishPlatform, PublishPlatforms.MacOs, StringComparison.Ordinal))
        {
            return ReadProperty(projectFile, "FileVersion", "Version");
        }

        if (string.Equals(publishPlatform, PublishPlatforms.Windows, StringComparison.Ordinal)
            || string.Equals(publishPlatform, PublishPlatforms.Linux, StringComparison.Ordinal))
        {
            return null;
        }

        return ReadProperty(projectFile, "FileVersion", "Version");
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
