using System.Net;
using CodePublisher.Features.NuGet.Feeds;
using CodePublisher.Features.NuGet.Models;
using CodePublisher.Features.NuGet.Services;

namespace CodePublisher.Tests.Features.NuGet;

public sealed class NuGetFeedClientTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private const string ServiceIndex =
        """{ "version": "3.0.0", "resources": [ { "@id": "https://nuget.pkg.github.com/omoi/", "@type": "PackageBaseAddress" } ] }""";

    [Test]
    public async Task ListVersions_ReadsServiceIndex_ThenFlatContainer()
    {
        var handler = new StubHandler(request =>
        {
            var json = request.RequestUri!.AbsolutePath.EndsWith("/index.json", StringComparison.Ordinal)
                && request.RequestUri.AbsolutePath.Contains("my.lib")
                    ? """{ "versions": [ "1.0.0", "2.0.0-beta.1" ] }"""
                    : ServiceIndex;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
        });
        var client = new NuGetFeedClient(new HttpClient(handler));
        var target = NuGetFeedCatalog.Targets.First(t => t.Kind == NuGetFeedKind.GitHubPackages);

        var versions = await client.ListVersionsAsync(target, "omoi", "My.Lib", "ghp-token", CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(string.Join(",", versions.Select(v => v.Version))).IsEqualTo("2.0.0-beta.1,1.0.0");
            await Assert.That(handler.Requests[0].RequestUri!.AbsoluteUri)
                .IsEqualTo("https://nuget.pkg.github.com/omoi/index.json");
            await Assert.That(handler.Requests[1].RequestUri!.AbsoluteUri)
                .IsEqualTo("https://nuget.pkg.github.com/omoi/my.lib/index.json");
            // GitHub Packages requires basic auth with the PAT as the password.
            await Assert.That(handler.Requests[1].Headers.Authorization?.Scheme).IsEqualTo("Basic");
            await Assert.That(handler.Requests[1].Headers.Authorization?.Parameter)
                .IsEqualTo(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("omoi:ghp-token")));
        }
    }

    [Test]
    public async Task ListVersions_GitHubWithoutKey_ThrowsBeforeAnyRequest()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new NuGetFeedClient(new HttpClient(handler));
        var target = NuGetFeedCatalog.Targets.First(t => t.Kind == NuGetFeedKind.GitHubPackages);

        await Assert.That(client.ListVersionsAsync(target, "omoi", "My.Lib", null, CancellationToken.None))
            .Throws<NuGetFeedException>();
        await Assert.That(handler.Requests).IsEmpty();
    }

    [Test]
    public async Task ListVersions_UnknownPackageOnNuGetOrg_ReturnsEmpty()
    {
        var handler = new StubHandler(request =>
        {
            var isFlatIndex = request.RequestUri!.AbsolutePath.Contains("flatcontainer");
            return isFlatIndex
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{ "version": "3.0.0", "resources": [ { "@id": "https://api.nuget.org/v3-flatcontainer/", "@type": "PackageBaseAddress" } ] }""")
                };
        });
        var client = new NuGetFeedClient(new HttpClient(handler));
        var target = NuGetFeedCatalog.Targets.First(t => t.Kind == NuGetFeedKind.NuGetOrg);

        var versions = await client.ListVersionsAsync(target, string.Empty, "Never.Pushed", null, CancellationToken.None);

        // nuget.org reads are anonymous: no Authorization header is sent.
        using (Assert.Multiple())
        {
            await Assert.That(versions).IsEmpty();
            await Assert.That(handler.Requests[0].Headers.Authorization).IsNull();
        }
    }

    [Test]
    public async Task ListVersions_LocalFolder_ReadsNupkgFileNames()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"cp-feed-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "My.Lib.1.0.0.nupkg"), "x");
            File.WriteAllText(Path.Combine(folder, "My.Lib.2.0.0.nupkg"), "x");
            File.WriteAllText(Path.Combine(folder, "Other.9.9.9.nupkg"), "x");
            var client = new NuGetFeedClient(new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))));
            var target = NuGetFeedCatalog.Targets.First(t => t.Kind == NuGetFeedKind.LocalFolder);

            var versions = await client.ListVersionsAsync(target, folder, "My.Lib", null, CancellationToken.None);

            using (Assert.Multiple())
            {
                await Assert.That(string.Join(",", versions.Select(v => v.Version))).IsEqualTo("2.0.0,1.0.0");
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
