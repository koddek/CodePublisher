using CodePublisher.Features.NuGet.Feeds;

namespace CodePublisher.Tests.Features.NuGet;

public sealed class GitHubOwnerDetectorTests
{
    [Test]
    public async Task ParseOwner_HandlesCommonGitHubUrlShapes()
    {
        using (Assert.Multiple())
        {
            await Assert.That(GitHubOwnerDetector.ParseOwner("https://github.com/omoi/Primify.git")).IsEqualTo("omoi");
            await Assert.That(GitHubOwnerDetector.ParseOwner("https://github.com/omoi/Primify")).IsEqualTo("omoi");
            await Assert.That(GitHubOwnerDetector.ParseOwner("https://user@github.com/my-org/primify.git")).IsEqualTo("my-org");
            await Assert.That(GitHubOwnerDetector.ParseOwner("git@github.com:omoi/Primify.git")).IsEqualTo("omoi");
            await Assert.That(GitHubOwnerDetector.ParseOwner("ssh://git@github.com/omoi/Primify.git")).IsEqualTo("omoi");
            await Assert.That(GitHubOwnerDetector.ParseOwner("https://github.com/omoi/Primify/")).IsEqualTo("omoi");
        }
    }

    [Test]
    public async Task ParseOwner_ReturnsNullForNonGitHub_OrInvalid()
    {
        using (Assert.Multiple())
        {
            await Assert.That(GitHubOwnerDetector.ParseOwner("https://gitlab.com/omoi/Primify.git")).IsNull();
            await Assert.That(GitHubOwnerDetector.ParseOwner("https://github.com/")).IsNull();
            await Assert.That(GitHubOwnerDetector.ParseOwner("not a url")).IsNull();
            await Assert.That(GitHubOwnerDetector.ParseOwner(null)).IsNull();
            await Assert.That(GitHubOwnerDetector.ParseOwner("")).IsNull();
            await Assert.That(GitHubOwnerDetector.ParseOwner("https://github.com/-bad-/x.git")).IsNull();
        }
    }

    [Test]
    public async Task GitConfigParser_ReadsTheNamedRemote()
    {
        const string config =
            """
            [core]
                repositoryformatversion = 0
            [remote "origin"]
                url = git@github.com:omoi/Primify.git
                fetch = +refs/heads/*:refs/remotes/origin/*
            [remote "upstream"]
                url = https://github.com/other/Primify.git
            """;

        using (Assert.Multiple())
        {
            await Assert.That(GitConfigParser.ParseRemoteUrl(config, "origin")).IsEqualTo("git@github.com:omoi/Primify.git");
            await Assert.That(GitConfigParser.ParseRemoteUrl(config, "upstream")).IsEqualTo("https://github.com/other/Primify.git");
            await Assert.That(GitConfigParser.ParseRemoteUrl(config, "missing")).IsNull();
            await Assert.That(GitConfigParser.ParseRemoteUrl("", "origin")).IsNull();
        }
    }

    [Test]
    public async Task RepositoryReader_PrefersCsprojUrl_ThenGitRemote_WalkingUp()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cp-owner-{Guid.NewGuid():N}");
        try
        {
            var projectDir = Path.Combine(root, "src", "MainLib");
            Directory.CreateDirectory(projectDir);
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            File.WriteAllText(
                Path.Combine(root, ".git", "config"),
                "[remote \"origin\"]\n\turl = https://github.com/remote-owner/Primify.git\n".Replace("\\t", "\t"));
            var projectFile = Path.Combine(projectDir, "MainLib.csproj");
            File.WriteAllText(
                projectFile,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><RepositoryUrl>https://github.com/csproj-owner/Primify</RepositoryUrl></PropertyGroup></Project>");

            using (Assert.Multiple())
            {
                // The csproj repository URL wins over the git remote.
                await Assert.That(NuGetProjectRepositoryReader.FindGitHubOwner(projectFile, root)).IsEqualTo("csproj-owner");
                // Without the property, the git remote up the tree is used.
                File.WriteAllText(projectFile, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
                await Assert.That(NuGetProjectRepositoryReader.FindGitHubOwner(projectFile, root)).IsEqualTo("remote-owner");
                // A non-GitHub remote yields nothing.
                File.WriteAllText(
                    Path.Combine(root, ".git", "config"),
                    "[remote \"origin\"]\n\turl = https://gitlab.com/x/y.git\n".Replace("\\t", "\t"));
                await Assert.That(NuGetProjectRepositoryReader.FindGitHubOwner(projectFile, root)).IsNull();
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
