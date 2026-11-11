namespace CodePublisher.Features.Publishing.Configure;

/// <summary>
/// The edges of the publish option graph that apply to every native desktop publish (macOS,
/// Windows, Linux). The Apple toolchain platforms carry their own rules in the Apple library;
/// Android's live in the Android library.
/// </summary>
public static class DesktopPublishOptionRules
{
    private static readonly string[] Desktop = [PublishPlatforms.MacOs, PublishPlatforms.Windows, PublishPlatforms.Linux];

    public static IReadOnlyList<PublishOptionRule> Rules { get; } =
    [
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
    ];
}
