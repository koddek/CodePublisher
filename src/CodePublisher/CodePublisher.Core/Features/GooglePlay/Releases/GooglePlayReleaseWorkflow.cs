using CodePublisher.Features.GooglePlay.Models;
using CodePublisher.Features.GooglePlay.Services;

namespace CodePublisher.Features.GooglePlay.Releases;

public sealed class GooglePlayReleaseWorkflow
{
    private readonly GooglePlayApiService _apiService;

    public GooglePlayReleaseWorkflow(GooglePlayApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<GooglePlayPublishResult> PublishAsync(
        string accountId,
        string packageName,
        string aabPath,
        string trackId,
        string releaseName,
        IReadOnlyList<GooglePlayReleaseNote> releaseNotes,
        int rolloutPercentage,
        Action<string> writeOutput,
        CancellationToken cancellationToken) =>
        _apiService.PublishAsync(
            accountId,
            packageName,
            aabPath,
            trackId,
            releaseName,
            releaseNotes,
            rolloutPercentage,
            writeOutput,
            cancellationToken);

    public Task<string> GetReleaseStateAsync(
        string accountId,
        string packageName,
        string trackId,
        string releaseName,
        CancellationToken cancellationToken) =>
        _apiService.GetReleaseStateAsync(accountId, packageName, trackId, releaseName, cancellationToken);
}
