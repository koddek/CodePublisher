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

/// <summary>One Play release track and the release currently on it.</summary>
/// <param name="Id">The track identifier, used verbatim against the Play API.</param>
/// <param name="DisplayName">The name shown in the UI.</param>
/// <param name="Group">The group the track belongs to, used to order the list.</param>
/// <param name="Status">The release status Play reported for this track.</param>
/// <param name="IsProduction">True for the production track and its form-factor variants.</param>
/// <param name="VersionCodes">The version codes on this track, highest first.</param>
public sealed record GooglePlayTrackInfo(
    string Id,
    string DisplayName,
    string Group,
    string Status,
    bool IsProduction)
{
    /// <summary>The version codes on this track, highest first.</summary>
    public IReadOnlyList<string> VersionCodes { get; init; } = [];
}

public sealed record GooglePlayServiceAccountInfo(
    string Id,
    string ProjectId,
    string ClientEmail,
    string PrivateKeyId,
    IReadOnlyList<string> PackageNames);

/// <summary>
/// One environment variable the Google Play workspace reads, with the guidance a user needs to
/// supply it. Lives in the core library because both the setup service and the view model need it.
/// </summary>
public sealed record GooglePlayEnvironmentVariableDefinition(
    string Name,
    string Description,
    bool IsRequired,
    bool IsSecret,
    string DisplayName = "",
    string Example = "",
    string SetupInstructions = "",
    string HelpUrl = "");

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
