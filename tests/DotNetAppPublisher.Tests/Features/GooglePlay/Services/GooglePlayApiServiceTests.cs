using System.Text.Json;
using DotNetAppPublisher.Features.GooglePlay.Services;

namespace DotNetAppPublisher.Tests.Features.GooglePlay.Services;

public sealed class GooglePlayApiServiceTests
{
    [Test]
    public async Task BuildTrackUpdatePayload_UsesSourceGeneratedJsonShape()
    {
        var payload = GooglePlayApiService.BuildTrackUpdatePayload(
            "production",
            "Release 1",
            42,
            [new GooglePlayReleaseNote("en-US", "Bug fixes")],
            50);

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        var release = root.GetProperty("releases")[0];

        using (Assert.Multiple())
        {
            await Assert.That(root.GetProperty("track").GetString()).IsEqualTo("production");
            await Assert.That(release.GetProperty("name").GetString()).IsEqualTo("Release 1");
            await Assert.That(release.GetProperty("versionCodes")[0].GetString()).IsEqualTo("42");
            await Assert.That(release.GetProperty("status").GetString()).IsEqualTo("inProgress");
            await Assert.That(release.GetProperty("userFraction").GetDouble()).IsEqualTo(0.5);
            await Assert.That(release.GetProperty("releaseNotes")[0].GetProperty("language").GetString()).IsEqualTo("en-US");
        }
    }

    [Test]
    public async Task BuildTrackUpdatePayload_OmitsUserFractionForCompletedRelease()
    {
        var payload = GooglePlayApiService.BuildTrackUpdatePayload(
            "internal",
            "Release 2",
            7,
            [new GooglePlayReleaseNote("en-US", "Notes")],
            100);

        using var document = JsonDocument.Parse(payload);
        var release = document.RootElement.GetProperty("releases")[0];

        using (Assert.Multiple())
        {
            await Assert.That(release.GetProperty("status").GetString()).IsEqualTo("completed");
            await Assert.That(release.TryGetProperty("userFraction", out _)).IsFalse();
        }
    }
}
