namespace DotNetAppPublisher.Features.Deployment.Android;

/// <summary>How a locally built app relates to the copy Google Play would deliver.</summary>
public enum AndroidPlayUpdateCompatibility
{
    /// <summary>Nothing was compared, so nothing can be said.</summary>
    Unknown,

    /// <summary>The local build carries the same certificate Google Play signs with, so a Play
    /// Store update can replace it.</summary>
    PlayUpdatable,

    /// <summary>The local build carries a different certificate, so a Play Store update cannot
    /// replace it. Only a build signed with the Play app signing key can be updated by Play.</summary>
    NotPlayUpdatable,
}

/// <summary>
/// Compares the certificate of a local build with the certificate Google Play signs releases
/// with, and says whether a later Play Store update can replace the local install.
///
/// Play re-signs every download with the app signing key, so a build signed with the upload key
/// is a different signer to the platform. Android only accepts an update when the signing
/// certificate matches, so such an install can never be updated by Play again.
/// </summary>
public static class AndroidPlayUpdateCheck
{
    /// <summary>
    /// Compares the certificate digests of a local build with the ones Google Play signs with.
    /// The digests are normalized before the comparison, so the Play Console's colon-separated
    /// uppercase form and the lowercase form apksigner prints are equal.
    /// </summary>
    public static AndroidPlayUpdateCompatibility Compare(
        IReadOnlyList<string>? localDigests,
        IReadOnlyList<string>? playDigests)
    {
        var local = Normalize(localDigests);
        var play = Normalize(playDigests);
        if (local.Count == 0 || play.Count == 0)
        {
            return AndroidPlayUpdateCompatibility.Unknown;
        }

        return local.SetEquals(play)
            ? AndroidPlayUpdateCompatibility.PlayUpdatable
            : AndroidPlayUpdateCompatibility.NotPlayUpdatable;
    }

    /// <summary>
    /// The message that tells the user what the comparison means and what to do about it.
    /// </summary>
    public static string Describe(
        AndroidPlayUpdateCompatibility compatibility,
        IReadOnlyList<string>? localDigests,
        IReadOnlyList<string>? playDigests)
    {
        var local = string.Join(", ", Normalize(localDigests));
        var play = string.Join(", ", Normalize(playDigests));

        return compatibility switch
        {
            AndroidPlayUpdateCompatibility.PlayUpdatable =>
                $"This build carries the same certificate Google Play signs with ({local}), so a Play Store " +
                "update can replace it.",
            AndroidPlayUpdateCompatibility.NotPlayUpdatable =>
                $"This build is signed with a different certificate than Google Play delivers ({local} versus {play}). " +
                "Android only accepts an update when the certificate matches, so a Play Store update will not " +
                "replace this install. Sign the build with the app signing key, or install the signed universal " +
                "APK that Play Console offers, and keep every version code higher than the one already installed.",
            _ =>
                "The signing certificate of this build could not be compared with the one Google Play signs with, " +
                "so it is unknown whether a Play Store update can replace it.",
        };
    }

    private static HashSet<string> Normalize(IReadOnlyList<string>? digests) =>
        digests is null
            ? []
            : digests
                .Where(digest => !string.IsNullOrWhiteSpace(digest))
                .Select(digest => digest.Replace(":", string.Empty, StringComparison.Ordinal).Trim().ToLowerInvariant())
                .ToHashSet(StringComparer.Ordinal);
}
