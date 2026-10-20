using DotNetAppPublisher.Features.GooglePlay.Models;

namespace DotNetAppPublisher.Features.GooglePlay.State;

public sealed record GooglePlayStateSnapshot(
    GooglePlayAccountInfo? Account,
    GooglePlayTrackInfo? Track,
    string ReleaseName,
    string ReleaseNotes,
    string ReleaseNotesLanguage,
    int RolloutPercentage,
    string SigningKeyPath,
    string SigningKeyAlias,
    bool IsSignedIn,
    GooglePlayPublishStage Stage,
    GooglePlayStatusTone StatusTone,
    string StatusHeadline,
    string StatusDetail)
{
    public static GooglePlayStateSnapshot Empty { get; } = new(
        null,
        null,
        string.Empty,
        string.Empty,
        "en-US",
        100,
        string.Empty,
        string.Empty,
        false,
        GooglePlayPublishStage.Idle,
        GooglePlayStatusTone.Info,
        "Not connected",
        "Your credentials stay in memory for this session.");
}
