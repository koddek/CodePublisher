using CodePublisher.Features.Publishing.Build;
using CodePublisher.Models;
using CodePublisher.Services;

namespace CodePublisher.Tests.Features.Publishing.Build;

/// <summary>
/// The Mono-only Android AOT knobs on the generated command, gated by the target framework:
/// .NET 11 Android targets CoreCLR and rejects RunAOTCompilation at any value (XA1044), while
/// net10.0-android and earlier are Mono by default and keep the Mono flags.
/// </summary>
public sealed class AndroidPublishCommandsTests
{
    [Test]
    public async Task Net11Build_OmitsMonoOnlyAotPropertiesAndLeavesReadyToRunToTheSdk()
    {
        var configuration = CreateAndroidConfiguration("net11.0-android");

        var command = AndroidPublishCommands.BuildCommand("/dotnet", configuration, "/tmp/App.csproj", customTrimProperty: null);

        using (Assert.Multiple())
        {
            await Assert.That(command.Any(argument => argument.StartsWith("-p:RunAOTCompilation=", StringComparison.Ordinal))).IsFalse();
            await Assert.That(command.Any(argument => argument.StartsWith("-p:AndroidEnableProfiledAot=", StringComparison.Ordinal))).IsFalse();
            await Assert.That(command.Any(argument => argument.StartsWith("-p:PublishReadyToRun=", StringComparison.Ordinal))).IsFalse();
        }
    }

    [Test]
    public async Task Net10Build_KeepsTheMonoFlags()
    {
        var configuration = CreateAndroidConfiguration("net10.0-android");

        var command = AndroidPublishCommands.BuildCommand("/dotnet", configuration, "/tmp/App.csproj", customTrimProperty: null);

        using (Assert.Multiple())
        {
            await Assert.That(command).Contains("-p:RunAOTCompilation=false");
            await Assert.That(command).Contains("-p:AndroidEnableProfiledAot=false");
            await Assert.That(command).Contains("-p:PublishReadyToRun=false");
        }
    }

    [Test]
    public async Task Net11Build_RejectsRunAotWithAnActionableMessage()
    {
        var configuration = CreateAndroidConfiguration("net11.0-android") with
        {
            RunAotCompilation = true
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            AndroidPublishCommands.BuildCommand("/dotnet", configuration, "/tmp/App.csproj", customTrimProperty: null));

        await Assert.That(exception.Message).Contains("XA1044");
    }

    [Test]
    public async Task Net11Baseline_OmitsMonoOnlyAotProperties()
    {
        var configuration = CreateAndroidConfiguration("net11.0-android");

        var command = AndroidPublishCommands.BuildBaselineCommand(
            "/dotnet", configuration, "/tmp/App.csproj", "/tmp/out", customTrimProperty: null);

        using (Assert.Multiple())
        {
            await Assert.That(command.Any(argument => argument.StartsWith("-p:RunAOTCompilation=", StringComparison.Ordinal))).IsFalse();
            await Assert.That(command.Any(argument => argument.StartsWith("-p:AndroidEnableProfiledAot=", StringComparison.Ordinal))).IsFalse();
            await Assert.That(command).Contains("-p:AndroidPackageFormats=apk");
        }
    }

    [Test]
    public async Task Net10Baseline_KeepsTheMonoFlags()
    {
        var configuration = CreateAndroidConfiguration("net10.0-android");

        var command = AndroidPublishCommands.BuildBaselineCommand(
            "/dotnet", configuration, "/tmp/App.csproj", "/tmp/out", customTrimProperty: null);

        using (Assert.Multiple())
        {
            await Assert.That(command).Contains("-p:RunAOTCompilation=false");
            await Assert.That(command).Contains("-p:AndroidEnableProfiledAot=false");
            await Assert.That(command).Contains("-p:PublishReadyToRun=false");
        }
    }

    [Test]
    public async Task Net11TrimmedBuild_EmitsLinkerAndTrimmerSwitches()
    {
        var configuration = CreateAndroidConfiguration("net11.0-android") with
        {
            AndroidLinkMode = "SdkOnly",
            PublishTrimmed = true
        };

        var command = AndroidPublishCommands.BuildCommand("/dotnet", configuration, "/tmp/App.csproj", customTrimProperty: null);

        using (Assert.Multiple())
        {
            await Assert.That(command).Contains("-p:AndroidLinkMode=SdkOnly");
            await Assert.That(command).Contains("-p:PublishTrimmed=true");
            await Assert.That(command).Contains("-p:AndroidDexTool=d8");
            await Assert.That(command).Contains("-p:AndroidLinkTool=r8");
        }
    }

    private static PublishConfiguration CreateAndroidConfiguration(string targetFramework) => new()
    {
        ProjectDirectory = "/tmp",
        PublishPlatform = PublisherService.AndroidPlatform,
        TargetFramework = targetFramework,
        RuntimeIdentifier = "android-arm64",
        Configuration = "Release",
        OutputDirectory = "/tmp/output",
        PackageId = "com.example.app",
        IncludeApk = true,
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
        KeystorePath = "",
        KeyAlias = "",
        KeystorePassword = "",
        KeyPassword = ""
    };
}
