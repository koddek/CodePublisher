using CodePublisher.Features.Publishing.Inspect;

namespace CodePublisher.Features.Publishing.Configure;

/// <summary>
/// What the Apple toolchain in this app can and cannot do, and why not. iOS and Mac Catalyst
/// share the forced trimming behavior of the Apple workload; the reasons are split by platform so
/// each says what actually applies to it.
/// </summary>
public static class ApplePublishPolicy
{
    public static bool IsReadyToRunEnabled() => false;

    /// <summary>
    /// On .NET 11 the Apple workload runs CoreCLR and enables ReadyToRun for the whole app by
    /// default. Verified with SDK 11.0.100: <c>PublishReadyToRun</c> evaluates to true for both
    /// iOS and Mac Catalyst, in Release and Debug, with no explicit property. The app must not
    /// emit <c>PublishReadyToRun=false</c> or it would turn the SDK's precompilation off.
    /// </summary>
    public static bool IsReadyToRunAutomatic(string? targetFramework) =>
        ProjectInspector.GetTargetFrameworkVersion(targetFramework) >= 11;

    public static string ReadyToRunDisabledReason(string platform, string? targetFramework = null)
    {
        if (IsReadyToRunAutomatic(targetFramework))
        {
            return "ReadyToRun is on automatically: .NET 11 CoreCLR precompiles the whole Apple app. " +
                "The SDK sets it, so the switch is read-only.";
        }

        return string.Equals(platform, PublishPlatforms.Ios, StringComparison.Ordinal)
            ? "ReadyToRun is not supported for iOS — the SDK AOTs automatically."
            : "ReadyToRun is not applicable to MacCatalyst; the Apple workload controls compilation.";
    }

    public static bool IsPublishAotEnabled() => false;

    public static string PublishAotDisabledReason(string platform)
        => string.Equals(platform, PublishPlatforms.Ios, StringComparison.Ordinal)
            ? "iOS is always trimmed and AOT is implied by the SDK."
            : "MacCatalyst always publishes trimmed — AOT flag not applicable.";

    /// <summary>The Apple workload forces trimming on; there is no opt-out switch to flip.</summary>
    public static bool IsPublishTrimmedEnabled() => false;

    /// <summary>
    /// Whether the .NET 11 Apple workload sets <c>PublishTrimmed</c> itself. Setting the property
    /// explicitly is an error on .NET 11; <c>MtouchLink</c> is the supported control.
    /// </summary>
    public static bool IsPublishTrimmedAutomatic(string? targetFramework) =>
        ProjectInspector.GetTargetFrameworkVersion(targetFramework) >= 11;

    public static string PublishTrimmedDisabledReason(string platform, string? targetFramework = null)
    {
        if (IsPublishTrimmedAutomatic(targetFramework))
        {
            return "Trimming is on automatically: the .NET 11 Apple workload controls it with " +
                "MtouchLink. Set MtouchLink=None in the project to relax linking.";
        }

        return string.Equals(platform, PublishPlatforms.Ios, StringComparison.Ordinal)
            ? "iOS SDK forces PublishTrimmed=true; to relax linking set MtouchLink=None in the project."
            : "MacCatalyst forces PublishTrimmed=true; to relax linking set MtouchLink=None in the project.";
    }

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
