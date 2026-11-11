using System.Text.RegularExpressions;
using CodePublisher.Features.NuGet.Models;
using CodePublisher.Features.Publishing.Inspect;

namespace CodePublisher.Features.NuGet.Feeds;

/// <summary>
/// Extracts the GitHub owner (user or organization) from a git remote URL or a csproj repository
/// property. Handles https, https with credentials, ssh (`git@github.com:…`), and
/// `ssh://git@github.com/…` forms. Pure string policy, so every shape is unit-tested.
/// </summary>
public static class GitHubOwnerDetector
{
    // Matches github.com followed by ':' (ssh scp-like) or '/' (https / ssh://), then owner/repo.
    private static readonly Regex GitHubPathPattern = new(
        @"github\.com[:/](?<owner>[^/\s]+)/(?<repo>[^/\s]+?)(?:\.git)?/?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>The owner, or null when the URL is not a GitHub URL or the owner is not a valid GitHub name.</summary>
    public static string? ParseOwner(string? remoteOrProjectUrl)
    {
        if (string.IsNullOrWhiteSpace(remoteOrProjectUrl))
        {
            return null;
        }

        var candidate = remoteOrProjectUrl.Trim();
        var match = GitHubPathPattern.Match(candidate);
        if (!match.Success)
        {
            return null;
        }

        var owner = match.Groups["owner"].Value;
        try
        {
            return NuGetFeedCatalog.ValidateGitHubOwner(owner);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}

/// <summary>Parses a .git/config file and returns the URL of one remote.</summary>
public static class GitConfigParser
{
    public static string? ParseRemoteUrl(string gitConfigText, string remoteName = "origin")
    {
        if (string.IsNullOrWhiteSpace(gitConfigText))
        {
            return null;
        }

        var inRemote = false;
        foreach (var rawLine in gitConfigText.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith('['))
            {
                inRemote = line.Equals($"[remote \"{remoteName}\"]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (inRemote && line.StartsWith("url", StringComparison.OrdinalIgnoreCase))
            {
                var value = line["url".Length..].TrimStart().TrimStart('=').Trim();
                return value.Length > 0 ? value : null;
            }
        }

        return null;
    }
}

/// <summary>
/// Finds the GitHub owner for a project: the csproj repository properties first
/// (RepositoryUrl / PackageProjectUrl / ProjectUrl), then the nearest .git/config remote.
/// </summary>
public static class NuGetProjectRepositoryReader
{
    private static readonly string[] RepositoryProperties = ["RepositoryUrl", "PackageProjectUrl", "ProjectUrl"];

    public static string? FindGitHubOwner(string? projectFilePath, string? projectDirectory)
    {
        if (!string.IsNullOrWhiteSpace(projectFilePath) && File.Exists(projectFilePath))
        {
            var projectFile = new FileInfo(projectFilePath);
            var fromProject = ReadOwnerFromProjectFile(projectFile);
            if (fromProject is not null)
            {
                return fromProject;
            }

            var directory = projectFile.Directory;
            if (directory is not null)
            {
                return FindOwnerFromGitConfig(directory);
            }
        }

        if (!string.IsNullOrWhiteSpace(projectDirectory) && Directory.Exists(projectDirectory))
        {
            return FindOwnerFromGitConfig(new DirectoryInfo(projectDirectory));
        }

        return null;
    }

    private static string? ReadOwnerFromProjectFile(FileInfo projectFile)
    {
        foreach (var propertyName in RepositoryProperties)
        {
            var value = ProjectInspector.ReadProperty(projectFile, propertyName);
            var owner = GitHubOwnerDetector.ParseOwner(value);
            if (owner is not null)
            {
                return owner;
            }
        }

        return null;
    }

    private static string? FindOwnerFromGitConfig(DirectoryInfo start)
    {
        var directory = start;
        while (directory is not null)
        {
            var gitPath = Path.Combine(directory.FullName, ".git");
            string? configPath = null;
            if (Directory.Exists(gitPath))
            {
                configPath = Path.Combine(gitPath, "config");
            }
            else if (File.Exists(gitPath))
            {
                // A worktree/submodule .git file points at the real git dir: "gitdir: <path>".
                try
                {
                    var pointer = File.ReadAllText(gitPath);
                    var marker = "gitdir:";
                    var index = pointer.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                    if (index >= 0)
                    {
                        var gitDir = pointer[(index + marker.Length)..].Trim();
                        configPath = Path.Combine(gitDir, "config");
                    }
                }
                catch
                {
                    // Unreadable pointer: keep walking up.
                }
            }

            if (configPath is not null && File.Exists(configPath))
            {
                try
                {
                    var owner = GitHubOwnerDetector.ParseOwner(GitConfigParser.ParseRemoteUrl(File.ReadAllText(configPath)));
                    if (owner is not null)
                    {
                        return owner;
                    }
                }
                catch
                {
                    // Unreadable config: keep walking up.
                }
            }

            directory = directory.Parent;
        }

        return null;
    }
}
