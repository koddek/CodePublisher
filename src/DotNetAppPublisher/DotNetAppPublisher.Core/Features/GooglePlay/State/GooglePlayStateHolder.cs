using DotNetAppPublisher.Features.GooglePlay.Models;

namespace DotNetAppPublisher.Features.GooglePlay.State;

public sealed class GooglePlayStateHolder
{
    private readonly object _gate = new();
    private GooglePlayStateSnapshot _snapshot = GooglePlayStateSnapshot.Empty;

    public GooglePlayStateSnapshot Current
    {
        get
        {
            lock (_gate)
            {
                return _snapshot;
            }
        }
    }

    public void SetAccount(GooglePlayAccountInfo? account)
    {
        Update(snapshot => snapshot with
        {
            Account = account,
            IsSignedIn = account is not null
        });
    }

    public void SetTrack(GooglePlayTrackInfo? track, int rolloutPercentage)
    {
        var normalizedRollout = track?.IsProduction == true
            ? Math.Clamp(rolloutPercentage, 1, 100)
            : 100;
        Update(snapshot => snapshot with
        {
            Track = track,
            RolloutPercentage = normalizedRollout
        });
    }

    public void SetRelease(string releaseName, string releaseNotes, string language, int rolloutPercentage)
    {
        if (rolloutPercentage is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(rolloutPercentage), "Rollout must be between 0 and 100.");
        }

        Update(snapshot => snapshot with
        {
            ReleaseName = releaseName?.Trim() ?? string.Empty,
            ReleaseNotes = releaseNotes ?? string.Empty,
            ReleaseNotesLanguage = string.IsNullOrWhiteSpace(language) ? "en-US" : language.Trim(),
            RolloutPercentage = snapshot.Track?.IsProduction == true
                ? Math.Clamp(rolloutPercentage, 1, 100)
                : 100
        });
    }

    public void SetSigningKey(string path, string alias)
    {
        Update(snapshot => snapshot with
        {
            SigningKeyPath = path?.Trim() ?? string.Empty,
            SigningKeyAlias = alias?.Trim() ?? string.Empty
        });
    }

    public void SetStage(GooglePlayPublishStage stage) => Update(snapshot => snapshot with { Stage = stage });

    public void SetStatus(string headline, string detail, GooglePlayStatusTone tone)
    {
        Update(snapshot => snapshot with
        {
            StatusHeadline = headline,
            StatusDetail = detail,
            StatusTone = tone
        });
    }

    public void ResetRelease()
    {
        Update(snapshot => snapshot with
        {
            Track = null,
            ReleaseName = string.Empty,
            ReleaseNotes = string.Empty,
            ReleaseNotesLanguage = "en-US",
            RolloutPercentage = 100
        });
    }

    public void ClearSession()
    {
        Update(snapshot => snapshot with
        {
            Account = null,
            Track = null,
            IsSignedIn = false,
            ReleaseName = string.Empty,
            ReleaseNotes = string.Empty,
            ReleaseNotesLanguage = "en-US",
            RolloutPercentage = 100
        });
    }

    private void Update(Func<GooglePlayStateSnapshot, GooglePlayStateSnapshot> transition)
    {
        lock (_gate)
        {
            _snapshot = transition(_snapshot);
        }
    }
}
