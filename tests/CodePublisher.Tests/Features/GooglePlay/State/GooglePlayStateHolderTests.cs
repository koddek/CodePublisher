using CodePublisher.Features.GooglePlay.Models;
using CodePublisher.Features.GooglePlay.State;

namespace CodePublisher.Tests.Features.GooglePlay.State;

public sealed class GooglePlayStateHolderTests
{
    [Test]
    public async Task SetTrack_PreservesExplicitTestingRollout()
    {
        var holder = new GooglePlayStateHolder();
        var track = new GooglePlayTrackInfo("beta", "Open testing", "Open testing", "Ready", false);

        holder.SetTrack(track, rolloutPercentage: 25);

        await Assert.That(holder.Current.RolloutPercentage).IsEqualTo(25);
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
    public async Task OnChange_FiresOncePerCommand()
    {
        var holder = new GooglePlayStateHolder();
        var changes = 0;
        holder.OnChange += () => changes++;

        holder.SetStage(GooglePlayPublishStage.Uploading);

        await Assert.That(changes).IsEqualTo(1);
    }

    [Test]
    public async Task SetStageAndStatus_NotifiesOnceForBothChanges()
    {
        var holder = new GooglePlayStateHolder();
        var changes = 0;
        holder.OnChange += () => changes++;

        holder.SetStageAndStatus(
            GooglePlayPublishStage.Published,
            "Published",
            "Current Google Play state: published",
            GooglePlayStatusTone.Success);

        using (Assert.Multiple())
        {
            await Assert.That(changes).IsEqualTo(1);
            await Assert.That(holder.Current.Stage).IsEqualTo(GooglePlayPublishStage.Published);
            await Assert.That(holder.Current.StatusHeadline).IsEqualTo("Published");
            await Assert.That(holder.Current.StatusTone).IsEqualTo(GooglePlayStatusTone.Success);
        }
    }

    [Test]
    public async Task Dispose_RemovesSubscribers()
    {
        var holder = new GooglePlayStateHolder();
        var changes = 0;
        holder.OnChange += () => changes++;

        holder.Dispose();
        holder.SetStage(GooglePlayPublishStage.Uploading);

        await Assert.That(changes).IsEqualTo(0);
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
