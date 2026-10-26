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
///   - Android RunAOTCompilation requires trimming (XA1030) and a linking mode.
///   - AndroidEnableProfiledAot only means something with RunAOTCompilation.
///   - AndroidCreateProguardMappingFile only applies with the R8 shrinker.
///   - iOS ArchiveOnBuild and BuildIpa are independent, and both need a device RID.
/// </summary>
public static class PublishOptionGraph
{
    private static readonly string[] Android = [PublishPlatforms.Android];
    private static readonly string[] Apple = [PublishPlatforms.Ios];
    private static readonly string[] Desktop = [PublishPlatforms.MacOs, PublishPlatforms.Windows, PublishPlatforms.Linux];

    /// <summary>Every rule in the graph.</summary>
    public static IReadOnlyList<PublishOptionRule> Rules { get; } =
    [
        // --- Desktop trimming and native compilation ---
        new(
            PublishOptionId.PublishAot,
            PublishOptionRelation.Requires,
            PublishOptionId.SelfContained,
            "Native AOT publishes a self-contained app; the SDK infers this and rejects a framework-dependent AOT publish.",
            Desktop),
        new(
            PublishOptionId.PublishAot,
            PublishOptionRelation.Requires,
            PublishOptionId.PublishTrimmed,
            "Native AOT always trims. The SDK fails the build with \"PublishTrimmed is implied by native compilation and cannot be disabled.\" when trimming is turned off.",
            Desktop),
        new(
            PublishOptionId.PublishAot,
            PublishOptionRelation.ConflictsWith,
            PublishOptionId.PublishReadyToRun,
            "Native AOT and ReadyToRun are mutually exclusive: AOT compiles ahead of time, ReadyToRun precompiles for the JIT.",
            Desktop),
        new(
            PublishOptionId.PublishAot,
            PublishOptionRelation.ConflictsWith,
            PublishOptionId.PublishSingleFile,
            "Native AOT already produces a single native executable; the single-file bundler is skipped when AOT is on.",
            Desktop),
        new(
            PublishOptionId.PublishTrimmed,
            PublishOptionRelation.Requires,
            PublishOptionId.SelfContained,
            "Trimming only works for a self-contained publish. The SDK fails the build with NETSDK1102 otherwise.",
            Desktop),
        new(
            PublishOptionId.PublishSingleFile,
            PublishOptionRelation.Requires,
            PublishOptionId.UseAppHost,
            "A single-file publish requires the application host. The SDK fails the build with NETSDK1098 otherwise.",
            Desktop),

        // --- Android ---
        new(
            PublishOptionId.RunAotCompilation,
            PublishOptionRelation.Requires,
            PublishOptionId.AndroidLinkMode,
            "Android ahead-of-time compilation needs trimming, and trimming follows the linker. Set Link Mode to SdkOnly or Full first.",
            Android,
            ["SdkOnly", "Full"]),
        new(
            PublishOptionId.EnableProfiledAot,
            PublishOptionRelation.Requires,
            PublishOptionId.RunAotCompilation,
            "Profiled AOT only affects ahead-of-time compiled methods, so it needs RunAOT turned on.",
            Android),
        new(
            PublishOptionId.CreateMappingFile,
            PublishOptionRelation.Requires,
            PublishOptionId.AndroidLinkMode,
            "The R8 mapping file is only produced when the shrinker runs, which needs a linking mode.",
            Android,
            ["SdkOnly", "Full"]),
        new(
            PublishOptionId.EnableMultiDex,
            PublishOptionRelation.Suggests,
            PublishOptionId.AndroidLinkMode,
            "Multi-dex support is only needed below API 21, and the SDK's default minSdk is higher.",
            Android,
            ["SdkOnly", "Full"]),
        new(
            PublishOptionId.EnableDesugar,
            PublishOptionRelation.Requires,
            PublishOptionId.AndroidDexTool,
            "Desugaring needs the D8 dex compiler. The SDK switches the dex compiler automatically.",
            Android,
            ["d8"]),
        new(
            PublishOptionId.IncludeAab,
            PublishOptionRelation.Suggests,
            PublishOptionId.SelfContained,
            "The Google Play bundle is a self-contained artifact; a framework-dependent bundle is not accepted by Play.",
            Android),

        // --- iOS ---
        new(
            PublishOptionId.BuildIpa,
            PublishOptionRelation.Requires,
            PublishOptionId.ArchiveOnBuild,
            "The .ipa is packaged from the Xcode archive, so archiving has to run as well.",
            Apple),
        new(
            PublishOptionId.ArchiveOnBuild,
            PublishOptionRelation.Suggests,
            PublishOptionId.BuildIpa,
            "An archive is normally shipped as an .ipa. Turn on BuildIpa to package it.",
            Apple),
    ];

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
