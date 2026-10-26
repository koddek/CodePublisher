namespace CodePublisher.Features.GooglePlay.Tracks;

/// <summary>
/// What a device currently has installed, as far as the release lines are concerned.
/// </summary>
/// <param name="InstalledVersionCode">The version code the device runs, when it could be read.</param>
/// <param name="InstalledByGooglePlay">True when Google Play installed the app.</param>
/// <param name="InstalledCertificateDigests">The certificate digests of the installed copy.</param>
public sealed record GooglePlayDeviceLineState(
    int? InstalledVersionCode,
    bool InstalledByGooglePlay,
    IReadOnlyList<string> InstalledCertificateDigests);

/// <summary>One release line and the version codes it serves.</summary>
/// <param name="TrackId">The track identifier.</param>
/// <param name="DisplayName">The name shown to the user.</param>
/// <param name="IsProduction">True for the production line.</param>
/// <param name="VersionCodes">The version codes the line serves, highest first.</param>
public sealed record GooglePlayTrackLineState(
    string TrackId,
    string DisplayName,
    bool IsProduction,
    IReadOnlyList<string> VersionCodes);

/// <summary>What it takes to move a device onto a target line.</summary>
/// <param name="CanReachTarget">True when the target line can serve the device right now.</param>
/// <param name="RequiredVersionCode">The version code the target line needs, when the device cannot
/// reach it yet.</param>
/// <param name="BlockingReason">Why the device cannot reach the target, in plain words.</param>
/// <param name="Steps">What the user has to do, in order.</param>
public sealed record GooglePlayReleaseLinePlan(
    bool CanReachTarget,
    int? RequiredVersionCode,
    string BlockingReason,
    IReadOnlyList<string> Steps);

/// <summary>
/// Works out whether a device can move onto a target release line, and what has to happen first.
///
/// Google Play serves the highest version code the user is eligible for across every line, and it
/// never downgrades. So a device on a testing line only moves to production when production
/// carries a version code higher than the one the device runs, and no other line is still serving
/// something higher. The version code the device runs is the floor: a production build below it
/// is never delivered to that device.
/// </summary>
public static class GooglePlayReleaseLinePolicy
{
    /// <summary>
    /// Plans the move of a device onto the target line.
    /// </summary>
    /// <param name="device">What the device has installed.</param>
    /// <param name="tracks">Every line and the version codes it serves.</param>
    /// <param name="targetTrackId">The line the user wants the device on.</param>
    public static GooglePlayReleaseLinePlan Plan(
        GooglePlayDeviceLineState device,
        IReadOnlyList<GooglePlayTrackLineState> tracks,
        string targetTrackId)
    {
        var target = tracks.FirstOrDefault(track =>
            string.Equals(track.TrackId, targetTrackId, StringComparison.Ordinal));
        if (target is null)
        {
            return new GooglePlayReleaseLinePlan(
                CanReachTarget: false,
                RequiredVersionCode: null,
                BlockingReason: $"The {targetTrackId} line does not exist for this app.",
                Steps: []);
        }

        var targetVersionCode = HighestVersionCode(target.VersionCodes);
        var installedVersionCode = device.InstalledVersionCode;

        if (installedVersionCode is null)
        {
            return new GooglePlayReleaseLinePlan(
                CanReachTarget: false,
                RequiredVersionCode: null,
                BlockingReason:
                    "The version code the device runs could not be read, so it is unknown whether " +
                    "the device can reach this line.",
                Steps:
                [
                    "Connect the device and select it, then check again.",
                ]);
        }

        // Every other line the user can still receive from. A line serving a higher version code
        // than the target keeps the device on it, because Play serves the highest eligible one.
        var competingVersionCode = tracks
            .Where(track => !string.Equals(track.TrackId, targetTrackId, StringComparison.Ordinal))
            .Select(track => HighestVersionCode(track.VersionCodes) ?? 0)
            .DefaultIfEmpty(0)
            .Max();

        var requiredVersionCode = Math.Max(installedVersionCode.Value, competingVersionCode) + 1;

        if (targetVersionCode is { } served && served >= requiredVersionCode)
        {
            return new GooglePlayReleaseLinePlan(
                CanReachTarget: true,
                RequiredVersionCode: null,
                BlockingReason: string.Empty,
                Steps: []);
        }

        var steps = new List<string>();
        if (competingVersionCode >= installedVersionCode.Value)
        {
            steps.Add(
                "Remove the release from the other line, so it stops serving a version code higher " +
                "than the one you want the device on.");
        }

        steps.Add(
            $"Publish the {target.DisplayName} line with version code {requiredVersionCode} or higher. " +
            $"The device runs version code {installedVersionCode}, and Google Play never downgrades.");

        return new GooglePlayReleaseLinePlan(
            CanReachTarget: false,
            RequiredVersionCode: requiredVersionCode,
            BlockingReason:
                $"The {target.DisplayName} line does not yet carry a version code high enough for this " +
                $"device (it runs version code {installedVersionCode}).",
            Steps: steps);
    }

    /// <summary>
    /// The line the device is effectively on: the one serving the highest version code the device
    /// is eligible for. When several lines qualify, the highest version code wins, because that is
    /// what Google Play delivers.
    /// </summary>
    public static GooglePlayTrackLineState? EffectiveLine(
        GooglePlayDeviceLineState device,
        IReadOnlyList<GooglePlayTrackLineState> tracks)
    {
        if (device.InstalledVersionCode is not { } installed)
        {
            return null;
        }

        return tracks
            .Where(track => track.VersionCodes.Any(code => ParseVersionCode(code) == installed))
            .OrderByDescending(track => HighestVersionCode(track.VersionCodes) ?? 0)
            .FirstOrDefault();
    }

    private static int? HighestVersionCode(IReadOnlyList<string> versionCodes)
    {
        var max = 0;
        foreach (var versionCode in versionCodes)
        {
            if (ParseVersionCode(versionCode) is { } value && value > max)
            {
                max = value;
            }
        }

        return max > 0 ? max : null;
    }

    private static int? ParseVersionCode(string versionCode) =>
        int.TryParse(versionCode, out var value) ? value : null;
}
