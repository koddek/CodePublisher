using CodePublisher.Features.Publishing.Inspect;

namespace CodePublisher.Features.Publishing.Configure;

/// <summary>
/// Resolves the iOS bundle identifier of a project: the evaluated CFBundleIdentifier first, then
/// the Info.plist on disk, then the csproj properties, then a synthesized bundle identifier.
/// </summary>
public static class IosProjectIdentifier
{
    public static string? Resolve(
        FileInfo projectFile,
        string projectName,
        IReadOnlyDictionary<string, string>? evaluated)
    {
        var evaluatedId = ProjectIdentifier.FromEvaluated(evaluated, "ApplicationId", "CFBundleIdentifier");
        if (!string.IsNullOrWhiteSpace(evaluatedId))
        {
            return evaluatedId;
        }

        var plistBundleId = ProjectInspector.ReadIosBundleIdentifier(projectFile);
        if (!string.IsNullOrWhiteSpace(plistBundleId))
        {
            return plistBundleId;
        }

        var csprojId = ProjectInspector.ReadProperty(projectFile, "ApplicationId", "CFBundleIdentifier", "ApplicationIdentifier");
        if (!string.IsNullOrWhiteSpace(csprojId))
        {
            return csprojId.Trim();
        }

        var packageId = ProjectIdentifier.FromEvaluated(evaluated, "PackageId")
            ?? ProjectInspector.ReadProperty(projectFile, "PackageId");
        return ProjectIdentifier.BuildBundleIdentifier(packageId ?? string.Empty, projectName);
    }
}
