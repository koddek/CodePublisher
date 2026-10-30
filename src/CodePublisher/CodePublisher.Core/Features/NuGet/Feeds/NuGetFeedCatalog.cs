using System.Globalization;
using System.Text.RegularExpressions;
using CodePublisher.Features.NuGet.Models;

namespace CodePublisher.Features.NuGet.Feeds;

/// <summary>
/// The NuGet destinations the app can push to, with the URLs and the input rules for each.
/// Pure policy: no I/O, so every rule is unit-testable.
/// </summary>
public static class NuGetFeedCatalog
{
    public const string NuGetOrgLabel = "NuGet.org";
    public const string GitHubPackagesLabel = "GitHub Packages";
    public const string LocalFolderLabel = "Local folder (test)";

    public const string NuGetOrgPushSource = "https://api.nuget.org/v3/index.json";
    public const string GitHubPackagesSourceTemplate = "https://nuget.pkg.github.com/{0}/index.json";

    // NuGet package ids: letters, digits, '-', '_', '.', must start and end with a letter or
    // digit, no empty segments, at most 100 characters (nuget.org rules).
    private static readonly Regex PackageIdPattern = new(
        @"^[A-Za-z0-9](?:[A-Za-z0-9_.-]*[A-Za-z0-9])?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // SemVer 2.0.0: https://semver.org — build metadata after '+', prerelease after '-'.
    private static readonly Regex SemVer2Pattern = new(
        @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-((?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*))*))?(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // GitHub user/org names: alphanumerics and single hyphens, cannot start or end with a hyphen,
    // at most 39 characters.
    private static readonly Regex GitHubOwnerPattern = new(
        @"^[A-Za-z0-9](?:[A-Za-z0-9-]{0,37}[A-Za-z0-9])?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IReadOnlyList<NuGetFeedTarget> Targets { get; } =
    [
        new NuGetFeedTarget(
            NuGetFeedKind.NuGetOrg,
            NuGetOrgLabel,
            "Public feed. Push needs a nuget.org API key; listing is public."),
        new NuGetFeedTarget(
            NuGetFeedKind.GitHubPackages,
            GitHubPackagesLabel,
            "Your GitHub account or organization. Push, list, and delete need a PAT with packages write/delete and read scopes."),
        new NuGetFeedTarget(
            NuGetFeedKind.LocalFolder,
            LocalFolderLabel,
            "A folder on this computer, used to test a package before it goes anywhere. No key needed."),
    ];

    public static NuGetFeedTarget DefaultTarget => Targets[0];

    /// <summary>The V3 service index URL of the feed, which is also the push source for both hosts.</summary>
    public static string GetServiceIndexUrl(NuGetFeedTarget target, string ownerOrFolder)
    {
        return target.Kind switch
        {
            NuGetFeedKind.NuGetOrg => NuGetOrgPushSource,
            NuGetFeedKind.GitHubPackages => string.Format(
                CultureInfo.InvariantCulture,
                GitHubPackagesSourceTemplate,
                ValidateGitHubOwner(ownerOrFolder)),
            NuGetFeedKind.LocalFolder => ValidateLocalFolder(ownerOrFolder),
            _ => throw new InvalidOperationException($"Unknown NuGet feed kind `{target.Kind}`.")
        };
    }

    /// <summary>The push source passed to `dotnet nuget push`/`delete`: a V3 index URL or a local folder.</summary>
    public static string GetPushSource(NuGetFeedTarget target, string ownerOrFolder) =>
        GetServiceIndexUrl(target, ownerOrFolder);

    /// <summary>Returns an error message, or null when the package id is valid.</summary>
    public static string? ValidatePackageId(string? packageId)
    {
        if (string.IsNullOrWhiteSpace(packageId))
        {
            return "Package id is empty.";
        }

        var trimmed = packageId.Trim();
        if (trimmed.Length > 100)
        {
            return "Package id is longer than 100 characters.";
        }

        return PackageIdPattern.IsMatch(trimmed)
            ? null
            : "Package id may contain only letters, digits, '-', '_', '.', and must start and end with a letter or digit.";
    }

    /// <summary>Returns an error message, or null when the version is valid SemVer 2.0.</summary>
    public static string? ValidateVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return "Package version is empty.";
        }

        return SemVer2Pattern.IsMatch(version.Trim())
            ? null
            : "Version must be SemVer 2.0, for example 1.2.3 or 1.2.3-beta.4.";
    }

    public static bool IsValidSemVer2(string version) => ValidateVersion(version) is null;

    /// <summary>Returns the owner, or throws when it is not a valid GitHub user/org name.</summary>
    public static string ValidateGitHubOwner(string? owner)
    {
        if (string.IsNullOrWhiteSpace(owner) || !GitHubOwnerPattern.IsMatch(owner.Trim()))
        {
            throw new InvalidOperationException(
                "GitHub Packages needs a GitHub username or organization name (letters, digits, single hyphens).");
        }

        return owner.Trim();
    }

    public static string ValidateLocalFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            throw new InvalidOperationException("Choose a local folder to use as a test feed.");
        }

        return folder.Trim();
    }

    /// <summary>Feed URLs address package ids in lower case (V3 flat container convention).</summary>
    public static string NormalizeIdForUrl(string packageId) => packageId.Trim().ToLowerInvariant();
}
