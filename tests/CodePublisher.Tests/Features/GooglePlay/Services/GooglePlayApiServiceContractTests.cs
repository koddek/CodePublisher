using System.Net;
using System.Text;
using CodePublisher.Features.GooglePlay.Authentication;
using CodePublisher.Features.GooglePlay.Models;
using CodePublisher.Features.GooglePlay.Services;

namespace CodePublisher.Tests.Features.GooglePlay.Services;

public sealed class GooglePlayApiServiceContractTests
{
    [Test]
    public async Task GetTracks_PreservesLiveMetadataAndAmbiguity()
    {
        var service = CreateService();
        var tracks = await service.GetTracksAsync("account", "com.example.app", CancellationToken.None);

        var production = tracks.Single(track => track.Id == "production");
        using (Assert.Multiple())
        {
            await Assert.That(production.ActiveReleaseCount).IsEqualTo(2);
            await Assert.That(production.HasUnambiguousLiveRelease).IsFalse();
            await Assert.That(production.LiveReleaseName).IsEqualTo("Live 42");
            await Assert.That(production.LiveStatus).IsEqualTo("inProgress");
            await Assert.That(production.LiveRolloutPercentage).IsEqualTo(50);
            await Assert.That(production.VersionCodes[0]).IsEqualTo("42");
            await Assert.That(production.LiveReleaseNotes[0].Text).IsEqualTo("Live notes");
        }
    }

    [Test]
    public async Task GetReleaseState_MatchesVersionCodeInsteadOfDuplicateName()
    {
        var service = CreateService();

        var state = await service.GetReleaseStateAsync(
            "account",
            "com.example.app",
            "production",
            42,
            CancellationToken.None);

        await Assert.That(state).IsEqualTo("RELEASE_LIFECYCLE_STATE_IN_REVIEW");
    }

    [Test]
    public async Task PublishAsync_AllowsEmptyNotesAndReportsCommittedVersion()
    {
        var service = CreateService();
        var aab = Path.Combine(Path.GetTempPath(), $"play-{Guid.NewGuid():N}.aab");
        try
        {
            await File.WriteAllBytesAsync(aab, [1, 2, 3]);
            var result = await service.PublishAsync(
                "account",
                "com.example.app",
                aab,
                "production",
                "Release 42",
                [],
                100,
                null,
                CancellationToken.None);

            using (Assert.Multiple())
            {
                await Assert.That(result.Stage).IsEqualTo(GooglePlayPublishStage.Published);
                await Assert.That(result.VersionCode).IsEqualTo(42);
                await Assert.That(result.Message).Contains("Change committed");
                await Assert.That(result.Message).DoesNotContain("review has started");
            }
        }
        finally
        {
            File.Delete(aab);
        }
    }

    private static GooglePlayApiService CreateService() =>
        new(new HttpClient(new ContractHandler()), new FakeCredentialStore());

    private sealed class ContractHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            if (path == "/upload/session")
            {
                response.Content = Json("""{"versionCode":42}""");
            }
            else if (path.Contains("/upload/androidpublisher/") && path.EndsWith("/bundles"))
            {
                response.Headers.Location = new Uri("https://session.test/upload/session");
            }
            else if (path.EndsWith("/edits", StringComparison.Ordinal))
            {
                response.Content = Json("""{"id":"edit-1"}""");
            }
            else if (path.EndsWith("/edits/edit-1/tracks", StringComparison.Ordinal))
            {
                response.Content = Json(TracksJson);
            }
            else if (path.EndsWith("/tracks/production/releases", StringComparison.Ordinal))
            {
                response.Content = Json(ReleaseSummariesJson);
            }
            else
            {
                response.Content = Json("{}");
            }

            return Task.FromResult(response);
        }

        private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

        private const string TracksJson = """
        {
          "tracks": [{
            "track": "production",
            "releases": [
              {
                "name": "Live 42",
                "versionCodes": ["42"],
                "status": "inProgress",
                "userFraction": 0.5,
                "releaseNotes": [{"language":"en-US","text":"Live notes"}]
              },
              {
                "name": "Live 41",
                "versionCodes": ["41"],
                "status": "completed"
              }
            ]
          }]
        }
        """;

        private const string ReleaseSummariesJson = """
        {
          "releases": [
            {
              "releaseName": "Duplicate",
              "activeArtifacts": [{"versionCode": 41}],
              "releaseLifecycleState": "RELEASE_LIFECYCLE_STATE_PUBLISHED"
            },
            {
              "releaseName": "Duplicate",
              "activeArtifacts": [{"versionCode": 42}],
              "releaseLifecycleState": "RELEASE_LIFECYCLE_STATE_IN_REVIEW"
            }
          ]
        }
        """;
    }

    private sealed class FakeCredentialStore : IGoogleCredentialStore
    {
        public GooglePlayAccountInfo Add(GoogleOAuthTokens tokens, GooglePlayAccountInfo account) => account;
        public Task<string?> GetAccessTokenAsync(string accountId, CancellationToken cancellationToken) =>
            Task.FromResult<string?>("token");
        public Task RevokeAsync(string accountId, CancellationToken cancellationToken) => Task.CompletedTask;
        public void Forget(string accountId) { }
    }
}
