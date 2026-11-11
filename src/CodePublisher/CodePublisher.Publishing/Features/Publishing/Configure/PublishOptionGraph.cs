namespace CodePublisher.Features.Publishing.Configure;

/// <summary>
/// The declarative graph of how publish options relate to each other.
///
/// Every cross-option rule lives here exactly once. The UI reads it to decide what a toggle will
/// do, the resolver applies it, and the command builder trusts the result. A rule that is not in
/// this table does not exist anywhere else in the product.
///
/// The relationships come from the .NET SDK's own behavior:
///   - PublishAot implies self-contained publish and trimming, and the SDK refuses an explicit
///     PublishTrimmed=false under AOT ("PublishTrimmed is implied by native compilation and cannot
///     be disabled.").
///   - PublishAot and PublishReadyToRun are mutually exclusive (dotnet/runtime#126598).
///   - PublishAot already produces a single native file, so the single-file bundler is skipped.
///   - PublishTrimmed requires a self-contained publish (NETSDK1102).
///   - PublishSingleFile requires the application host (NETSDK1098).
///   - Android RunAOTCompilation requires trimming (XA1030) and a linking mode. It is a Mono
///     option and is removed on .NET 11 (CoreCLR); the .NET 11 target frameworks strip it before
///     the graph ever sees it.
///   - AndroidEnableProfiledAot only means something with RunAOTCompilation (Mono).
///   - AndroidCreateProguardMappingFile only applies with the R8 shrinker.
///   - iOS ArchiveOnBuild and BuildIpa are independent, and both need a device RID.
/// </summary>
public static class PublishOptionGraph
{
    /// <summary>Every rule in the graph, aggregated from the platform libraries.</summary>
    public static IReadOnlyList<PublishOptionRule> Rules { get; } =
        [.. DesktopPublishOptionRules.Rules, .. AndroidPublishOptionRules.Rules, .. ApplePublishOptionRules.Rules];

    /// <summary>The rules that apply to a publish platform.</summary>
    public static IReadOnlyList<PublishOptionRule> RulesFor(string publishPlatform) =>
        Rules.Where(rule => rule.Platforms.Count == 0 ||
                            rule.Platforms.Contains(publishPlatform, StringComparer.Ordinal))
            .ToArray();

    /// <summary>The rules declared from one option.</summary>
    public static IReadOnlyList<PublishOptionRule> RulesFrom(PublishOptionId option, string publishPlatform) =>
        RulesFor(publishPlatform).Where(rule => rule.Source == option).ToArray();

    /// <summary>The rules that point at one option.</summary>
    public static IReadOnlyList<PublishOptionRule> RulesTo(PublishOptionId option, string publishPlatform) =>
        RulesFor(publishPlatform).Where(rule => rule.Target == option).ToArray();
}
