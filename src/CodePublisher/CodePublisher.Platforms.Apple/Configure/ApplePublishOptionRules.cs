namespace CodePublisher.Features.Publishing.Configure;

/// <summary>
/// The edges of the publish option graph that only exist on iOS. The graph aggregates these with
/// the desktop rules; Mac Catalyst carries no rules of its own because nothing about Catalyst is
/// expressible as an option relationship in this app.
/// </summary>
public static class ApplePublishOptionRules
{
    private static readonly string[] Apple = [PublishPlatforms.Ios];

    public static IReadOnlyList<PublishOptionRule> Rules { get; } =
    [
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
}
