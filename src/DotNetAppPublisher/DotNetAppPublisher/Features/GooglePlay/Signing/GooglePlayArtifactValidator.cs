using System.IO.Compression;
using DotNetAppPublisher.Features.GooglePlay.Models;

namespace DotNetAppPublisher.Features.GooglePlay.Signing;

internal sealed class GooglePlayArtifactValidator
{
    private static readonly string[] RequiredEntries =
    [
        "BundleConfig.pb"
    ];

    private static readonly string[] AcceptedManifestEntries =
    [
        "base/manifest/AndroidManifest.xml",
        "base/manifest/AndroidManifest.pb"
    ];

    public async Task<GooglePlayArtifactInfo> ValidateAsync(
        string aabPath,
        CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsBrowser())
        {
            return new GooglePlayArtifactInfo(
                aabPath,
                0,
                false,
                "AAB validation is available in the desktop app only.",
                RequiredEntries);
        }

        return await ValidateDesktopAsync(aabPath, cancellationToken);
    }

    private async Task<GooglePlayArtifactInfo> ValidateDesktopAsync(
        string aabPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(aabPath))
        {
            return new GooglePlayArtifactInfo(aabPath, 0, false, "The Android App Bundle was not found.", RequiredEntries);
        }

        var file = new FileInfo(aabPath);
        if (file.Length == 0)
        {
            return new GooglePlayArtifactInfo(aabPath, 0, false, "The Android App Bundle is empty.", RequiredEntries);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var stream = File.OpenRead(aabPath);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            var entries = archive.Entries.Select(entry => entry.FullName.Replace('\\', '/')).ToHashSet(StringComparer.Ordinal);
            var missing = RequiredEntries.Where(required => !entries.Contains(required)).ToList();
            if (!AcceptedManifestEntries.Any(entries.Contains))
            {
                missing.Add("base/manifest/AndroidManifest.xml");
            }

            var missingEntries = missing.ToArray();
            return new GooglePlayArtifactInfo(
                aabPath,
                file.Length,
                missingEntries.Length == 0,
                missingEntries.Length == 0
                    ? "Android App Bundle structure check passed. Package, version, and signature are verified by Google Play during upload."
                    : $"Android App Bundle is missing: {string.Join(", ", missingEntries)}.",
                RequiredEntries);
        }
        catch (InvalidDataException)
        {
            return new GooglePlayArtifactInfo(aabPath, file.Length, false, "The Android App Bundle is not a readable ZIP archive.", RequiredEntries);
        }
    }
}
