using System.Text.Json.Serialization;

namespace DotNetAppPublisher.Features.GooglePlay.Services;

internal sealed class GooglePlayTrackUpdateRequest
{
    [JsonPropertyName("track")]
    public required string Track { get; init; }

    [JsonPropertyName("releases")]
    public required IReadOnlyList<GooglePlayReleaseUpdateRequest> Releases { get; init; }
}

internal sealed class GooglePlayReleaseUpdateRequest
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("versionCodes")]
    public required IReadOnlyList<string> VersionCodes { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("userFraction")]
    public double? UserFraction { get; init; }

    [JsonPropertyName("releaseNotes")]
    public required IReadOnlyList<GooglePlayReleaseNoteUpdateRequest> ReleaseNotes { get; init; }
}

internal sealed class GooglePlayReleaseNoteUpdateRequest
{
    [JsonPropertyName("language")]
    public required string Language { get; init; }

    [JsonPropertyName("text")]
    public required string Text { get; init; }
}

/// <summary>The body of an <c>edits.tracks.create</c> call. The API only creates closed
/// testing tracks; the default tracks always exist.</summary>
internal sealed class GooglePlayTrackConfigRequest
{
    public GooglePlayTrackConfigRequest(string track, string type, string formFactor)
    {
        Track = track;
        Type = type;
        FormFactor = formFactor;
    }

    [JsonPropertyName("track")]
    public string Track { get; }

    [JsonPropertyName("type")]
    public string Type { get; }

    [JsonPropertyName("formFactor")]
    public string FormFactor { get; }
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(GooglePlayTrackUpdateRequest))]
[JsonSerializable(typeof(GooglePlayReleaseUpdateRequest))]
[JsonSerializable(typeof(GooglePlayReleaseNoteUpdateRequest))]
[JsonSerializable(typeof(GooglePlayTrackConfigRequest))]
internal sealed partial class GooglePlayJsonContext : JsonSerializerContext;
