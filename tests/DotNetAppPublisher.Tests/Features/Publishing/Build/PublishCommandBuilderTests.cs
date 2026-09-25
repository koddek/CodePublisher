using DotNetAppPublisher.Features.Publishing.Build;
using DotNetAppPublisher.Models;
using DotNetAppPublisher.Services;

namespace DotNetAppPublisher.Tests.Features.Publishing.Build;

public sealed class PublishCommandBuilderTests
{
    [Test]
    public async Task AndroidBuild_UsesSelectedFormatsAndMasksSigningSecrets()
    {
        var configuration = CreateConfiguration(PublisherService.AndroidPlatform) with
        {
            IncludeApk = true,
            IncludeAab = true,
            SignMode = "Sign",
            KeystorePath = "/tmp/upload.keystore",
            KeyAlias = "upload",
            KeystorePassword = "store-secret",
            KeyPassword = "key-secret"
        };

        var command = PublishCommandBuilder.Build(
            "/usr/local/share/dotnet/dotnet",
            configuration,
            "/tmp/App.csproj",
            "/tmp/output",
            customTrimProperty: null);

        using (Assert.Multiple())
        {
            await Assert.That(command).Contains("-p:AndroidPackageFormats=aab%3Bapk");
            await Assert.That(command).Contains("-p:AndroidSigningStorePass=$(DOTNET_APP_PUBLISHER_KEYSTORE_PASSWORD)");
            await Assert.That(PublishCommandBuilder.Mask(command)).DoesNotContain("store-secret");
            await Assert.That(PublishCommandBuilder.Mask(command)).DoesNotContain("key-secret");
        }
    }

    [Test]
    public async Task MacCatalystBuild_ForcesTrimmingAndDisablesLinkingWhenRequested()
    {
        var configuration = CreateConfiguration(PublisherService.MacOsPlatform) with
        {
            TargetFramework = "net10.0-maccatalyst",
            RuntimeIdentifier = "maccatalyst-arm64",
            PublishTrimmed = false
        };

        var command = PublishCommandBuilder.Build(
            "/dotnet",
            configuration,
            "/tmp/App.csproj",
            "/tmp/output",
            customTrimProperty: null);

        using (Assert.Multiple())
        {
            await Assert.That(command).Contains("-p:PublishTrimmed=true");
            await Assert.That(command).Contains("-p:MtouchLink=None");
        }
    }

    [Test]
    public async Task IosSimulatorBuild_UsesBuildVerbAndOmitsArchiveOptions()
    {
        var configuration = CreateConfiguration(PublisherService.IosPlatform) with
        {
            RuntimeIdentifier = "iossimulator-arm64",
            ArchiveOnBuild = true,
            BuildIpa = true
        };

        var command = PublishCommandBuilder.Build(
            "/dotnet",
            configuration,
            "/tmp/App.csproj",
            "/tmp/output",
            customTrimProperty: null);

        using (Assert.Multiple())
        {
            await Assert.That(command[1]).IsEqualTo("build");
            await Assert.That(command).DoesNotContain("-p:ArchiveOnBuild=true");
            await Assert.That(command).DoesNotContain("-p:BuildIpa=true");
        }
    }

    [Test]
    public async Task AndroidBaseline_UsesSafeDefaults()
    {
        var configuration = CreateConfiguration(PublisherService.AndroidPlatform);

        var command = PublishCommandBuilder.BuildBaseline(
            "/dotnet",
            configuration,
            "/tmp/App.csproj",
            "/tmp/output",
            customTrimProperty: null);

        using (Assert.Multiple())
        {
            await Assert.That(command).Contains("-p:AndroidLinkMode=None");
            await Assert.That(command).Contains("-p:RunAOTCompilation=false");
            await Assert.That(command).Contains("-p:AndroidPackageFormats=apk");
            await Assert.That(command).Contains("-p:PublishTrimmed=false");
        }
    }

    [Test]
    public async Task AndroidBuild_RejectsRunAotWithoutLinker()
    {
        var configuration = CreateConfiguration(PublisherService.AndroidPlatform) with
        {
            AndroidLinkMode = "None",
            RunAotCompilation = true
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            PublishCommandBuilder.Build(
                "/dotnet",
                configuration,
                "/tmp/App.csproj",
                "/tmp/output",
                customTrimProperty: null));

        await Assert.That(exception.Message).Contains("RunAOTCompilation requires linking");
    }

    [Test]
    public async Task WindowsAot_AddsAotPropertyAndDisablesReadyToRun()
    {
        var configuration = CreateConfiguration(PublisherService.WindowsPlatform) with
        {
            TargetFramework = "net10.0-windows",
            RuntimeIdentifier = "win-x64",
            PublishAot = true,
            PublishReadyToRun = false,
            PublishSingleFile = false
        };

        var command = PublishCommandBuilder.Build(
            "/dotnet",
            configuration,
            "/tmp/App.csproj",
            "/tmp/output",
            customTrimProperty: null);

        using (Assert.Multiple())
        {
            await Assert.That(command).Contains("-p:PublishAot=true");
            await Assert.That(command).Contains("-p:PublishReadyToRun=false");
            await Assert.That(command).Contains("-p:PublishSingleFile=false");
        }
    }

    [Test]
    public async Task FrameworkDependentTrim_IsRejectedBeforeCommandCreation()
    {
        var configuration = CreateConfiguration(PublisherService.LinuxPlatform) with
        {
            SelfContained = false,
            PublishTrimmed = true
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            PublishCommandBuilder.Build(
                "/dotnet",
                configuration,
                "/tmp/App.csproj",
                "/tmp/output",
                customTrimProperty: null));

        await Assert.That(exception.Message).Contains("self-contained");
    }

    [Test]
    public async Task AndroidRunAot_RequiresTrimming()
    {
        var configuration = CreateConfiguration(PublisherService.AndroidPlatform) with
        {
            AndroidLinkMode = "SdkOnly",
            RunAotCompilation = true,
            PublishTrimmed = false
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            PublishCommandBuilder.Build(
                "/dotnet",
                configuration,
                "/tmp/App.csproj",
                "/tmp/output",
                customTrimProperty: null));

        await Assert.That(exception.Message).Contains("PublishTrimmed=true");
    }

    [Test]
    public async Task AndroidD8WithProguard_IsRejected()
    {
        var configuration = CreateConfiguration(PublisherService.AndroidPlatform) with
        {
            AndroidLinkMode = "SdkOnly",
            AndroidLinkTool = "proguard",
            AndroidDexTool = "d8",
            PublishTrimmed = true
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            PublishCommandBuilder.Build(
                "/dotnet",
                configuration,
                "/tmp/App.csproj",
                "/tmp/output",
                customTrimProperty: null));

        await Assert.That(exception.Message).Contains("R8");
    }

    [Test]
    public async Task NativeAotAndSingleFile_IsRejected()
    {
        var configuration = CreateConfiguration(PublisherService.LinuxPlatform) with
        {
            PublishAot = true,
            PublishSingleFile = true
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            PublishCommandBuilder.Build(
                "/dotnet",
                configuration,
                "/tmp/App.csproj",
                "/tmp/output",
                customTrimProperty: null));

        await Assert.That(exception.Message).Contains("already produces a native executable");
    }

    [Test]
    public async Task CustomTrimProperty_KeepsStandardTrimPropertyInSync()
    {
        var configuration = CreateConfiguration(PublisherService.LinuxPlatform) with
        {
            PublishTrimmed = true
        };

        var command = PublishCommandBuilder.Build(
            "/dotnet",
            configuration,
            "/tmp/App.csproj",
            "/tmp/output",
            customTrimProperty: "MyPublishTrimmed");

        using (Assert.Multiple())
        {
            await Assert.That(command).Contains("-p:PublishTrimmed=true");
            await Assert.That(command).Contains("-p:MyPublishTrimmed=true");
        }
    }

    [Test]
    [Arguments(PublisherService.AndroidPlatform)]
    [Arguments(PublisherService.MacOsPlatform)]
    [Arguments(PublisherService.WindowsPlatform)]
    [Arguments(PublisherService.IosPlatform)]
    [Arguments(PublisherService.LinuxPlatform)]
    public async Task DefaultProfile_ExplicitlyOverridesProjectOptimizationDefaults(string platform)
    {
        var command = PublishCommandBuilder.Build(
            "/dotnet",
            CreateConfiguration(platform),
            "/tmp/App.csproj",
            "/tmp/output",
            customTrimProperty: null);

        using (Assert.Multiple())
        {
            await Assert.That(command).Contains("-p:PublishAot=false");
            await Assert.That(command).Contains("-p:PublishReadyToRun=false");
            await Assert.That(command.Any(item => item.StartsWith("-p:SelfContained=", StringComparison.Ordinal))).IsTrue();
        }
    }

    private static PublishConfiguration CreateConfiguration(string platform) => new()
    {
        ProjectDirectory = "/tmp",
        PublishPlatform = platform,
        TargetFramework = platform == PublisherService.AndroidPlatform ? "net10.0-android" : "net10.0",
        RuntimeIdentifier = platform == PublisherService.AndroidPlatform ? "android-arm64" : "linux-x64",
        Configuration = "Release",
        OutputDirectory = "/tmp/output",
        PackageId = "com.example.app",
        IncludeApk = platform == PublisherService.AndroidPlatform,
        IncludeAab = false,
        SelfContained = true,
        PublishTrimmed = false,
        PublishAot = false,
        PublishReadyToRun = false,
        PublishSingleFile = true,
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
