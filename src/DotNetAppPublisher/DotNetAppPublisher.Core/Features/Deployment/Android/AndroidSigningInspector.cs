namespace DotNetAppPublisher.Features.Deployment.Android;

/// <summary>
/// What the device reports about an already-installed package: who installed it (Google Play is
/// <c>com.android.vending</c>) and the SHA-256 digests of its signing certificates, empty when the
/// installed APK could not be inspected.
/// </summary>
public sealed record AndroidInstalledPackageInfo(string? InstallerPackage, IReadOnlyList<string> CertificateDigests);

/// <summary>
/// Pure parsers for signing and package-manager output. Google Play can only update an installed
/// app when the certificate on the device matches the certificate Play delivers, so install compares
/// certificates before touching the phone instead of discovering the clash afterwards.
/// </summary>
public static class AndroidSigningInspector
{
    private const string DigestLabel = "certificate SHA-256 digest";
    private const string PackagePrefix = "package:";
    private const string InstallerMarker = "installer=";
    public const string GooglePlayInstaller = "com.android.vending";

    /// <summary>
    /// Extracts every SHA-256 certificate digest from `apksigner verify --print-certs` output.
    /// Digests are normalized to lowercase hex without colons, whatever shape the tool printed.
    /// </summary>
    public static IReadOnlyList<string> ParseApkSignerDigests(string? output)
    {
        var digests = new List<string>();
        if (string.IsNullOrWhiteSpace(output))
        {
            return digests;
        }

        var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var index = 0; index < lines.Length; index++)
        {
            var labelStart = lines[index].IndexOf(DigestLabel, StringComparison.OrdinalIgnoreCase);
            if (labelStart < 0)
            {
                continue;
            }

            var value = lines[index][(labelStart + DigestLabel.Length)..].Trim().TrimStart(':').Trim();
            if (value.Length == 0 && index + 1 < lines.Length)
            {
                value = lines[index + 1].Trim();
            }

            if (NormalizeDigest(value) is { } digest && !digests.Contains(digest, StringComparer.Ordinal))
            {
                digests.Add(digest);
            }
        }

        return digests;
    }

    /// <summary>
    /// True when both sides carry the same set of signing certificates. Either side being empty
    /// means "unknown", never "matches": an unknown comparison must not be reported as safe.
    /// </summary>
    public static bool CertificatesMatch(IReadOnlyList<string>? apkDigests, IReadOnlyList<string>? installedDigests)
    {
        var left = DistinctNormalized(apkDigests);
        var right = DistinctNormalized(installedDigests);
        if (left.Length == 0 || right.Length != left.Length)
        {
            return false;
        }

        var rightSet = new HashSet<string>(right, StringComparer.Ordinal);
        return left.All(rightSet.Contains);
    }

    /// <summary>
    /// Picks the base APK path from `pm path &lt;package&gt;`, preferring `base.apk` when split APKs
    /// are listed. Returns null when the package is not installed for this user.
    /// </summary>
    public static string? ParseBaseApkPath(string? pmPathOutput)
    {
        if (string.IsNullOrWhiteSpace(pmPathOutput))
        {
            return null;
        }

        string? firstPath = null;
        foreach (var rawLine in pmPathOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!rawLine.StartsWith(PackagePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var path = rawLine[PackagePrefix.Length..].Trim();
            if (path.Length == 0)
            {
                continue;
            }

            firstPath ??= path;
            if (path.EndsWith("/base.apk", StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }
        }

        return firstPath;
    }

    /// <summary>
    /// Reads the installer of record from `pm list packages -i &lt;package&gt;`. Returns null when the
    /// package is missing from the listing or was installed outside any known store (`installer=null`).
    /// </summary>
    public static string? ParseInstallerPackage(string? pmListOutput, string packageId)
    {
        if (string.IsNullOrWhiteSpace(pmListOutput) || string.IsNullOrWhiteSpace(packageId))
        {
            return null;
        }

        var expected = PackagePrefix + packageId.Trim();
        foreach (var rawLine in pmListOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var line = rawLine.Trim();
            if (!line.StartsWith(expected, StringComparison.Ordinal)
                || (line.Length > expected.Length && !char.IsWhiteSpace(line[expected.Length])))
            {
                continue;
            }

            var markerIndex = line.IndexOf(InstallerMarker, StringComparison.Ordinal);
            if (markerIndex < 0)
            {
                continue;
            }

            var installer = line[(markerIndex + InstallerMarker.Length)..].Trim();
            return installer.Length == 0 || string.Equals(installer, "null", StringComparison.OrdinalIgnoreCase)
                ? null
                : installer;
        }

        return null;
    }

    /// <summary>True when Google Play is the installer of record for the package.</summary>
    public static bool IsGooglePlayInstaller(string? installerPackage) =>
        string.Equals(installerPackage?.Trim(), GooglePlayInstaller, StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeDigest(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var compact = value.Replace(":", string.Empty, StringComparison.Ordinal).Trim();
        if (compact.Length != 64)
        {
            return null;
        }

        foreach (var character in compact)
        {
            var isHex = character is >= '0' and <= '9'
                or >= 'a' and <= 'f'
                or >= 'A' and <= 'F';
            if (!isHex)
            {
                return null;
            }
        }

        return compact.ToLowerInvariant();
    }

    private static string[] DistinctNormalized(IReadOnlyList<string>? digests) =>
        digests is null
            ? []
            : digests
                .Where(digest => !string.IsNullOrWhiteSpace(digest))
                .Select(digest => digest.Replace(":", string.Empty, StringComparison.Ordinal).Trim().ToLowerInvariant())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(digest => digest, StringComparer.Ordinal)
                .ToArray();
}
