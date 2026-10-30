using CodePublisher.Features.NuGet.Feeds;

namespace CodePublisher.Tests.Features.NuGet;

public sealed class NuGetVersionListParserTests
{
    [Test]
    public async Task GetPackageBaseAddress_ReadsTheFlatContainerResource()
    {
        const string index =
            """
            {
              "version": "3.0.0",
              "resources": [
                { "@id": "https://api.nuget.org/v3/registration/", "@type": "RegistrationsBaseUrl" },
                { "@id": "https://api.nuget.org/v3-flatcontainer/", "@type": "PackageBaseAddress" }
              ]
            }
            """;

        var baseAddress = NuGetVersionListParser.GetPackageBaseAddress(index);

        await Assert.That(baseAddress).IsEqualTo("https://api.nuget.org/v3-flatcontainer/");
    }

    [Test]
    public async Task GetPackageBaseAddress_ReturnsNull_WhenResourceMissing()
    {
        const string index = """{ "version": "3.0.0", "resources": [] }""";

        await Assert.That(NuGetVersionListParser.GetPackageBaseAddress(index)).IsNull();
    }

    [Test]
    public async Task GetPackageBaseAddress_ThrowsOnInvalidJson()
    {
        await Assert.That(() => NuGetVersionListParser.GetPackageBaseAddress("not json"))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task BuildFlatContainerIndexUrl_LowercasesId_AndTrimsSlash()
    {
        var url = NuGetVersionListParser.BuildFlatContainerIndexUrl(
            "https://api.nuget.org/v3-flatcontainer/", "MyCompany.Lib");

        await Assert.That(url).IsEqualTo("https://api.nuget.org/v3-flatcontainer/mycompany.lib/index.json");
    }

    [Test]
    public async Task ParseFlatContainerVersions_ReadsAllVersions()
    {
        const string json = """{ "versions": [ "1.0.0", "1.1.0-beta.1", "2.0.0" ] }""";

        var versions = NuGetVersionListParser.ParseFlatContainerVersions(json);

        using (Assert.Multiple())
        {
            await Assert.That(versions.Count()).IsEqualTo(3);
            await Assert.That(versions).Contains("1.1.0-beta.1");
        }
    }

    [Test]
    public async Task ParseFlatContainerVersions_EmptyOrMalformedShape_ReturnsEmpty()
    {
        using (Assert.Multiple())
        {
            await Assert.That(NuGetVersionListParser.ParseFlatContainerVersions("""{ "versions": [] }""")).IsEmpty();
            await Assert.That(NuGetVersionListParser.ParseFlatContainerVersions("""{ }""")).IsEmpty();
            await Assert.That(NuGetVersionListParser.ParseFlatContainerVersions("""[1,2]""")).IsEmpty();
        }
    }
}
