using DotNetAppPublisher.Features.GooglePlay.Models;

namespace DotNetAppPublisher.Features.GooglePlay.Authentication;

internal interface IGoogleCredentialStore
{
    GooglePlayAccountInfo Add(GoogleOAuthTokens tokens, GooglePlayAccountInfo account);

    Task<string?> GetAccessTokenAsync(string accountId, CancellationToken cancellationToken);

    Task RevokeAsync(string accountId, CancellationToken cancellationToken);

    void Forget(string accountId);
}
