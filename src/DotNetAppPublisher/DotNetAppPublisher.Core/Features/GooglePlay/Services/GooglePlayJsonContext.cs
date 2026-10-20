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

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(GooglePlayTrackUpdateRequest))]
[JsonSerializable(typeof(GooglePlayReleaseUpdateRequest))]
[JsonSerializable(typeof(GooglePlayReleaseNoteUpdateRequest))]
internal sealed partial class GooglePlayJsonContext : JsonSerializerContext;
