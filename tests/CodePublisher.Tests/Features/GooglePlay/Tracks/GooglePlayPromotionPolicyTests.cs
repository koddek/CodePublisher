using CodePublisher.Features.GooglePlay.Models;
using CodePublisher.Features.GooglePlay.Tracks;

namespace CodePublisher.Tests.Features.GooglePlay.Tracks;

public sealed class GooglePlayPromotionPolicyTests
{
    [Test]
    public async Task Promotion_PreservesLiveStatusAndRollout()
    {
        var source = Track("qa", "inProgress", 50, activeReleases: 1);
        var target = Track("beta", "completed", 100, activeReleases: 1);

        var result = GooglePlayPromotionPolicy.Resolve(source, target);

        using (Assert.Multiple())
        {
            await Assert.That(result.Status).IsEqualTo("inProgress");
            await Assert.That(result.RolloutPercentage).IsEqualTo(50);
        }
    }

    [Test]
    public async Task FirstProductionPromotion_UsesCompletedFullRollout()
    {
        var source = Track("qa", "inProgress", 50, activeReleases: 1);
        var target = Track("production", string.Empty, null, activeReleases: 0);

        var result = GooglePlayPromotionPolicy.Resolve(source, target);

        using (Assert.Multiple())
        {
            await Assert.That(result.Status).IsEqualTo("completed");
            await Assert.That(result.RolloutPercentage).IsEqualTo(100);
        }
    }

    [Test]
    public async Task MultiReleaseTarget_IsNotAllowed()
    {
        var target = Track("beta", "inProgress", 50, activeReleases: 2);

        await Assert.That(GooglePlayPromotionPolicy.IsTargetAllowed(target)).IsFalse();
    }

    private static GooglePlayTrackInfo Track(string id, string status, int? rollout, int activeReleases) =>
        new(id, id, id, status, id == "production")
        {
            ActiveReleaseCount = activeReleases,
            LiveStatus = status,
            LiveRolloutPercentage = rollout,
            VersionCodes = activeReleases == 0 ? [] : ["42"]
        };
}
