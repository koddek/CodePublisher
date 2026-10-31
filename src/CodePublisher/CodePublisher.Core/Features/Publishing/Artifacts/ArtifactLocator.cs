namespace CodePublisher.Features.Publishing.Artifacts;

public static class ArtifactLocator
{
    public static FileInfo? FindBestApk(string outputDirectory, string? projectName = null, string? packageId = null) =>
        FindBest(outputDirectory, "*.apk", projectName, packageId);

    public static FileInfo? FindBestAab(string outputDirectory, string? projectName = null, string? packageId = null) =>
        FindBest(outputDirectory, "*.aab", projectName, packageId);

    /// <summary>
    /// The single folder that holds the published packages: the directory of the newest best-scored
    /// APK or AAB under <paramref name="outputDirectory"/>. Null when no package exists yet.
    /// </summary>
    public static DirectoryInfo? FindBestPackageFolder(string outputDirectory, string? projectName = null, string? packageId = null)
    {
        var apk = FindBestApk(outputDirectory, projectName, packageId);
        var aab = FindBestAab(outputDirectory, projectName, packageId);
        var best = (apk, aab) switch
        {
            (null, null) => null,
            (not null, null) => apk,
            (null, not null) => aab,
            _ => apk!.LastWriteTimeUtc >= aab!.LastWriteTimeUtc ? apk : aab
        };

        return best?.Directory;
    }

    /// <summary>
    /// The SDK-built universal APK for an AAB+APK publish: when the AAB is the primary format the SDK
    /// derives a universal APK from the bundle and leaves it in the build output rather than the
    /// publish folder. Searches the given roots for the best <c>*-Signed.apk</c> without throwing for
    /// missing directories. Null when no signed APK exists.
    /// </summary>
    public static FileInfo? FindBestUniversalApk(
        IEnumerable<string> searchRoots,
        string? projectName = null,
        string? packageId = null) =>
        searchRoots
            .Where(root => !string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
            .SelectMany(root => new DirectoryInfo(root).EnumerateFiles("*-Signed.apk", SearchOption.AllDirectories))
            .OrderByDescending(file => GetScore(file, projectName, packageId))
            .ThenByDescending(file => file.LastWriteTimeUtc)
            .ThenBy(file => file.FullName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

    private static FileInfo? FindBest(
        string outputDirectory,
        string searchPattern,
        string? projectName,
        string? packageId)
    {
        var directory = new DirectoryInfo(outputDirectory);
        if (!directory.Exists)
        {
            return null;
        }

        return directory
            .EnumerateFiles(searchPattern, SearchOption.AllDirectories)
            .OrderByDescending(file => GetScore(file, projectName, packageId))
            .ThenByDescending(file => file.LastWriteTimeUtc)
            .ThenBy(file => file.FullName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static int GetScore(FileInfo artifact, string? projectName, string? packageId)
    {
        var score = 0;
        var fileName = artifact.Name;
        var stem = Path.GetFileNameWithoutExtension(fileName);

        // The selected project's own package must beat another project's package in the same
        // output folder, even when the other one is signed and this one is not. A package id is
        // the strongest signal because Android names artifacts after it; the project name is the
        // fallback for desktop and library artifacts.
        if (!string.IsNullOrWhiteSpace(packageId))
        {
            var id = packageId.Trim();
            if (stem.StartsWith(id, StringComparison.OrdinalIgnoreCase))
            {
                score += 1500;
            }
            else if (fileName.Contains(id, StringComparison.OrdinalIgnoreCase))
            {
                score += 750;
            }
        }

        if (!string.IsNullOrWhiteSpace(projectName))
        {
            var name = projectName.Trim();
            if (stem.StartsWith(name, StringComparison.OrdinalIgnoreCase))
            {
                score += 1000;
            }
            else if (fileName.Contains(name, StringComparison.OrdinalIgnoreCase))
            {
                score += 500;
            }
        }

        if (fileName.EndsWith("-Signed.apk", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith("-Signed.aab", StringComparison.OrdinalIgnoreCase))
        {
            score += 100;
        }

        if (fileName.EndsWith("-debug.apk", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith("-debug.aab", StringComparison.OrdinalIgnoreCase))
        {
            score -= 50;
        }

        return score;
    }
}
