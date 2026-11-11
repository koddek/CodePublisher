namespace CodePublisher.Features.Publishing.Configure;

/// <summary>
/// What the Apple toolchain in this app can and cannot do, and why not. iOS and Mac Catalyst
/// share the forced trimming behavior of the Apple workload; the reasons are split by platform so
/// each says what actually applies to it.
/// </summary>
public static class ApplePublishPolicy
{
    public static bool IsReadyToRunEnabled() => false;

    public static string ReadyToRunDisabledReason(string platform)
        => string.Equals(platform, PublishPlatforms.Ios, StringComparison.Ordinal)
            ? "ReadyToRun is not supported for iOS — the SDK AOTs automatically."
            : "ReadyToRun is not applicable to MacCatalyst; the Apple workload controls compilation.";

    public static bool IsPublishAotEnabled() => false;

    public static string PublishAotDisabledReason(string platform)
        => string.Equals(platform, PublishPlatforms.Ios, StringComparison.Ordinal)
            ? "iOS is always trimmed and AOT is implied by the SDK."
            : "MacCatalyst always publishes trimmed — AOT flag not applicable.";

    /// <summary>The Apple workload forces trimming on; there is no opt-out switch to flip.</summary>
    public static bool IsPublishTrimmedEnabled() => false;

    public static string PublishTrimmedDisabledReason(string platform)
        => string.Equals(platform, PublishPlatforms.Ios, StringComparison.Ordinal)
            ? "iOS SDK forces PublishTrimmed=true; to relax linking set TrimMode=copy in the project."
            : "MacCatalyst forces PublishTrimmed=true; to relax linking set TrimMode=copy in the project.";

    public static bool IsPublishSingleFileEnabled() => false;

    public static bool IsUseAppHostEnabled(string platform)
        => !string.Equals(platform, PublishPlatforms.Ios, StringComparison.Ordinal);

    public static bool IsArchiveOnBuildEnabled(string platform, string runtimeIdentifier)
    {
        if (!string.Equals(platform, PublishPlatforms.Ios, StringComparison.Ordinal))
        {
            return false;
        }

        return !runtimeIdentifier.StartsWith("iossimulator-", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsBuildIpaEnabled(string platform, string runtimeIdentifier)
        => IsArchiveOnBuildEnabled(platform, runtimeIdentifier);
}
