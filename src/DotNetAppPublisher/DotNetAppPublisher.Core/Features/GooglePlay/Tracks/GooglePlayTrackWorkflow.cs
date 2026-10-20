using DotNetAppPublisher.Features.GooglePlay.Models;
using DotNetAppPublisher.Features.GooglePlay.Services;

namespace DotNetAppPublisher.Features.GooglePlay.Tracks;

public sealed class GooglePlayTrackWorkflow
{
    private readonly GooglePlayApiService _apiService;

    public GooglePlayTrackWorkflow(GooglePlayApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<IReadOnlyList<GooglePlayTrackInfo>> GetTracksAsync(
        string accountId,
        string packageName,
        CancellationToken cancellationToken) =>
        _apiService.GetTracksAsync(accountId, packageName, cancellationToken);
}
