using DotNetAppPublisher.Features.Publishing.Inspect;

namespace DotNetAppPublisher.Tests.Features.Publishing.Inspect;

public sealed class ProjectOutputLayoutResolverTests
{
    [Test]
    public async Task ResolveAsync_UsesCentralizedArtifactsProperties()
    {
        var root = CreateProjectDirectory();
        try
        {
            var projectFile = new FileInfo(Path.Combine(root.FullName, "App.csproj"));
            await File.WriteAllTextAsync(projectFile.FullName, "<Project />");
            var json = "{\"Properties\":{\"UseArtifactsOutput\":\"true\",\"PublishDir\":\"artifacts/bin/App/\",\"OutputPath\":\"artifacts/obj/App/\",\"IntermediateOutputPath\":\"artifacts/obj/App/\"}}";

            var layout = await ProjectOutputLayoutResolver.ResolveAsync(
                "/dotnet",
                projectFile,
                "Release",
                "net10.0",
                "linux-x64",
                (_, _) => Task.FromResult((0, json)),
                CancellationToken.None);

            using (Assert.Multiple())
            {
                await Assert.That(layout.UsesCentralizedArtifacts).IsTrue();
                await Assert.That(layout.DefaultOutputDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)).IsEqualTo(Path.GetFullPath(Path.Combine(root.FullName, "artifacts/bin/App")));
                await Assert.That(layout.BuildOutputDirectory!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)).IsEqualTo(Path.GetFullPath(Path.Combine(root.FullName, "artifacts/obj/App")));
            }
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Test]
    public async Task ResolveAsync_FallsBackWhenMsbuildFails()
    {
        var root = CreateProjectDirectory();
        try
        {
            var projectFile = new FileInfo(Path.Combine(root.FullName, "App.csproj"));
            await File.WriteAllTextAsync(projectFile.FullName, "<Project />");

            var layout = await ProjectOutputLayoutResolver.ResolveAsync(
                "/dotnet",
                projectFile,
                "Release",
                "net10.0",
                "linux-x64",
                (_, _) => Task.FromResult((1, "failure")),
                CancellationToken.None);

            using (Assert.Multiple())
            {
                await Assert.That(layout.UsesCentralizedArtifacts).IsFalse();
                await Assert.That(layout.DefaultOutputDirectory).IsEqualTo(Path.Combine(root.FullName, "bin", "Release", "net10.0", "linux-x64"));
                await Assert.That(layout.Description).Contains("could not evaluate");
            }
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private static DirectoryInfo CreateProjectDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dnap-layout-{Guid.NewGuid():N}");
        return Directory.CreateDirectory(path);
    }
}
