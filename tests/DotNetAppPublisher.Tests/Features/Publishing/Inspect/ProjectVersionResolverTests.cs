using DotNetAppPublisher.Features.Publishing.Configure;
using DotNetAppPublisher.Features.Publishing.Inspect;

namespace DotNetAppPublisher.Tests.Features.Publishing.Inspect;

public sealed class ProjectVersionResolverTests
{
    [Test]
    public async Task ResolveAsync_ReturnsEvaluatedProperties_AndDropsEmptyValues()
    {
        var root = CreateProjectDirectory();
        try
        {
            var projectFile = new FileInfo(Path.Combine(root.FullName, "App.csproj"));
            await File.WriteAllTextAsync(projectFile.FullName, "<Project />");
            var json = "{\"Properties\":{\"Version\":\"2.3.4\",\"ApplicationDisplayVersion\":\"2.3.4\",\"ApplicationVersion\":\"7\",\"FileVersion\":\"\"}}";

            var values = await ProjectVersionResolver.ResolveAsync(
                "/dotnet",
                projectFile,
                "Release",
                "net10.0-android",
                "android-arm64",
                (_, _) => Task.FromResult((0, json)),
                CancellationToken.None);

            using (Assert.Multiple())
            {
                await Assert.That(values).IsNotNull();
                await Assert.That(values!["Version"]).IsEqualTo("2.3.4");
                await Assert.That(values["ApplicationVersion"]).IsEqualTo("7");
                await Assert.That(values.ContainsKey("FileVersion")).IsFalse();
            }
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Test]
    public async Task ResolveAsync_ReturnsNull_WhenMsbuildFails()
    {
        var root = CreateProjectDirectory();
        try
        {
            var projectFile = new FileInfo(Path.Combine(root.FullName, "App.csproj"));
            await File.WriteAllTextAsync(projectFile.FullName, "<Project />");

            var values = await ProjectVersionResolver.ResolveAsync(
                "/dotnet",
                projectFile,
                "Release",
                "net10.0",
                "osx-arm64",
                (_, _) => Task.FromResult((1, "failure")),
                CancellationToken.None);

            await Assert.That(values).IsNull();
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Test]
    public async Task ReadVersion_UsesEvaluatedValue_WhenVersionLivesInImportedProps()
    {
        var root = CreateProjectDirectory();
        try
        {
            // Simulates a project whose version is set by Directory.Build.props /
            // Directory.Version.props: the csproj itself declares nothing.
            var projectFile = new FileInfo(Path.Combine(root.FullName, "App.csproj"));
            await File.WriteAllTextAsync(projectFile.FullName, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            var evaluated = new Dictionary<string, string> { ["Version"] = "2.3.4" };

            var display = ProjectInspector.ReadVersion(projectFile, PublishPlatforms.MacOs, isDisplay: true, evaluated);

            await Assert.That(display).IsEqualTo("2.3.4");
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Test]
    public async Task ReadVersion_Android_PrefersEvaluatedApplicationProperties()
    {
        var root = CreateProjectDirectory();
        try
        {
            var projectFile = new FileInfo(Path.Combine(root.FullName, "App.csproj"));
            await File.WriteAllTextAsync(projectFile.FullName, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            var evaluated = new Dictionary<string, string>
            {
                ["ApplicationDisplayVersion"] = "1.5.0",
                ["ApplicationVersion"] = "42",
                ["Version"] = "9.9.9",
                ["FileVersion"] = "9.9.9.0"
            };

            var display = ProjectInspector.ReadVersion(projectFile, PublishPlatforms.Android, isDisplay: true, evaluated);
            var internalVersion = ProjectInspector.ReadVersion(projectFile, PublishPlatforms.Android, isDisplay: false, evaluated);

            using (Assert.Multiple())
            {
                await Assert.That(display).IsEqualTo("1.5.0");
                await Assert.That(internalVersion).IsEqualTo("42");
            }
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Test]
    public async Task ReadVersion_FallsBackToProjectFile_WhenEvaluationIsUnavailable()
    {
        var root = CreateProjectDirectory();
        try
        {
            var projectFile = new FileInfo(Path.Combine(root.FullName, "App.csproj"));
            await File.WriteAllTextAsync(
                projectFile.FullName,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><Version>1.2.3</Version></PropertyGroup></Project>");

            var display = ProjectInspector.ReadVersion(projectFile, PublishPlatforms.MacOs, isDisplay: true, evaluated: null);

            await Assert.That(display).IsEqualTo("1.2.3");
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Test]
    public async Task ReadVersion_FallsBackToProjectFile_WhenEvaluationYieldsNoValues()
    {
        var root = CreateProjectDirectory();
        try
        {
            var projectFile = new FileInfo(Path.Combine(root.FullName, "App.csproj"));
            await File.WriteAllTextAsync(
                projectFile.FullName,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><ApplicationVersion>5</ApplicationVersion></PropertyGroup></Project>");
            var evaluated = new Dictionary<string, string>();

            var internalVersion = ProjectInspector.ReadVersion(projectFile, PublishPlatforms.Android, isDisplay: false, evaluated);

            await Assert.That(internalVersion).IsEqualTo("5");
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private static DirectoryInfo CreateProjectDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dnap-version-{Guid.NewGuid():N}");
        return Directory.CreateDirectory(path);
    }
}
