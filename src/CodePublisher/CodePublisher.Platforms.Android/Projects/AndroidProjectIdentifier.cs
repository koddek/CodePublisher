using CodePublisher.Features.Publishing.Inspect;

namespace CodePublisher.Features.Publishing.Configure;

/// <summary>
/// Resolves the Android package identifier of a project: the merged manifest first (MSBuild
/// evaluation reads it when the project produces one), then a manifest on disk, then the csproj
/// properties, then the package id when it looks like a reverse-DNS name. Falls back to a
/// synthesized bundle identifier so every project has one.
/// </summary>
public static class AndroidProjectIdentifier
{
    public static string? Resolve(
        FileInfo projectFile,
        string projectName,
        IReadOnlyDictionary<string, string>? evaluated)
    {
        // 1. MSBuild evaluated properties (ApplicationId / PackageName)
        var evaluatedId = ProjectIdentifier.FromEvaluated(evaluated, "ApplicationId", "PackageName");
        if (!string.IsNullOrWhiteSpace(evaluatedId))
        {
            return evaluatedId;
        }

        // 2. AndroidManifest.xml package attribute
        var manifestPackage = ProjectInspector.ReadAndroidManifestPackage(projectFile);
        if (!string.IsNullOrWhiteSpace(manifestPackage))
        {
            return manifestPackage;
        }

        // 3. csproj ApplicationId / PackageName
        var csprojId = ProjectInspector.ReadProperty(projectFile, "ApplicationId", "PackageName");
        if (!string.IsNullOrWhiteSpace(csprojId))
        {
            return csprojId.Trim();
        }

        // 4. Fallback to evaluated PackageId or csproj PackageId if it looks like a reverse-DNS identifier
        var packageId = ProjectIdentifier.FromEvaluated(evaluated, "PackageId")
            ?? ProjectInspector.ReadProperty(projectFile, "PackageId", "ApplicationIdentifier");
        if (!string.IsNullOrWhiteSpace(packageId))
        {
            var trimmed = packageId.Trim();
            if (trimmed.Contains('.', StringComparison.Ordinal))
            {
                return trimmed;
            }
        }

        return ProjectIdentifier.BuildBundleIdentifier(packageId ?? string.Empty, projectName);
    }
}
