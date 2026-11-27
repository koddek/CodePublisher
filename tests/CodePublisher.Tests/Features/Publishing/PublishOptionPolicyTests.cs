using CodePublisher.Features.Publishing.Configure;
using CodePublisher.Services;

namespace CodePublisher.Tests.Features.Publishing;

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
    public async Task AndroidTrimCheckboxStaysEnabledWhenLinkModeIsNone()
    {
        // Link Mode and trimming form a cycle; a hard-disabled checkbox would strand the user.
        var enabled = AndroidPublishPolicy.IsPublishTrimmedEnabled(selfContained: true, androidLinkMode: "None");
        var viaFacade = PublishOptionPolicy.IsPublishTrimmedEnabled(
            publishAot: false,
            selfContained: true,
            publishPlatform: PublisherService.AndroidPlatform,
            targetFramework: "net11.0-android",
            runtimeIdentifier: "android-arm64",
            androidLinkMode: "None");

        using (Assert.Multiple())
        {
            await Assert.That(enabled).IsTrue();
            await Assert.That(viaFacade).IsTrue();
        }
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
            androidLinkMode: "None")).Contains("always trims");
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
    [Arguments("net11.0-android", "Release", true, true)]
    [Arguments("net11.0-android", "Debug", true, false)]
    [Arguments("net11.0-android", "Release", false, false)]
    [Arguments("net10.0-android", "Release", true, false)]
    public async Task IsReadyToRunAutomatic_MatchesCoreClrReleaseTrimming(
        string targetFramework,
        string configuration,
        bool publishTrimmed,
        bool expected)
    {
        // SDK 11.0.100 evaluates PublishReadyToRun to true for a net11.0-android Release
        // publish whenever PublishTrimmed is true. The checkbox reflects that SDK decision.
        var result = PublishOptionPolicy.IsReadyToRunAutomatic(
            PublisherService.AndroidPlatform, targetFramework, configuration, publishTrimmed);

        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task ReadyToRunDisabledReason_ExplainsTheAutomaticCoreClrSetting()
    {
        var reason = PublishOptionPolicy.ReadyToRunDisabledReason(
            publishAot: false,
            publishPlatform: PublisherService.AndroidPlatform,
            targetFramework: "net11.0-android",
            runtimeIdentifier: "android-arm64",
            configuration: "Release",
            publishTrimmed: true);

        await Assert.That(reason).Contains("on automatically");
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
    public async Task PublishTrimmedDisabledReason_ExplainsThatAotAlwaysTrims()
    {
        // Native AOT implies trimming, and the SDK refuses an explicit PublishTrimmed=false with
        // "PublishTrimmed is implied by native compilation and cannot be disabled." The switch is
        // therefore on and locked, not off.
        var result = PublishOptionPolicy.PublishTrimmedDisabledReason(
            publishAot: true,
            selfContained: true,
            publishPlatform: PublisherService.LinuxPlatform,
            targetFramework: "net10.0",
            runtimeIdentifier: "linux-x64",
            androidLinkMode: "None");

        await Assert.That(result).Contains("always trims");
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
    [Arguments("r8", "dx", "The .NET for Android workload supports D8; the legacy DX compiler is deprecated and is not a supported profile.")]
    public async Task AndroidToolValidation_RejectsUnsupportedCombinations(
        string linkTool,
        string dexTool,
        string expectedMessage)
    {
        var message = PublishOptionPolicy.AndroidToolValidationMessage(linkTool, dexTool);

        await Assert.That(message).IsEqualTo(expectedMessage);
    }

    [Test]
    public async Task Net11AndroidPreflight_RejectsMonoPropertiesEvenWhenFalse()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            PublishOptionPolicy.ValidateProjectProperties(
                PublisherService.AndroidPlatform,
                "net11.0-android",
                new Dictionary<string, string> { ["RunAOTCompilation"] = "false" }));

        await Assert.That(exception.Message).Contains("XA1044");
        PublishOptionPolicy.ValidateProjectProperties(
            PublisherService.AndroidPlatform,
            "net10.0-android",
            new Dictionary<string, string> { ["RunAOTCompilation"] = "false" });
    }

    [Test]
    public async Task Net11AndroidPreflight_RejectsProfiledAotFromImportedProps()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            PublishOptionPolicy.ValidateProjectProperties(
                PublisherService.AndroidPlatform,
                "net11.0-android",
                new Dictionary<string, string> { ["AndroidEnableProfiledAot"] = "true" }));

        await Assert.That(exception.Message).Contains("imported props");
    }

    [Test]
    public async Task Net11ApplePreflight_RejectsExplicitPublishTrimmed()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            PublishOptionPolicy.ValidateProjectProperties(
                PublisherService.IosPlatform,
                "net11.0-ios",
                new Dictionary<string, string> { ["PublishTrimmed"] = "true" }));

        await Assert.That(exception.Message).Contains("TrimMode=copy");
        PublishOptionPolicy.ValidateProjectProperties(
            PublisherService.IosPlatform,
            "net10.0-ios",
            new Dictionary<string, string> { ["PublishTrimmed"] = "true" });
    }

    [Test]
    public async Task Net11CatalystPreflight_RejectsExplicitPublishTrimmed()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            PublishOptionPolicy.ValidateProjectProperties(
                PublisherService.MacOsPlatform,
                "net11.0-maccatalyst",
                new Dictionary<string, string> { ["PublishTrimmed"] = "false" }));

        await Assert.That(exception.Message).Contains("Remove it");
    }
}
