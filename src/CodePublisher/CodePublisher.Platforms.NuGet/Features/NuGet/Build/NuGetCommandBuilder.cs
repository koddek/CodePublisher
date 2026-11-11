namespace CodePublisher.Features.NuGet.Build;

/// <summary>
/// Builds every `dotnet` command the NuGet pipeline runs. Pure: tests assert the exact argument
/// lists, and <see cref="Mask"/> keeps API keys out of any preview or log line.
/// </summary>
public static class NuGetCommandBuilder
{
    public const string LocalTestSourceName = "codepublisher-local-test";

    /// <summary>`dotnet pack` — builds the project and writes the .nupkg into the output folder.</summary>
    public static IReadOnlyList<string> BuildPackArguments(
        string dotnetPath,
        string projectFilePath,
        string configuration,
        string outputDirectory,
        string? versionOverride,
        bool includeSymbols,
        bool noBuild = false)
    {
        var arguments = new List<string>
        {
            dotnetPath,
            "pack",
            projectFilePath,
            "-c",
            configuration.Trim(),
            "-o",
            outputDirectory
        };

        if (!string.IsNullOrWhiteSpace(versionOverride))
        {
            arguments.Add($"-p:PackageVersion={versionOverride.Trim()}");
        }

        if (includeSymbols)
        {
            arguments.Add("--include-symbols");
        }

        if (noBuild)
        {
            arguments.Add("--no-build");
        }

        return arguments;
    }

    /// <summary>Forced rebuild, used when pack reports NU5026 (stale up-to-date markers, missing dll).</summary>
    public static IReadOnlyList<string> BuildForceBuildArguments(
        string dotnetPath,
        string projectFilePath,
        string configuration) =>
    [
        dotnetPath,
        "build",
        projectFilePath,
        "-c",
        configuration.Trim(),
        "--no-incremental"
    ];

    /// <summary>`dotnet nuget push` — uploads one .nupkg. The key is an argument so it never lands in a config file.</summary>
    public static IReadOnlyList<string> BuildPushArguments(
        string dotnetPath,
        string packagePath,
        string source,
        string apiKey)
    {
        return
        [
            dotnetPath,
            "nuget",
            "push",
            packagePath,
            "--source",
            source,
            "--api-key",
            apiKey,
            "--skip-duplicate"
        ];
    }

    /// <summary>`dotnet nuget delete` — removes one id+version from the feed, never prompting.</summary>
    public static IReadOnlyList<string> BuildDeleteArguments(
        string dotnetPath,
        string packageId,
        string version,
        string source,
        string apiKey)
    {
        return
        [
            dotnetPath,
            "nuget",
            "delete",
            packageId,
            version,
            "--source",
            source,
            "--api-key",
            apiKey,
            "--non-interactive"
        ];
    }

    /// <summary>Registers a local folder as a NuGet source for the package test.</summary>
    public static IReadOnlyList<string> BuildAddLocalSourceArguments(string dotnetPath, string folder) =>
    [
        dotnetPath,
        "nuget",
        "add",
        "source",
        folder,
        "--name",
        LocalTestSourceName
    ];

    public static IReadOnlyList<string> BuildRemoveLocalSourceArguments(string dotnetPath) =>
    [dotnetPath, "nuget", "remove", "source", LocalTestSourceName];

    /// <summary>A throwaway consumer project that proves the package installs and compiles.</summary>
    public static IReadOnlyList<string> BuildNewConsumerArguments(string dotnetPath, string consumerDirectory) =>
    [dotnetPath, "new", "classlib", "-o", consumerDirectory];

    public static IReadOnlyList<string> BuildAddPackageArguments(
        string dotnetPath,
        string consumerProjectFile,
        string packageId,
        string version,
        string sourceFolder) =>
    [
        dotnetPath,
        "add",
        consumerProjectFile,
        "package",
        packageId,
        "--version",
        version,
        "--source",
        sourceFolder
    ];

    public static IReadOnlyList<string> BuildConsumerArguments(string dotnetPath, string consumerProjectFile) =>
    [dotnetPath, "build", consumerProjectFile, "-v", "q", "--nologo"];

    /// <summary>Lists the global-packages folder so a re-test cannot pass on a cached copy.</summary>
    public static IReadOnlyList<string> BuildLocalsArguments(string dotnetPath) =>
    [dotnetPath, "nuget", "locals", "global-packages", "--list"];

    /// <summary>
    /// Joins arguments for display with the API key replaced by asterisks, so previews and the
    /// live log never carry the credential.
    /// </summary>
    public static string Mask(IEnumerable<string> arguments)
    {
        var masked = new List<string>();
        var maskNext = false;
        foreach (var argument in arguments)
        {
            if (maskNext)
            {
                masked.Add("********");
                maskNext = false;
                continue;
            }

            if (argument is "--api-key" or "-k" or "--symbol-api-key" or "-sk")
            {
                masked.Add(argument);
                maskNext = true;
                continue;
            }

            masked.Add(argument);
        }

        return string.Join(" ", masked.Select(QuoteArgument));
    }

    public static string QuoteArgument(string argument)
    {
        if (string.IsNullOrEmpty(argument))
        {
            return "\"\"";
        }

        if (argument.All(character => !char.IsWhiteSpace(character) && character != '"' && character != '\''))
        {
            return argument;
        }

        return "\"" + argument.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }
}
