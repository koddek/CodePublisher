using CodePublisher.Features.NuGet;
using CodePublisher.Features.NuGet.Feeds;
using CodePublisher.Features.NuGet.Models;
using CodePublisher.Features.NuGet.Services;
using CodePublisher.Services;

namespace CodePublisher.Tests.Features.NuGet;

public sealed class NuGetViewModelTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(
                    request.RequestUri!.AbsolutePath.Contains("flatcontainer")
                        ? """{ "versions": [] }"""
                        : """{ "version": "3.0.0", "resources": [ { "@id": "https://api.nuget.org/v3-flatcontainer/", "@type": "PackageBaseAddress" } ] }""")
            });
    }

    private static NuGetViewModel CreateViewModel() =>
        new(
            new PublisherService(),
            new DesktopInteractionService(),
            new NuGetPackageService("/dotnet", new NuGetFeedClient(new HttpClient(new StubHandler()))),
            _ => { });

    [Test]
    public async Task Pack_IsDisabled_UntilAProjectIsSelected()
    {
        var viewModel = CreateViewModel();

        using (Assert.Multiple())
        {
            await Assert.That(viewModel.PackCommand.CanExecute(null)).IsFalse();
            viewModel.UpdateContext("/proj", "/proj/Lib.csproj", "Lib", "1.2.3");
            await Assert.That(viewModel.PackCommand.CanExecute(null)).IsTrue();
        }
    }

    [Test]
    public async Task UpdateContext_PrefillsPackageFields_AndBuildsPackPreview()
    {
        var viewModel = CreateViewModel();

        viewModel.UpdateContext("/proj", "/proj/Lib.csproj", "My.Lib", "2.0.0");

        using (Assert.Multiple())
        {
            await Assert.That(viewModel.PackageId).IsEqualTo("My.Lib");
            await Assert.That(viewModel.PackageVersion).IsEqualTo("2.0.0");
            await Assert.That(viewModel.OutputDirectory).IsEqualTo(Path.Combine("/proj", "nupkgs"));
            await Assert.That(viewModel.PackCommandPreview).Contains("pack");
            await Assert.That(viewModel.PackCommandPreview).Contains("-p:PackageVersion=2.0.0");
        }
    }

    [Test]
    public async Task Push_RequiresPackedPackage_AndFeedCredentials()
    {
        var viewModel = CreateViewModel();
        viewModel.UpdateContext("/proj", "/proj/Lib.csproj", "Lib", "1.0.0");

        using (Assert.Multiple())
        {
            // Nothing packed yet.
            await Assert.That(viewModel.PushCommand.CanExecute(null)).IsFalse();

            // Pretend a pack succeeded.
            viewModel.PackedPackagePath = "/proj/nupkgs/Lib.1.0.0.nupkg";
            // NuGet.org is the default feed and pushing there requires an API key.
            await Assert.That(viewModel.PushCommand.CanExecute(null)).IsFalse();
            viewModel.ApiKey = "key";
            await Assert.That(viewModel.PushCommand.CanExecute(null)).IsTrue();
        }
    }

    [Test]
    public async Task GitHubFeed_RequiresOwnerAndKey_AndShowsBothFields()
    {
        var viewModel = CreateViewModel();
        viewModel.UpdateContext("/proj", "/proj/Lib.csproj", "Lib", "1.0.0");
        viewModel.PackedPackagePath = "/proj/nupkgs/Lib.1.0.0.nupkg";
        viewModel.SelectedFeed = NuGetFeedCatalog.Targets.First(t => t.Kind == NuGetFeedKind.GitHubPackages);

        using (Assert.Multiple())
        {
            await Assert.That(viewModel.IsFeedOwnerVisible).IsTrue();
            await Assert.That(viewModel.IsApiKeyVisible).IsTrue();
            await Assert.That(viewModel.FeedOwnerLabel).IsEqualTo("GitHub owner");
            await Assert.That(viewModel.PushCommand.CanExecute(null)).IsFalse();
            viewModel.FeedOwnerOrFolder = "omoi";
            await Assert.That(viewModel.PushCommand.CanExecute(null)).IsFalse();
            viewModel.ApiKey = "ghp_x";
            await Assert.That(viewModel.PushCommand.CanExecute(null)).IsTrue();
        }
    }

    [Test]
    public async Task LocalFolderFeed_NeedsNoKey_ButNeedsFolder()
    {
        var viewModel = CreateViewModel();
        viewModel.UpdateContext("/proj", "/proj/Lib.csproj", "Lib", "1.0.0");
        viewModel.PackedPackagePath = "/proj/nupkgs/Lib.1.0.0.nupkg";
        viewModel.SelectedFeed = NuGetFeedCatalog.Targets.First(t => t.Kind == NuGetFeedKind.LocalFolder);

        using (Assert.Multiple())
        {
            await Assert.That(viewModel.IsApiKeyVisible).IsFalse();
            await Assert.That(viewModel.IsFeedOwnerVisible).IsTrue();
            await Assert.That(viewModel.PushCommand.CanExecute(null)).IsFalse();
            viewModel.FeedOwnerOrFolder = "/tmp/feed";
            await Assert.That(viewModel.PushCommand.CanExecute(null)).IsTrue();
        }
    }

    [Test]
    public async Task RefreshVersions_PopulatesRows_AndBlocksDeleteWhileBusy()
    {
        var handler = new StubHandler();
        var viewModel = new NuGetViewModel(
            new PublisherService(),
            new DesktopInteractionService(),
            new NuGetPackageService("/dotnet", new NuGetFeedClient(new HttpClient(handler))),
            _ => { });
        viewModel.UpdateContext("/proj", "/proj/Lib.csproj", "Lib", "1.0.0");
        viewModel.SelectedFeed = NuGetFeedCatalog.Targets.First(t => t.Kind == NuGetFeedKind.LocalFolder);
        var folder = Path.Combine(Path.GetTempPath(), $"cp-vm-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "Lib.1.0.0.nupkg"), "x");
            viewModel.FeedOwnerOrFolder = folder;

            await viewModel.RefreshVersionsCommand.ExecuteAsync(null);

            using (Assert.Multiple())
            {
                await Assert.That(viewModel.HasFeedVersions).IsTrue();
                await Assert.That(viewModel.FeedVersions[0].Version).IsEqualTo("1.0.0");
                await Assert.That(viewModel.FeedStatus).Contains("1 version(s)");
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
