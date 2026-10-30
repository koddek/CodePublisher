namespace CodePublisher.Features.NuGet.Models;

/// <summary>Where a packed .nupkg can be hosted.</summary>
public enum NuGetFeedKind
{
    NuGetOrg,
    GitHubPackages,
    LocalFolder
}

/// <summary>A selectable publish destination for a NuGet package.</summary>
public sealed record NuGetFeedTarget(NuGetFeedKind Kind, string Label, string Description)
{
    public bool RequiresApiKey => Kind != NuGetFeedKind.LocalFolder;

    public bool RequiresOwner => Kind == NuGetFeedKind.GitHubPackages;
}

/// <summary>The id and version a packed package carries inside its nuspec.</summary>
public sealed record NuGetPackageIdentity(string Id, string Version);

public enum NuGetIssueSeverity
{
    Warning,
    Error
}

public sealed record NuGetValidationIssue(NuGetIssueSeverity Severity, string Message)
{
    public bool IsError => Severity == NuGetIssueSeverity.Error;
}

/// <summary>The result of inspecting and validating one .nupkg file.</summary>
public sealed record NuGetValidationReport(
    string PackagePath,
    NuGetPackageIdentity? Identity,
    IReadOnlyList<string> Entries,
    IReadOnlyList<NuGetValidationIssue> Issues)
{
    public bool IsValid => Issues.All(issue => !issue.IsError);

    public string Summary => IsValid
        ? (Issues.Count == 0
            ? "Package is valid."
            : $"Package is valid with {Issues.Count} warning(s).")
        : $"Package is invalid: {Issues.Count(issue => issue.IsError)} error(s), {Issues.Count(issue => !issue.IsError)} warning(s).";
}

/// <summary>One version of a package as the feed reports it.</summary>
public sealed record NuGetFeedVersion(string Version, bool IsListed, DateTimeOffset? PublishedAt);

/// <summary>One nuspec dependency group entry: a dependency id with its version range, per target framework group.</summary>
public sealed record NuGetDependency(string? TargetFramework, string Id, string VersionRange);

/// <summary>A finished pack: the artifact on disk plus its validation report.</summary>
public sealed record NuGetPackResult(string PackagePath, NuGetValidationReport Validation);

public sealed record NuGetOperationResult(bool Success, string Message);
