namespace CodePublisher.Features.Publishing.Configure;

/// <summary>
/// One publish option in the dependency graph. The graph is the single source of truth for how
/// options relate, so the UI, the normalizer, and the command builder cannot disagree.
/// </summary>
public enum PublishOptionId
{
    SelfContained,
    PublishTrimmed,
    PublishAot,
    PublishReadyToRun,
    PublishSingleFile,
    UseAppHost,
    CreateMacAppBundle,
    CreateWindowsExecutable,
    BuildIpa,
    ArchiveOnBuild,
    RunAotCompilation,
    EnableProfiledAot,
    CreateMappingFile,
    EnableMultiDex,
    UseAapt2,
    EnableDesugar,
    IncludeApk,
    IncludeAab,

    /// <summary>A choice option: its value is one of a fixed set of strings rather than on/off.</summary>
    AndroidLinkMode,
    AndroidLinkTool,
    AndroidDexTool,
}

/// <summary>How two options relate to each other.</summary>
public enum PublishOptionRelation
{
    /// <summary>The target must be on for the source to be on. Turning the source on turns the
    /// target on; turning the target off turns the source off.</summary>
    Requires,

    /// <summary>The two options cannot be on at the same time. Turning either on turns the
    /// other off.</summary>
    ConflictsWith,

    /// <summary>The target is a normal companion of the source but is not required. The source
    /// stays off until the user turns it on, and the UI offers it as a suggestion.</summary>
    Suggests,
}

/// <summary>One edge in the option graph.</summary>
/// <param name="Source">The option the relation is declared from.</param>
/// <param name="Relation">How the two options relate.</param>
/// <param name="Target">The option the relation points at.</param>
/// <param name="Reason">Plain-English explanation shown in the UI.</param>
/// <param name="Platforms">The publish platforms the relation applies to. Empty means all of them.</param>
/// <param name="TargetValues">The accepted values for a choice target. Empty means "on" for a
/// toggle target and "any value" for a choice target.</param>
public sealed record PublishOptionRule(
    PublishOptionId Source,
    PublishOptionRelation Relation,
    PublishOptionId Target,
    string Reason,
    IReadOnlyList<string> Platforms,
    IReadOnlyList<string> TargetValues)
{
    public PublishOptionRule(
        PublishOptionId source,
        PublishOptionRelation relation,
        PublishOptionId target,
        string reason)
        : this(source, relation, target, reason, [], [])
    {
    }

    public PublishOptionRule(
        PublishOptionId source,
        PublishOptionRelation relation,
        PublishOptionId target,
        string reason,
        IReadOnlyList<string> platforms)
        : this(source, relation, target, reason, platforms, [])
    {
    }
}

/// <summary>A change the resolver applied, with the reason it applied it.</summary>
public sealed record PublishOptionChange(PublishOptionId Option, bool Value, string Reason);

/// <summary>An optional companion the resolver did not turn on, offered to the user instead.</summary>
public sealed record PublishOptionSuggestion(PublishOptionId Option, string Reason);

/// <summary>The result of resolving the option graph: the new state, what changed, and what the
/// user may still want to turn on.</summary>
public sealed record PublishOptionResolution(
    PublishSettingsState State,
    IReadOnlyList<PublishOptionChange> Changes,
    IReadOnlyList<PublishOptionSuggestion> Suggestions);
