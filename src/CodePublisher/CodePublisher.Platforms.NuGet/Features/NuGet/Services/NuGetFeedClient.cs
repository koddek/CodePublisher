using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using CodePublisher.Features.NuGet.Feeds;
using CodePublisher.Features.NuGet.Models;

namespace CodePublisher.Features.NuGet.Services;

/// <summary>Thrown when a feed answers with an error, so the UI can show the feed's own message.</summary>
public sealed class NuGetFeedException : InvalidOperationException
{
    public NuGetFeedException(string message) : base(message)
    {
    }
}

/// <summary>
/// Lists package versions over the NuGet V3 protocol: read the service index, take the
/// PackageBaseAddress resource, then read the flat-container index for the package id.
/// nuget.org allows anonymous reads; GitHub Packages requires basic auth with a PAT
/// (any username, the token as password) even for public packages.
/// </summary>
public sealed class NuGetFeedClient
{
    private readonly HttpClient _httpClient;

    public NuGetFeedClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<NuGetFeedVersion>> ListVersionsAsync(
        NuGetFeedTarget target,
        string ownerOrFolder,
        string packageId,
        string? apiKey,
        CancellationToken cancellationToken)
    {
        if (target.Kind == NuGetFeedKind.LocalFolder)
        {
            return ListLocalVersions(target, ownerOrFolder, packageId);
        }

        var idError = NuGetFeedCatalog.ValidatePackageId(packageId);
        if (idError is not null)
        {
            throw new NuGetFeedException(idError);
        }

        var serviceIndexUrl = NuGetFeedCatalog.GetServiceIndexUrl(target, ownerOrFolder);
        var serviceIndexJson = await GetStringAsync(serviceIndexUrl, target, ownerOrFolder, apiKey, cancellationToken)
            .ConfigureAwait(false);
        var packageBaseAddress = NuGetVersionListParser.GetPackageBaseAddress(serviceIndexJson);
        if (string.IsNullOrWhiteSpace(packageBaseAddress))
        {
            throw new NuGetFeedException(
                $"The feed at {serviceIndexUrl} does not advertise a PackageBaseAddress resource, so versions cannot be listed.");
        }

        var flatIndexUrl = NuGetVersionListParser.BuildFlatContainerIndexUrl(packageBaseAddress, packageId);
        string flatIndexJson;
        try
        {
            flatIndexJson = await GetStringAsync(flatIndexUrl, target, ownerOrFolder, apiKey, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (NuGetFeedException exception) when (exception.Message.StartsWith("404", StringComparison.Ordinal))
        {
            // The package has never been pushed: an empty version list, not an error.
            return [];
        }

        return NuGetVersionListParser.ParseFlatContainerVersions(flatIndexJson)
            .Select(version => new NuGetFeedVersion(version, IsListed: true, PublishedAt: null))
            .OrderByDescending(version => version.Version, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<NuGetFeedVersion> ListLocalVersions(NuGetFeedTarget target, string folder, string packageId)
    {
        var normalized = NuGetFeedCatalog.NormalizeIdForUrl(packageId);
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(folder, "*.nupkg", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name is not null && name.StartsWith(normalized + ".", StringComparison.OrdinalIgnoreCase))
            .Select(name => name![normalized.Length..].TrimStart('.'))
            .Select(version => new NuGetFeedVersion(version, IsListed: true, PublishedAt: null))
            .OrderByDescending(version => version.Version, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<string> GetStringAsync(
        string url,
        NuGetFeedTarget target,
        string ownerOrFolder,
        string? apiKey,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (target.Kind == NuGetFeedKind.GitHubPackages)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new NuGetFeedException(
                    "GitHub Packages requires a personal access token with read:packages, even to list public packages.");
            }

            // GitHub Packages accepts any username with the PAT as the password.
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ownerOrFolder}:{apiKey}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var snippet = body.Length > 300 ? body[..300] + "..." : body;
            throw new NuGetFeedException(
                $"{(int)response.StatusCode} {response.ReasonPhrase} from {url}: {snippet}");
        }

        return body;
    }
}
