using DotNetAppPublisher.Models;

namespace DotNetAppPublisher.Features.GooglePlay.Models;

public sealed record GooglePlayProjectContext(
    PublishConfiguration PublishConfiguration,
    string PackageId,
    string DisplayVersion,
    string VersionCode,
    bool IsAndroidProject);

public sealed record GooglePlayAccountInfo(
    string Id,
    string Email,
    string DisplayName,
    string Initials,
    bool HasCloudAdminAccess,
    DateTimeOffset? TokenExpiry,
    IReadOnlyList<string> Scopes);

public sealed record GooglePlayTrackInfo(
    string Id,
    string DisplayName,
    string Group,
    string Status,
    bool IsProduction);

public sealed record GooglePlayServiceAccountInfo(
    string Id,
    string ProjectId,
    string ClientEmail,
    string PrivateKeyId,
    IReadOnlyList<string> PackageNames);

public sealed record GooglePlayArtifactInfo(
    string Path,
    long SizeBytes,
    bool IsValid,
    string Message,
    IReadOnlyList<string> RequiredEntries);

public sealed record GooglePlaySigningKeyInfo(
    string Path,
    string Alias,
    string StoreType,
    string Algorithm,
    int KeySize,
    string Sha1Fingerprint,
    string Sha256Fingerprint,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ExpiresAt);

public enum GooglePlayPublishStage
{
    Idle,
    Building,
    Uploading,
    Submitting,
    InReview,
    Published,
    Failed
}

public enum GooglePlayStatusTone
{
    Info,
    Progress,
    Success,
    Warning,
    Error
}

public sealed record GooglePlayPublishResult(
    bool Succeeded,
    GooglePlayPublishStage Stage,
    string Message,
    string? ReleaseName = null,
    string? TrackId = null,
    int? VersionCode = null);
