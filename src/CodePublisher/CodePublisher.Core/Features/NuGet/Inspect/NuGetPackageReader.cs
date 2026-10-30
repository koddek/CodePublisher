using System.IO.Compression;
using System.Xml.Linq;

namespace CodePublisher.Features.NuGet.Inspect;

/// <summary>What one .nupkg contains: its nuspec identity, dependency groups, and its entry list.</summary>
public sealed record NuGetPackageContents(
    string PackagePath,
    string? Id,
    string? Version,
    string? Description,
    string? NuspecEntryName,
    IReadOnlyList<Models.NuGetDependency> Dependencies,
    IReadOnlyList<string> Entries,
    bool HasLibAssets,
    bool HasRefAssets,
    bool HasBuildAssets,
    bool HasRuntimeAssets)
{
    public bool HasAnyAssets => HasLibAssets || HasRefAssets || HasBuildAssets || HasRuntimeAssets;
}

/// <summary>
/// Reads a .nupkg (a ZIP with one .nuspec at the root). The nuspec fields are matched by local
/// name so the 2010/2013 nuspec namespaces both parse.
/// </summary>
public static class NuGetPackageReader
{
    public static NuGetPackageContents Read(string packagePath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var entries = archive.Entries.Select(entry => entry.FullName).ToArray();
        var nuspec = archive.Entries.FirstOrDefault(entry =>
            entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)
            && !entry.FullName.Contains('/'));

        string? id = null;
        string? version = null;
        string? description = null;
        string? nuspecName = null;
        IReadOnlyList<Models.NuGetDependency> dependencies = [];
        if (nuspec is not null)
        {
            nuspecName = nuspec.FullName;
            using var stream = nuspec.Open();
            var document = XDocument.Load(stream); // throws on malformed nuspec XML
            id = MetadataValue(document, "id");
            version = MetadataValue(document, "version");
            description = MetadataValue(document, "description");
            dependencies = ReadDependencies(document);
        }

        return new NuGetPackageContents(
            packagePath,
            id,
            version,
            description,
            nuspecName,
            dependencies,
            entries,
            entries.Any(IsUnderFolder("lib")),
            entries.Any(IsUnderFolder("ref")),
            entries.Any(IsUnderFolder("build")),
            entries.Any(IsUnderFolder("runtimes")));
    }

    private static IReadOnlyList<Models.NuGetDependency> ReadDependencies(XDocument document)
    {
        var result = new List<Models.NuGetDependency>();
        var metadata = document.Descendants().FirstOrDefault(e => e.Name.LocalName == "metadata");
        var dependenciesElement = metadata?.Descendants().FirstOrDefault(e => e.Name.LocalName == "dependencies");
        if (dependenciesElement is null)
        {
            return result;
        }

        foreach (var group in dependenciesElement.Elements().Where(e => e.Name.LocalName == "group"))
        {
            var tfm = group.Attribute("targetFramework")?.Value;
            foreach (var dependency in group.Elements().Where(e => e.Name.LocalName == "dependency"))
            {
                result.Add(new Models.NuGetDependency(
                    tfm,
                    dependency.Attribute("id")?.Value.Trim() ?? string.Empty,
                    dependency.Attribute("version")?.Value.Trim() ?? string.Empty));
            }
        }

        return result;
    }

    private static Func<string, bool> IsUnderFolder(string folder) =>
        entry => entry.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase);

    private static string? MetadataValue(XDocument document, string localName)
    {
        // The id/version live under <metadata> in the nuspec; match by local name so the
        // namespace (2010 or 2013 schema) does not matter.
        return document
            .Descendants()
            .FirstOrDefault(element =>
                element.Name.LocalName == "metadata")
            ?.Descendants()
            .FirstOrDefault(element => element.Name.LocalName == localName)
            ?.Value.Trim();
    }
}
