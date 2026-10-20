using DotNetAppPublisher.Features.GooglePlay.Models;

namespace DotNetAppPublisher.Features.GooglePlay.Tracks;

/// <summary>
/// The release tracks a Play app can use, and what each one means for a release.
///
/// Track identifiers come from <c>edits.tracks.list</c> and are used verbatim. The three default
/// tracks are <c>production</c>, <c>beta</c> (open testing) and <c>qa</c> (internal testing).
/// Closed testing tracks carry the name the developer gave them, and form-factor tracks carry a
/// prefix such as <c>wear:production</c>.
/// </summary>
public static class GooglePlayTrackCatalog
{
    /// <summary>The production track. Every user is eligible for it.</summary>
    public const string Production = "production";

    /// <summary>The open testing track.</summary>
    public const string OpenTesting = "beta";

    /// <summary>The internal testing track. Only its own testers receive its releases.</summary>
    public const string InternalTesting = "qa";

    /// <summary>The legacy default closed testing track.</summary>
    public const string ClosedTesting = "alpha";

    /// <summary>The form-factor prefixes Play uses for dedicated release tracks.</summary>
    public static readonly IReadOnlyList<string> FormFactorPrefixes =
    [
        "automotive",
        "android_xr",
        "google_play_games_pc",
        "tv",
        "wear",
    ];

    /// <summary>True when the track is the production track or a form-factor production track.</summary>
    public static bool IsProduction(string trackId) =>
        string.Equals(trackId, Production, StringComparison.Ordinal) ||
        trackId.EndsWith(":production", StringComparison.Ordinal);

    /// <summary>True when the track serves internal testing.</summary>
    public static bool IsInternalTesting(string trackId) =>
        string.Equals(trackId, InternalTesting, StringComparison.Ordinal) ||
        string.Equals(trackId, "internal", StringComparison.Ordinal) ||
        trackId.EndsWith(":qa", StringComparison.Ordinal) ||
        trackId.EndsWith(":internal", StringComparison.Ordinal);

    /// <summary>True when the track serves open testing.</summary>
    public static bool IsOpenTesting(string trackId) =>
        string.Equals(trackId, OpenTesting, StringComparison.Ordinal) ||
        trackId.EndsWith(":beta", StringComparison.Ordinal);

    /// <summary>True when the track is a closed testing track, either the legacy default one or a
    /// track with a developer-given name.</summary>
    public static bool IsClosedTesting(string trackId) =>
        !IsProduction(trackId) &&
        !IsInternalTesting(trackId) &&
        !IsOpenTesting(trackId);

    /// <summary>True when the track belongs to a form factor, such as <c>wear:production</c>.</summary>
    public static bool IsFormFactor(string trackId)
    {
        var separator = trackId.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0)
        {
            return false;
        }

        var prefix = trackId[..separator];
        return FormFactorPrefixes.Contains(prefix, StringComparer.Ordinal);
    }

    /// <summary>The form-factor prefix of a track, or null when it is a default track.</summary>
    public static string? FormFactorOf(string trackId)
    {
        var separator = trackId.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0)
        {
            return null;
        }

        var prefix = trackId[..separator];
        return FormFactorPrefixes.Contains(prefix, StringComparer.Ordinal) ? prefix : null;
    }

    /// <summary>The track name without its form-factor prefix.</summary>
    public static string NameWithoutFormFactor(string trackId)
    {
        var separator = trackId.IndexOf(':', StringComparison.Ordinal);
        return separator <= 0 ? trackId : trackId[(separator + 1)..];
    }

    /// <summary>
    /// A human-readable name for a track. Production and the default testing tracks get their
    /// Play Console names; a named closed testing track keeps the name the developer gave it.
    /// </summary>
    public static string DisplayName(string trackId)
    {
        if (IsProduction(trackId))
        {
            return "Production";
        }

        if (IsInternalTesting(trackId))
        {
            return "Internal testing";
        }

        if (IsOpenTesting(trackId))
        {
            return "Open testing";
        }

        if (string.Equals(trackId, ClosedTesting, StringComparison.Ordinal))
        {
            return "Closed testing";
        }

        return NameWithoutFormFactor(trackId);
    }

    /// <summary>The group a track belongs to, used to order the track list.</summary>
    public static string Group(string trackId)
    {
        if (IsProduction(trackId))
        {
            return "Production";
        }

        if (IsInternalTesting(trackId))
        {
            return "Internal testing";
        }

        if (IsOpenTesting(trackId))
        {
            return "Open testing";
        }

        if (IsFormFactor(trackId))
        {
            return "Form factor";
        }

        return "Closed testing";
    }

    /// <summary>
    /// Builds the track record the UI shows. The release status comes from Play itself, so the
    /// user sees the real state of every line rather than an assumption.
    /// </summary>
    public static GooglePlayTrackInfo Describe(string trackId, string? releaseStatus, IReadOnlyList<string> versionCodes)
    {
        var status = string.IsNullOrWhiteSpace(releaseStatus) ? "Not configured" : releaseStatus;
        if (versionCodes.Count > 0)
        {
            status = $"{status} · version {string.Join(", ", versionCodes)}";
        }

        return new GooglePlayTrackInfo(
            trackId,
            DisplayName(trackId),
            Group(trackId),
            status,
            IsProduction(trackId))
        {
            VersionCodes = versionCodes
        };
    }
}
