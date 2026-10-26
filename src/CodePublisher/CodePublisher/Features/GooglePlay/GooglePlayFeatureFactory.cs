using System.Net;
using System.Net.Http;
using CodePublisher.Features.GooglePlay.Authentication;
using CodePublisher.Features.GooglePlay.Releases;
using CodePublisher.Features.GooglePlay.Services;
using CodePublisher.Features.GooglePlay.Setup;
using CodePublisher.Features.GooglePlay.Signing;
using CodePublisher.Features.GooglePlay.Tracks;
using CodePublisher.Services;

namespace CodePublisher.Features.GooglePlay;

public static class GooglePlayFeatureFactory
{
    public static GooglePlayViewModel Create(
        PublisherService publisherService,
        DesktopInteractionService desktopInteractionService,
        Action? openSigning = null)
    {
        var httpClient = OperatingSystem.IsBrowser()
            ? new HttpClient { Timeout = TimeSpan.FromMinutes(20) }
            : new HttpClient(new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All
            })
            {
                Timeout = TimeSpan.FromMinutes(20)
            };
        var environmentVariableService = new EnvironmentVariableService();
        var clientIdProvider = () => environmentVariableService.GoogleClientId ?? string.Empty;
        var credentialStore = new InMemoryGoogleCredentialStore(httpClient, clientIdProvider);
        var oauthService = new GoogleOAuthService(httpClient, clientIdProvider);
        var apiService = new GooglePlayApiService(httpClient, credentialStore);
        var signingKeyService = new AndroidSigningKeyService();
        var artifactValidator = new GooglePlayArtifactValidator();
        return new GooglePlayViewModel(
            publisherService,
            desktopInteractionService,
            new GoogleAuthenticationWorkflow(oauthService, credentialStore),
            new GooglePlayTrackWorkflow(apiService),
            new GooglePlayReleaseWorkflow(apiService),
            new GooglePlaySigningWorkflow(signingKeyService, artifactValidator),
            environmentVariableService,
            openSigning);
    }
}
