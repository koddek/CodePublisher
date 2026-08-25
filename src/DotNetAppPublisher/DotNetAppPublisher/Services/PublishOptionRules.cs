namespace DotNetAppPublisher.Services;

public static class PublishOptionRules
{
    public static bool IsReadyToRunEnabled(bool publishAot) => !publishAot;

    public static string ReadyToRunDisabledReason(bool publishAot)
    {
        if (publishAot)
            return "Disabled: AOT and ReadyToRun are mutually exclusive (dotnet/runtime#126598).";
        return string.Empty;
    }

    public static bool IsPublishAotEnabled(string publishPlatform, string targetFramework, string runtimeIdentifier)
    {
        if (string.Equals(publishPlatform, PublisherService.AndroidPlatform, StringComparison.Ordinal))
            return false;
        if (string.Equals(publishPlatform, PublisherService.IosPlatform, StringComparison.Ordinal))
            return false;
        if (string.Equals(publishPlatform, PublisherService.MacOsPlatform, StringComparison.Ordinal)
            && PlatformDefaults.IsMacCatalyst(targetFramework, runtimeIdentifier))
            return false;
        return string.Equals(publishPlatform, PublisherService.MacOsPlatform, StringComparison.Ordinal)
            || string.Equals(publishPlatform, PublisherService.WindowsPlatform, StringComparison.Ordinal)
            || string.Equals(publishPlatform, PublisherService.LinuxPlatform, StringComparison.Ordinal);
    }

    public static string PublishAotDisabledReason(string publishPlatform, string targetFramework, string runtimeIdentifier)
    {
        if (string.Equals(publishPlatform, PublisherService.AndroidPlatform, StringComparison.Ordinal))
            return "Android uses RunAOTCompilation instead of PublishAot.";
        if (string.Equals(publishPlatform, PublisherService.IosPlatform, StringComparison.Ordinal))
            return "iOS is always trimmed and AOT is implied by the SDK.";
        if (string.Equals(publishPlatform, PublisherService.MacOsPlatform, StringComparison.Ordinal)
            && PlatformDefaults.IsMacCatalyst(targetFramework, runtimeIdentifier))
            return "MacCatalyst always publishes trimmed (MtouchLink) — AOT flag not applicable.";
        if (!IsPublishAotEnabled(publishPlatform, targetFramework, runtimeIdentifier))
            return "AOT only applies to native desktop RIDs (osx, win, linux).";
        return string.Empty;
    }

    public static bool IsPublishTrimmedEnabled(bool publishAot, string publishPlatform, string targetFramework, string runtimeIdentifier)
    {
        if (publishAot)
            return false;
        if (string.Equals(publishPlatform, PublisherService.IosPlatform, StringComparison.Ordinal))
            return false;
        if (string.Equals(publishPlatform, PublisherService.MacOsPlatform, StringComparison.Ordinal)
            && PlatformDefaults.IsMacCatalyst(targetFramework, runtimeIdentifier))
            return false;
        return true;
    }

    public static string PublishTrimmedDisabledReason(bool publishAot, string publishPlatform, string targetFramework, string runtimeIdentifier)
    {
        if (publishAot)
            return "Trimming is implied by Native AOT.";
        if (string.Equals(publishPlatform, PublisherService.IosPlatform, StringComparison.Ordinal))
            return "iOS SDK forces PublishTrimmed=true (Xamarin.Shared.Sdk.targets).";
        if (string.Equals(publishPlatform, PublisherService.MacOsPlatform, StringComparison.Ordinal)
            && PlatformDefaults.IsMacCatalyst(targetFramework, runtimeIdentifier))
            return "MacCatalyst forces PublishTrimmed=true — use MtouchLink=None to disable linking.";
        return string.Empty;
    }

    public static bool IsShrinkerSettingsEnabled(string androidLinkMode)
        => !string.Equals(androidLinkMode, "None", StringComparison.Ordinal);

    public static bool IsProfiledAotEnabled(bool runAotCompilation) => runAotCompilation;

    public static string ProfiledAotDisabledReason(bool runAotCompilation)
        => runAotCompilation ? string.Empty : "Enable RunAOT first; profiled AOT requires AOT.";

    public static bool IsUseAppHostEnabled(string publishPlatform)
    {
        if (string.Equals(publishPlatform, PublisherService.IosPlatform, StringComparison.Ordinal))
            return false;
        return true;
    }

    public static bool IsArchiveOnBuildEnabled(string publishPlatform, string runtimeIdentifier)
    {
        if (!string.Equals(publishPlatform, PublisherService.IosPlatform, StringComparison.Ordinal))
            return false;
        return !runtimeIdentifier.StartsWith("iossimulator-", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsBuildIpaEnabled(string publishPlatform, string runtimeIdentifier)
        => IsArchiveOnBuildEnabled(publishPlatform, runtimeIdentifier);

    public static bool IsPublishSingleFileEnabled(bool publishAot, string publishPlatform)
    {
        // SingleFile with AOT is allowed but emits IncludeNativeLibrariesForSelfExtract.
        // Keep enabled universally except where platform builder ignores it (iOS/Android).
        if (string.Equals(publishPlatform, PublisherService.AndroidPlatform, StringComparison.Ordinal))
            return false;
        if (string.Equals(publishPlatform, PublisherService.IosPlatform, StringComparison.Ordinal))
            return false;
        return true;
    }
}
