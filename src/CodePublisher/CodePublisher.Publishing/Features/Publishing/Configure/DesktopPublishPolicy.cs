namespace CodePublisher.Features.Publishing.Configure;

/// <summary>
/// The desktop capability gates (macOS, Windows, Linux): where Native AOT applies and what it
/// implies. Shared by the three desktop libraries through the composition root; each platform
/// library owns its own profile and command, not this predicate set.
/// </summary>
public static class DesktopPublishPolicy
{
    public static bool IsReadyToRunEnabled(bool publishAot) => !publishAot;

    public static bool IsPublishAotEnabled(string publishPlatform)
        => string.Equals(publishPlatform, PublishPlatforms.MacOs, StringComparison.Ordinal)
            || string.Equals(publishPlatform, PublishPlatforms.Windows, StringComparison.Ordinal)
            || string.Equals(publishPlatform, PublishPlatforms.Linux, StringComparison.Ordinal);

    public static string PublishAotDisabledReason() => "AOT only applies to native desktop RIDs (osx, win, linux).";

    public static bool IsPublishSingleFileEnabled(bool publishAot) => !publishAot;
}
