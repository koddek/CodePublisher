using CodePublisher.Features.Publishing.Configure;

namespace CodePublisher.Tests.Features.Publishing;

/// <summary>
/// Characterization tests for <see cref="PublishOptionSession"/>: user intent is kept separate
/// from derived effective state, so deselecting an option restores what it changed. These capture
/// the restoration behavior the flat resolver alone cannot provide.
/// </summary>
public sealed class PublishOptionSessionTests
{
    private static PublishSettingsState State(
        string platform = "Windows",
        string targetFramework = "net11.0-windows",
        string runtimeIdentifier = "win-x64",
        bool selfContained = true,
        bool publishAot = false,
        bool publishReadyToRun = false,
        bool publishSingleFile = false,
        bool publishTrimmed = false,
        string androidLinkMode = "None",
        bool runAotCompilation = false,
        bool enableProfiledAot = false,
        bool useAppHost = true,
        bool buildIpa = false,
        bool archiveOnBuild = false,
        bool enableDesugar = true,
        string androidLinkTool = "r8",
        string androidDexTool = "d8") => new(
        platform, targetFramework, runtimeIdentifier,
        selfContained, publishAot, publishReadyToRun, publishSingleFile, publishTrimmed,
        androidLinkMode, androidLinkTool, androidDexTool,
        runAotCompilation, enableProfiledAot,
        IncludeApk: true, IncludeAab: false,
        useAppHost, CreateMacAppBundle: true, CreateWindowsExecutable: true,
        buildIpa, archiveOnBuild,
        CreateMappingFile: false, EnableMultiDex: false, UseAapt2: true, enableDesugar,
        Configuration: "Release");

    [Test]
    public async Task DisablingPublishAotRestoresTrimmingAndSingleFileToBaseline()
    {
        // The reported bug: on default Windows, enabling Native AOT turns trimming on and
        // single-file off; disabling AOT must restore both.
        var session = new PublishOptionSession(State(publishSingleFile: true, publishTrimmed: false));

        var baseline = session.Effective;
        session.Apply(PublishOptionId.PublishAot, true);
        var restored = session.Apply(PublishOptionId.PublishAot, false);

        using (Assert.Multiple())
        {
            await Assert.That(baseline.PublishTrimmed).IsFalse();
            await Assert.That(baseline.PublishSingleFile).IsTrue();
            await Assert.That(restored.State).IsEqualTo(baseline);
        }
    }

    [Test]
    public async Task DisablingBuildIpaRestoresArchiveOnBuildOnIos()
    {
        var session = new PublishOptionSession(State(
            platform: "iOS", targetFramework: "net11.0-ios", runtimeIdentifier: "iossimulator-arm64",
            publishTrimmed: true, archiveOnBuild: false, buildIpa: false, useAppHost: false,
            selfContained: true));

        var baseline = session.Effective;
        session.Apply(PublishOptionId.BuildIpa, true);
        var restored = session.Apply(PublishOptionId.BuildIpa, false);

        using (Assert.Multiple())
        {
            await Assert.That(baseline.ArchiveOnBuild).IsFalse();
            await Assert.That(restored.State.ArchiveOnBuild).IsFalse();
            await Assert.That(restored.State).IsEqualTo(baseline);
        }
    }

    [Test]
    public async Task TurningOffTheWinningOptionRestoresTheConflictingOption()
    {
        // PublishAot conflicts with PublishReadyToRun. Turning ReadyToRun on turns AOT off;
        // turning ReadyToRun back off restores AOT.
        var session = new PublishOptionSession(State(publishAot: true));

        session.Apply(PublishOptionId.PublishAot, true);
        session.Apply(PublishOptionId.PublishReadyToRun, true);
        var restored = session.Apply(PublishOptionId.PublishReadyToRun, false);

        using (Assert.Multiple())
        {
            await Assert.That(restored.State.PublishReadyToRun).IsFalse();
            await Assert.That(restored.State.PublishAot).IsTrue();
        }
    }

    [Test]
    public async Task AndroidLinkModeChoiceIsPreservedAndRestored()
    {
        var session = new PublishOptionSession(State(
            platform: "Android", targetFramework: "net11.0-android", runtimeIdentifier: "android-arm64",
            selfContained: true, publishTrimmed: true, androidLinkMode: "Full"));

        // The explicit Full choice survives; it is not reset to SdkOnly.
        var on = session.ApplyChoice(PublishOptionId.AndroidLinkMode, "Full", true);
        await Assert.That(on.State.AndroidLinkMode).IsEqualTo("Full");

        // Turning the linker off turns trimming off; turning it back on restores prior state.
        var off = session.ApplyChoice(PublishOptionId.AndroidLinkMode, "None", false);
        await Assert.That(off.State.AndroidLinkMode).IsEqualTo("None");
        await Assert.That(off.State.PublishTrimmed).IsFalse();

        // A no-op re-resolution must not resurrect the previous Full choice from the baseline.
        session.SetContext();
        await Assert.That(session.Effective.AndroidLinkMode).IsEqualTo("None");

        var backOn = session.ApplyChoice(PublishOptionId.AndroidLinkMode, "Full", true);

        using (Assert.Multiple())
        {
            await Assert.That(backOn.State.AndroidLinkMode).IsEqualTo("Full");
            await Assert.That(backOn.State.PublishTrimmed).IsTrue();
        }
    }

    [Test]
    public async Task ConflictWinnerSurvivesContextChangeAndUnrelatedEdit()
    {
        // Regression: after AOT yields to ReadyToRun, an unrelated change must not flip them.
        var session = new PublishOptionSession(State(publishAot: true, publishSingleFile: true));

        session.Apply(PublishOptionId.PublishAot, true);
        session.Apply(PublishOptionId.PublishReadyToRun, true);

        session.SetContext(configuration: "Debug");
        using (Assert.Multiple())
        {
            await Assert.That(session.Effective.PublishAot).IsFalse();
            await Assert.That(session.Effective.PublishReadyToRun).IsTrue();
        }

        session.Apply(PublishOptionId.PublishTrimmed, true);
        using (Assert.Multiple())
        {
            await Assert.That(session.Effective.PublishAot).IsFalse();
            await Assert.That(session.Effective.PublishReadyToRun).IsTrue();
        }
    }

    [Test]
    public async Task UnrelatedSingleFileChangeDoesNotDisableTheConflictWinner()
    {
        // Regression: the AOT/ReadyToRun conflict used to pick its loser from LastChanged, so
        // enabling single-file (no conflict edge with ReadyToRun) disabled ReadyToRun as well.
        var session = new PublishOptionSession(State(publishAot: true, publishSingleFile: false));

        session.Apply(PublishOptionId.PublishAot, true);
        session.Apply(PublishOptionId.PublishReadyToRun, true);
        var singleFile = session.Apply(PublishOptionId.PublishSingleFile, true);

        using (Assert.Multiple())
        {
            await Assert.That(singleFile.State.PublishSingleFile).IsTrue();
            await Assert.That(singleFile.State.PublishReadyToRun).IsTrue();
            await Assert.That(singleFile.State.PublishAot).IsFalse();
        }
    }

    [Test]
    public async Task ReEnablingAConflictLoserMakesItTheNewWinner()
    {
        // Regression: after ReadyToRun beats AOT, explicitly enabling AOT again must win.
        var session = new PublishOptionSession(State(publishAot: false));

        session.Apply(PublishOptionId.PublishAot, true);
        session.Apply(PublishOptionId.PublishReadyToRun, true);

        var backToAot = session.Apply(PublishOptionId.PublishAot, true);
        session.SetContext(configuration: "Debug");

        using (Assert.Multiple())
        {
            await Assert.That(backToAot.State.PublishAot).IsTrue();
            await Assert.That(backToAot.State.PublishReadyToRun).IsFalse();
            await Assert.That(session.Effective.PublishAot).IsTrue();
            await Assert.That(session.Effective.PublishReadyToRun).IsFalse();
        }
    }

    [Test]
    public async Task EnablingChoiceThroughApplySurvivesLaterResolution()
    {
        // Regression: Apply(choice, true) used to leave the baseline at "None".
        var session = new PublishOptionSession(State(
            platform: "Android", targetFramework: "net11.0-android", runtimeIdentifier: "android-arm64",
            selfContained: true, publishTrimmed: false, androidLinkMode: "None"));

        session.ApplyChoice(PublishOptionId.AndroidLinkMode, "None", false);
        var on = session.Apply(PublishOptionId.AndroidLinkMode, true);
        await Assert.That(on.State.AndroidLinkMode).IsEqualTo("SdkOnly");

        session.SetContext();
        await Assert.That(session.Effective.AndroidLinkMode).IsEqualTo("SdkOnly");
    }

    [Test]
    public async Task EnablingAndroidLinkModeClearsAnEarlierTrimmedOffChoice()
    {
        // The reported UI deadlock: unchecking trimming sets Link Mode to None. Choosing
        // SdkOnly again must re-enable trimming instead of being rejected as a stale user-off.
        var session = new PublishOptionSession(State(
            platform: "Android", targetFramework: "net11.0-android", runtimeIdentifier: "android-arm64",
            selfContained: true, publishTrimmed: true, androidLinkMode: "SdkOnly"));

        var off = session.Apply(PublishOptionId.PublishTrimmed, false);
        await Assert.That(off.State.AndroidLinkMode).IsEqualTo("None");

        var backOn = session.ApplyChoice(PublishOptionId.AndroidLinkMode, "SdkOnly", true);

        using (Assert.Multiple())
        {
            await Assert.That(backOn.State.AndroidLinkMode).IsEqualTo("SdkOnly");
            await Assert.That(backOn.State.PublishTrimmed).IsTrue();
            await Assert.That(backOn.State.PublishReadyToRun).IsTrue();
        }
    }

    [Test]
    public async Task EnablingTrimmedClearsAnEarlierSelfContainedOffChoice()
    {
        var session = new PublishOptionSession(State(selfContained: true, publishTrimmed: false));

        session.Apply(PublishOptionId.SelfContained, false);
        var trimmed = session.Apply(PublishOptionId.PublishTrimmed, true);

        using (Assert.Multiple())
        {
            await Assert.That(trimmed.State.SelfContained).IsTrue();
            await Assert.That(trimmed.State.PublishTrimmed).IsTrue();
        }
    }

    [Test]
    [Arguments("net11.0-android")]
    [Arguments("net10.0-android")]
    public async Task EnablingLinkModeClearsTransitiveSelfContainedOffChoice(string targetFramework)
    {
        var session = new PublishOptionSession(State(
            platform: "Android", targetFramework: targetFramework, runtimeIdentifier: "android-arm64",
            selfContained: true, publishTrimmed: true, androidLinkMode: "SdkOnly"));

        session.Apply(PublishOptionId.SelfContained, false);
        var restored = session.ApplyChoice(PublishOptionId.AndroidLinkMode, "SdkOnly", true);

        using (Assert.Multiple())
        {
            await Assert.That(restored.State.SelfContained).IsTrue();
            await Assert.That(restored.State.PublishTrimmed).IsTrue();
            await Assert.That(restored.State.AndroidLinkMode).IsEqualTo("SdkOnly");
        }
    }

    [Test]
    public async Task UnrelatedExplicitOffChoicesSurvivePrerequisiteRecovery()
    {
        var session = new PublishOptionSession(State(
            platform: "Android", targetFramework: "net11.0-android", runtimeIdentifier: "android-arm64",
            selfContained: true, publishTrimmed: true, androidLinkMode: "SdkOnly"));

        session.Apply(PublishOptionId.EnableMultiDex, false);
        session.Apply(PublishOptionId.IncludeAab, false);
        session.Apply(PublishOptionId.SelfContained, false);
        var restored = session.ApplyChoice(PublishOptionId.AndroidLinkMode, "Full", true);

        using (Assert.Multiple())
        {
            await Assert.That(restored.State.SelfContained).IsTrue();
            await Assert.That(restored.State.PublishTrimmed).IsTrue();
            await Assert.That(restored.State.EnableMultiDex).IsFalse();
            await Assert.That(restored.State.IncludeAab).IsFalse();
        }
    }

    [Test]
    public async Task ResolverAcceptsPersistedUserDisabledSet()
    {
        // Direct resolver callers can supply a persisted user-off set.
        var state = State(selfContained: false, publishTrimmed: true);

        var result = PublishOptionResolver.Resolve(
            state,
            changedOption: PublishOptionId.PublishTrimmed,
            changedValue: true,
            userDisabled: [PublishOptionId.SelfContained]);

        using (Assert.Multiple())
        {
            await Assert.That(result.State.SelfContained).IsFalse();
            await Assert.That(result.State.PublishTrimmed).IsFalse();
        }
    }
}
