using CodePublisher.Features.Publishing.Bundle;

namespace CodePublisher.Tests.Features.Publishing;

public sealed class MacAppBundleServiceTests
{
    [Test]
    public async Task FindBundleRoot_ReturnsTheBundleForAProjectNamedBundle()
    {
        var output = Path.Combine("out", "publish", "MyApp.app", "Contents", "MacOS");

        var root = MacAppBundleService.FindBundleRoot(output);

        await Assert.That(root).IsEqualTo(Path.GetFullPath(Path.Combine("out", "publish", "MyApp.app")));
    }

    [Test]
    public async Task FindBundleRoot_ReturnsTheBundleWhenItIsNamedDotApp()
    {
        var output = Path.Combine("out", ".app", "Contents", "MacOS");

        var root = MacAppBundleService.FindBundleRoot(output);

        await Assert.That(root).IsEqualTo(Path.GetFullPath(Path.Combine("out", ".app")));
    }

    [Test]
    public async Task FindBundleRoot_ReturnsNullForAPlainPublishFolder()
    {
        var root = MacAppBundleService.FindBundleRoot(Path.Combine("out", "publish"));

        await Assert.That(root).IsNull();
    }

    [Test]
    public async Task FindBundleRoot_ReturnsNullWhenContentsHasNoBundleOwner()
    {
        var root = MacAppBundleService.FindBundleRoot(Path.Combine("out", "Tools", "Contents", "MacOS"));

        await Assert.That(root).IsNull();
    }
}
