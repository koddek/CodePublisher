using DotNetAppPublisher.Features.GooglePlay.Models;
using DotNetAppPublisher.Features.GooglePlay.State;

namespace DotNetAppPublisher.Tests.Features.GooglePlay.State;

public sealed class GooglePlayStateHolderTests
{
    [Test]
    public async Task SetTrack_NormalizesTestingRolloutToFull()
    {
        var holder = new GooglePlayStateHolder();
        var track = new GooglePlayTrackInfo("beta", "Open testing", "Open testing", "Ready", false);

        holder.SetTrack(track, rolloutPercentage: 25);

        await Assert.That(holder.Current.RolloutPercentage).IsEqualTo(100);
    }

    [Test]
    public async Task SetTrack_ClampsProductionRollout()
    {
        var holder = new GooglePlayStateHolder();
        var track = new GooglePlayTrackInfo("production", "Production", "Production", "Ready", true);

        holder.SetTrack(track, rolloutPercentage: 250);

        await Assert.That(holder.Current.RolloutPercentage).IsEqualTo(100);
    }

    [Test]
    public async Task ClearSession_RemovesAccountAndReleaseState()
    {
        var holder = new GooglePlayStateHolder();
        var account = new GooglePlayAccountInfo(
            "account-1",
            "user@example.com",
            "User",
            "U",
            false,
            null,
            []);
        holder.SetAccount(account);
        holder.SetRelease("1.2.3", "Notes", "en-US", 50);

        holder.ClearSession();

        using (Assert.Multiple())
        {
            await Assert.That(holder.Current.Account).IsNull();
            await Assert.That(holder.Current.IsSignedIn).IsFalse();
            await Assert.That(holder.Current.ReleaseName).IsEmpty();
            await Assert.That(holder.Current.ReleaseNotes).IsEmpty();
        }
    }
}
