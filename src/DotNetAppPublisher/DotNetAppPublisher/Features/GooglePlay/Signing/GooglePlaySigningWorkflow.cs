using DotNetAppPublisher.Features.GooglePlay.Models;

namespace DotNetAppPublisher.Features.GooglePlay.Signing;

internal sealed class GooglePlaySigningWorkflow
{
    private readonly AndroidSigningKeyService _keyService;
    private readonly GooglePlayArtifactValidator _artifactValidator;

    public GooglePlaySigningWorkflow(
        AndroidSigningKeyService keyService,
        GooglePlayArtifactValidator artifactValidator)
    {
        _keyService = keyService;
        _artifactValidator = artifactValidator;
    }

    public Task<GooglePlaySigningKeyInfo> CreateAsync(
        string path,
        string alias,
        string storePassword,
        string keyPassword,
        string ownerName,
        CancellationToken cancellationToken) =>
        _keyService.CreateAsync(path, alias, storePassword, keyPassword, ownerName, cancellationToken);

    public Task<GooglePlaySigningKeyInfo> InspectAsync(
        string path,
        string alias,
        string storePassword,
        CancellationToken cancellationToken) =>
        _keyService.InspectAsync(path, alias, storePassword, cancellationToken);

    public string CreateStrongPassword() => _keyService.CreateStrongPassword();

    public Task<GooglePlayArtifactInfo> ValidateAsync(string aabPath, CancellationToken cancellationToken) =>
        _artifactValidator.ValidateAsync(aabPath, cancellationToken);
}
