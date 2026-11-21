using System.Net;
using System.Text;
using CodePublisher.Features.GooglePlay;
using CodePublisher.Features.GooglePlay.Authentication;
using CodePublisher.Features.GooglePlay.Models;
using CodePublisher.Features.GooglePlay.Releases;
using CodePublisher.Features.GooglePlay.Services;
using CodePublisher.Features.GooglePlay.Setup;
using CodePublisher.Features.GooglePlay.Signing;
using CodePublisher.Features.GooglePlay.Tracks;
using CodePublisher.Models;
using CodePublisher.Services;

namespace CodePublisher.Tests.Features.GooglePlay;

public sealed class GooglePlayViewModelTests
{
    [Test]
    public async Task ChangingAccount_ReplacesTracksAndLoadedContext()
    {
        var viewModel = CreateViewModel();
        viewModel.UpdateContext(Context());

        viewModel.SelectedAccount = Account("one");
        await WaitUntilAsync(() => viewModel.SelectedTrack?.Id == "qa");
        viewModel.SelectedAccount = Account("two");
        await WaitUntilAsync(() => viewModel.SelectedTrack?.Id == "beta");

        using (Assert.Multiple())
        {
            await Assert.That(viewModel.SelectedTrack?.Id).IsEqualTo("beta");
            await Assert.That(viewModel.Tracks.Any(track => track.Id == "qa")).IsFalse();
            await Assert.That(viewModel.HasLoadedTracksForContext).IsTrue();
            await Assert.That(viewModel.StatusDetail).DoesNotContain("Ready to publish");
        }
    }

    [Test]
    public async Task MultipleLiveReleases_BlockManagementWithConsoleGuidance()
    {
        var viewModel = CreateViewModel();
        viewModel.UpdateContext(Context());
        viewModel.SelectedAccount = Account("three");
        await WaitUntilAsync(() => viewModel.SelectedTrack?.ActiveReleaseCount == 2);

        using (Assert.Multiple())
        {
            await Assert.That(viewModel.CanManageTrack).IsFalse();
            await Assert.That(viewModel.TrackManagementReason).Contains("Play Console");
            await Assert.That(viewModel.LiveReleaseSummary).Contains("multiple active releases");
        }
    }

    [Test]
    public async Task FirstProductionRelease_RequiresFullRollout()
    {
        var viewModel = CreateViewModel();
        viewModel.UpdateContext(Context());
        viewModel.SelectedAccount = Account("four");
        await WaitUntilAsync(() => viewModel.SelectedTrack?.Id == "production");

        using (Assert.Multiple())
        {
            await Assert.That(viewModel.IsFirstProductionRelease).IsTrue();
            await Assert.That(viewModel.IsProductionRolloutEnabled).IsFalse();
            await Assert.That(viewModel.RolloutPercentage).IsEqualTo(100);
            await Assert.That(viewModel.RolloutLabel).Contains("first production release");
        }
    }

    [Test]
    public async Task ClearSession_RemovesDraftAndLoadedReleaseState()
    {
        var viewModel = CreateViewModel();
        viewModel.UpdateContext(Context());
        viewModel.SelectedAccount = Account("one");
        await WaitUntilAsync(() => viewModel.SelectedTrack is not null);
        viewModel.ReleaseName = "Release 42";
        viewModel.ReleaseNotes = "Notes";
        viewModel.ReleaseNotesLanguage = "fr-FR";

        viewModel.ClearSessionAndForget();

        using (Assert.Multiple())
        {
            await Assert.That(viewModel.ReleaseName).IsEmpty();
            await Assert.That(viewModel.ReleaseNotes).IsEmpty();
            await Assert.That(viewModel.ReleaseNotesLanguage).IsEqualTo("en-US");
            await Assert.That(viewModel.Tracks).IsEmpty();
            await Assert.That(viewModel.SelectedTrack).IsNull();
            await Assert.That(viewModel.HasLoadedTracksForContext).IsFalse();
        }
    }

    [Test]
    public async Task StaleTrackResponse_CannotBeAssignedToNewAccount()
    {
        var handler = new AccountTrackHandler
        {
            FirstTrackRequestStarted = new TaskCompletionSource(),
            ReleaseFirstTrackRequest = new TaskCompletionSource()
        };
        var viewModel = CreateViewModel(handler);
        viewModel.UpdateContext(Context());
        viewModel.SelectedAccount = Account("one");
        await handler.FirstTrackRequestStarted.Task;

        viewModel.SelectedAccount = Account("two");
        handler.ReleaseFirstTrackRequest.SetResult();
        await WaitUntilAsync(() => viewModel.SelectedTrack?.Id == "beta");

        using (Assert.Multiple())
        {
            await Assert.That(viewModel.SelectedTrack?.Id).IsEqualTo("beta");
            await Assert.That(viewModel.Tracks.Any(track => track.Id == "qa")).IsFalse();
            await Assert.That(viewModel.HasLoadedTracksForContext).IsTrue();
        }
    }

    [Test]
    public async Task DisconnectDuringRevoke_PreservesNewAccountAndTracks()
    {
        var store = new FakeCredentialStore
        {
            RevokeStarted = new TaskCompletionSource(),
            RevokeRelease = new TaskCompletionSource()
        };
        var viewModel = CreateViewModel(credentialStore: store);
        viewModel.UpdateContext(Context());
        viewModel.SelectedAccount = Account("one");
        await WaitUntilAsync(() => viewModel.SelectedTrack?.Id == "qa");
        var disconnect = viewModel.DisconnectCommand.ExecuteAsync(null);
        await store.RevokeStarted.Task;

        viewModel.SelectedAccount = Account("two");
        await WaitUntilAsync(() => viewModel.SelectedTrack?.Id == "beta");
        store.RevokeRelease.SetResult();
        await disconnect;

        using (Assert.Multiple())
        {
            await Assert.That(viewModel.SelectedAccount?.Id).IsEqualTo("two");
            await Assert.That(viewModel.Accounts.Any(account => account.Id == "one")).IsFalse();
            await Assert.That(viewModel.SelectedTrack?.Id).IsEqualTo("beta");
            await Assert.That(viewModel.HasLoadedTracksForContext).IsTrue();
        }
    }

    private static GooglePlayViewModel CreateViewModel(
        AccountTrackHandler? handler = null,
        FakeCredentialStore? credentialStore = null)
    {
        handler ??= new AccountTrackHandler();
        var httpClient = new HttpClient(handler);
        var credentials = credentialStore ?? new FakeCredentialStore();
        var api = new GooglePlayApiService(httpClient, credentials);
        return new GooglePlayViewModel(
            new PublisherService(),
            new DesktopInteractionService(),
            new GoogleAuthenticationWorkflow(new GoogleOAuthService(httpClient, () => "client"), credentials),
            new GooglePlayTrackWorkflow(api),
            new GooglePlayReleaseWorkflow(api),
            new GooglePlaySigningWorkflow(new AndroidSigningKeyService(), new GooglePlayArtifactValidator()),
            new EnvironmentVariableService());
    }

    private static GooglePlayProjectContext Context() => new(
        Configuration(),
        "com.example.app",
        "1.2.3",
        "42",
        true);

    private static PublishConfiguration Configuration() => new()
    {
        ProjectDirectory = "/tmp",
        ProjectFilePath = "/tmp/App.csproj",
        PublishPlatform = PublisherService.AndroidPlatform,
        TargetFramework = "net11.0-android",
        RuntimeIdentifier = "android-arm64",
        Configuration = "Release",
        OutputDirectory = "/tmp/output",
        PackageId = "com.example.app",
        IncludeApk = false,
        IncludeAab = true,
        SelfContained = true,
        PublishTrimmed = true,
        PublishAot = false,
        PublishReadyToRun = false,
        PublishSingleFile = false,
        UseAppHost = true,
        CreateMacAppBundle = false,
        CreateWindowsExecutable = false,
        BuildIpa = false,
        ArchiveOnBuild = false,
        RunAotCompilation = false,
        EnableProfiledAot = false,
        AndroidLinkMode = "SdkOnly",
        AndroidLinkTool = "r8",
        AndroidDexTool = "d8",
        CreateMappingFile = false,
        EnableMultiDex = false,
        UseAapt2 = true,
        EnableDesugar = true,
        DeleteBin = false,
        DeleteObj = false,
        SignMode = "Auto",
        KeystorePath = string.Empty,
        KeyAlias = string.Empty,
        KeystorePassword = string.Empty,
        KeyPassword = string.Empty,
        ProjectName = "App"
    };

    private static GooglePlayAccountInfo Account(string id) =>
        new(id, $"{id}@example.com", id, id[..1].ToUpperInvariant(), false, null, []);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        await Assert.That(condition()).IsTrue();
    }

    private sealed class AccountTrackHandler : HttpMessageHandler
    {
        public TaskCompletionSource? FirstTrackRequestStarted { get; init; }
        public TaskCompletionSource? ReleaseFirstTrackRequest { get; init; }
        private int _trackRequests;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/edits/edit-1/tracks", StringComparison.Ordinal)
                && _trackRequests++ == 0 && FirstTrackRequestStarted is not null && ReleaseFirstTrackRequest is not null)
            {
                FirstTrackRequestStarted.SetResult();
                await ReleaseFirstTrackRequest.Task;
            }

            var path = request.RequestUri!.AbsolutePath;
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            if (path.EndsWith("/edits", StringComparison.Ordinal))
            {
                response.Content = Json("""{"id":"edit-1"}""");
            }
            else if (path.EndsWith("/edits/edit-1/tracks", StringComparison.Ordinal))
            {
                var token = request.Headers.Authorization?.Parameter;
                var body = token switch
                {
                    "token-one" => Track("qa", 1),
                    "token-two" => Track("beta", 1),
                    "token-three" => Track("production", 2),
                    _ => Track("production", 0)
                };
                response.Content = Json($"{{\"tracks\":[{body}]}}");
            }
            else
            {
                response.Content = Json("{}");
            }

            return response;
        }

        private static string Track(string id, int releaseCount)
        {
            var releases = releaseCount == 0
                ? "[]"
                : $"[{string.Join(",", Enumerable.Range(1, releaseCount).Select(index =>
                    $"{{\"name\":\"Live {index}\",\"versionCodes\":[\"{40 + index}\"],\"status\":\"inProgress\",\"userFraction\":0.5,\"releaseNotes\":[{{\"language\":\"en-US\",\"text\":\"Live notes\"}}]}}"))}]";
            return $"{{\"track\":\"{id}\",\"releases\":{releases}}}";
        }

        private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");
    }

    private sealed class FakeCredentialStore : IGoogleCredentialStore
    {
        public GooglePlayAccountInfo Add(GoogleOAuthTokens tokens, GooglePlayAccountInfo account) => account;

        public Task<string?> GetAccessTokenAsync(string accountId, CancellationToken cancellationToken) =>
            Task.FromResult<string?>($"token-{accountId}");

        public TaskCompletionSource? RevokeStarted { get; init; }
        public TaskCompletionSource? RevokeRelease { get; init; }

        public async Task RevokeAsync(string accountId, CancellationToken cancellationToken)
        {
            RevokeStarted?.SetResult();
            if (RevokeRelease is not null)
            {
                await RevokeRelease.Task;
            }
        }

        public void Forget(string accountId) { }
    }
}
