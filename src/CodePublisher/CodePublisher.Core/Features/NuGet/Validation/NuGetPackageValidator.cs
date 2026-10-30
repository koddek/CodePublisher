using System.IO.Compression;
using CodePublisher.Features.NuGet.Feeds;
using CodePublisher.Features.NuGet.Inspect;
using CodePublisher.Features.NuGet.Models;

namespace CodePublisher.Features.NuGet.Validation;

/// <summary>
/// The pre-deploy checks every packed package goes through, without installing it anywhere:
/// the archive opens as a ZIP, the nuspec parses and names a valid id + SemVer 2.0 version,
/// dependency groups are well formed, no entry escapes the archive, every lib DLL is a real
/// managed assembly (PE + CLR header), and common packaging gaps (no assets, no readme) are
/// reported. Pure policy over the archive (NuGet.Packaging was tried and dropped: its signing
/// P/Invokes break the browser WASM build), so tests build real .nupkg files in temp folders.
/// </summary>
public static class NuGetPackageValidator
{
    public static NuGetValidationReport Validate(string packagePath)
    {
        var issues = new List<NuGetValidationIssue>();

        if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
        {
            issues.Add(new NuGetValidationIssue(
                NuGetIssueSeverity.Error,
                $"Package file not found: {packagePath}"));
            return new NuGetValidationReport(packagePath, null, [], issues);
        }

        if (!packagePath.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new NuGetValidationIssue(
                NuGetIssueSeverity.Error,
                "The packed artifact is not a .nupkg file."));
        }

        // Zip-level view: entry list and archive-safety (no absolute paths or '..').
        NuGetPackageContents contents;
        try
        {
            contents = NuGetPackageReader.Read(packagePath);
        }
        catch (Exception exception) when (exception is not InvalidOperationException)
        {
            issues.Add(new NuGetValidationIssue(
                NuGetIssueSeverity.Error,
                $"The package could not be opened as a .nupkg (ZIP): {exception.Message}"));
            return new NuGetValidationReport(packagePath, null, [], issues);
        }

        var escaped = contents.Entries
            .Where(entry => entry.StartsWith('/') || entry.Split('/').Contains(".."))
            .ToArray();
        if (escaped.Length > 0)
        {
            issues.Add(new NuGetValidationIssue(
                NuGetIssueSeverity.Error,
                $"The package contains {escaped.Length} entr(y/ies) that escape the archive (absolute path or '..'): {escaped[0]}"));
        }

        if (contents.NuspecEntryName is null)
        {
            issues.Add(new NuGetValidationIssue(
                NuGetIssueSeverity.Error,
                "The package has no .nuspec at its root; dotnet pack did not produce a valid package."));
        }

        // Nuspec metadata and dependency groups come from the reader (XDocument throws on
        // malformed XML, which lands in the catch below as an error).
        var id = contents.Id;
        var version = contents.Version;
        foreach (var dependency in contents.Dependencies)
        {
            var dependencyIdError = NuGetFeedCatalog.ValidatePackageId(dependency.Id);
            if (dependencyIdError is not null || string.IsNullOrWhiteSpace(dependency.VersionRange))
            {
                issues.Add(new NuGetValidationIssue(
                    NuGetIssueSeverity.Error,
                    $"Malformed dependency in nuspec: id='{dependency.Id}', version='{dependency.VersionRange}'"
                    + (dependency.TargetFramework is null ? string.Empty : $" (group '{dependency.TargetFramework}')")));
            }
        }

        // Every lib DLL must be a real managed assembly (PE + CLR header), so a broken build
        // can never be packed silently.
        var libDlls = contents.Entries
            .Where(entry => entry.StartsWith("lib/", StringComparison.OrdinalIgnoreCase)
                && entry.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (libDlls.Length > 0)
        {
            try
            {
                using var archive = ZipFile.OpenRead(packagePath);
                foreach (var dll in libDlls)
                {
                    var entry = archive.GetEntry(dll);
                    if (entry is null)
                    {
                        continue;
                    }

                    using var entryStream = entry.Open();
                    using var memory = new MemoryStream();
                    entryStream.CopyTo(memory);
                    memory.Position = 0;
                    if (!ManagedAssemblyInspector.IsManagedAssembly(memory))
                    {
                        issues.Add(new NuGetValidationIssue(
                            NuGetIssueSeverity.Error,
                            $"{dll} is not a managed .NET assembly (no CLR header); the package would install a broken DLL."));
                    }
                }
            }
            catch (Exception exception) when (exception is not InvalidOperationException)
            {
                issues.Add(new NuGetValidationIssue(
                    NuGetIssueSeverity.Error,
                    $"The package assemblies could not be inspected: {exception.Message}"));
            }
        }

        var idError = NuGetFeedCatalog.ValidatePackageId(id ?? contents.Id);
        if (idError is not null)
        {
            issues.Add(new NuGetValidationIssue(
                NuGetIssueSeverity.Error,
                $"Package id problem: {idError}"));
        }

        var versionError = NuGetFeedCatalog.ValidateVersion(version ?? contents.Version);
        if (versionError is not null)
        {
            issues.Add(new NuGetValidationIssue(
                NuGetIssueSeverity.Error,
                $"Package version problem: {versionError}"));
        }

        if (!contents.HasAnyAssets)
        {
            issues.Add(new NuGetValidationIssue(
                NuGetIssueSeverity.Warning,
                "The package has no lib/, ref/, build/, or runtimes/ assets; consumers get no assemblies from it."));
        }

        if (!contents.Entries.Any(entry => entry.Equals("readme.md", StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add(new NuGetValidationIssue(
                NuGetIssueSeverity.Warning,
                "The package has no readme.md; nuget.org and the SDK warn (NU5048) that a readme helps consumers."));
        }

        var identity = idError is null && versionError is null
            ? new NuGetPackageIdentity(id ?? contents.Id!, version ?? contents.Version!)
            : null;

        return new NuGetValidationReport(packagePath, identity, contents.Entries, issues);
    }
}
