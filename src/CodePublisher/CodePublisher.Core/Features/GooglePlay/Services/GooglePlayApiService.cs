using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CodePublisher.Features.GooglePlay.Authentication;
using CodePublisher.Features.GooglePlay.Models;
using CodePublisher.Features.GooglePlay.Tracks;

namespace CodePublisher.Features.GooglePlay.Services;

public sealed class GooglePlayApiService
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

    /// <summary>
    /// Removes the releases from a track, so the track stops serving them.
    ///
    /// This is how a tester gets back onto the production line. Play serves the highest version
    /// code the user is eligible for across every track, and a tester on a closed or open testing
    /// track keeps receiving that track's build until the track stops serving it. Removing the
    /// release leaves the track empty, and the next update the device receives is the production
    /// build, provided its version code is at least as high as the one on the device.
    /// </summary>
    public Task<GooglePlayPublishResult> RemoveReleaseFromTrackAsync(
        string accountId,
        string packageName,
        string trackId,
        Action<string>? reportProgress,
        CancellationToken cancellationToken) =>
        UpdateTrackAsync(
            accountId,
            packageName,
            trackId,
            releases: [],
            reportProgress,
            cancellationToken,
            successMessage: $"The release was removed from {trackId}. Testers on that track now receive the production build.");

    /// <summary>
    /// Halts a release on a track. A halted release is no longer served to new users, and the
    /// previous completed release takes its place. A first release on a track cannot be halted,
    /// because there would be nothing to fall back to.
    /// </summary>
    public Task<GooglePlayPublishResult> HaltReleaseAsync(
        string accountId,
        string packageName,
        string trackId,
        string releaseName,
        IReadOnlyList<string> versionCodes,
        Action<string>? reportProgress,
        CancellationToken cancellationToken) =>
        UpdateTrackAsync(
            accountId,
            packageName,
            trackId,
            releases:
            [
                new GooglePlayTrackRelease(
                    releaseName,
                    versionCodes,
                    GooglePlayTrackReleaseStatus.Halted,
                    null)
            ],
            reportProgress,
            cancellationToken,
            successMessage: $"The release on {trackId} was halted. It is no longer served to new users.");

    /// <summary>
    /// Changes the rollout of a release that is already on a track. A staged rollout is expressed
    /// as a fraction of the eligible users; a full rollout completes the release.
    /// </summary>
    public Task<GooglePlayPublishResult> UpdateRolloutAsync(
        string accountId,
        string packageName,
        string trackId,
        string releaseName,
        IReadOnlyList<string> versionCodes,
        int rolloutPercentage,
        IReadOnlyList<GooglePlayReleaseNote> releaseNotes,
        Action<string>? reportProgress,
        CancellationToken cancellationToken)
    {
        if (rolloutPercentage is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(rolloutPercentage), "Rollout must be between 1 and 100 percent.");
        }

        var status = rolloutPercentage >= 100
            ? GooglePlayTrackReleaseStatus.Completed
            : GooglePlayTrackReleaseStatus.InProgress;
        double? userFraction = rolloutPercentage >= 100
            ? null
            : (double)rolloutPercentage / 100d;

        return UpdateTrackAsync(
            accountId,
            packageName,
            trackId,
            releases:
            [
                new GooglePlayTrackRelease(releaseName, versionCodes, status, userFraction, releaseNotes)
            ],
            reportProgress,
            cancellationToken,
            successMessage: $"The rollout on {trackId} is now {rolloutPercentage}%.");
    }

    /// <summary>
    /// Copies a release from one track to another, which is how a tested build moves to the
    /// production line. The version code stays the same, so the bundle is not uploaded again.
    /// </summary>
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
        CancellationToken cancellationToken)
    {
        if (string.Equals(sourceTrackId, targetTrackId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The source and target tracks are the same.");
        }

        if (versionCodes.Count == 0)
        {
            throw new InvalidOperationException("The release has no version code to promote.");
        }

        if (rolloutPercentage is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(rolloutPercentage), "Rollout must be between 1 and 100 percent.");
        }

        var status = rolloutPercentage >= 100
            ? GooglePlayTrackReleaseStatus.Completed
            : GooglePlayTrackReleaseStatus.InProgress;
        double? userFraction = rolloutPercentage >= 100
            ? null
            : (double)rolloutPercentage / 100d;

        return UpdateTrackAsync(
            accountId,
            packageName,
            targetTrackId,
            releases:
            [
                new GooglePlayTrackRelease(releaseName, versionCodes, status, userFraction, releaseNotes)
            ],
            reportProgress,
            cancellationToken,
            successMessage: $"The release was promoted to {targetTrackId}.");
    }

    /// <summary>
    /// Creates a closed testing track. This is the only track kind the Play API can create; the
    /// default tracks always exist.
    /// </summary>
    public Task<GooglePlayPublishResult> CreateClosedTestingTrackAsync(
        string accountId,
        string packageName,
        string trackName,
        Action<string>? reportProgress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(trackName))
        {
            throw new InvalidOperationException("A track name is required.");
        }

        return ExecuteAsync(accountId, cancellationToken, async accessToken =>
        {
            reportProgress?.Invoke($"Creating the closed testing track {trackName}");
            var edit = await CreateEditAsync(accessToken, packageName, cancellationToken);
            try
            {
                using var response = await SendAsync(
                    HttpMethod.Post,
                    $"{BaseUrl}/applications/{Uri.EscapeDataString(packageName)}/edits/{edit}/tracks",
                    accessToken,
                    cancellationToken,
                    JsonSerializer.Serialize(
                        new GooglePlayTrackConfigRequest(trackName.Trim(), "CLOSED_TESTING", "DEFAULT"),
                        GooglePlayJsonContext.Default.GooglePlayTrackConfigRequest));
                await EnsureSuccessAsync(response, $"Could not create the closed testing track {trackName}", cancellationToken);

                reportProgress?.Invoke("Validating the new track");
                using (var validateResponse = await SendAsync(
                           HttpMethod.Post,
                           $"{BaseUrl}/applications/{Uri.EscapeDataString(packageName)}/edits/{edit}:validate",
                           accessToken,
                           cancellationToken))
                {
                    await EnsureSuccessAsync(validateResponse, "Google Play rejected the new track", cancellationToken);
                }

                reportProgress?.Invoke("Committing the new track");
                using var commitRequest = new HttpRequestMessage(
                    HttpMethod.Post,
                    $"{BaseUrl}/applications/{Uri.EscapeDataString(packageName)}/edits/{edit}:commit?changesInReviewBehavior=ERROR_IF_IN_REVIEW")
                {
                    Content = new StringContent(string.Empty, Encoding.UTF8, "application/json")
                };
                commitRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                using var commitResponse = await _httpClient.SendAsync(commitRequest, cancellationToken);
                await EnsureSuccessAsync(commitResponse, "Google Play did not accept the new track", cancellationToken);

                return new GooglePlayPublishResult(
                    true,
                    GooglePlayPublishStage.Published,
                    $"The closed testing track {trackName} was created.",
                    trackName,
                    trackName);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Creating the closed testing track {trackName} failed. The temporary edit {edit} was kept; retry or let it expire.",
                    ex);
            }
        });
    }

    /// <summary>
    /// Reads the version codes that are already used on a track, so a new release can use a
    /// higher one. Play refuses a version code that was used anywhere on the app.
    /// </summary>
    public Task<IReadOnlyList<string>> GetUsedVersionCodesAsync(
        string accountId,
        string packageName,
        CancellationToken cancellationToken) =>
        ExecuteAsync(accountId, cancellationToken, async accessToken =>
        {
            var edit = await CreateEditAsync(accessToken, packageName, cancellationToken);
            try
            {
                using var response = await SendAsync(
                    HttpMethod.Get,
                    $"{BaseUrl}/applications/{Uri.EscapeDataString(packageName)}/edits/{edit}/tracks",
                    accessToken,
                    cancellationToken);
                await EnsureSuccessAsync(response, "Could not load the Google Play version codes", cancellationToken);
                await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
                var codes = ReadUsedVersionCodes(document.RootElement);
                await DeleteEditBestEffortAsync(accessToken, packageName, edit, CancellationToken.None);
                return codes;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Reading the Google Play version codes failed for edit {edit}. The temporary edit was kept; retry or let it expire.",
                    ex);
            }
        });

    /// <summary>
    /// Fetches the certificate the Play app signing key signed a version with. Comparing it with
    /// a local build tells the user whether Google Play will be able to update that build later.
    /// </summary>
    public Task<string?> GetAppSigningCertificateAsync(
        string accountId,
        string packageName,
        int versionCode,
        CancellationToken cancellationToken) =>
        ExecuteAsync(accountId, cancellationToken, async accessToken =>
        {
            using var response = await SendAsync(
                HttpMethod.Get,
                $"{BaseUrl}/applications/{Uri.EscapeDataString(packageName)}/generatedApks/{versionCode}",
                accessToken,
                cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }

            await EnsureSuccessAsync(response, "Could not read the Google Play signing certificate", cancellationToken);
            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
            return ReadAppSigningCertificate(document.RootElement);
        });

    private Task<GooglePlayPublishResult> UpdateTrackAsync(
        string accountId,
        string packageName,
        string trackId,
        IReadOnlyList<GooglePlayTrackRelease> releases,
        Action<string>? reportProgress,
        CancellationToken cancellationToken,
        string successMessage)
    {
        if (string.IsNullOrWhiteSpace(trackId))
        {
            throw new InvalidOperationException("A release track is required.");
        }

        return ExecuteAsync(accountId, cancellationToken, async accessToken =>
        {
            reportProgress?.Invoke($"Updating the {trackId} track");
            var edit = await CreateEditAsync(accessToken, packageName, cancellationToken);
            try
            {
                var payload = BuildTrackUpdatePayload(trackId, releases);
                using (var response = await SendAsync(
                           HttpMethod.Put,
                           $"{BaseUrl}/applications/{Uri.EscapeDataString(packageName)}/edits/{edit}/tracks/{Uri.EscapeDataString(trackId)}",
                           accessToken,
                           cancellationToken,
                           payload))
                {
                    await EnsureSuccessAsync(response, $"Could not update the {trackId} track", cancellationToken);
                }

                reportProgress?.Invoke("Validating the change");
                using (var validateResponse = await SendAsync(
                           HttpMethod.Post,
                           $"{BaseUrl}/applications/{Uri.EscapeDataString(packageName)}/edits/{edit}:validate",
                           accessToken,
                           cancellationToken))
                {
                    await EnsureSuccessAsync(validateResponse, "Google Play rejected the change", cancellationToken);
                }

                reportProgress?.Invoke("Committing the change");
                using var commitRequest = new HttpRequestMessage(
                    HttpMethod.Post,
                    $"{BaseUrl}/applications/{Uri.EscapeDataString(packageName)}/edits/{edit}:commit?changesInReviewBehavior=ERROR_IF_IN_REVIEW")
                {
                    Content = new StringContent(string.Empty, Encoding.UTF8, "application/json")
                };
                commitRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                using var commitResponse = await _httpClient.SendAsync(commitRequest, cancellationToken);
                await EnsureSuccessAsync(commitResponse, "Google Play did not accept the change", cancellationToken);

                return new GooglePlayPublishResult(
                    true,
                    GooglePlayPublishStage.Published,
                    successMessage,
                    trackId,
                    trackId);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Updating the {trackId} track failed. The temporary edit {edit} was kept; retry or let it expire.",
                    ex);
            }
        });
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

    public static string BuildTrackUpdatePayload(
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

        var status = rolloutPercentage >= 100
            ? GooglePlayTrackReleaseStatus.Completed
            : GooglePlayTrackReleaseStatus.InProgress;
        double? userFraction = rolloutPercentage >= 100
            ? null
            : (double)rolloutPercentage / 100d;

        return BuildTrackUpdatePayload(
            trackId,
            [new GooglePlayTrackRelease(releaseName, [versionCode.ToString()], status, userFraction, releaseNotes)]);
    }

    /// <summary>
    /// Builds the body of an <c>edits.tracks.update</c> call. The releases array is the desired
    /// state of the track, so an empty array removes the releases from it.
    /// </summary>
    public static string BuildTrackUpdatePayload(string trackId, IReadOnlyList<GooglePlayTrackRelease> releases)
    {
        if (string.IsNullOrWhiteSpace(trackId))
        {
            throw new InvalidOperationException("A release track is required.");
        }

        var request = new GooglePlayTrackUpdateRequest
        {
            Track = trackId,
            Releases = releases
                .Select(release => new GooglePlayReleaseUpdateRequest
                {
                    Name = release.Name,
                    VersionCodes = [.. release.VersionCodes],
                    Status = release.Status,
                    UserFraction = release.UserFraction,
                    ReleaseNotes = (release.ReleaseNotes ?? [])
                        .Select(note => new GooglePlayReleaseNoteUpdateRequest
                        {
                            Language = note.Language,
                            Text = note.Text
                        })
                        .ToArray()
                })
                .ToArray()
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
            var status = "Not configured";
            IReadOnlyList<string> versionCodes = [];

            if (track.TryGetProperty("releases", out var releases) &&
                releases.ValueKind == JsonValueKind.Array &&
                releases.GetArrayLength() > 0)
            {
                var active = releases[0];
                status = active.TryGetProperty("status", out var releaseStatus)
                    ? releaseStatus.GetString() ?? "Unknown"
                    : "Unknown";
                versionCodes = ReadVersionCodes(active);
            }

            return GooglePlayTrackCatalog.Describe(id, status, versionCodes);
        }).OrderBy(track => track.Group).ThenBy(track => track.DisplayName).ToArray();
    }

    /// <summary>Reads the version codes of one release, highest first.</summary>
    private static IReadOnlyList<string> ReadVersionCodes(JsonElement release)
    {
        if (!release.TryGetProperty("versionCodes", out var versionCodes) ||
            versionCodes.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return versionCodes.EnumerateArray()
            .Select(code => code.ValueKind == JsonValueKind.String ? code.GetString() : code.ToString())
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code!)
            .OrderByDescending(code => long.TryParse(code, out var value) ? value : long.MinValue)
            .ToArray();
    }

    /// <summary>Reads every version code already used on the app, highest first.</summary>
    private static IReadOnlyList<string> ReadUsedVersionCodes(JsonElement root)
    {
        if (!root.TryGetProperty("tracks", out var tracks) || tracks.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var codes = new List<string>();
        foreach (var track in tracks.EnumerateArray())
        {
            if (!track.TryGetProperty("releases", out var releases) || releases.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var release in releases.EnumerateArray())
            {
                codes.AddRange(ReadVersionCodes(release));
            }
        }

        return codes
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(code => long.TryParse(code, out var value) ? value : long.MinValue)
            .ToArray();
    }

    /// <summary>
    /// Reads the SHA-256 certificate the Play app signing key signed a version with. Play signs
    /// the delivered app with the app signing key, not the upload key, so this is the certificate a
    /// local build has to match for Google Play to be able to update it later.
    /// </summary>
    private static string? ReadAppSigningCertificate(JsonElement root)
    {
        if (!root.TryGetProperty("generatedApks", out var generated) || generated.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var entry in generated.EnumerateArray())
        {
            if (entry.TryGetProperty("certificateSha256Hash", out var certificate) &&
                certificate.ValueKind == JsonValueKind.String)
            {
                var value = certificate.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }
        }

        return null;
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
        throw new InvalidOperationException(
            $"{context} ({(int)response.StatusCode}): {message}{ExplainPlayError(message)}");
    }

    /// <summary>
    /// Adds the next step for the errors that regularly block a release, because the bare Google
    /// message does not say what to do about it.
    /// </summary>
    private static string ExplainPlayError(string message)
    {
        if (message.Contains("already been used", StringComparison.OrdinalIgnoreCase))
        {
            return " That version code is used by this app on some line already, and Google Play " +
                "never reuses one. Raise the version code of the build and publish again.";
        }

        if (message.Contains("CHANGES_ALREADY_IN_REVIEW", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("changes in review", StringComparison.OrdinalIgnoreCase))
        {
            return " A previous change is still in review. Wait for it to finish, or cancel it in the " +
                "Play Console before publishing again.";
        }

        if (message.Contains("multiApkShadowedActiveApk", StringComparison.OrdinalIgnoreCase))
        {
            return " A higher version code already exists on another line, so this one is never served. " +
                "Publish a higher version code, or remove the higher release from the other line.";
        }

        if (message.Contains("required permission", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("403", StringComparison.OrdinalIgnoreCase))
        {
            return " The account may not have permission for this app. Check that the Google account is " +
                "an app owner or tester with release access.";
        }

        return string.Empty;
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

public sealed record GooglePlayReleaseNote(string Language, string Text);

/// <summary>The release states a Play track release can be in.</summary>
public static class GooglePlayTrackReleaseStatus
{
    /// <summary>The release's bundles are served to a fraction of the users.</summary>
    public const string InProgress = "inProgress";

    /// <summary>The release is served to everyone and will not change further.</summary>
    public const string Completed = "completed";

    /// <summary>The release is no longer served. The previous release takes its place.</summary>
    public const string Halted = "halted";

    /// <summary>The release is not served to anyone yet.</summary>
    public const string Draft = "draft";
}

/// <summary>One release on a track, as the Play API describes it.</summary>
/// <param name="Name">The release name. Play only shows it in the Play Console.</param>
/// <param name="VersionCodes">The version codes in the release.</param>
/// <param name="Status">The release state.</param>
/// <param name="UserFraction">The fraction of users a staged rollout serves. Null means the
/// release is complete.</param>
/// <param name="ReleaseNotes">The release notes, one per language.</param>
public sealed record GooglePlayTrackRelease(
    string Name,
    IReadOnlyList<string> VersionCodes,
    string Status,
    double? UserFraction = null,
    IReadOnlyList<GooglePlayReleaseNote>? ReleaseNotes = null);
