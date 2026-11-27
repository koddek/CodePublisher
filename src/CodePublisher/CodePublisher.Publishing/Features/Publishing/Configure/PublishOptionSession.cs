namespace CodePublisher.Features.Publishing.Configure;

/// <summary>
/// Keeps user intent separate from derived effective state for one publish-options session.
///
/// The pure <see cref="PublishOptionResolver"/> re-derives effective options from a snapshot but
/// cannot remember what a value was before another option changed it, because it receives the
/// already-derived snapshot back on the next call. This session keeps a clean baseline of what the
/// user actually chose (and which prerequisites the user turned off), and re-derives effective
/// state from that baseline on every change. Deselecting an option therefore removes its
/// contribution and restores options it had auto-changed, without a separate restoration ledger.
///
/// Platform invariants (Apple trimming, .NET 11 Android clearing, capability gates, NuGet clearing)
/// are still applied by <see cref="PublishSettingsPolicy.ApplyPlatformRules"/> on top of effective
/// output and are not restorable; only user-chosen baseline values are restored.
/// </summary>
public sealed class PublishOptionSession
{
    private readonly HashSet<PublishOptionId> _userDisabled = [];
    private readonly HashSet<PublishOptionId> _conflictWinners = [];
    private PublishSettingsState _baseline;

    /// <summary>Starts a session from a known-good baseline (a platform profile plus context).</summary>
    public PublishOptionSession(PublishSettingsState baseline)
    {
        _baseline = baseline;
        Effective = PublishOptionResolver.Resolve(baseline, userDisabled: _userDisabled).State;
    }

    /// <summary>What the user explicitly chose, before any cross-option adjustment.</summary>
    public PublishSettingsState Baseline => _baseline;

    /// <summary>The current effective options after graph and platform rules.</summary>
    public PublishSettingsState Effective { get; private set; }

    /// <summary>Options the user has explicitly turned off.</summary>
    public IReadOnlyCollection<PublishOptionId> UserDisabled => _userDisabled;

    /// <summary>Options that win their active mutual-exclusion conflicts in this session.</summary>
    public IReadOnlyCollection<PublishOptionId> ConflictWinners => _conflictWinners;

    /// <summary>Applies a user change to a toggle option and re-derives effective state.</summary>
    public PublishOptionResolution Apply(PublishOptionId changedOption, bool changedValue)
    {
        TrackUserIntent(changedOption, changedValue);

        // Choice options have a value, not a toggle. Turning one off clears it. Turning one on
        // keeps the current value when it is already on, otherwise it moves to the first value
        // accepted by the graph, matching the resolver's implicit SetChoice behavior.
        _baseline = PublishOptionValues.IsChoice(changedOption)
            ? SetValue(_baseline, changedOption, ChoiceBaselineValue(changedOption, changedValue))
            : SetToggle(_baseline, changedOption, changedValue);

        return Reapply(changedOption, changedValue);
    }

    /// <summary>Applies a user change to a choice option and re-derives effective state.</summary>
    public PublishOptionResolution ApplyChoice(PublishOptionId changedOption, string value, bool on)
    {
        TrackUserIntent(changedOption, on);
        _baseline = SetValue(_baseline, changedOption, value);
        return Reapply(changedOption, on);
    }

    /// <summary>Replaces context fields (platform, framework, RID, configuration) on the baseline.</summary>
    public PublishOptionResolution SetContext(
        string? publishPlatform = null,
        string? targetFramework = null,
        string? runtimeIdentifier = null,
        string? configuration = null)
    {
        _baseline = _baseline with
        {
            PublishPlatform = publishPlatform ?? _baseline.PublishPlatform,
            TargetFramework = targetFramework ?? _baseline.TargetFramework,
            RuntimeIdentifier = runtimeIdentifier ?? _baseline.RuntimeIdentifier,
            Configuration = configuration ?? _baseline.Configuration
        };

        return Reapply(null, true);
    }

    /// <summary>Resets the baseline to a known-good profile and clears user-off tracking.</summary>
    public PublishOptionResolution Reset(PublishSettingsState baseline)
    {
        _baseline = baseline;
        _userDisabled.Clear();
        _conflictWinners.Clear();
        return Reapply(null, true);
    }

    /// <summary>Records the persistent intent behind a user change.</summary>
    private void TrackUserIntent(PublishOptionId option, bool on)
    {
        if (on)
        {
            _userDisabled.Remove(option);

            // Turning an option on is a newer explicit decision than any earlier off choice on
            // its prerequisites. Clear those so the resolver enables what the option requires
            // instead of rejecting the user's action. This breaks the Android trim/link-mode
            // cycle: setting Link Mode back to SdkOnly re-enables trimming.
            foreach (var prerequisite in Prerequisites(option))
            {
                _userDisabled.Remove(prerequisite);
            }

            // A newer explicit choice wins its conflicts. Drop the previous winner of every
            // conflict involving this option before recording the new one.
            foreach (var partner in ConflictPartners(option))
            {
                _conflictWinners.Remove(partner);
            }

            _conflictWinners.Add(option);
        }
        else
        {
            _userDisabled.Add(option);
            _conflictWinners.Remove(option);
        }
    }

    private IEnumerable<PublishOptionId> Prerequisites(PublishOptionId option)
    {
        var requires = PublishOptionGraph.RulesFor(_baseline.PublishPlatform)
            .Where(rule => rule.Relation == PublishOptionRelation.Requires)
            .ToArray();
        var seen = new HashSet<PublishOptionId> { option };
        var pending = new Stack<PublishOptionId>();
        pending.Push(option);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var rule in requires.Where(rule => rule.Source == current))
            {
                // The Android trim/link-mode cycle would revisit both options forever without
                // the visited set.
                if (!seen.Add(rule.Target))
                {
                    continue;
                }

                yield return rule.Target;
                pending.Push(rule.Target);
            }
        }
    }

    private IEnumerable<PublishOptionId> ConflictPartners(PublishOptionId option)
    {
        foreach (var rule in PublishOptionGraph.RulesFor(_baseline.PublishPlatform))
        {
            if (rule.Relation != PublishOptionRelation.ConflictsWith)
            {
                continue;
            }

            if (rule.Source == option)
            {
                yield return rule.Target;
            }
            else if (rule.Target == option)
            {
                yield return rule.Source;
            }
        }
    }

    private string ChoiceBaselineValue(PublishOptionId option, bool on)
    {
        var current = ValueOf(_baseline, option);
        if (!on)
        {
            return option == PublishOptionId.AndroidLinkMode ? "None" : string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(current) &&
            !string.Equals(current, "None", StringComparison.Ordinal))
        {
            return current;
        }

        var accepted = PublishOptionGraph.RulesFor(_baseline.PublishPlatform)
            .FirstOrDefault(rule => rule.Relation == PublishOptionRelation.Requires &&
                                    rule.Target == option &&
                                    rule.TargetValues.Count > 0)
            ?.TargetValues;

        return accepted is { Count: > 0 } ? accepted[0] : current;
    }

    private static string ValueOf(PublishSettingsState state, PublishOptionId option) => option switch
    {
        PublishOptionId.AndroidLinkMode => state.AndroidLinkMode,
        PublishOptionId.AndroidLinkTool => state.AndroidLinkTool,
        PublishOptionId.AndroidDexTool => state.AndroidDexTool,
        _ => string.Empty,
    };

    private PublishOptionResolution Reapply(PublishOptionId? changedOption, bool changedValue)
    {
        var resolution = PublishOptionResolver.Resolve(
            _baseline,
            changedOption,
            changedValue,
            userDisabled: _userDisabled,
            conflictWinners: _conflictWinners);

        Effective = resolution.State;
        return resolution;
    }

    private static PublishSettingsState SetToggle(PublishSettingsState state, PublishOptionId option, bool value) => option switch
    {
        PublishOptionId.SelfContained => state with { SelfContained = value },
        PublishOptionId.PublishTrimmed => state with { PublishTrimmed = value },
        PublishOptionId.PublishAot => state with { PublishAot = value },
        PublishOptionId.PublishReadyToRun => state with { PublishReadyToRun = value },
        PublishOptionId.PublishSingleFile => state with { PublishSingleFile = value },
        PublishOptionId.UseAppHost => state with { UseAppHost = value },
        PublishOptionId.CreateMacAppBundle => state with { CreateMacAppBundle = value },
        PublishOptionId.CreateWindowsExecutable => state with { CreateWindowsExecutable = value },
        PublishOptionId.BuildIpa => state with { BuildIpa = value },
        PublishOptionId.ArchiveOnBuild => state with { ArchiveOnBuild = value },
        PublishOptionId.RunAotCompilation => state with { RunAotCompilation = value },
        PublishOptionId.EnableProfiledAot => state with { EnableProfiledAot = value },
        PublishOptionId.CreateMappingFile => state with { CreateMappingFile = value },
        PublishOptionId.EnableMultiDex => state with { EnableMultiDex = value },
        PublishOptionId.UseAapt2 => state with { UseAapt2 = value },
        PublishOptionId.EnableDesugar => state with { EnableDesugar = value },
        PublishOptionId.IncludeApk => state with { IncludeApk = value },
        PublishOptionId.IncludeAab => state with { IncludeAab = value },
        _ => state
    };

    private static PublishSettingsState SetValue(PublishSettingsState state, PublishOptionId option, string value) => option switch
    {
        PublishOptionId.AndroidLinkMode => state with { AndroidLinkMode = value },
        PublishOptionId.AndroidLinkTool => state with { AndroidLinkTool = value },
        PublishOptionId.AndroidDexTool => state with { AndroidDexTool = value },
        _ => state
    };
}
