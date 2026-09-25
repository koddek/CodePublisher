using System.Net;
using System.Net.Http;
using DotNetAppPublisher.Features.GooglePlay.Authentication;
using DotNetAppPublisher.Features.GooglePlay.Releases;
using DotNetAppPublisher.Features.GooglePlay.Services;
using DotNetAppPublisher.Features.GooglePlay.Setup;
using DotNetAppPublisher.Features.GooglePlay.Signing;
using DotNetAppPublisher.Features.GooglePlay.Tracks;
using DotNetAppPublisher.Services;

namespace DotNetAppPublisher.Features.GooglePlay;

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
