using CodePublisher.Features.NuGet.Feeds;
using CodePublisher.Features.NuGet.Models;
using CodePublisher.Features.NuGet.Services;

namespace CodePublisher.Tests.Features.NuGet;

public sealed class NuGetCredentialDetectionTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""{ "versions": [] }""")
            });
    }

    private static NuGetPackageService CreateService(
        NuGetProcessRunner? runner = null,
        Func<string, string?>? env = null) =>
        new("/dotnet", new NuGetFeedClient(new HttpClient(new StubHandler())), runner, env);

    [Test]
    public async Task DetectApiKey_GitHub_PrefersGhTokenEnv()
    {
        var service = CreateService(env: name => name == "GH_TOKEN" ? "ghp_env" : null);

        var detected = await service.DetectApiKeyAsync(NuGetFeedKind.GitHubPackages, CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(detected).IsNotNull();
            await Assert.That(detected!.Token).IsEqualTo("ghp_env");
            await Assert.That(detected.Source).Contains("GH_TOKEN");
        }
    }

    [Test]
    public async Task DetectApiKey_GitHub_FallsBackToGhCli()
    {
        var calls = new List<IReadOnlyList<string>>();
        var service = CreateService(
            runner: (args, write, ct, wd) =>
            {
                calls.Add(args);
                write(args is ["gh", "auth", "token"] ? "ghp_from_cli\n" : string.Empty);
                return Task.FromResult(0);
            },
            env: _ => null);

        var detected = await service.DetectApiKeyAsync(NuGetFeedKind.GitHubPackages, CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(detected!.Token).IsEqualTo("ghp_from_cli");
            await Assert.That(detected.Source).Contains("gh");
            await Assert.That(calls[0]).Contains("auth");
        }
    }

    [Test]
    public async Task DetectApiKey_NuGetOrg_UsesNuGetApiKey_AndNeverCallsGh()
    {
        var calls = new List<IReadOnlyList<string>>();
        var service = CreateService(
            runner: (args, write, ct, wd) =>
            {
                calls.Add(args);
                return Task.FromResult(1);
            },
            env: name => name == "NUGET_API_KEY" ? "nuget-key" : null);

        var detected = await service.DetectApiKeyAsync(NuGetFeedKind.NuGetOrg, CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(detected!.Token).IsEqualTo("nuget-key");
            await Assert.That(calls).IsEmpty();
        }
    }

    [Test]
    public async Task DetectApiKey_NothingConfigured_ReturnsNull()
    {
        var service = CreateService(
            runner: (args, write, ct, wd) => Task.FromResult(1),
            env: _ => null);

        var detected = await service.DetectApiKeyAsync(NuGetFeedKind.GitHubPackages, CancellationToken.None);

        await Assert.That(detected).IsNull();
    }

    [Test]
    public async Task DetectGitHubOwner_ReadsProjectFile()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cp-owner-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            var projectFile = Path.Combine(root, "Lib.csproj");
            File.WriteAllText(
                projectFile,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><PackageProjectUrl>git@github.com:omoi/Lib.git</PackageProjectUrl></PropertyGroup></Project>");

            await Assert.That(NuGetPackageService.DetectGitHubOwner(projectFile, root)).IsEqualTo("omoi");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
