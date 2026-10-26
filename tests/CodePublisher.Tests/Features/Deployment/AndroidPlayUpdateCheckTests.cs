using CodePublisher.Features.Deployment.Android;

namespace CodePublisher.Tests.Features.Deployment;

/// <summary>
/// The Play update check compares the certificate of a local build with the certificate Google
/// Play signs releases with. A mismatch means Google Play can never update that install, so the
/// comparison and its wording matter.
/// </summary>
public sealed class AndroidPlayUpdateCheckTests
{
    private const string PlayDigest = "fb0cde301eaeb0d79ed5e30e5b690799b0a3155d5bb82493f7a910965264cab2";
    private const string OtherDigest = "aa1cde301eaeb0d79ed5e30e5b690799b0a3155d5bb82493f7a910965264cab2";

    [Test]
    public async Task Compare_SaysPlayUpdatable_WhenTheCertificatesMatch()
    {
        var result = AndroidPlayUpdateCheck.Compare([PlayDigest], [PlayDigest]);

        await Assert.That(result).IsEqualTo(AndroidPlayUpdateCompatibility.PlayUpdatable);
    }

    [Test]
    public async Task Compare_SaysNotPlayUpdatable_WhenTheCertificatesDiffer()
    {
        var result = AndroidPlayUpdateCheck.Compare([PlayDigest], [OtherDigest]);

        await Assert.That(result).IsEqualTo(AndroidPlayUpdateCompatibility.NotPlayUpdatable);
    }

    [Test]
    public async Task Compare_IgnoresTheColonSeparatedFormThePlayConsoleShows()
    {
        // The Play Console shows the fingerprint in uppercase with colons; apksigner prints it
        // in lowercase without them. They are the same certificate.
        var consoleForm = string.Join(
            ":",
            Enumerable.Range(0, PlayDigest.Length / 2)
                .Select(index => PlayDigest.Substring(index * 2, 2).ToUpperInvariant()));

        var result = AndroidPlayUpdateCheck.Compare([PlayDigest], [consoleForm]);

        await Assert.That(result).IsEqualTo(AndroidPlayUpdateCompatibility.PlayUpdatable);
    }

    [Test]
    public async Task Compare_SaysUnknown_WhenEitherSideIsMissing()
    {
        using (Assert.Multiple())
        {
            await Assert.That(AndroidPlayUpdateCheck.Compare(null, [PlayDigest]))
                .IsEqualTo(AndroidPlayUpdateCompatibility.Unknown);
            await Assert.That(AndroidPlayUpdateCheck.Compare([PlayDigest], []))
                .IsEqualTo(AndroidPlayUpdateCompatibility.Unknown);
            await Assert.That(AndroidPlayUpdateCheck.Compare([], []))
                .IsEqualTo(AndroidPlayUpdateCompatibility.Unknown);
        }
    }

    [Test]
    public async Task Describe_ExplainsThatAMismatchBlocksPlayUpdates()
    {
        var message = AndroidPlayUpdateCheck.Describe(
            AndroidPlayUpdateCompatibility.NotPlayUpdatable,
            [PlayDigest],
            [OtherDigest]);

        using (Assert.Multiple())
        {
            await Assert.That(message).Contains("different certificate");
            await Assert.That(message).Contains("will not");
            await Assert.That(message).Contains(PlayDigest);
            await Assert.That(message).Contains(OtherDigest);
        }
    }

    [Test]
    public async Task Describe_ConfirmsThatAMatchKeepsPlayUpdatesWorking()
    {
        var message = AndroidPlayUpdateCheck.Describe(
            AndroidPlayUpdateCompatibility.PlayUpdatable,
            [PlayDigest],
            [PlayDigest]);

        await Assert.That(message).Contains("can replace it");
    }

    [Test]
    public async Task Describe_SaysTheComparisonIsUnknown_WhenNothingCouldBeCompared()
    {
        var message = AndroidPlayUpdateCheck.Describe(
            AndroidPlayUpdateCompatibility.Unknown,
            null,
            null);

        await Assert.That(message).Contains("unknown");
    }
}
