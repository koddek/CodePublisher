using CodePublisher.Features.GooglePlay.Services;
using CodePublisher.Features.GooglePlay.Tracks;

namespace CodePublisher.Tests.Features.GooglePlay.Tracks;

/// <summary>
/// The track catalog decides what each Play track means. These tests pin the identifiers, the
/// groups, and the rules that separate the default tracks from the named and form-factor ones.
/// </summary>
public sealed class GooglePlayTrackCatalogTests
{
    [Test]
    [Arguments("production", true, false, false, false, "Production")]
    [Arguments("beta", false, false, true, false, "Open testing")]
    [Arguments("qa", false, true, false, false, "Internal testing")]
    [Arguments("internal", false, true, false, false, "Internal testing")]
    [Arguments("alpha", false, false, false, true, "Closed testing")]
    [Arguments("MyPrivateTrack", false, false, false, true, "MyPrivateTrack")]
    [Arguments("wear:production", true, false, false, false, "Production")]
    [Arguments("wear:beta", false, false, true, false, "Open testing")]
    [Arguments("wear:qa", false, true, false, false, "Internal testing")]
    [Arguments("automotive:production", true, false, false, false, "Production")]
    public async Task Describe_ClassifiesEveryTrackKind(
        string trackId,
        bool isProduction,
        bool isInternal,
        bool isOpen,
        bool isClosed,
        string expectedName)
    {
        using (Assert.Multiple())
        {
            await Assert.That(GooglePlayTrackCatalog.IsProduction(trackId)).IsEqualTo(isProduction);
            await Assert.That(GooglePlayTrackCatalog.IsInternalTesting(trackId)).IsEqualTo(isInternal);
            await Assert.That(GooglePlayTrackCatalog.IsOpenTesting(trackId)).IsEqualTo(isOpen);
            await Assert.That(GooglePlayTrackCatalog.IsClosedTesting(trackId)).IsEqualTo(isClosed);
            await Assert.That(GooglePlayTrackCatalog.DisplayName(trackId)).IsEqualTo(expectedName);
        }
    }

    [Test]
    [Arguments("wear:production", "wear")]
    [Arguments("tv:beta", "tv")]
    [Arguments("automotive:qa", "automotive")]
    [Arguments("production", null)]
    [Arguments("MyPrivateTrack", null)]
    public async Task FormFactorOf_ReadsThePrefixOnlyForFormFactorTracks(string trackId, string? expected)
    {
        await Assert.That(GooglePlayTrackCatalog.FormFactorOf(trackId)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("wear:production", "production")]
    [Arguments("production", "production")]
    [Arguments("MyPrivateTrack", "MyPrivateTrack")]
    public async Task NameWithoutFormFactor_StripsThePrefix(string trackId, string expected)
    {
        await Assert.That(GooglePlayTrackCatalog.NameWithoutFormFactor(trackId)).IsEqualTo(expected);
    }

    [Test]
    public async Task Describe_ReportsTheVersionCodesOnTheTrack()
    {
        var track = GooglePlayTrackCatalog.Describe("beta", "inProgress", ["42", "40"]);

        using (Assert.Multiple())
        {
            await Assert.That(track.Status).Contains("inProgress");
            await Assert.That(track.Status).Contains("42");
            await Assert.That(track.VersionCodes).IsEquivalentTo(new[] { "42", "40" });
        }
    }

    [Test]
    public async Task Describe_ReportsAnUnconfiguredTrack()
    {
        var track = GooglePlayTrackCatalog.Describe("qa", null, []);

        await Assert.That(track.Status).IsEqualTo("Not configured");
    }
}

/// <summary>
/// The payloads the Play API accepts. The releases array of a track update is the desired state
/// of the track, so an empty array is how a release is removed from it.
/// </summary>
public sealed class GooglePlayTrackPayloadTests
{
    [Test]
    public async Task AnEmptyReleaseArrayRemovesTheReleaseFromTheTrack()
    {
        var payload = GooglePlayApiService.BuildTrackUpdatePayload("beta", []);

        using var document = System.Text.Json.JsonDocument.Parse(payload);
        var root = document.RootElement;

        using (Assert.Multiple())
        {
            await Assert.That(root.GetProperty("track").GetString()).IsEqualTo("beta");
            await Assert.That(root.GetProperty("releases").GetArrayLength()).IsEqualTo(0);
        }
    }

    [Test]
    public async Task AHaltedReleaseKeepsItsVersionCodes()
    {
        var payload = GooglePlayApiService.BuildTrackUpdatePayload(
            "beta",
            [new GooglePlayTrackRelease("Release 1", ["42"], GooglePlayTrackReleaseStatus.Halted)]);

        using var document = System.Text.Json.JsonDocument.Parse(payload);
        var release = document.RootElement.GetProperty("releases")[0];

        using (Assert.Multiple())
        {
            await Assert.That(release.GetProperty("status").GetString()).IsEqualTo("halted");
            await Assert.That(release.GetProperty("versionCodes")[0].GetString()).IsEqualTo("42");
            await Assert.That(release.TryGetProperty("userFraction", out _)).IsFalse();
        }
    }

    [Test]
    public async Task AStagedRolloutCarriesItsUserFraction()
    {
        var payload = GooglePlayApiService.BuildTrackUpdatePayload(
            "production",
            [new GooglePlayTrackRelease("Release 1", ["42"], GooglePlayTrackReleaseStatus.InProgress, 0.25)]);

        using var document = System.Text.Json.JsonDocument.Parse(payload);
        var release = document.RootElement.GetProperty("releases")[0];

        using (Assert.Multiple())
        {
            await Assert.That(release.GetProperty("status").GetString()).IsEqualTo("inProgress");
            await Assert.That(release.GetProperty("userFraction").GetDouble()).IsEqualTo(0.25);
        }
    }

    [Test]
    public async Task AFullRolloutOmitsTheUserFraction()
    {
        var payload = GooglePlayApiService.BuildTrackUpdatePayload(
            "production",
            [new GooglePlayTrackRelease("Release 1", ["42"], GooglePlayTrackReleaseStatus.Completed)]);

        using var document = System.Text.Json.JsonDocument.Parse(payload);
        var release = document.RootElement.GetProperty("releases")[0];

        using (Assert.Multiple())
        {
            await Assert.That(release.GetProperty("status").GetString()).IsEqualTo("completed");
            await Assert.That(release.TryGetProperty("userFraction", out _)).IsFalse();
        }
    }

    [Test]
    public async Task TheLegacyPayloadBuilderStillProducesTheSameShape()
    {
        var payload = GooglePlayApiService.BuildTrackUpdatePayload(
            "production",
            "Release 1",
            42,
            [new GooglePlayReleaseNote("en-US", "Notes")],
            50);

        using var document = System.Text.Json.JsonDocument.Parse(payload);
        var release = document.RootElement.GetProperty("releases")[0];

        using (Assert.Multiple())
        {
            await Assert.That(release.GetProperty("name").GetString()).IsEqualTo("Release 1");
            await Assert.That(release.GetProperty("versionCodes")[0].GetString()).IsEqualTo("42");
            await Assert.That(release.GetProperty("status").GetString()).IsEqualTo("inProgress");
            await Assert.That(release.GetProperty("userFraction").GetDouble()).IsEqualTo(0.5);
        }
    }
}
