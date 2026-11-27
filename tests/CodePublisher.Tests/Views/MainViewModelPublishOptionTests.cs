using CodePublisher.Services;
using CodePublisher.ViewModels;

namespace CodePublisher.Tests.Views;

/// <summary>
/// Perimeter tests over the option state and enable gates that the Build tab binds to. These
/// reproduce the reported UI deadlock: unchecking Publish trimmed must not leave Link Mode or
/// the trimming checkbox in a state the user cannot escape.
/// </summary>
public sealed class MainViewModelPublishOptionTests
{
    private static MainViewModel Create(string platform, string targetFramework, string runtimeIdentifier)
    {
        var viewModel = new MainViewModel(new PublisherService(), new DesktopInteractionService());
        viewModel.PublishPlatform = platform;
        viewModel.TargetFramework = targetFramework;
        viewModel.RuntimeIdentifier = runtimeIdentifier;
        return viewModel;
    }

    [Test]
    [Arguments("net11.0-android", false)]
    [Arguments("net10.0-android", true)]
    public async Task AndroidTrimAndLinkModeCycleNeverStrandsTheUser(string targetFramework, bool monoAot)
    {
        var viewModel = Create("Android", targetFramework, "android-arm64");

        await Assert.That(viewModel.PublishTrimmed).IsTrue();
        await Assert.That(viewModel.AndroidLinkMode).IsEqualTo("SdkOnly");

        viewModel.PublishTrimmed = false;

        using (Assert.Multiple())
        {
            await Assert.That(viewModel.PublishTrimmed).IsFalse();
            await Assert.That(viewModel.AndroidLinkMode).IsEqualTo("None");
            // The key UI fix: the checkbox stays actionable even while Link Mode is None.
            await Assert.That(viewModel.IsPublishTrimmedEnabled).IsTrue();
        }

        viewModel.AndroidLinkMode = "SdkOnly";

        using (Assert.Multiple())
        {
            await Assert.That(viewModel.AndroidLinkMode).IsEqualTo("SdkOnly");
            await Assert.That(viewModel.PublishTrimmed).IsTrue();
            await Assert.That(viewModel.IsPublishTrimmedEnabled).IsTrue();
            await Assert.That(viewModel.IsShrinkerSettingsEnabled).IsTrue();
            await Assert.That(viewModel.RunAotCompilation).IsEqualTo(monoAot);
            await Assert.That(viewModel.EnableProfiledAot).IsEqualTo(monoAot);
        }
    }

    [Test]
    public async Task AndroidTrimCanBeReEnabledDirectlyFromLinkModeNone()
    {
        var viewModel = Create("Android", "net11.0-android", "android-arm64");

        viewModel.AndroidLinkMode = "None";
        await Assert.That(viewModel.PublishTrimmed).IsFalse();

        viewModel.PublishTrimmed = true;

        using (Assert.Multiple())
        {
            await Assert.That(viewModel.PublishTrimmed).IsTrue();
            await Assert.That(viewModel.AndroidLinkMode).IsEqualTo("SdkOnly");
        }
    }

    [Test]
    [Arguments("net11.0-android")]
    [Arguments("net10.0-android")]
    public async Task FullLinkModeRestoresTrimAfterItWasTurnedOff(string targetFramework)
    {
        var viewModel = Create("Android", targetFramework, "android-arm64");

        viewModel.PublishTrimmed = false;
        viewModel.AndroidLinkMode = "Full";

        using (Assert.Multiple())
        {
            await Assert.That(viewModel.AndroidLinkMode).IsEqualTo("Full");
            await Assert.That(viewModel.PublishTrimmed).IsTrue();
            await Assert.That(viewModel.IsPublishTrimmedEnabled).IsTrue();
        }
    }

    [Test]
    [Arguments("net11.0-android")]
    [Arguments("net10.0-android")]
    public async Task SelectingLinkModeAfterSelfContainedOffRestoresWholeChain(string targetFramework)
    {
        var viewModel = Create("Android", targetFramework, "android-arm64");

        viewModel.SelfContained = false;
        await Assert.That(viewModel.AndroidLinkMode).IsEqualTo("None");

        viewModel.AndroidLinkMode = "SdkOnly";

        using (Assert.Multiple())
        {
            await Assert.That(viewModel.SelfContained).IsTrue();
            await Assert.That(viewModel.PublishTrimmed).IsTrue();
            await Assert.That(viewModel.AndroidLinkMode).IsEqualTo("SdkOnly");
        }
    }

    [Test]
    public async Task Net10ProfiledAotReturnsWhenRunAotIsTurnedBackOn()
    {
        var viewModel = Create("Android", "net10.0-android", "android-arm64");

        viewModel.RunAotCompilation = false;
        using (Assert.Multiple())
        {
            await Assert.That(viewModel.RunAotCompilation).IsFalse();
            await Assert.That(viewModel.EnableProfiledAot).IsFalse();
            await Assert.That(viewModel.PublishTrimmed).IsTrue();
            await Assert.That(viewModel.AndroidLinkMode).IsEqualTo("SdkOnly");
        }

        viewModel.RunAotCompilation = true;
        await Assert.That(viewModel.EnableProfiledAot).IsTrue();
    }

    [Test]
    [Arguments("Android", "net11.0-android", "android-arm64", false, false)]
    [Arguments("Android", "net10.0-android", "android-arm64", false, false)]
    [Arguments("iOS", "net11.0-ios", "iossimulator-arm64", false, false)]
    [Arguments("macOS", "net11.0-maccatalyst", "maccatalyst-arm64", false, true)]
    [Arguments("macOS", "net11.0", "osx-arm64", true, true)]
    [Arguments("Windows", "net11.0-windows", "win-x64", true, false)]
    [Arguments("Linux", "net11.0", "linux-x64", true, false)]
    [Arguments("NuGet", "net11.0", "any", false, false)]
    public async Task OptionsUnusedByAPlatformAreHidden(
        string platform,
        string targetFramework,
        string runtimeIdentifier,
        bool expectDesktopOptions,
        bool expectMacBundle)
    {
        var viewModel = Create(platform, targetFramework, runtimeIdentifier);

        using (Assert.Multiple())
        {
            await Assert.That(viewModel.IsPublishAotVisible).IsEqualTo(expectDesktopOptions);
            await Assert.That(viewModel.IsPublishSingleFileVisible).IsEqualTo(expectDesktopOptions);
            await Assert.That(viewModel.IsUseAppHostVisible).IsEqualTo(expectDesktopOptions);
            await Assert.That(viewModel.IsReadyToRunVisible).IsEqualTo(expectDesktopOptions);
            await Assert.That(viewModel.IsMacOsPublishOptionsVisible).IsEqualTo(expectMacBundle);
        }
    }

    [Test]
    [Arguments("Android", "net11.0-android", "android-arm64", true)]
    [Arguments("Android", "net10.0-android", "android-arm64", false)]
    [Arguments("iOS", "net11.0-ios", "iossimulator-arm64", true)]
    [Arguments("iOS", "net10.0-ios", "iossimulator-arm64", false)]
    [Arguments("Windows", "net11.0-windows", "win-x64", true)]
    public async Task ReadyToRunRowVisibilityFollowsAutomaticOrManualAvailability(
        string platform,
        string targetFramework,
        string runtimeIdentifier,
        bool expectedVisible)
    {
        var viewModel = Create(platform, targetFramework, runtimeIdentifier);
        await Assert.That(viewModel.IsReadyToRunOptionVisible).IsEqualTo(expectedVisible);
    }

    [Test]
    public async Task CatalystSingleFileIsDisabledAndNormalizedOff()
    {
        var viewModel = Create("macOS", "net11.0-maccatalyst", "maccatalyst-arm64");

        using (Assert.Multiple())
        {
            await Assert.That(viewModel.IsPublishSingleFileEnabled).IsFalse();
            await Assert.That(viewModel.PublishSingleFile).IsFalse();
        }
    }

    [Test]
    public async Task AndroidMappingFileFollowsLinkModeAndComesBack()
    {
        var viewModel = Create("Android", "net11.0-android", "android-arm64");

        viewModel.CreateMappingFile = true;
        await Assert.That(viewModel.CreateMappingFile).IsTrue();

        viewModel.AndroidLinkMode = "None";
        using (Assert.Multiple())
        {
            await Assert.That(viewModel.AndroidLinkMode).IsEqualTo("None");
            await Assert.That(viewModel.CreateMappingFile).IsFalse();
            await Assert.That(viewModel.IsShrinkerSettingsEnabled).IsFalse();
        }

        viewModel.AndroidLinkMode = "Full";
        using (Assert.Multiple())
        {
            await Assert.That(viewModel.AndroidLinkMode).IsEqualTo("Full");
            await Assert.That(viewModel.CreateMappingFile).IsTrue();
            await Assert.That(viewModel.IsShrinkerSettingsEnabled).IsTrue();
        }
    }
}
