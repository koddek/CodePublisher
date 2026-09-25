using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DotNetAppPublisher.Features.GooglePlay.Authentication;
using DotNetAppPublisher.Features.GooglePlay.Models;

namespace DotNetAppPublisher.Features.GooglePlay.Services;

internal sealed class GooglePlayApiService
{
    private const string BaseUrl = "https://androidpublisher.googleapis.com/androidpublisher/v3";
    private readonly HttpClient _httpClient;
    private readonly IGoogleCredentialStore _credentialStore;

    public GooglePlayApiService(HttpClient httpClient, IGoogleCredentialStore credentialStore)
    {
        _httpClient = httpClient;
        _credentialStore = credentialStore;
    }

    public Task<IReadOnlyList<GooglePlayTrackInfo>> GetTracksAsync(
        string accountId,
        string packageName,
        CancellationToken cancellationToken) =>
        ExecuteAsync(accountId, cancellationToken, async accessToken =>
        {
            var edit = await CreateEditAsync(accessToken, packageName, cancellationToken);
            try
            {
                // Listing tracks is the only read that requires an edit. This edit is
                // deleted after a successful read and retained after failures.
                using var response = await SendAsync(
                    HttpMethod.Get,
                    $"{BaseUrl}/applications/{Uri.EscapeDataString(packageName)}/edits/{edit}/tracks",
                    accessToken,
                    cancellationToken);
                await EnsureSuccessAsync(response, "Could not load Google Play tracks", cancellationToken);
                await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
                var tracks = ReadTracks(document.RootElement);
                await DeleteEditBestEffortAsync(accessToken, packageName, edit, CancellationToken.None);
                return tracks;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Google Play track discovery failed for edit {edit}. The temporary edit was kept; retry or let it expire.",
                    ex);
            }
        });

    public Task<GooglePlayPublishResult> PublishAsync(
        string accountId,
        string packageName,
        string aabPath,
        string trackId,
        string releaseName,
        IReadOnlyList<GooglePlayReleaseNote> releaseNotes,
        int rolloutPercentage,
        Action<string>? reportProgress,
        CancellationToken cancellationToken)
    {
        if (rolloutPercentage is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(rolloutPercentage), "Rollout must be between 1 and 100 percent.");
        }

        if (releaseNotes.Count == 0 || releaseNotes.Any(note => string.IsNullOrWhiteSpace(note.Language) || string.IsNullOrWhiteSpace(note.Text)))
        {
            throw new InvalidOperationException("At least one release note with a language and text is required.");
        }

        return PublishCoreAsync(accountId, packageName, aabPath, trackId, releaseName, releaseNotes, rolloutPercentage, reportProgress, cancellationToken);
    }

    private Task<GooglePlayPublishResult> PublishCoreAsync(
        string accountId,
        string packageName,
        string aabPath,
        string trackId,
        string releaseName,
        IReadOnlyList<GooglePlayReleaseNote> releaseNotes,
        int rolloutPercentage,
        Action<string>? reportProgress,
        CancellationToken cancellationToken) =>
        ExecuteAsync(accountId, cancellationToken, async accessToken =>
        {
            reportProgress?.Invoke("Creating Google Play edit");
            var edit = await CreateEditAsync(accessToken, packageName, cancellationToken);
            reportProgress?.Invoke("Uploading Android App Bundle");
            var versionCode = await UploadBundleAsync(accessToken, packageName, edit, aabPath, cancellationToken);
            reportProgress?.Invoke($"Assigning release to {trackId}");
            await UpdateTrackAsync(
                accessToken,
                packageName,
                edit,
                trackId,
                releaseName,
                versionCode,
                releaseNotes,
                rolloutPercentage,
                cancellationToken);
            reportProgress?.Invoke("Validating release");
            using (var validateResponse = await SendAsync(
                       HttpMethod.Post,
                       $"{BaseUrl}/applications/{Uri.EscapeDataString(packageName)}/edits/{edit}:validate",
                       accessToken,
                       cancellationToken))
            {
                await EnsureSuccessAsync(validateResponse, "Google Play rejected the release before commit", cancellationToken);
            }

            reportProgress?.Invoke("Submitting release for review");
            using var commitRequest = new HttpRequestMessage(
                HttpMethod.Post,
                $"{BaseUrl}/applications/{Uri.EscapeDataString(packageName)}/edits/{edit}:commit?changesInReviewBehavior=ERROR_IF_IN_REVIEW")
            {
                Content = new StringContent(string.Empty, Encoding.UTF8, "application/json")
            };
            commitRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var commitResponse = await _httpClient.SendAsync(commitRequest, cancellationToken);
            await EnsureSuccessAsync(commitResponse, "Google Play did not accept the release", cancellationToken);
            return new GooglePlayPublishResult(
                true,
                GooglePlayPublishStage.InReview,
                "Release submitted. Google Play review has started.",
                releaseName,
                trackId,
                versionCode);
        });

    public async Task<string> GetReleaseStateAsync(
        string accountId,
        string packageName,
        string trackId,
        string? releaseName,
        CancellationToken cancellationToken)
    {
        var token = await RequireAccessTokenAsync(accountId, cancellationToken);
        var parent = $"applications/{Uri.EscapeDataString(packageName)}/tracks/{Uri.EscapeDataString(trackId)}";
        using var response = await SendAsync(
            HttpMethod.Get,
            $"{BaseUrl}/{parent}/releases",
            token,
            cancellationToken);
        await EnsureSuccessAsync(response, "Could not read Google Play release status", cancellationToken);
        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("releases", out var releases) ||
            releases.ValueKind != JsonValueKind.Array ||
            releases.GetArrayLength() == 0)
        {
            return "No active release";
        }

        if (!string.IsNullOrWhiteSpace(releaseName))
        {
            var matchingRelease = releases.EnumerateArray()
                .FirstOrDefault(release => string.Equals(
                    release.TryGetProperty("name", out var name) ? name.GetString() : null,
                    releaseName,
                    StringComparison.Ordinal));
            if (matchingRelease.ValueKind == JsonValueKind.Object)
            {
                return ReadReleaseState(matchingRelease);
            }

            return "Release not found on this track";
        }

        if (releases.GetArrayLength() > 1)
        {
            return "Multiple releases on this track";
        }

        return ReadReleaseState(releases[0]);
    }

    private static string ReadReleaseState(JsonElement release) =>
        release.TryGetProperty("releaseLifecycleState", out var state)
            ? state.GetString() ?? "Unknown"
            : "Unknown";

    private async Task<string> CreateEditAsync(string accessToken, string packageName, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            HttpMethod.Post,
            $"{BaseUrl}/applications/{Uri.EscapeDataString(packageName)}/edits",
            accessToken,
            cancellationToken);
        await EnsureSuccessAsync(response, "Could not access this app in Google Play", cancellationToken);
        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
        return document.RootElement.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("Google Play did not return an edit ID.");
    }

    private async Task<int> UploadBundleAsync(
        string accessToken,
        string packageName,
        string edit,
        string aabPath,
        CancellationToken cancellationToken)
    {
        var uploadUrl = $"https://androidpublisher.googleapis.com/upload/androidpublisher/v3/applications/{Uri.EscapeDataString(packageName)}/edits/{edit}/bundles?uploadType=resumable";
        var fileInfo = new FileInfo(aabPath);
        if (!fileInfo.Exists || fileInfo.Length == 0)
        {
            throw new InvalidOperationException("The selected Android App Bundle is missing or empty.");
        }
        using var initiateRequest = new HttpRequestMessage(HttpMethod.Post, uploadUrl)
        {
            Content = new ByteArrayContent([])
        };
        initiateRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        initiateRequest.Headers.TryAddWithoutValidation("X-Upload-Content-Type", "application/octet-stream");
        initiateRequest.Headers.TryAddWithoutValidation("X-Upload-Content-Length", fileInfo.Length.ToString());
        using var initiateResponse = await _httpClient.SendAsync(initiateRequest, cancellationToken);
        await EnsureSuccessAsync(initiateResponse, "Could not start the Google Play upload", cancellationToken);
        var sessionUri = initiateResponse.Headers.Location
            ?? throw new InvalidOperationException("Google Play did not return an upload session.");
        if (!sessionUri.IsAbsoluteUri)
        {
            sessionUri = new Uri(new Uri("https://androidpublisher.googleapis.com"), sessionUri);
        }

        await using var file = File.OpenRead(aabPath);
        using var uploadRequest = new HttpRequestMessage(HttpMethod.Put, sessionUri)
        {
            Content = new StreamContent(file)
        };
        uploadRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var uploadResponse = await _httpClient.SendAsync(uploadRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(uploadResponse, "Google Play could not upload the Android App Bundle", cancellationToken);
        await using var uploadStream = await uploadResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(uploadStream, cancellationToken: cancellationToken);
        return document.RootElement.GetProperty("versionCode").GetInt32();
    }

    private async Task UpdateTrackAsync(
        string accessToken,
        string packageName,
        string edit,
        string trackId,
        string releaseName,
        int versionCode,
        IReadOnlyList<GooglePlayReleaseNote> releaseNotes,
        int rolloutPercentage,
        CancellationToken cancellationToken)
    {
        var payload = BuildTrackUpdatePayload(trackId, releaseName, versionCode, releaseNotes, rolloutPercentage);
        using var response = await SendAsync(
            HttpMethod.Put,
            $"{BaseUrl}/applications/{Uri.EscapeDataString(packageName)}/edits/{edit}/tracks/{Uri.EscapeDataString(trackId)}",
            accessToken,
            cancellationToken,
            payload);
        await EnsureSuccessAsync(response, $"Could not assign the release to {trackId}", cancellationToken);
    }

    internal static string BuildTrackUpdatePayload(
        string trackId,
        string releaseName,
        int versionCode,
        IReadOnlyList<GooglePlayReleaseNote> releaseNotes,
        int rolloutPercentage)
    {
        if (string.IsNullOrWhiteSpace(releaseName))
        {
            throw new InvalidOperationException("A release name is required.");
        }

        if (string.IsNullOrWhiteSpace(trackId))
        {
            throw new InvalidOperationException("A release track is required.");
        }

        var request = new GooglePlayTrackUpdateRequest
        {
            Track = trackId,
            Releases =
            [
                new GooglePlayReleaseUpdateRequest
                {
                    Name = releaseName,
                    VersionCodes = [versionCode.ToString()],
                    Status = rolloutPercentage >= 100 ? "completed" : "inProgress",
                    UserFraction = rolloutPercentage >= 100 ? null : rolloutPercentage / 100d,
                    ReleaseNotes = releaseNotes
                        .Select(note => new GooglePlayReleaseNoteUpdateRequest
                        {
                            Language = note.Language,
                            Text = note.Text
                        })
                        .ToArray()
                }
            ]
        };

        return JsonSerializer.Serialize(request, GooglePlayJsonContext.Default.GooglePlayTrackUpdateRequest);
    }

    private async Task<T> ExecuteAsync<T>(
        string accountId,
        CancellationToken cancellationToken,
        Func<string, Task<T>> action)
    {
        var accessToken = await RequireAccessTokenAsync(accountId, cancellationToken);
        return await action(accessToken);
    }

    private async Task<string> RequireAccessTokenAsync(string accountId, CancellationToken cancellationToken)
    {
        return await _credentialStore.GetAccessTokenAsync(accountId, cancellationToken)
            ?? throw new InvalidOperationException("Google sign-in expired. Connect the account again.");
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string requestUri,
        string accessToken,
        CancellationToken cancellationToken,
        string? json = null)
    {
        using var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private async Task DeleteEditBestEffortAsync(
        string accessToken,
        string packageName,
        string edit,
        CancellationToken cancellationToken)
    {
        try
        {
            using var cleanupCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cleanupCts.CancelAfter(TimeSpan.FromSeconds(10));
            using var response = await SendAsync(
                HttpMethod.Delete,
                $"{BaseUrl}/applications/{Uri.EscapeDataString(packageName)}/edits/{edit}",
                accessToken,
                cleanupCts.Token);
        }
        catch
        {
            // Edit expiry is the fallback if cleanup itself fails.
        }
    }

    private static IReadOnlyList<GooglePlayTrackInfo> ReadTracks(JsonElement root)
    {
        if (!root.TryGetProperty("tracks", out var tracks) || tracks.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return tracks.EnumerateArray().Select(track =>
        {
            var id = track.GetProperty("track").GetString() ?? string.Empty;
            var status = track.TryGetProperty("releases", out var releases) &&
                          releases.ValueKind == JsonValueKind.Array &&
                          releases.GetArrayLength() > 0
                ? releases[0].TryGetProperty("status", out var releaseStatus)
                    ? releaseStatus.GetString() ?? "Unknown"
                    : "Unknown"
                : "Not configured";
            var group = id switch
            {
                "qa" => "Internal testing",
                "beta" => "Open testing",
                "production" => "Production",
                _ when id.Contains(':', StringComparison.Ordinal) => "Form factor",
                _ => "Closed testing"
            };
            return new GooglePlayTrackInfo(
                id,
                id switch
                {
                    "qa" => "Internal testing",
                    "beta" => "Open testing",
                    "production" => "Production",
                    _ => id
                },
                group,
                status,
                id.EndsWith(":production", StringComparison.Ordinal) || id == "production");
        }).OrderBy(track => track.Group).ThenBy(track => track.DisplayName).ToArray();
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string context,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var message = TryReadGoogleError(body) ?? "Google Play returned an unknown error.";
        throw new InvalidOperationException($"{context} ({(int)response.StatusCode}): {message}");
    }

    private static string? TryReadGoogleError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                if (error.TryGetProperty("message", out var message))
                {
                    return message.GetString();
                }
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }
}

internal sealed record GooglePlayReleaseNote(string Language, string Text);
