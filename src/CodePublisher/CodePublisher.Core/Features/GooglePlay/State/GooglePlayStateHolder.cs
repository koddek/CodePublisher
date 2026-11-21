using CodePublisher.Features.GooglePlay.Images;
using CodePublisher.Features.GooglePlay.Models;

namespace CodePublisher.Features.GooglePlay.State;

public sealed class GooglePlayStateHolder : IDisposable
{
    private readonly object _gate = new();
    private GooglePlayStateSnapshot _snapshot = GooglePlayStateSnapshot.Empty;

    /// <summary>
    /// Raised once after any command changes the snapshot. The view model subscribes once and
    /// refreshes every derived property from a single notification instead of each caller
    /// fanning out property-changed notifications by hand.
    /// </summary>
    public event Action? OnChange;

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
        var normalizedRollout = Math.Clamp(rolloutPercentage, 1, 100);
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
            RolloutPercentage = Math.Clamp(rolloutPercentage, 1, 100)
        });
    }

    public void SetImages(IReadOnlyList<GooglePlayStoreImage> images) =>
        Update(snapshot => snapshot with { Images = [.. images] });

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

    /// <summary>
    /// Applies a status check in one transition. Stage and status change together after a status
    /// check, so the subscriber is notified once rather than twice.
    /// </summary>
    public void SetStageAndStatus(
        GooglePlayPublishStage stage,
        string headline,
        string detail,
        GooglePlayStatusTone tone) => Update(snapshot => snapshot with
        {
            Stage = stage,
            StatusHeadline = headline,
            StatusDetail = detail,
            StatusTone = tone
        });

    public void ResetRelease()
    {
        Update(snapshot => snapshot with
        {
            Track = null,
            ReleaseName = string.Empty,
            ReleaseNotes = string.Empty,
            ReleaseNotesLanguage = "en-US",
            RolloutPercentage = 100,
            Images = []
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
            RolloutPercentage = 100,
            Images = []
        });
    }

    private void Update(Func<GooglePlayStateSnapshot, GooglePlayStateSnapshot> transition)
    {
        lock (_gate)
        {
            _snapshot = transition(_snapshot);
        }

        OnChange?.Invoke();
    }

    public void Dispose()
    {
        // Release every subscriber so a disposed holder cannot keep a view model alive.
        OnChange = null;
    }
}
