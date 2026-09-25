using DotNetAppPublisher.Features.Publishing.Inspect;
using DotNetAppPublisher.Models;
using DotNetAppPublisher.Services;

namespace DotNetAppPublisher.Tests.Features.Publishing.Inspect;

public sealed class ProjectInspectorTests
{
    [Test]
    public async Task FindsProjectAndCalculatesDefaultOutputDirectory()
    {
        var root = CreateProjectDirectory();
        try
        {
            var projectFile = new FileInfo(Path.Combine(root.FullName, "App.csproj"));
            await File.WriteAllTextAsync(projectFile.FullName, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

            var found = ProjectInspector.FindProjectFile(root);
            var output = ProjectInspector.GetDefaultOutputDirectory(root, "Release", "net10.0", "linux-x64");

            using (Assert.Multiple())
            {
                await Assert.That(found?.FullName).IsEqualTo(projectFile.FullName);
                await Assert.That(output).IsEqualTo(Path.Combine(root.FullName, "bin", "Release", "net10.0", "linux-x64"));
            }
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Test]
    public async Task ValidatesDeclaredTargetFramework()
    {
        var root = CreateProjectDirectory();
        try
        {
            var projectFile = new FileInfo(Path.Combine(root.FullName, "App.csproj"));
            await File.WriteAllTextAsync(projectFile.FullName, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0-android</TargetFramework></PropertyGroup></Project>");

            ProjectInspector.ValidateProjectTargetFramework(projectFile, "net10.0-android");

            var exception = Assert.Throws<InvalidOperationException>(() =>
                ProjectInspector.ValidateProjectTargetFramework(projectFile, "net10.0-ios"));

            await Assert.That(exception.Message).Contains("not declared");
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Test]
    public async Task ValidatesRuntimeForPlatform()
    {
        var configuration = CreateConfiguration(PublisherService.AndroidPlatform) with
        {
            RuntimeIdentifier = "linux-x64"
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ProjectInspector.ValidateRuntimeForPlatform(configuration));

        await Assert.That(exception.Message).Contains("android-*");
    }

    [Test]
    public async Task ReadsVersionAndCustomTrimProperty()
    {
        var root = CreateProjectDirectory();
        try
        {
            var projectFile = new FileInfo(Path.Combine(root.FullName, "App.csproj"));
            await File.WriteAllTextAsync(projectFile.FullName, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0-android</TargetFramework><ApplicationDisplayVersion>2.4.0</ApplicationDisplayVersion><ApplicationVersion>240</ApplicationVersion><PublishTrimmed Condition=\"'$(MyPublishTrimmed)' == 'true'\">true</PublishTrimmed></PropertyGroup></Project>");

            using (Assert.Multiple())
            {
                await Assert.That(ProjectInspector.ReadVersion(projectFile, PublisherService.AndroidPlatform, isDisplay: true)).IsEqualTo("2.4.0");
                await Assert.That(ProjectInspector.ReadVersion(projectFile, PublisherService.AndroidPlatform, isDisplay: false)).IsEqualTo("240");
                await Assert.That(ProjectInspector.DetectCustomTrimProperty(projectFile)).IsEqualTo("MyPublishTrimmed");
                await Assert.That(ProjectInspector.SupportsInternalVersion(PublisherService.AndroidPlatform)).IsTrue();
            }
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Test]
    public async Task ReadTargetFramework_SupportsNet11MultiTargeting()
    {
        var root = CreateProjectDirectory();
        try
        {
            var projectFile = new FileInfo(Path.Combine(root.FullName, "App.csproj"));
            await File.WriteAllTextAsync(
                projectFile.FullName,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFrameworks>net11.0-android;net11.0;net11.0-windows</TargetFrameworks></PropertyGroup></Project>");

            using (Assert.Multiple())
            {
                await Assert.That(ProjectInspector.ReadTargetFramework(projectFile, PublisherService.AndroidPlatform)).IsEqualTo("net11.0-android");
                await Assert.That(ProjectInspector.ReadTargetFramework(projectFile, PublisherService.LinuxPlatform)).IsEqualTo("net11.0");
                await Assert.That(ProjectInspector.ReadTargetFramework(projectFile, PublisherService.WindowsPlatform)).IsEqualTo("net11.0");
            }
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private static DirectoryInfo CreateProjectDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dnap-inspector-{Guid.NewGuid():N}");
        return Directory.CreateDirectory(path);
    }

    private static PublishConfiguration CreateConfiguration(string platform) => new()
    {
        ProjectDirectory = "/tmp",
        PublishPlatform = platform,
        TargetFramework = "net10.0",
        RuntimeIdentifier = "linux-x64",
        Configuration = "Release",
        OutputDirectory = "/tmp/output",
        PackageId = "com.example.app",
        IncludeApk = false,
        IncludeAab = false,
        SelfContained = true,
        PublishTrimmed = false,
        PublishAot = false,
        PublishReadyToRun = false,
        PublishSingleFile = false,
        UseAppHost = true,
        CreateMacAppBundle = false,
        CreateWindowsExecutable = false,
        BuildIpa = false,
        ArchiveOnBuild = false,
        RunAotCompilation = false,
        EnableProfiledAot = false,
        AndroidLinkMode = "None",
        AndroidLinkTool = "r8",
        AndroidDexTool = "d8",
        CreateMappingFile = false,
        EnableMultiDex = false,
        UseAapt2 = true,
        EnableDesugar = true,
        DeleteBin = false,
        DeleteObj = false,
        SignMode = "Auto",
        KeystorePath = string.Empty,
        KeyAlias = string.Empty,
        KeystorePassword = string.Empty,
        KeyPassword = string.Empty
    };
}
