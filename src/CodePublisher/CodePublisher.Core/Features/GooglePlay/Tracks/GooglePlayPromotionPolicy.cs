using CodePublisher.Features.GooglePlay.Models;
using CodePublisher.Features.GooglePlay.Services;

namespace CodePublisher.Features.GooglePlay.Tracks;

public static class GooglePlayPromotionPolicy
{
    public static bool IsTargetAllowed(GooglePlayTrackInfo target) => target.ActiveReleaseCount <= 1;

    public static (string Status, int? RolloutPercentage) Resolve(GooglePlayTrackInfo source, GooglePlayTrackInfo target)
    {
        if (target.IsProduction && target.ActiveReleaseCount == 0)
        {
            return (GooglePlayTrackReleaseStatus.Completed, 100);
        }

        return (source.LiveStatus, source.LiveRolloutPercentage);
    }
}
