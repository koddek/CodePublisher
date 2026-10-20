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
    public async Task Normalize_TurnsOnTheLinker_WhenAndroidRunAotIsRequested()
    {
        // Android ahead-of-time compilation needs the linker, and trimming follows the linker.
        // Turning RunAOT on therefore switches the link mode to SdkOnly instead of silently
        // dropping the request.
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
            await Assert.That(result.AndroidLinkMode).IsEqualTo("SdkOnly");
            await Assert.That(result.PublishTrimmed).IsTrue();
            await Assert.That(result.RunAotCompilation).IsTrue();
            await Assert.That(result.EnableProfiledAot).IsTrue();
        }
    }

    [Test]
    public async Task Normalize_TurnsOffAndroidRunAot_WhenTheLinkerIsTurnedOff()
    {
        // Unchecking a prerequisite turns off everything that relies on it: without the linker
        // there is no trimming, and ahead-of-time compilation needs trimming.
        var state = new PublishSettingsState(
            PublisherService.AndroidPlatform,
            "net10.0-android",
            "android-arm64",
            SelfContained: true,
            PublishAot: false,
            PublishReadyToRun: false,
            PublishSingleFile: true,
            PublishTrimmed: true,
            AndroidLinkMode: "SdkOnly",
            AndroidLinkTool: "r8",
            AndroidDexTool: "d8",
            RunAotCompilation: true,
            EnableProfiledAot: true,
            IncludeApk: true,
            IncludeAab: false);

        var result = PublishSettingsPolicy.Normalize(
            state with { AndroidLinkMode = "None" },
            PublishOptionId.AndroidLinkMode,
            changedValue: false);

        using (Assert.Multiple())
        {
            await Assert.That(result.AndroidLinkMode).IsEqualTo("None");
            await Assert.That(result.PublishTrimmed).IsFalse();
            await Assert.That(result.RunAotCompilation).IsFalse();
            await Assert.That(result.EnableProfiledAot).IsFalse();
        }
    }

    [Test]
    public async Task Normalize_AlwaysIncludesApk_WhenNoPackageFormatIsSelected()
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

        await Assert.That(result.IncludeApk).IsTrue();
        await Assert.That(result.IncludeAab).IsFalse();
    }

    [Test]
    public async Task Normalize_SelectingAab_AlsoBuildsApk()
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
            IncludeAab: true);

        var result = PublishSettingsPolicy.Normalize(state);

        using (Assert.Multiple())
        {
            await Assert.That(result.IncludeApk).IsTrue();
            await Assert.That(result.IncludeAab).IsTrue();
        }
    }

    [Test]
    public async Task Normalize_TurnsOnSelfContained_WhenTrimmingIsRequested()
    {
        // Trimming needs a self-contained publish. The .NET SDK infers self-contained for a
        // trimmed publish since .NET 8, so the option set does the same instead of silently
        // dropping the user's trimming request.
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

        using (Assert.Multiple())
        {
            await Assert.That(result.SelfContained).IsTrue();
            await Assert.That(result.PublishTrimmed).IsTrue();
        }
    }

    [Test]
    public async Task Normalize_TurnsOffTrimming_WhenSelfContainedIsTurnedOff()
    {
        // Unchecking a prerequisite turns off everything that relies on it.
        var state = new PublishSettingsState(
            PublisherService.LinuxPlatform,
            "net10.0",
            "linux-x64",
            SelfContained: true,
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

        var result = PublishSettingsPolicy.Normalize(
            state with { SelfContained = false },
            PublishOptionId.SelfContained,
            changedValue: false);

        using (Assert.Multiple())
        {
            await Assert.That(result.SelfContained).IsFalse();
            await Assert.That(result.PublishTrimmed).IsFalse();
        }
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
            PublishTrimmed: false,
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

            // Native AOT always trims. The SDK fails the build with "PublishTrimmed is implied
            // by native compilation and cannot be disabled." when trimming is off, so trimming
            // stays on.
            await Assert.That(result.PublishTrimmed).IsTrue();
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
