using System.Text.Json;

namespace DotNetAppPublisher.Features.Publishing.Inspect;

/// <summary>
/// Resolves the effective version-related MSBuild properties of a project through a real MSBuild
/// evaluation. Version properties can legally be declared in the csproj, in Directory.Build.props,
/// in a shared props file such as Directory.Version.props imported by it, or in any other imported
/// file, so scanning the csproj XML alone cannot see centrally managed versions. Evaluation with
/// `dotnet msbuild -getProperty:` sees the final value wherever it was set.
///
/// Returns null when MSBuild cannot run or fails; callers then keep the plain csproj XML scan.
/// </summary>
public static class ProjectVersionResolver
{
    /// <summary>The version-shaped properties an opened project may declare, evaluated per load.</summary>
    public static readonly string[] Properties =
    [
        "ApplicationDisplayVersion",
        "ApplicationVersion",
        "Version",
        "VersionPrefix",
        "VersionSuffix",
        "AssemblyVersion",
        "FileVersion",
        "InformationalVersion"
    ];

    public static async Task<IReadOnlyDictionary<string, string>?> ResolveAsync(
        string dotnetPath,
        FileInfo projectFile,
        string configuration,
        string targetFramework,
        string runtimeIdentifier,
        Func<IReadOnlyList<string>, CancellationToken, Task<(int ExitCode, string Output)>> evaluate,
        CancellationToken cancellationToken)
    {
        // A Directory.Build.props or shared props file may condition versions on Configuration or
        // TargetFramework, so the evaluation carries the same context as a real publish.
        var arguments = new List<string>
        {
            dotnetPath,
            "msbuild",
            projectFile.FullName,
            "-nologo",
            $"-getProperty:{string.Join(",", Properties)}",
            $"-p:Configuration={configuration.Trim()}",
            $"-p:TargetFramework={targetFramework.Trim()}",
            $"-p:RuntimeIdentifier={runtimeIdentifier.Trim()}"
        };

        try
        {
            var result = await evaluate(arguments, cancellationToken);
            if (result.ExitCode != 0)
            {
                return null;
            }

            using var document = JsonDocument.Parse(result.Output);
            if (!document.RootElement.TryGetProperty("Properties", out var properties))
            {
                return null;
            }

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var name in Properties)
            {
                var value = ProjectInspector.GetMsBuildProperty(properties, name);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    values[name] = value.Trim();
                }
            }

            return values;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }
}
