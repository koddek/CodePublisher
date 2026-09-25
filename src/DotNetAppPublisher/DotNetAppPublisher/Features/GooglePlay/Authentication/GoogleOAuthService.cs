using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotNetAppPublisher.Features.GooglePlay.Models;

namespace DotNetAppPublisher.Features.GooglePlay.Authentication;

internal sealed class GoogleOAuthService
{
    private const string AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string UserInfoEndpoint = "https://openidconnect.googleapis.com/v1/userinfo";
    private const string CallbackPath = "/oauth2callback";
    private const string AndroidPublisherScope = "https://www.googleapis.com/auth/androidpublisher";
    private const string CloudPlatformScope = "https://www.googleapis.com/auth/cloud-platform";
    private readonly HttpClient _httpClient;
    private readonly Func<string> _clientIdProvider;

    public GoogleOAuthService(HttpClient httpClient, Func<string> clientIdProvider)
    {
        _httpClient = httpClient;
        _clientIdProvider = clientIdProvider;
    }

    internal async Task<GoogleOAuthResult> SignInAsync(
        bool includeCloudAdmin,
        CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsBrowser())
        {
            throw new PlatformNotSupportedException("Google sign-in is available in the desktop app only.");
        }

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var clientId = _clientIdProvider();
        if (string.IsNullOrWhiteSpace(clientId))
        {
            listener.Stop();
            throw new InvalidOperationException("A Google Desktop OAuth client ID is required before sign-in.");
        }
        var redirectUri = $"http://127.0.0.1:{port}{CallbackPath}";
        var state = CreateRandomUrlSafeValue();
        var verifier = CreateRandomUrlSafeValue(64);
        var challenge = Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var scope = includeCloudAdmin
            ? new[] { "openid", "email", "profile", AndroidPublisherScope, CloudPlatformScope }
            : new[] { "openid", "email", "profile", AndroidPublisherScope };
        var scopeValue = string.Join(' ', scope);

        var authorizationUrl =
            $"{AuthorizationEndpoint}?client_id={Uri.EscapeDataString(clientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
            $"&response_type=code&scope={Uri.EscapeDataString(scopeValue)}" +
            $"&state={Uri.EscapeDataString(state)}&code_challenge={Uri.EscapeDataString(challenge)}" +
            "&code_challenge_method=S256&access_type=offline&prompt=select_account%20consent";

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            if (OperatingSystem.IsBrowser())
            {
                throw new PlatformNotSupportedException("Google sign-in is available in the desktop app only.");
            }

            _ = Process.Start(new ProcessStartInfo(authorizationUrl) { UseShellExecute = true })
                ?? throw new InvalidOperationException("The browser could not be opened for Google sign-in.");

            var callback = await ReceiveCallbackAsync(listener, port, timeoutCts.Token);
            if (!string.IsNullOrWhiteSpace(callback.ErrorCode) || !string.IsNullOrWhiteSpace(callback.Error))
            {
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(callback.Error)
                        ? $"Google sign-in was not completed: {callback.ErrorCode}"
                        : $"Google sign-in was not completed: {callback.Error}");
            }

            if (string.IsNullOrWhiteSpace(callback.Code))
            {
                throw new InvalidOperationException("Google sign-in did not return an authorization code.");
            }

            if (!string.Equals(callback.State, state, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Google sign-in returned an invalid state value.");
            }

            var tokens = await ExchangeCodeAsync(callback.Code!, verifier, redirectUri, clientId, timeoutCts.Token);
            var user = await GetUserInfoAsync(tokens.AccessToken, timeoutCts.Token);
            var hasCloudAdmin = scopeValue.Contains(CloudPlatformScope, StringComparison.Ordinal);
            var account = new GooglePlayAccountInfo(
                user.Subject,
                user.Email,
                string.IsNullOrWhiteSpace(user.Name) ? user.Email : user.Name,
                CreateInitials(user.Name, user.Email),
                hasCloudAdmin,
                tokens.AccessTokenExpiry,
                scope);
            return new GoogleOAuthResult(account, tokens);
        }
        finally
        {
            listener.Stop();
        }
    }

    private async Task<GoogleOAuthTokens> ExchangeCodeAsync(
        string code,
        string verifier,
        string redirectUri,
        string clientId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["code"] = code,
                ["code_verifier"] = verifier,
                ["grant_type"] = "authorization_code",
                ["redirect_uri"] = redirectUri
            })
        };
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var accessToken = root.GetProperty("access_token").GetString() ?? throw new InvalidOperationException("Google did not return an access token.");
        var refreshToken = root.TryGetProperty("refresh_token", out var refreshTokenElement)
            ? refreshTokenElement.GetString() ?? string.Empty
            : throw new InvalidOperationException("Google did not return a refresh token. Revoke access and sign in again.");
        var expiresIn = root.TryGetProperty("expires_in", out var expiresInElement)
            ? expiresInElement.GetInt32()
            : 3600;
        return new GoogleOAuthTokens(accessToken, refreshToken, DateTimeOffset.UtcNow.AddSeconds(expiresIn));
    }

    private async Task<GoogleUserInfo> GetUserInfoAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, UserInfoEndpoint);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        return new GoogleUserInfo(
            root.GetProperty("sub").GetString() ?? throw new InvalidOperationException("Google did not return an account ID."),
            root.TryGetProperty("email", out var email) ? email.GetString() ?? string.Empty : string.Empty,
            root.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty);
    }

    private static async Task<OAuthCallback> ReceiveCallbackAsync(TcpListener listener, int port, CancellationToken cancellationToken)
    {
        using var client = await listener.AcceptTcpClientAsync(cancellationToken);
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        var requestLine = await reader.ReadLineAsync(cancellationToken) ?? string.Empty;
        var parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !Uri.TryCreate(parts[1], UriKind.RelativeOrAbsolute, out var requestUri))
        {
            throw new InvalidOperationException("Google returned an invalid OAuth callback.");
        }

        var callbackUri = requestUri.IsAbsoluteUri
            ? requestUri
            : new Uri(new Uri($"http://127.0.0.1:{port}"), requestUri);
        if (!string.Equals(callbackUri.AbsolutePath, CallbackPath, StringComparison.Ordinal) ||
            !string.Equals(callbackUri.Host, "127.0.0.1", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Google returned an invalid OAuth callback.");
        }

        while (!string.IsNullOrEmpty(await reader.ReadLineAsync(cancellationToken)))
        {
        }

        var response = "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nConnection: close\r\n\r\n" +
                       "<!doctype html><html><body><h1>Signed in</h1><p>You can close this window and return to .NET App Publisher.</p></body></html>";
        var bytes = Encoding.UTF8.GetBytes(response);
        await stream.WriteAsync(bytes, cancellationToken);
        return new OAuthCallback(
            GetQueryValue(callbackUri, "error_description"),
            GetQueryValue(callbackUri, "code"),
            GetQueryValue(callbackUri, "state"),
            GetQueryValue(callbackUri, "error"));
    }

    private static string? GetQueryValue(Uri uri, string key)
    {
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && Uri.UnescapeDataString(parts[0]) == key)
            {
                return Uri.UnescapeDataString(parts[1].Replace('+', ' '));
            }
        }

        return null;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var message = TryReadError(body) ?? "Google authentication returned an unknown error.";
        throw new InvalidOperationException($"Google authentication failed ({(int)response.StatusCode}): {message}");
    }

    private static string? TryReadError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error_description", out var description))
            {
                return description.GetString();
            }
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                return error.ValueKind == JsonValueKind.String
                    ? error.GetString()
                    : error.TryGetProperty("message", out var message) ? message.GetString() : null;
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static string CreateRandomUrlSafeValue(int bytes = 32) =>
        Base64UrlEncode(RandomNumberGenerator.GetBytes(bytes));

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string CreateInitials(string? name, string email)
    {
        var source = string.IsNullOrWhiteSpace(name) ? email : name;
        var initials = string.Join("", source
            .Split([' ', '.', '-', '_'], StringSplitOptions.RemoveEmptyEntries)
            .Take(2)
            .Select(part => char.ToUpperInvariant(part[0])));
        return string.IsNullOrWhiteSpace(initials) ? "G" : initials;
    }

    internal sealed record GoogleOAuthResult(GooglePlayAccountInfo Account, GoogleOAuthTokens Tokens);
    private sealed record GoogleUserInfo(string Subject, string Email, string Name);
    private sealed record OAuthCallback(string? Error, string? Code, string? State, string? ErrorCode);
}
