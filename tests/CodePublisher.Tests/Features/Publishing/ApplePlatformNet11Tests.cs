using CodePublisher.Features.Publishing.Build;
using CodePublisher.Features.Publishing.Configure;
using CodePublisher.Models;
using CodePublisher.Services;

namespace CodePublisher.Tests.Features.Publishing;

/// <summary>
/// Verifies the option state and the generated command for the .NET 11 Apple targets against the
/// CoreCLR behavior of SDK 11.0.100. On .NET 11 the workload owns trimming and ReadyToRun, so the
/// app must not emit properties that set or disable them.
/// </summary>
public sealed class ApplePlatformNet11Tests
{
    private static PublishConfiguration Configuration(
        string platform = PublisherService.IosPlatform,
        string targetFramework = "net11.0-ios",
        string runtimeIdentifier = "ios-arm64") => new()
    {
        ProjectDirectory = "/tmp",
        PublishPlatform = platform,
        TargetFramework = targetFramework,
        RuntimeIdentifier = runtimeIdentifier,
        Configuration = "Release",
        OutputDirectory = "/tmp/output",
        PackageId = "com.example.app",
        IncludeApk = false,
        IncludeAab = false,
        SelfContained = true,
        PublishTrimmed = true,
        PublishAot = false,
        PublishReadyToRun = false,
        PublishSingleFile = false,
        UseAppHost = false,
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
        KeyPassword = string.Empty,
        ProjectName = "App"
    };

    [Test]
    public async Task IosNet11Command_OmitsTrimAndReadyToRunProperties()
    {
        var command = PublishCommandBuilder.Build("/dotnet", Configuration(), "/tmp/App.csproj", "/tmp/output", null);

        using (Assert.Multiple())
        {
            await Assert.That(command.Any(item => item.StartsWith("-p:PublishTrimmed=", StringComparison.Ordinal))).IsFalse();
            await Assert.That(command.Any(item => item.StartsWith("-p:PublishReadyToRun=", StringComparison.Ordinal))).IsFalse();
            await Assert.That(command).Contains("-p:PublishAot=false");
        }
    }

    [Test]
    public async Task CatalystNet11Command_OmitsTrimAndReadyToRunProperties()
    {
        var config = Configuration(PublisherService.MacOsPlatform, "net11.0-maccatalyst", "maccatalyst-arm64");
        var command = PublishCommandBuilder.Build("/dotnet", config, "/tmp/App.csproj", "/tmp/output", null);

        using (Assert.Multiple())
        {
            await Assert.That(command.Any(item => item.StartsWith("-p:PublishTrimmed=", StringComparison.Ordinal))).IsFalse();
            await Assert.That(command.Any(item => item.StartsWith("-p:PublishReadyToRun=", StringComparison.Ordinal))).IsFalse();
        }
    }

    [Test]
    public async Task IosNet10Command_KeepsMonoTrimFlags()
    {
        var config = Configuration(targetFramework: "net10.0-ios");
        var command = PublishCommandBuilder.Build("/dotnet", config, "/tmp/App.csproj", "/tmp/output", null);

        using (Assert.Multiple())
        {
            await Assert.That(command).Contains("-p:PublishTrimmed=true");
            await Assert.That(command).Contains("-p:PublishReadyToRun=false");
        }
    }

    [Test]
    public async Task IosNet11OptOut_UsesMtouchLinkInsteadOfPublishTrimmed()
    {
        var config = Configuration() with { PublishTrimmed = false };
        var command = PublishCommandBuilder.Build("/dotnet", config, "/tmp/App.csproj", "/tmp/output", null);

        using (Assert.Multiple())
        {
            await Assert.That(command).Contains("-p:MtouchLink=None");
            await Assert.That(command.Any(item => item.StartsWith("-p:PublishTrimmed=", StringComparison.Ordinal))).IsFalse();
        }
    }

    [Test]
    public async Task ReadyToRunAutomatic_IsTrueForIosAndCatalystNet11()
    {
        using (Assert.Multiple())
        {
            await Assert.That(PublishOptionPolicy.IsReadyToRunAutomatic(
                PublisherService.IosPlatform, "net11.0-ios", "Release", true)).IsTrue();
            await Assert.That(PublishOptionPolicy.IsReadyToRunAutomatic(
                PublisherService.MacOsPlatform, "net11.0-maccatalyst", "Release", true)).IsTrue();
            await Assert.That(PublishOptionPolicy.IsReadyToRunAutomatic(
                PublisherService.IosPlatform, "net10.0-ios", "Release", true)).IsFalse();
        }
    }

    [Test]
    public async Task PublishTrimmedAutomatic_IsTrueForIosAndCatalystNet11()
    {
        using (Assert.Multiple())
        {
            await Assert.That(PublishOptionPolicy.IsPublishTrimmedAutomatic(
                PublisherService.IosPlatform, "net11.0-ios")).IsTrue();
            await Assert.That(PublishOptionPolicy.IsPublishTrimmedAutomatic(
                PublisherService.MacOsPlatform, "net11.0-maccatalyst")).IsTrue();
            await Assert.That(PublishOptionPolicy.IsPublishTrimmedAutomatic(
                PublisherService.IosPlatform, "net10.0-ios")).IsFalse();
        }
    }
}
