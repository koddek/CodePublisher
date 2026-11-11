using CodePublisher.Features.NuGet;
using CodePublisher.Features.Publishing.Configure;
using CodePublisher.Features.Publishing.Inspect;

namespace CodePublisher.Services;

/// <summary>
/// The identifier of the app being published: each platform library resolves its own (Android
/// from the manifest, iOS from the Info.plist, NuGet from the package id); the native desktop
/// platforms and the macOS-with-Catalyst path share the package-id shape, so that resolution
/// lives here at the composition root.
/// </summary>
internal static class ProjectIdentifierResolver
{
    public static string? Resolve(
        FileInfo projectFile,
        string projectName,
        string publishPlatform,
        IReadOnlyDictionary<string, string>? evaluated)
    {
        if (string.Equals(publishPlatform, PublishPlatforms.Android, StringComparison.Ordinal))
        {
            return AndroidProjectIdentifier.Resolve(projectFile, projectName, evaluated);
        }

        if (string.Equals(publishPlatform, PublishPlatforms.Ios, StringComparison.Ordinal))
        {
            return IosProjectIdentifier.Resolve(projectFile, projectName, evaluated);
        }

        if (string.Equals(publishPlatform, PublishPlatforms.Windows, StringComparison.Ordinal)
            || string.Equals(publishPlatform, PublishPlatforms.Linux, StringComparison.Ordinal))
        {
            return DesktopProjectIdentifier.Resolve(projectFile, evaluated);
        }

        if (string.Equals(publishPlatform, PublishPlatforms.NuGet, StringComparison.Ordinal))
        {
            return NuGetProjectIdentifier.Resolve(projectFile, projectName, evaluated);
        }

        return GenericProjectIdentifier.Resolve(projectFile, projectName, evaluated);
    }

    private static class DesktopProjectIdentifier
    {
        public static string? Resolve(FileInfo projectFile, IReadOnlyDictionary<string, string>? evaluated)
            => ProjectIdentifier.FromEvaluated(evaluated, "PackageId", "ApplicationId")
                ?? ProjectInspector.ReadProperty(projectFile, "PackageId", "ApplicationId", "PackageName");
    }

    private static class GenericProjectIdentifier
    {
        public static string? Resolve(
            FileInfo projectFile,
            string projectName,
            IReadOnlyDictionary<string, string>? evaluated)
        {
            var genericId = ProjectIdentifier.FromEvaluated(evaluated, "PackageId", "ApplicationId", "CFBundleIdentifier")
                ?? ProjectInspector.ReadProperty(projectFile, "PackageId", "ApplicationId", "CFBundleIdentifier", "ApplicationIdentifier");
            if (!string.IsNullOrWhiteSpace(genericId))
            {
                return genericId.Trim();
            }

            return ProjectIdentifier.BuildBundleIdentifier(string.Empty, projectName);
        }
    }
}
