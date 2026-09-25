using DotNetAppPublisher.Features.GooglePlay.Models;

namespace DotNetAppPublisher.Features.GooglePlay.Authentication;

internal sealed class GoogleAuthenticationWorkflow
{
    private readonly GoogleOAuthService _oauthService;
    private readonly IGoogleCredentialStore _credentialStore;

    public GoogleAuthenticationWorkflow(
        GoogleOAuthService oauthService,
        IGoogleCredentialStore credentialStore)
    {
        _oauthService = oauthService;
        _credentialStore = credentialStore;
    }

    public async Task<GooglePlayAccountInfo> SignInAsync(CancellationToken cancellationToken)
    {
        var result = await _oauthService.SignInAsync(includeCloudAdmin: false, cancellationToken);
        return _credentialStore.Add(result.Tokens, result.Account);
    }

    public Task RevokeAsync(string accountId, CancellationToken cancellationToken) =>
        _credentialStore.RevokeAsync(accountId, cancellationToken);

    public void Forget(string accountId) => _credentialStore.Forget(accountId);
}
