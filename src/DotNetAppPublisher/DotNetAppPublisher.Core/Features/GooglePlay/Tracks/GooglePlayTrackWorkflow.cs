using DotNetAppPublisher.Features.GooglePlay.Models;
using DotNetAppPublisher.Features.GooglePlay.Services;

namespace DotNetAppPublisher.Features.GooglePlay.Tracks;

/// <summary>
/// The release-track operations a developer needs to move a release between lines without
/// opening the Play Console website.
/// </summary>
public sealed class GooglePlayTrackWorkflow
{
    private readonly GooglePlayApiService _apiService;

    public GooglePlayTrackWorkflow(GooglePlayApiService apiService)
    {
        _apiService = apiService;
    }

    /// <summary>Lists the release tracks of the app, with the release each one currently serves.</summary>
    public Task<IReadOnlyList<GooglePlayTrackInfo>> GetTracksAsync(
        string accountId,
        string packageName,
        CancellationToken cancellationToken) =>
        _apiService.GetTracksAsync(accountId, packageName, cancellationToken);

    /// <summary>Removes the release from a track, so the track stops serving it.</summary>
    public Task<GooglePlayPublishResult> RemoveReleaseFromTrackAsync(
        string accountId,
        string packageName,
        string trackId,
        Action<string>? reportProgress,
        CancellationToken cancellationToken) =>
        _apiService.RemoveReleaseFromTrackAsync(accountId, packageName, trackId, reportProgress, cancellationToken);

    /// <summary>Halts a release on a track, so it is no longer served to new users.</summary>
    public Task<GooglePlayPublishResult> HaltReleaseAsync(
        string accountId,
        string packageName,
        string trackId,
        string releaseName,
        IReadOnlyList<string> versionCodes,
        Action<string>? reportProgress,
        CancellationToken cancellationToken) =>
        _apiService.HaltReleaseAsync(accountId, packageName, trackId, releaseName, versionCodes, reportProgress, cancellationToken);

    /// <summary>Changes the rollout of a release that is already on a track.</summary>
    public Task<GooglePlayPublishResult> UpdateRolloutAsync(
        string accountId,
        string packageName,
        string trackId,
        string releaseName,
        IReadOnlyList<string> versionCodes,
        int rolloutPercentage,
        IReadOnlyList<GooglePlayReleaseNote> releaseNotes,
        Action<string>? reportProgress,
        CancellationToken cancellationToken) =>
        _apiService.UpdateRolloutAsync(
            accountId,
            packageName,
            trackId,
            releaseName,
            versionCodes,
            rolloutPercentage,
            releaseNotes,
            reportProgress,
            cancellationToken);

    /// <summary>Copies a release from one track to another, keeping the same version code.</summary>
    public Task<GooglePlayPublishResult> PromoteReleaseAsync(
        string accountId,
        string packageName,
        string sourceTrackId,
        string targetTrackId,
        string releaseName,
        IReadOnlyList<string> versionCodes,
        IReadOnlyList<GooglePlayReleaseNote> releaseNotes,
        int rolloutPercentage,
        Action<string>? reportProgress,
        CancellationToken cancellationToken) =>
        _apiService.PromoteReleaseAsync(
            accountId,
            packageName,
            sourceTrackId,
            targetTrackId,
            releaseName,
            versionCodes,
            releaseNotes,
            rolloutPercentage,
            reportProgress,
            cancellationToken);

    /// <summary>Creates a closed testing track, which is the only kind the API can create.</summary>
    public Task<GooglePlayPublishResult> CreateClosedTestingTrackAsync(
        string accountId,
        string packageName,
        string trackName,
        Action<string>? reportProgress,
        CancellationToken cancellationToken) =>
        _apiService.CreateClosedTestingTrackAsync(accountId, packageName, trackName, reportProgress, cancellationToken);

    /// <summary>Reads the version codes already used anywhere on the app.</summary>
    public Task<IReadOnlyList<string>> GetUsedVersionCodesAsync(
        string accountId,
        string packageName,
        CancellationToken cancellationToken) =>
        _apiService.GetUsedVersionCodesAsync(accountId, packageName, cancellationToken);

    /// <summary>Reads the certificate the Play app signing key signed a version with.</summary>
    public Task<string?> GetAppSigningCertificateAsync(
        string accountId,
        string packageName,
        int versionCode,
        CancellationToken cancellationToken) =>
        _apiService.GetAppSigningCertificateAsync(accountId, packageName, versionCode, cancellationToken);
}
