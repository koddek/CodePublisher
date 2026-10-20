using DotNetAppPublisher.Features.GooglePlay.Tracks;

namespace DotNetAppPublisher.Tests.Features.GooglePlay.Tracks;

/// <summary>
/// The release line policy answers the question a tester actually has: my device is on a testing
/// line, what stops it from moving to production. Google Play serves the highest version code the
/// user is eligible for and never downgrades, so the version code the device runs is the floor.
/// </summary>
public sealed class GooglePlayReleaseLinePolicyTests
{
    private static readonly GooglePlayTrackLineState Production =
        new("production", "Production", true, ["50"]);

    private static readonly GooglePlayTrackLineState Beta =
        new("beta", "Open testing", false, ["100"]);

    private static GooglePlayDeviceLineState Device(int? versionCode) =>
        new(versionCode, true, []);

    [Test]
    public async Task Plan_SaysTheDeviceCannotReachTheTarget_WhenTheTargetIsLower()
    {
        // The device runs 100 and production serves 50. Play never downgrades, so production at 50
        // is never delivered to this device.
        var plan = GooglePlayReleaseLinePolicy.Plan(
            Device(100),
            [Beta, Production],
            "production");

        using (Assert.Multiple())
        {
            await Assert.That(plan.CanReachTarget).IsFalse();
            await Assert.That(plan.RequiredVersionCode).IsEqualTo(101);
            await Assert.That(plan.BlockingReason).Contains("version code 100");
        }
    }

    [Test]
    public async Task Plan_SaysTheDeviceCanReachTheTarget_WhenTheTargetIsHigher()
    {
        var plan = GooglePlayReleaseLinePolicy.Plan(
            Device(40),
            [new GooglePlayTrackLineState("production", "Production", true, ["50"])],
            "production");

        using (Assert.Multiple())
        {
            await Assert.That(plan.CanReachTarget).IsTrue();
            await Assert.That(plan.RequiredVersionCode).IsNull();
            await Assert.That(plan.Steps).IsEmpty();
        }
    }

    [Test]
    public async Task Plan_RequiresRemovingTheOtherLine_WhenItServesAHigherVersionCode()
    {
        // The device runs 40, beta serves 100 and production serves 50. Production alone is high
        // enough, but beta would still win, so the beta release has to go first.
        var plan = GooglePlayReleaseLinePolicy.Plan(
            Device(40),
            [Beta, Production],
            "production");

        using (Assert.Multiple())
        {
            await Assert.That(plan.CanReachTarget).IsFalse();
            await Assert.That(plan.RequiredVersionCode).IsEqualTo(101);
            await Assert.That(plan.Steps.Any(step => step.Contains("Remove the release"))).IsTrue();
            await Assert.That(plan.Steps.Any(step => step.Contains("101"))).IsTrue();
        }
    }

    [Test]
    public async Task Plan_AsksForTheVersionCodeAboveTheDevice()
    {
        // Nothing else competes, so the target only has to beat the version code on the device.
        var plan = GooglePlayReleaseLinePolicy.Plan(
            Device(100),
            [Production],
            "production");

        await Assert.That(plan.RequiredVersionCode).IsEqualTo(101);
    }

    [Test]
    public async Task Plan_SaysItCannotTell_WhenTheDeviceVersionCodeIsUnknown()
    {
        var plan = GooglePlayReleaseLinePolicy.Plan(
            Device(null),
            [Beta, Production],
            "production");

        using (Assert.Multiple())
        {
            await Assert.That(plan.CanReachTarget).IsFalse();
            await Assert.That(plan.BlockingReason).Contains("could not be read");
            await Assert.That(plan.Steps.Any(step => step.Contains("Connect the device"))).IsTrue();
        }
    }

    [Test]
    public async Task Plan_SaysTheLineDoesNotExist_WhenTheTargetIsUnknown()
    {
        var plan = GooglePlayReleaseLinePolicy.Plan(
            Device(10),
            [Production],
            "does-not-exist");

        using (Assert.Multiple())
        {
            await Assert.That(plan.CanReachTarget).IsFalse();
            await Assert.That(plan.BlockingReason).Contains("does not exist");
        }
    }

    [Test]
    public async Task EffectiveLine_FindsTheLineServingTheInstalledVersionCode()
    {
        var effective = GooglePlayReleaseLinePolicy.EffectiveLine(
            Device(100),
            [Beta, Production]);

        await Assert.That(effective?.TrackId).IsEqualTo("beta");
    }

    [Test]
    public async Task EffectiveLine_IsNull_WhenNoLineServesTheInstalledVersionCode()
    {
        var effective = GooglePlayReleaseLinePolicy.EffectiveLine(
            Device(999),
            [Beta, Production]);

        await Assert.That(effective).IsNull();
    }

    [Test]
    public async Task EffectiveLine_IsNull_WhenTheDeviceVersionCodeIsUnknown()
    {
        var effective = GooglePlayReleaseLinePolicy.EffectiveLine(
            Device(null),
            [Beta, Production]);

        await Assert.That(effective).IsNull();
    }
}
