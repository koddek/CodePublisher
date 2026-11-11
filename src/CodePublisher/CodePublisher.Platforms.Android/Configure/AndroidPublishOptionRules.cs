namespace CodePublisher.Features.Publishing.Configure;

/// <summary>
/// The edges of the publish option graph that only exist on Android. The graph aggregates these
/// with the desktop rules; the platform rules and the dependency graph are applied together until
/// the result settles.
/// </summary>
public static class AndroidPublishOptionRules
{
    private static readonly string[] Android = [PublishPlatforms.Android];

    public static IReadOnlyList<PublishOptionRule> Rules { get; } =
    [
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
    ];
}
