using CodePublisher.Features.NuGet.Feeds;
using CodePublisher.Features.NuGet.Models;

namespace CodePublisher.Tests.Features.NuGet;

public sealed class NuGetFeedCatalogTests
{
    [Test]
    public async Task Targets_OfferNuGetOrg_GitHubPackages_AndLocalFolder()
    {
        var kinds = NuGetFeedCatalog.Targets.Select(target => target.Kind).ToArray();

        using (Assert.Multiple())
        {
            await Assert.That(kinds).Contains(NuGetFeedKind.NuGetOrg);
            await Assert.That(kinds).Contains(NuGetFeedKind.GitHubPackages);
            await Assert.That(kinds).Contains(NuGetFeedKind.LocalFolder);
            await Assert.That(NuGetFeedCatalog.DefaultTarget.Kind).IsEqualTo(NuGetFeedKind.NuGetOrg);
        }
    }

    [Test]
    public async Task GitHubPackages_BuildsOwnerScopedServiceIndexUrl()
    {
        var target = NuGetFeedCatalog.Targets.First(t => t.Kind == NuGetFeedKind.GitHubPackages);

        var url = NuGetFeedCatalog.GetServiceIndexUrl(target, "My-Org");

        await Assert.That(url).IsEqualTo("https://nuget.pkg.github.com/My-Org/index.json");
    }

    [Test]
    public async Task NuGetOrg_UsesThePublicV3Index()
    {
        var target = NuGetFeedCatalog.Targets.First(t => t.Kind == NuGetFeedKind.NuGetOrg);

        await Assert.That(NuGetFeedCatalog.GetPushSource(target, string.Empty))
            .IsEqualTo("https://api.nuget.org/v3/index.json");
    }

    [Test]
    public async Task GitHubPackages_RejectsInvalidOwner()
    {
        var target = NuGetFeedCatalog.Targets.First(t => t.Kind == NuGetFeedKind.GitHubPackages);

        using (Assert.Multiple())
        {
            await Assert.That(async () => await Task.Run(() => NuGetFeedCatalog.GetServiceIndexUrl(target, "-bad-")))
                .Throws<InvalidOperationException>();
            await Assert.That(async () => await Task.Run(() => NuGetFeedCatalog.GetServiceIndexUrl(target, "has space")))
                .Throws<InvalidOperationException>();
            await Assert.That(async () => await Task.Run(() => NuGetFeedCatalog.GetServiceIndexUrl(target, string.Empty)))
                .Throws<InvalidOperationException>();
        }
    }

    [Test]
    public async Task ValidatePackageId_AcceptsNormalIds_AndRejectsBadOnes()
    {
        using (Assert.Multiple())
        {
            await Assert.That(NuGetFeedCatalog.ValidatePackageId("MyCompany.MyLibrary")).IsNull();
            await Assert.That(NuGetFeedCatalog.ValidatePackageId("A")).IsNull();
            await Assert.That(NuGetFeedCatalog.ValidatePackageId("")).IsNotNull();
            await Assert.That(NuGetFeedCatalog.ValidatePackageId(".starts-with-dot")).IsNotNull();
            await Assert.That(NuGetFeedCatalog.ValidatePackageId("ends-with-dot.")).IsNotNull();
            await Assert.That(NuGetFeedCatalog.ValidatePackageId("has space")).IsNotNull();
            await Assert.That(NuGetFeedCatalog.ValidatePackageId(new string('a', 101))).IsNotNull();
        }
    }

    [Test]
    public async Task ValidateVersion_AcceptsSemVer2_AndRejectsNonSemVer()
    {
        using (Assert.Multiple())
        {
            await Assert.That(NuGetFeedCatalog.ValidateVersion("1.0.0")).IsNull();
            await Assert.That(NuGetFeedCatalog.ValidateVersion("1.2.3-beta.4")).IsNull();
            await Assert.That(NuGetFeedCatalog.ValidateVersion("1.0.0+build.5")).IsNull();
            await Assert.That(NuGetFeedCatalog.ValidateVersion("1.0")).IsNotNull();
            await Assert.That(NuGetFeedCatalog.ValidateVersion("01.0.0")).IsNotNull();
            await Assert.That(NuGetFeedCatalog.ValidateVersion("")).IsNotNull();
            await Assert.That(NuGetFeedCatalog.ValidateVersion("v1.0.0")).IsNotNull();
        }
    }

    [Test]
    public async Task NormalizeIdForUrl_LowercasesTheId()
    {
        await Assert.That(NuGetFeedCatalog.NormalizeIdForUrl("MyCompany.Lib")).IsEqualTo("mycompany.lib");
    }
}
