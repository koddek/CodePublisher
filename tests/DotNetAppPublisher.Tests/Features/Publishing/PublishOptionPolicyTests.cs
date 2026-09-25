using DotNetAppPublisher.Features.Publishing.Configure;
using DotNetAppPublisher.Services;

namespace DotNetAppPublisher.Tests.Features.Publishing;

public sealed class PublishOptionPolicyTests
{
    [Test]
    [Arguments(PublisherService.AndroidPlatform, false, false)]
    [Arguments(PublisherService.AndroidPlatform, true, false)]
    [Arguments(PublisherService.MacOsPlatform, false, true)]
    [Arguments(PublisherService.WindowsPlatform, true, false)]
    public async Task IsReadyToRunEnabled_ReturnsExpectedValue(
        string platform,
        bool publishAot,
        bool expected)
    {
        var result = PublishOptionPolicy.IsReadyToRunEnabled(publishAot, platform);

        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    [Arguments("None", false)]
    [Arguments("SdkOnly", true)]
    [Arguments("Full", true)]
    public async Task IsAndroidAotEnabled_ReflectsLinkMode(
        string linkMode,
        bool expected)
    {
        var result = PublishOptionPolicy.IsAndroidAotEnabled(linkMode);

        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task IsPublishTrimmedEnabled_DisablesTrimming_WhenAotEnabled()
    {
        var result = PublishOptionPolicy.IsPublishTrimmedEnabled(
            publishAot: true,
            selfContained: true,
            publishPlatform: PublisherService.LinuxPlatform,
            targetFramework: "net10.0",
            runtimeIdentifier: "linux-x64",
            androidLinkMode: "None");

        await Assert.That(result).IsFalse();
        await Assert.That(PublishOptionPolicy.PublishTrimmedDisabledReason(
            publishAot: true,
            selfContained: true,
            publishPlatform: PublisherService.LinuxPlatform,
            targetFramework: "net10.0",
            runtimeIdentifier: "linux-x64",
            androidLinkMode: "None")).IsEqualTo("Trimming is implied by Native AOT.");
    }

    [Test]
    public async Task IsArchiveOnBuildEnabled_OnlyForDeviceIosRuntime()
    {
        await Assert.That(PublishOptionPolicy.IsArchiveOnBuildEnabled(
            PublisherService.IosPlatform,
            "ios-arm64")).IsTrue();
        await Assert.That(PublishOptionPolicy.IsArchiveOnBuildEnabled(
            PublisherService.IosPlatform,
            "iossimulator-arm64")).IsFalse();
        await Assert.That(PublishOptionPolicy.IsArchiveOnBuildEnabled(
            PublisherService.AndroidPlatform,
            "android-arm64")).IsFalse();
    }

    [Test]
    public async Task IsReadyToRunEnabled_DisablesMacCatalyst()
    {
        var result = PublishOptionPolicy.IsReadyToRunEnabled(
            publishAot: false,
            publishPlatform: PublisherService.MacOsPlatform,
            targetFramework: "net10.0-maccatalyst",
            runtimeIdentifier: "maccatalyst-arm64");

        using (Assert.Multiple())
        {
            await Assert.That(result).IsFalse();
            await Assert.That(PublishOptionPolicy.ReadyToRunDisabledReason(
                publishAot: false,
                publishPlatform: PublisherService.MacOsPlatform,
                targetFramework: "net10.0-maccatalyst",
                runtimeIdentifier: "maccatalyst-arm64")).Contains("MacCatalyst");
        }
    }

    [Test]
    public async Task IsPublishTrimmedEnabled_RejectsFrameworkDependentPublish()
    {
        var result = PublishOptionPolicy.IsPublishTrimmedEnabled(
            publishAot: false,
            selfContained: false,
            publishPlatform: PublisherService.LinuxPlatform,
            targetFramework: "net10.0",
            runtimeIdentifier: "linux-x64",
            androidLinkMode: "None");

        await Assert.That(result).IsFalse();
        await Assert.That(PublishOptionPolicy.PublishTrimmedDisabledReason(
            publishAot: false,
            selfContained: false,
            publishPlatform: PublisherService.LinuxPlatform,
            targetFramework: "net10.0",
            runtimeIdentifier: "linux-x64",
            androidLinkMode: "None")).IsEqualTo("Trimming requires a self-contained publish.");
    }

    [Test]
    public async Task IsPublishSingleFileEnabled_IsDisabledForNativeAot()
    {
        var result = PublishOptionPolicy.IsPublishSingleFileEnabled(
            publishAot: true,
            publishPlatform: PublisherService.LinuxPlatform);

        await Assert.That(result).IsFalse();
    }

    [Test]
    [Arguments("r8", "d8", "")]
    [Arguments("proguard", "d8", "D8 requires the R8 code shrinker; ProGuard with D8 is no longer supported.")]
    [Arguments("r8", "dx", "The .NET 10 Android workload supports D8; the legacy DX compiler is deprecated and is not a supported profile.")]
    public async Task AndroidToolValidation_RejectsUnsupportedCombinations(
        string linkTool,
        string dexTool,
        string expectedMessage)
    {
        var message = PublishOptionPolicy.AndroidToolValidationMessage(linkTool, dexTool);

        await Assert.That(message).IsEqualTo(expectedMessage);
    }
}
