using System.Net.Http;
using System.Text;
using CodePublisher.Features.NuGet.Build;
using CodePublisher.Features.NuGet.Feeds;
using CodePublisher.Features.NuGet.Models;
using CodePublisher.Features.NuGet.Validation;
using CodePublisher.Features.Publishing.Process;

namespace CodePublisher.Features.NuGet.Services;

/// <summary>One process execution. Defaults to <see cref="ProcessRunner"/>; tests inject an imposter.</summary>
public delegate Task<int> NuGetProcessRunner(
    IReadOnlyList<string> arguments,
    Action<string> writeOutput,
    CancellationToken cancellationToken,
    string? workingDirectory);

public sealed record NuGetPackOutcome(bool Success, string Message, NuGetPackResult? Package);

/// <summary>A credential found on this machine, with where it came from. The token stays in memory only.</summary>
public sealed record DetectedCredential(string Token, string Source);

/// <summary>
/// Orchestrates the NuGet pipeline: pack, validate, test-install from a local feed, push, list,
/// and delete. All external effects go through the <see cref="NuGetProcessRunner"/> seam and the
/// <see cref="NuGetFeedClient"/>, so tests never start a real process or call a real feed.
/// </summary>
public sealed class NuGetPackageService
{
    private readonly string? _dotnetPath;
    private readonly NuGetProcessRunner _run;
    private readonly NuGetFeedClient _feedClient;
    private readonly Func<string, string?> _getEnvironmentVariable;

    public NuGetPackageService(
        string? dotnetPath,
        NuGetFeedClient feedClient,
        NuGetProcessRunner? processRunner = null,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        _dotnetPath = dotnetPath;
        _feedClient = feedClient;
        _run = processRunner
            ?? ((arguments, writeOutput, cancellationToken, workingDirectory) =>
                ProcessRunner.RunAsync(arguments, writeOutput, cancellationToken, workingDirectory));
        _getEnvironmentVariable = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;
    }

    /// <summary>
    /// Finds an API key that is already configured on this machine, so the user does not have to
    /// paste it: for GitHub Packages a `gh auth token` / GH_TOKEN / GITHUB_TOKEN, for nuget.org
    /// NUGET_API_KEY. Returns null when nothing is set. The token is never written to the log.
    /// </summary>
    public async Task<DetectedCredential?> DetectApiKeyAsync(NuGetFeedKind kind, CancellationToken cancellationToken)
    {
        var variableNames = kind == NuGetFeedKind.GitHubPackages
            ? new[] { "GH_TOKEN", "GITHUB_TOKEN" }
            : new[] { "NUGET_API_KEY" };

        foreach (var name in variableNames)
        {
            var value = _getEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return new DetectedCredential(value, $"environment variable {name}");
            }
        }

        if (kind == NuGetFeedKind.GitHubPackages)
        {
            // The GitHub CLI keeps its own token; `gh auth token` prints it when gh is installed
            // and the user is signed in.
            var output = new StringBuilder();
            var exitCode = await _run(
                ["gh", "auth", "token"],
                text => output.Append(text),
                cancellationToken,
                null).ConfigureAwait(false);
            var token = output.ToString().Trim();
            if (exitCode == 0 && !string.IsNullOrWhiteSpace(token))
            {
                return new DetectedCredential(token, "gh auth token (GitHub CLI)");
            }
        }

        return null;
    }

    /// <summary>The GitHub owner named by the project's repository URL or git remote, when there is one.</summary>
    public static string? DetectGitHubOwner(string? projectFilePath, string? projectDirectory) =>
        NuGetProjectRepositoryReader.FindGitHubOwner(projectFilePath, projectDirectory);

    private string DotnetPath => _dotnetPath
        ?? throw new InvalidOperationException("`dotnet` was not found. Install the .NET SDK or add dotnet to PATH.");

    public async Task<NuGetPackOutcome> PackAsync(
        string projectFilePath,
        string configuration,
        string outputDirectory,
        string? versionOverride,
        bool includeSymbols,
        Action<string> writeOutput,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath) || !File.Exists(projectFilePath))
        {
            return new NuGetPackOutcome(false, $"Project file not found: {projectFilePath}", null);
        }

        Directory.CreateDirectory(outputDirectory);
        var arguments = NuGetCommandBuilder.BuildPackArguments(
            DotnetPath, projectFilePath, configuration, outputDirectory, versionOverride, includeSymbols);
        writeOutput(Environment.NewLine + "=== NuGet pack started ===" + Environment.NewLine);
        writeOutput(NuGetCommandBuilder.Mask(arguments) + Environment.NewLine + Environment.NewLine);

        var output = new StringBuilder();
        var exitCode = await _run(
            arguments,
            text =>
            {
                output.Append(text);
                writeOutput(text);
            },
            cancellationToken,
            Path.GetDirectoryName(projectFilePath)).ConfigureAwait(false);

        // NU5026: pack skipped the build as "up to date" (stale obj markers after a cleaned bin),
        // so the dll to pack does not exist. Force a real rebuild, then pack without building.
        if (exitCode != 0 && output.ToString().Contains("NU5026", StringComparison.OrdinalIgnoreCase))
        {
            writeOutput(Environment.NewLine
                + "--- Pack skipped the build but the assembly is missing (NU5026); forcing a rebuild, then packing with --no-build ---"
                + Environment.NewLine);
            var buildArguments = NuGetCommandBuilder.BuildForceBuildArguments(DotnetPath, projectFilePath, configuration);
            writeOutput(NuGetCommandBuilder.Mask(buildArguments) + Environment.NewLine + Environment.NewLine);
            var buildExitCode = await _run(
                buildArguments,
                text =>
                {
                    output.Append(text);
                    writeOutput(text);
                },
                cancellationToken,
                Path.GetDirectoryName(projectFilePath)).ConfigureAwait(false);
            if (buildExitCode != 0)
            {
                return new NuGetPackOutcome(false, $"Forced rebuild failed (exit code {buildExitCode}). See the live output.", null);
            }

            var repackArguments = NuGetCommandBuilder.BuildPackArguments(
                DotnetPath, projectFilePath, configuration, outputDirectory, versionOverride, includeSymbols, noBuild: true);
            writeOutput(Environment.NewLine + NuGetCommandBuilder.Mask(repackArguments) + Environment.NewLine + Environment.NewLine);
            exitCode = await _run(
                repackArguments,
                text =>
                {
                    output.Append(text);
                    writeOutput(text);
                },
                cancellationToken,
                Path.GetDirectoryName(projectFilePath)).ConfigureAwait(false);
        }

        if (exitCode != 0)
        {
            return new NuGetPackOutcome(false, $"dotnet pack failed (exit code {exitCode}). See the live output.", null);
        }

        var packagePath = FindNewestNupkg(outputDirectory);
        if (packagePath is null)
        {
            return new NuGetPackOutcome(false, $"dotnet pack succeeded but no .nupkg was found in {outputDirectory}.", null);
        }

        var validation = NuGetPackageValidator.Validate(packagePath);
        writeOutput(Environment.NewLine + $"=== Pack produced {Path.GetFileName(packagePath)} — {validation.Summary} ===" + Environment.NewLine);
        return new NuGetPackOutcome(
            validation.IsValid,
            validation.IsValid ? $"Packed {Path.GetFileName(packagePath)}." : $"Packed {Path.GetFileName(packagePath)}, but the package failed validation.",
            new NuGetPackResult(packagePath, validation));
    }

    public NuGetValidationReport ValidatePackage(string packagePath) => NuGetPackageValidator.Validate(packagePath);

    /// <summary>
    /// Proves the packed package installs: registers its folder as a local source, clears the
    /// cached copy of this id+version, creates a throwaway classlib, adds the package to it, and
    /// builds it. Everything is removed afterwards, so a re-test always exercises the real artifact.
    /// </summary>
    public async Task<NuGetOperationResult> TestPackageAsync(
        NuGetPackageIdentity identity,
        string packageFolder,
        Action<string> writeOutput,
        CancellationToken cancellationToken)
    {
        var packagePath = FindNewestNupkg(packageFolder, identity.Id);
        if (packagePath is null)
        {
            return new NuGetOperationResult(false, $"No {identity.Id}.*.nupkg found in {packageFolder}. Pack first.");
        }

        var validation = NuGetPackageValidator.Validate(packagePath);
        if (!validation.IsValid)
        {
            return new NuGetOperationResult(
                false,
                "The package failed validation and was not test-installed: "
                + string.Join(" ", validation.Issues.Where(issue => issue.IsError).Select(issue => issue.Message)));
        }

        await ClearCachedCopyAsync(identity, writeOutput, cancellationToken).ConfigureAwait(false);

        var testRoot = Path.Combine(Path.GetTempPath(), $"codepublisher-nuget-test-{Guid.NewGuid():N}");
        var consumerDirectory = Path.Combine(testRoot, "consumer");
        var consumerProject = Path.Combine(consumerDirectory, "consumer.csproj");
        try
        {
            Directory.CreateDirectory(testRoot);
            var steps = new (string Label, IReadOnlyList<string> Arguments)[]
            {
                ("Register local feed", NuGetCommandBuilder.BuildAddLocalSourceArguments(DotnetPath, packageFolder)),
                ("Create consumer project", NuGetCommandBuilder.BuildNewConsumerArguments(DotnetPath, consumerDirectory)),
                ("Add the package to the consumer", NuGetCommandBuilder.BuildAddPackageArguments(DotnetPath, consumerProject, identity.Id, identity.Version, packageFolder)),
                ("Build the consumer against the package", NuGetCommandBuilder.BuildConsumerArguments(DotnetPath, consumerProject))
            };

            writeOutput(Environment.NewLine + $"=== Testing {identity.Id} {identity.Version} from a local feed ===" + Environment.NewLine);
            foreach (var (label, arguments) in steps)
            {
                writeOutput(Environment.NewLine + $"--- {label}: {NuGetCommandBuilder.Mask(arguments)} ---" + Environment.NewLine);
                var exitCode = await _run(arguments, writeOutput, cancellationToken, testRoot).ConfigureAwait(false);
                if (exitCode != 0)
                {
                    return new NuGetOperationResult(false, $"Test failed at '{label}' (exit code {exitCode}). See the live output.");
                }
            }

            return new NuGetOperationResult(true, $"{identity.Id} {identity.Version} installed into a fresh consumer project and built successfully.");
        }
        finally
        {
            await _run(NuGetCommandBuilder.BuildRemoveLocalSourceArguments(DotnetPath), _ => { }, CancellationToken.None, testRoot)
                .ConfigureAwait(false);
            TryDeleteDirectory(testRoot);
        }
    }

    /// <summary>
    /// Pushes the packed package to the selected feed. NuGet feeds are immutable per version, so a
    /// version that already exists is reported and skipped rather than pushed again. A local folder
    /// "push" is a copy into that folder.
    /// </summary>
    public async Task<NuGetOperationResult> PushAsync(
        string packagePath,
        NuGetFeedTarget target,
        string ownerOrFolder,
        string apiKey,
        Action<string> writeOutput,
        CancellationToken cancellationToken)
    {
        var validation = NuGetPackageValidator.Validate(packagePath);
        if (!validation.IsValid || validation.Identity is null)
        {
            return new NuGetOperationResult(
                false,
                "Refusing to push an invalid package: "
                + string.Join(" ", validation.Issues.Where(issue => issue.IsError).Select(issue => issue.Message)));
        }

        var identity = validation.Identity;
        var source = NuGetFeedCatalog.GetPushSource(target, ownerOrFolder);

        if (target.Kind == NuGetFeedKind.LocalFolder)
        {
            var folder = NuGetFeedCatalog.ValidateLocalFolder(ownerOrFolder);
            Directory.CreateDirectory(folder);
            var destination = Path.Combine(folder, Path.GetFileName(packagePath));
            File.Copy(packagePath, destination, overwrite: true);
            writeOutput($"Copied {Path.GetFileName(packagePath)} to local feed {destination}{Environment.NewLine}");
            return new NuGetOperationResult(true, $"Copied {identity.Id} {identity.Version} to {destination}.");
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new NuGetOperationResult(false, $"{target.Label} requires an API key (a nuget.org API key, or a GitHub PAT with write:packages).");
        }

        // Feeds never overwrite a version, so a duplicate is refused up front. If the version list
        // cannot be read (offline, feed hiccup), the push still runs: --skip-duplicate makes the
        // server reject a duplicate instead of overwriting it.
        try
        {
            var existing = await ListVersionsAsync(target, ownerOrFolder, identity.Id, apiKey, cancellationToken).ConfigureAwait(false);
            if (existing.Any(version => string.Equals(version.Version, identity.Version, StringComparison.OrdinalIgnoreCase)))
            {
                return new NuGetOperationResult(
                    false,
                    $"{identity.Id} {identity.Version} already exists on {target.Label}; feeds never overwrite a version. Bump the version and pack again.");
            }
        }
        catch (Exception exception) when (exception is NuGetFeedException or HttpRequestException)
        {
            writeOutput($"Could not list existing versions on {target.Label} ({exception.Message}); pushing anyway, the feed rejects duplicates.{Environment.NewLine}");
        }

        var arguments = NuGetCommandBuilder.BuildPushArguments(DotnetPath, packagePath, source, apiKey);
        writeOutput(Environment.NewLine + $"=== Pushing {identity.Id} {identity.Version} to {target.Label} ===" + Environment.NewLine);
        writeOutput(NuGetCommandBuilder.Mask(arguments) + Environment.NewLine + Environment.NewLine);
        var exitCode = await _run(arguments, writeOutput, cancellationToken, Path.GetDirectoryName(packagePath)).ConfigureAwait(false);
        return exitCode == 0
            ? new NuGetOperationResult(true, $"Pushed {identity.Id} {identity.Version} to {target.Label}.")
            : new NuGetOperationResult(false, $"Push to {target.Label} failed (exit code {exitCode}). See the live output.");
    }

    public Task<IReadOnlyList<NuGetFeedVersion>> ListVersionsAsync(
        NuGetFeedTarget target,
        string ownerOrFolder,
        string packageId,
        string? apiKey,
        CancellationToken cancellationToken) =>
        _feedClient.ListVersionsAsync(target, ownerOrFolder, packageId, apiKey, cancellationToken);

    public async Task<NuGetOperationResult> DeleteVersionAsync(
        NuGetFeedTarget target,
        string ownerOrFolder,
        string packageId,
        string version,
        string apiKey,
        Action<string> writeOutput,
        CancellationToken cancellationToken)
    {
        var idError = NuGetFeedCatalog.ValidatePackageId(packageId);
        if (idError is not null)
        {
            return new NuGetOperationResult(false, idError);
        }

        var versionError = NuGetFeedCatalog.ValidateVersion(version);
        if (versionError is not null)
        {
            return new NuGetOperationResult(false, versionError);
        }

        if (target.Kind == NuGetFeedKind.LocalFolder)
        {
            var folder = NuGetFeedCatalog.ValidateLocalFolder(ownerOrFolder);
            var file = Path.Combine(folder, $"{NuGetFeedCatalog.NormalizeIdForUrl(packageId)}.{version}.nupkg");
            if (!File.Exists(file))
            {
                file = Directory.Exists(folder)
                    ? Directory.EnumerateFiles(folder, "*.nupkg")
                        .FirstOrDefault(path => Path.GetFileNameWithoutExtension(path)
                            .EndsWith($".{version}", StringComparison.OrdinalIgnoreCase))
                    : null;
            }

            if (file is null || !File.Exists(file))
            {
                return new NuGetOperationResult(false, $"{packageId} {version} was not found in {folder}.");
            }

            File.Delete(file);
            return new NuGetOperationResult(true, $"Deleted {Path.GetFileName(file)} from the local folder.");
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new NuGetOperationResult(false, $"Deleting from {target.Label} requires an API key (GitHub PATs also need delete:packages).");
        }

        var source = NuGetFeedCatalog.GetPushSource(target, ownerOrFolder);
        var arguments = NuGetCommandBuilder.BuildDeleteArguments(DotnetPath, packageId, version, source, apiKey);
        writeOutput(Environment.NewLine + $"=== Deleting {packageId} {version} from {target.Label} ===" + Environment.NewLine);
        writeOutput(NuGetCommandBuilder.Mask(arguments) + Environment.NewLine + Environment.NewLine);
        var exitCode = await _run(arguments, writeOutput, cancellationToken, null).ConfigureAwait(false);
        return exitCode == 0
            ? new NuGetOperationResult(true, $"Deleted {packageId} {version} from {target.Label}.")
            : new NuGetOperationResult(false, $"Delete from {target.Label} failed (exit code {exitCode}). See the live output.");
    }

    /// <summary>The newest .nupkg in a folder, optionally filtered to one package id.</summary>
    public static string? FindNewestNupkg(string folder, string? packageId = null)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return null;
        }

        var prefix = packageId is null ? null : NuGetFeedCatalog.NormalizeIdForUrl(packageId) + ".";
        return Directory
            .EnumerateFiles(folder, "*.nupkg", SearchOption.TopDirectoryOnly)
            .Where(path => prefix is null
                || Path.GetFileNameWithoutExtension(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private async Task ClearCachedCopyAsync(
        NuGetPackageIdentity identity,
        Action<string> writeOutput,
        CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        var exitCode = await _run(
            NuGetCommandBuilder.BuildLocalsArguments(DotnetPath),
            text => output.Append(text),
            cancellationToken,
            null).ConfigureAwait(false);

        var line = output.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(text => text.StartsWith("global-packages:", StringComparison.OrdinalIgnoreCase));
        if (exitCode != 0 || line is null)
        {
            return;
        }

        var folder = line["global-packages:".Length..].Trim();
        var cached = Path.Combine(folder, NuGetFeedCatalog.NormalizeIdForUrl(identity.Id), identity.Version);
        if (Directory.Exists(cached))
        {
            TryDeleteDirectory(cached);
            writeOutput($"Cleared the cached copy of {identity.Id} {identity.Version} so the test installs the fresh artifact.{Environment.NewLine}");
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // A leftover temp folder is harmless; the test still uses a fresh consumer project.
        }
    }
}
