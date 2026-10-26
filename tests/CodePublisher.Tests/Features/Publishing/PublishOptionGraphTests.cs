using CodePublisher.Features.Publishing.Configure;

namespace CodePublisher.Tests.Features.Publishing;

/// <summary>
/// The publish option graph is the one place that decides how the options relate. These tests
/// cover every rule it declares, on the platform the rule belongs to.
/// </summary>
public sealed class PublishOptionGraphTests
{
    private static PublishSettingsState State(
        string platform = "Windows",
        string targetFramework = "net10.0-windows",
        string runtimeIdentifier = "win-x64",
        bool selfContained = true,
        bool publishAot = false,
        bool publishReadyToRun = false,
        bool publishSingleFile = false,
        bool publishTrimmed = false,
        string androidLinkMode = "None",
        bool runAotCompilation = false,
        bool enableProfiledAot = false,
        bool includeApk = true,
        bool includeAab = false,
        bool useAppHost = true,
        bool buildIpa = false,
        bool archiveOnBuild = false,
        bool createMappingFile = false,
        bool enableDesugar = true) => new(
        platform,
        targetFramework,
        runtimeIdentifier,
        selfContained,
        publishAot,
        publishReadyToRun,
        publishSingleFile,
        publishTrimmed,
        androidLinkMode,
        "r8",
        "d8",
        runAotCompilation,
        enableProfiledAot,
        includeApk,
        includeAab,
        useAppHost,
        true,
        true,
        buildIpa,
        archiveOnBuild,
        createMappingFile,
        false,
        true,
        enableDesugar);

    [Test]
    public async Task EveryRuleNamesBothOptionsAndExplainsItself()
    {
        using (Assert.Multiple())
        {
            foreach (var rule in PublishOptionGraph.Rules)
            {
                await Assert.That(rule.Source).IsNotEqualTo(rule.Target);
                await Assert.That(rule.Reason).IsNotEmpty();
                await Assert.That(rule.Platforms.Count).IsGreaterThan(0);
            }
        }
    }

    [Test]
    public async Task EveryRuleAppliesToAKnownPlatform()
    {
        var platforms = new[] { "Android", "iOS", "macOS", "Windows", "Linux" };

        foreach (var rule in PublishOptionGraph.Rules)
        {
            foreach (var platform in rule.Platforms)
            {
                await Assert.That(platforms).Contains(platform);
            }
        }
    }

    [Test]
    public async Task RulesFor_FiltersByPlatform()
    {
        var androidRules = PublishOptionGraph.RulesFor("Android");
        var windowsRules = PublishOptionGraph.RulesFor("Windows");

        using (Assert.Multiple())
        {
            await Assert.That(androidRules.Any(rule => rule.Source == PublishOptionId.RunAotCompilation)).IsTrue();
            await Assert.That(windowsRules.Any(rule => rule.Source == PublishOptionId.RunAotCompilation)).IsFalse();
            await Assert.That(windowsRules.Any(rule => rule.Source == PublishOptionId.PublishAot)).IsTrue();
        }
    }

    [Test]
    public async Task Aot_TurnsOnSelfContainedAndTrimming()
    {
        // The SDK infers a self-contained publish for AOT and refuses an explicit
        // PublishTrimmed=false, so both have to be on.
        var result = PublishOptionResolver.Resolve(
            State(selfContained: false, publishAot: true, publishTrimmed: false),
            PublishOptionId.PublishAot,
            changedValue: true);

        using (Assert.Multiple())
        {
            await Assert.That(result.State.SelfContained).IsTrue();
            await Assert.That(result.State.PublishTrimmed).IsTrue();
        }
    }

    [Test]
    public async Task Aot_TurnsOffReadyToRunAndSingleFile()
    {
        // AOT and ReadyToRun are mutually exclusive, and the single-file bundler is skipped
        // under AOT, so neither belongs in an AOT publish.
        var result = PublishOptionResolver.Resolve(
            State(publishAot: true, publishReadyToRun: true, publishSingleFile: true),
            PublishOptionId.PublishAot,
            changedValue: true);

        using (Assert.Multiple())
        {
            await Assert.That(result.State.PublishReadyToRun).IsFalse();
            await Assert.That(result.State.PublishSingleFile).IsFalse();
        }
    }

    [Test]
    public async Task Aot_TurnsOffWhenSelfContainedIsTurnedOff()
    {
        // Unchecking a prerequisite turns off everything that relies on it.
        var result = PublishOptionResolver.Resolve(
            State(selfContained: true, publishAot: true, publishTrimmed: true),
            PublishOptionId.SelfContained,
            changedValue: false);

        using (Assert.Multiple())
        {
            await Assert.That(result.State.SelfContained).IsFalse();
            await Assert.That(result.State.PublishAot).IsFalse();
            await Assert.That(result.State.PublishTrimmed).IsFalse();
        }
    }

    [Test]
    public async Task ReadyToRun_WinsWhenTheUserTurnsItOnAgainstAot()
    {
        // The user's most recent choice settles the mutual exclusion.
        var result = PublishOptionResolver.Resolve(
            State(publishAot: true, publishReadyToRun: false),
            PublishOptionId.PublishReadyToRun,
            changedValue: true);

        using (Assert.Multiple())
        {
            await Assert.That(result.State.PublishReadyToRun).IsTrue();
            await Assert.That(result.State.PublishAot).IsFalse();
        }
    }

    [Test]
    public async Task Trimming_TurnsOnSelfContained()
    {
        // Trimming only works for a self-contained publish, and the SDK infers self-contained
        // for a trimmed publish since .NET 8.
        var result = PublishOptionResolver.Resolve(
            State(selfContained: false, publishTrimmed: true),
            PublishOptionId.PublishTrimmed,
            changedValue: true);

        using (Assert.Multiple())
        {
            await Assert.That(result.State.SelfContained).IsTrue();
            await Assert.That(result.State.PublishTrimmed).IsTrue();
        }
    }

    [Test]
    public async Task SingleFile_TurnsOnTheAppHost()
    {
        // A single-file publish needs the application host (NETSDK1098).
        var result = PublishOptionResolver.Resolve(
            State(publishSingleFile: true, useAppHost: false),
            PublishOptionId.PublishSingleFile,
            changedValue: true);

        await Assert.That(result.State.UseAppHost).IsTrue();
    }

    [Test]
    public async Task AndroidRunAot_TurnsOnTheLinker()
    {
        // Android ahead-of-time compilation needs trimming, and trimming follows the linker.
        var result = PublishOptionResolver.Resolve(
            State(
                platform: "Android",
                targetFramework: "net10.0-android",
                runtimeIdentifier: "android-arm64",
                androidLinkMode: "None",
                runAotCompilation: true),
            PublishOptionId.RunAotCompilation,
            changedValue: true);

        using (Assert.Multiple())
        {
            await Assert.That(result.State.AndroidLinkMode).IsEqualTo("SdkOnly");
            await Assert.That(result.State.PublishTrimmed).IsTrue();
            await Assert.That(result.State.RunAotCompilation).IsTrue();
        }
    }

    [Test]
    public async Task AndroidRunAot_TurnsOffWhenTheLinkerIsTurnedOff()
    {
        var result = PublishOptionResolver.Resolve(
            State(
                platform: "Android",
                targetFramework: "net10.0-android",
                runtimeIdentifier: "android-arm64",
                androidLinkMode: "SdkOnly",
                runAotCompilation: true,
                enableProfiledAot: true),
            PublishOptionId.AndroidLinkMode,
            changedValue: false);

        using (Assert.Multiple())
        {
            await Assert.That(result.State.AndroidLinkMode).IsEqualTo("None");
            await Assert.That(result.State.PublishTrimmed).IsFalse();
            await Assert.That(result.State.RunAotCompilation).IsFalse();
            await Assert.That(result.State.EnableProfiledAot).IsFalse();
        }
    }

    [Test]
    public async Task ProfiledAot_TurnsOnRunAot()
    {
        // Profiled AOT only affects ahead-of-time compiled methods, so it needs RunAOT.
        var result = PublishOptionResolver.Resolve(
            State(
                platform: "Android",
                targetFramework: "net10.0-android",
                runtimeIdentifier: "android-arm64",
                runAotCompilation: false,
                enableProfiledAot: true),
            PublishOptionId.EnableProfiledAot,
            changedValue: true);

        using (Assert.Multiple())
        {
            await Assert.That(result.State.RunAotCompilation).IsTrue();
            await Assert.That(result.State.EnableProfiledAot).IsTrue();
        }
    }

    [Test]
    public async Task IosBuildIpa_TurnsOnArchiving()
    {
        // The .ipa is packaged from the Xcode archive, so archiving has to run as well.
        var result = PublishOptionResolver.Resolve(
            State(
                platform: "iOS",
                targetFramework: "net10.0-ios",
                runtimeIdentifier: "ios-arm64",
                buildIpa: true,
                archiveOnBuild: false),
            PublishOptionId.BuildIpa,
            changedValue: true);

        using (Assert.Multiple())
        {
            await Assert.That(result.State.ArchiveOnBuild).IsTrue();
            await Assert.That(result.State.BuildIpa).IsTrue();
        }
    }

    [Test]
    public async Task IosBuildIpa_TurnsOffWhenArchivingIsTurnedOff()
    {
        var result = PublishOptionResolver.Resolve(
            State(
                platform: "iOS",
                targetFramework: "net10.0-ios",
                runtimeIdentifier: "ios-arm64",
                buildIpa: true,
                archiveOnBuild: true),
            PublishOptionId.ArchiveOnBuild,
            changedValue: false);

        using (Assert.Multiple())
        {
            await Assert.That(result.State.ArchiveOnBuild).IsFalse();
            await Assert.That(result.State.BuildIpa).IsFalse();
        }
    }

    [Test]
    public async Task AnOptionalCompanionIsOfferedRatherThanForced()
    {
        // Archiving on iOS without an .ipa is a valid choice, so the .ipa is only offered.
        var result = PublishOptionResolver.Resolve(
            State(
                platform: "iOS",
                targetFramework: "net10.0-ios",
                runtimeIdentifier: "ios-arm64",
                buildIpa: false,
                archiveOnBuild: true),
            PublishOptionId.ArchiveOnBuild,
            changedValue: true);

        using (Assert.Multiple())
        {
            await Assert.That(result.State.BuildIpa).IsFalse();
            await Assert.That(result.Suggestions.Any(suggestion => suggestion.Option == PublishOptionId.BuildIpa)).IsTrue();
        }
    }

    [Test]
    public async Task NoRuleIsDeclaredTwice()
    {
        // A duplicated rule would apply the same relationship twice and report it twice.
        var keys = PublishOptionGraph.Rules
            .Select(rule => $"{rule.Source}|{rule.Relation}|{rule.Target}|{string.Join(",", rule.Platforms)}")
            .ToArray();

        await Assert.That(keys).IsEquivalentTo(keys.Distinct());
    }

    [Test]
    public async Task EveryOptionIsReportedOnce()
    {
        // The walk may touch an option more than once, but the report the user reads must name
        // each option a single time.
        var result = PublishOptionResolver.Resolve(
            State(selfContained: false, publishAot: true, publishReadyToRun: true, publishSingleFile: true),
            PublishOptionId.PublishAot,
            changedValue: true);

        var options = result.Changes.Select(change => change.Option).ToArray();

        await Assert.That(options).IsEquivalentTo(options.Distinct());
    }

    [Test]
    public async Task EveryRuleThatTargetsAChoiceNamesTheValuesItAccepts()
    {
        // A choice target with no accepted values could never be satisfied, so the resolver
        // would switch the source off and report it every pass. The graph must not contain one.
        foreach (var rule in PublishOptionGraph.Rules)
        {
            var targetIsChoice =
                rule.Target is PublishOptionId.AndroidLinkMode or
                    PublishOptionId.AndroidLinkTool or
                    PublishOptionId.AndroidDexTool;

            if (targetIsChoice)
            {
                await Assert.That(rule.TargetValues.Count).IsGreaterThan(0);
            }
        }
    }

    [Test]
    public async Task ResolvingTheSameStateTwiceGivesTheSameResult()
    {
        // The resolution has to settle, so the order the user clicked in cannot matter.
        var state = State(selfContained: false, publishAot: true, publishReadyToRun: true, publishTrimmed: false);

        var first = PublishOptionResolver.Resolve(state, PublishOptionId.PublishAot, changedValue: true);
        var second = PublishOptionResolver.Resolve(first.State);

        await Assert.That(second.State).IsEqualTo(first.State);
    }

    [Test]
    public async Task ResolvingAnAlreadySettledStateChangesNothing()
    {
        var state = State(selfContained: false, publishAot: true, publishReadyToRun: true, publishTrimmed: false);
        var settled = PublishOptionResolver.Resolve(state, PublishOptionId.PublishAot, changedValue: true).State;

        var result = PublishOptionResolver.Resolve(settled);

        using (Assert.Multiple())
        {
            await Assert.That(result.State).IsEqualTo(settled);
            await Assert.That(result.Changes).IsEmpty();
        }
    }
}
