using System.Text.Json;
using DotNetAppPublisher.Models;

namespace DotNetAppPublisher.Features.Publishing.Inspect;

public static class ProjectOutputLayoutResolver
{
    public static async Task<ProjectOutputLayout> ResolveAsync(
        string dotnetPath,
        FileInfo projectFile,
        string configuration,
        string targetFramework,
        string runtimeIdentifier,
        Func<IReadOnlyList<string>, CancellationToken, Task<(int ExitCode, string Output)>> evaluate,
        CancellationToken cancellationToken)
    {
        var fallbackOutputDirectory = ProjectInspector.GetDefaultOutputDirectory(
            projectFile.Directory!,
            configuration,
            targetFramework,
            runtimeIdentifier);
        var arguments = new List<string>
        {
            dotnetPath,
            "msbuild",
            projectFile.FullName,
            "-nologo",
            "-getProperty:UseArtifactsOutput,PublishDir,OutputPath,IntermediateOutputPath",
            $"-p:Configuration={configuration.Trim()}",
            $"-p:TargetFramework={targetFramework.Trim()}",
            $"-p:RuntimeIdentifier={runtimeIdentifier.Trim()}"
        };

        try
        {
            var result = await evaluate(arguments, cancellationToken);
            if (result.ExitCode != 0)
            {
                return ProjectInspector.CreateUnavailableOutputLayout(
                    fallbackOutputDirectory,
                    "MSBuild could not evaluate the project output layout. The standard output folder will be used.");
            }

            using var document = JsonDocument.Parse(result.Output);
            if (!document.RootElement.TryGetProperty("Properties", out var properties))
            {
                return ProjectInspector.CreateUnavailableOutputLayout(
                    fallbackOutputDirectory,
                    "MSBuild did not return project output properties. The standard output folder will be used.");
            }

            var usesCentralizedArtifacts = string.Equals(
                ProjectInspector.GetMsBuildProperty(properties, "UseArtifactsOutput"),
                "true",
                StringComparison.OrdinalIgnoreCase);
            if (!usesCentralizedArtifacts)
            {
                return new ProjectOutputLayout(
                    false,
                    fallbackOutputDirectory,
                    null,
                    null,
                    "Standard project output detected.");
            }

            var publishDirectory = ProjectInspector.NormalizeProjectPath(
                ProjectInspector.GetMsBuildProperty(properties, "PublishDir"),
                projectFile.Directory!);
            if (string.IsNullOrWhiteSpace(publishDirectory))
            {
                return ProjectInspector.CreateUnavailableOutputLayout(
                    fallbackOutputDirectory,
                    "Centralized artifacts were detected, but MSBuild did not resolve a publish directory.");
            }

            return new ProjectOutputLayout(
                true,
                publishDirectory,
                ProjectInspector.NormalizeProjectPath(ProjectInspector.GetMsBuildProperty(properties, "OutputPath"), projectFile.Directory!),
                ProjectInspector.NormalizeProjectPath(ProjectInspector.GetMsBuildProperty(properties, "IntermediateOutputPath"), projectFile.Directory!),
                "Centralized artifacts detected.");
        }
        catch (JsonException)
        {
            return ProjectInspector.CreateUnavailableOutputLayout(
                fallbackOutputDirectory,
                "MSBuild returned an unreadable output-layout response. The standard output folder will be used.");
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return ProjectInspector.CreateUnavailableOutputLayout(
                fallbackOutputDirectory,
                "The project output layout could not be detected. The standard output folder will be used.");
        }
    }
}
