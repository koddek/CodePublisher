using CodePublisher.Features.Publishing.Inspect;

namespace CodePublisher.Features.NuGet;

/// <summary>
/// Resolves the package identifier a <c>dotnet pack</c> run uses: the evaluated PackageId first,
/// then the csproj, then the project file name.
/// </summary>
public static class NuGetProjectIdentifier
{
    public static string Resolve(
        FileInfo projectFile,
        string projectName,
        IReadOnlyDictionary<string, string>? evaluated)
        => ProjectIdentifier.FromEvaluated(evaluated, "PackageId")
            ?? ProjectInspector.ReadProperty(projectFile, "PackageId")
            ?? projectName;
}
