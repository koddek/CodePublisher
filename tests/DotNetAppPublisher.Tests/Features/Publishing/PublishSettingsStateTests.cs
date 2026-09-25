using DotNetAppPublisher.Features.Publishing.Configure;
using DotNetAppPublisher.Services;

namespace DotNetAppPublisher.Tests.Features.Publishing;

public sealed class PublishSettingsStateTests
{
    [Test]
    public async Task Normalize_DisablesReadyToRun_WhenAotIsEnabled()
    {
        var state = new PublishSettingsState(
            PublisherService.LinuxPlatform,
            "net10.0",
            "linux-x64",
            SelfContained: true,
            PublishAot: true,
            PublishReadyToRun: true,
            PublishSingleFile: true,
            PublishTrimmed: false,
            AndroidLinkMode: "None",
            AndroidLinkTool: "r8",
            AndroidDexTool: "d8",
            RunAotCompilation: true,
            EnableProfiledAot: false,
            IncludeApk: true,
            IncludeAab: false);

        var result = PublishSettingsPolicy.Normalize(state);

        await Assert.That(result.PublishReadyToRun).IsFalse();
    }

    [Test]
    public async Task Normalize_DisablesAndroidRunAot_WhenLinkerIsNone()
    {
        var state = new PublishSettingsState(
            PublisherService.AndroidPlatform,
            "net10.0-android",
            "android-arm64",
            SelfContained: true,
            PublishAot: false,
            PublishReadyToRun: false,
            PublishSingleFile: true,
            PublishTrimmed: false,
            AndroidLinkMode: "None",
            AndroidLinkTool: "r8",
            AndroidDexTool: "d8",
            RunAotCompilation: true,
            EnableProfiledAot: true,
            IncludeApk: true,
            IncludeAab: false);

        var result = PublishSettingsPolicy.Normalize(state);

        using (Assert.Multiple())
        {
            await Assert.That(result.RunAotCompilation).IsFalse();
            await Assert.That(result.EnableProfiledAot).IsFalse();
        }
    }

    [Test]
    public async Task Normalize_RequiresAtLeastOnePackageFormat()
    {
        var state = new PublishSettingsState(
            PublisherService.AndroidPlatform,
            "net10.0-android",
            "android-arm64",
            SelfContained: true,
            PublishAot: false,
            PublishReadyToRun: false,
            PublishSingleFile: true,
            PublishTrimmed: false,
            AndroidLinkMode: "SdkOnly",
            AndroidLinkTool: "r8",
            AndroidDexTool: "d8",
            RunAotCompilation: false,
            EnableProfiledAot: false,
            IncludeApk: false,
            IncludeAab: false);

        var result = PublishSettingsPolicy.Normalize(state);

        await Assert.That(result.IncludeAab).IsTrue();
        await Assert.That(result.IncludeApk).IsFalse();
    }

    [Test]
    public async Task Normalize_DisablesTrimming_ForFrameworkDependentPublish()
    {
        var state = new PublishSettingsState(
            PublisherService.LinuxPlatform,
            "net10.0",
            "linux-x64",
            SelfContained: false,
            PublishAot: false,
            PublishReadyToRun: false,
            PublishSingleFile: true,
            PublishTrimmed: true,
            AndroidLinkMode: "None",
            AndroidLinkTool: "r8",
            AndroidDexTool: "d8",
            RunAotCompilation: false,
            EnableProfiledAot: false,
            IncludeApk: false,
            IncludeAab: false);

        var result = PublishSettingsPolicy.Normalize(state);

        await Assert.That(result.PublishTrimmed).IsFalse();
        await Assert.That(PublishOptionPolicy.PublishTrimmedDisabledReason(
            result.PublishAot,
            result.SelfContained,
            result.PublishPlatform,
            result.TargetFramework,
            result.RuntimeIdentifier,
            result.AndroidLinkMode)).Contains("self-contained");
    }

    [Test]
    public async Task Normalize_MakesAotSelfContainedAndRemovesConflictingOptions()
    {
        var state = new PublishSettingsState(
            PublisherService.WindowsPlatform,
            "net10.0-windows",
            "win-x64",
            SelfContained: false,
            PublishAot: true,
            PublishReadyToRun: true,
            PublishSingleFile: true,
            PublishTrimmed: true,
            AndroidLinkMode: "None",
            AndroidLinkTool: "r8",
            AndroidDexTool: "d8",
            RunAotCompilation: false,
            EnableProfiledAot: false,
            IncludeApk: false,
            IncludeAab: false);

        var result = PublishSettingsPolicy.Normalize(state);

        using (Assert.Multiple())
        {
            await Assert.That(result.SelfContained).IsTrue();
            await Assert.That(result.PublishAot).IsTrue();
            await Assert.That(result.PublishReadyToRun).IsFalse();
            await Assert.That(result.PublishSingleFile).IsFalse();
            await Assert.That(result.PublishTrimmed).IsFalse();
        }
    }

    [Test]
    public async Task Normalize_AndroidLinkerEnablesTrimmingAndRunAot()
    {
        var state = new PublishSettingsState(
            PublisherService.AndroidPlatform,
            "net10.0-android",
            "android-arm64",
            SelfContained: true,
            PublishAot: false,
            PublishReadyToRun: false,
            PublishSingleFile: false,
            PublishTrimmed: false,
            AndroidLinkMode: "SdkOnly",
            AndroidLinkTool: "r8",
            AndroidDexTool: "d8",
            RunAotCompilation: true,
            EnableProfiledAot: true,
            IncludeApk: true,
            IncludeAab: false);

        var result = PublishSettingsPolicy.Normalize(state);

        using (Assert.Multiple())
        {
            await Assert.That(result.PublishTrimmed).IsTrue();
            await Assert.That(result.RunAotCompilation).IsTrue();
            await Assert.That(result.EnableProfiledAot).IsTrue();
        }
    }

    [Test]
    public async Task Normalize_AndroidToolsFallsBackToD8AndR8()
    {
        var state = new PublishSettingsState(
            PublisherService.AndroidPlatform,
            "net10.0-android",
            "android-arm64",
            SelfContained: true,
            PublishAot: false,
            PublishReadyToRun: false,
            PublishSingleFile: false,
            PublishTrimmed: true,
            AndroidLinkMode: "Full",
            AndroidLinkTool: "proguard",
            AndroidDexTool: "dx",
            RunAotCompilation: false,
            EnableProfiledAot: false,
            IncludeApk: true,
            IncludeAab: false);

        var result = PublishSettingsPolicy.Normalize(state);

        using (Assert.Multiple())
        {
            await Assert.That(result.AndroidLinkTool).IsEqualTo("r8");
            await Assert.That(result.AndroidDexTool).IsEqualTo("d8");
        }
    }
}
