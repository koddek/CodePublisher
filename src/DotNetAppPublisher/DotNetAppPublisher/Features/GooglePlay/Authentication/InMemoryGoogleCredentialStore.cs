using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;
using DotNetAppPublisher.Features.GooglePlay.Models;

namespace DotNetAppPublisher.Features.GooglePlay.Authentication;

internal sealed class InMemoryGoogleCredentialStore : IGoogleCredentialStore
{
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private readonly HttpClient _httpClient;
    private readonly Func<string> _clientIdProvider;
    private readonly ConcurrentDictionary<string, Credential> _credentials = new(StringComparer.Ordinal);

    public InMemoryGoogleCredentialStore(HttpClient httpClient, Func<string> clientIdProvider)
    {
        _httpClient = httpClient;
        _clientIdProvider = clientIdProvider;
    }

    public Task<string?> GetAccessTokenAsync(string accountId, CancellationToken cancellationToken)
    {
        if (!_credentials.TryGetValue(accountId, out var credential))
        {
            return Task.FromResult<string?>(null);
        }

        if (credential.AccessTokenExpiry > DateTimeOffset.UtcNow.AddMinutes(1) && !string.IsNullOrWhiteSpace(credential.AccessToken))
        {
            return Task.FromResult<string?>(credential.AccessToken);
        }

        return RefreshAsync(accountId, credential, cancellationToken);
    }

    public async Task RevokeAsync(string accountId, CancellationToken cancellationToken)
    {
        if (!_credentials.TryGetValue(accountId, out var credential))
        {
            return;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/revoke")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["token"] = credential.RefreshToken
                })
            };
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Google returned {(int)response.StatusCode} while revoking the session.");
            }
        }
        finally
        {
            Forget(accountId);
        }
    }

    public void Forget(string accountId)
    {
        if (_credentials.TryRemove(accountId, out var credential))
        {
            credential.AccessToken = null;
            credential.RefreshToken = string.Empty;
        }
    }

    GooglePlayAccountInfo IGoogleCredentialStore.Add(GoogleOAuthTokens tokens, GooglePlayAccountInfo account)
    {
        _credentials[account.Id] = new Credential
        {
            RefreshToken = tokens.RefreshToken,
            AccessToken = tokens.AccessToken,
            AccessTokenExpiry = tokens.AccessTokenExpiry
        };
        return account;
    }

    private async Task<string?> RefreshAsync(
        string accountId,
        Credential credential,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _clientIdProvider(),
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = credential.RefreshToken
            })
        };
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            Forget(accountId);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        credential.AccessToken = root.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Google did not return a refreshed access token.");
        credential.AccessTokenExpiry = DateTimeOffset.UtcNow.AddSeconds(
            root.TryGetProperty("expires_in", out var expiresIn) ? expiresIn.GetInt32() : 3600);
        return credential.AccessToken;
    }

    private sealed class Credential
    {
        public string RefreshToken { get; set; } = string.Empty;
        public string? AccessToken { get; set; }
        public DateTimeOffset AccessTokenExpiry { get; set; }
    }
}

internal sealed record GoogleOAuthTokens(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiry);
