namespace DotNetAppPublisher.Features.Publishing.Configure;

public static class PublishOptionPolicy
{
    public static bool IsReadyToRunEnabled(
        bool publishAot,
        string publishPlatform,
        string? targetFramework = null,
        string? runtimeIdentifier = null)
    {
        if (publishAot)
            return false;
        if (string.Equals(publishPlatform, PublishPlatforms.Android, StringComparison.Ordinal))
            return false;
        if (string.Equals(publishPlatform, PublishPlatforms.Ios, StringComparison.Ordinal))
            return false;
        if (string.Equals(publishPlatform, PublishPlatforms.MacOs, StringComparison.Ordinal)
            && PlatformProfiles.IsMacCatalyst(targetFramework ?? string.Empty, runtimeIdentifier ?? string.Empty))
            return false;
        return true;
    }

    public static string ReadyToRunDisabledReason(
        bool publishAot,
        string publishPlatform,
        string? targetFramework = null,
        string? runtimeIdentifier = null)
    {
        if (publishAot)
            return "Disabled: AOT and ReadyToRun are mutually exclusive (dotnet/runtime#126598).";
        if (string.Equals(publishPlatform, PublishPlatforms.Android, StringComparison.Ordinal))
            return "ReadyToRun is not supported for Android — use RunAOT instead.";
        if (string.Equals(publishPlatform, PublishPlatforms.Ios, StringComparison.Ordinal))
            return "ReadyToRun is not supported for iOS — the SDK AOTs automatically.";
        if (string.Equals(publishPlatform, PublishPlatforms.MacOs, StringComparison.Ordinal)
            && PlatformProfiles.IsMacCatalyst(targetFramework ?? string.Empty, runtimeIdentifier ?? string.Empty))
            return "ReadyToRun is not applicable to MacCatalyst; the Apple workload controls compilation.";
        return string.Empty;
    }

    public static bool IsAndroidAotEnabled(string androidLinkMode)
        => !string.Equals(androidLinkMode, "None", StringComparison.Ordinal);

    public static string AndroidAotDisabledReason(string androidLinkMode)
        => IsAndroidAotEnabled(androidLinkMode)
            ? string.Empty
            : "RunAOT requires linking — set Link Mode to SdkOnly or Full first.";

    public static bool IsPublishAotEnabled(string publishPlatform, string targetFramework, string runtimeIdentifier)
    {
        if (string.Equals(publishPlatform, PublishPlatforms.Android, StringComparison.Ordinal))
            return false;
        if (string.Equals(publishPlatform, PublishPlatforms.Ios, StringComparison.Ordinal))
            return false;
        if (string.Equals(publishPlatform, PublishPlatforms.MacOs, StringComparison.Ordinal)
            && PlatformProfiles.IsMacCatalyst(targetFramework, runtimeIdentifier))
            return false;
        return string.Equals(publishPlatform, PublishPlatforms.MacOs, StringComparison.Ordinal)
            || string.Equals(publishPlatform, PublishPlatforms.Windows, StringComparison.Ordinal)
            || string.Equals(publishPlatform, PublishPlatforms.Linux, StringComparison.Ordinal);
    }

    public static string PublishAotDisabledReason(string publishPlatform, string targetFramework, string runtimeIdentifier)
    {
        if (string.Equals(publishPlatform, PublishPlatforms.Android, StringComparison.Ordinal))
            return "Android uses RunAOTCompilation instead of PublishAot.";
        if (string.Equals(publishPlatform, PublishPlatforms.Ios, StringComparison.Ordinal))
            return "iOS is always trimmed and AOT is implied by the SDK.";
        if (string.Equals(publishPlatform, PublishPlatforms.MacOs, StringComparison.Ordinal)
            && PlatformProfiles.IsMacCatalyst(targetFramework, runtimeIdentifier))
            return "MacCatalyst always publishes trimmed (MtouchLink) — AOT flag not applicable.";
        if (!IsPublishAotEnabled(publishPlatform, targetFramework, runtimeIdentifier))
            return "AOT only applies to native desktop RIDs (osx, win, linux).";
        return string.Empty;
    }

    public static bool IsPublishTrimmedEnabled(
        bool publishAot,
        bool selfContained,
        string publishPlatform,
        string targetFramework,
        string runtimeIdentifier,
        string androidLinkMode)
    {
        if (publishAot)
            return false;
        if (string.Equals(publishPlatform, PublishPlatforms.Android, StringComparison.Ordinal))
            return selfContained && IsAndroidAotEnabled(androidLinkMode);
        if (!selfContained)
            return false;
        if (string.Equals(publishPlatform, PublishPlatforms.Ios, StringComparison.Ordinal))
            return false;
        if (string.Equals(publishPlatform, PublishPlatforms.MacOs, StringComparison.Ordinal)
            && PlatformProfiles.IsMacCatalyst(targetFramework, runtimeIdentifier))
            return false;
        return true;
    }

    public static string PublishTrimmedDisabledReason(
        bool publishAot,
        bool selfContained,
        string publishPlatform,
        string targetFramework,
        string runtimeIdentifier,
        string androidLinkMode)
    {
        if (publishAot)
            return "Trimming is implied by Native AOT.";
        if (string.Equals(publishPlatform, PublishPlatforms.Android, StringComparison.Ordinal))
            return IsAndroidAotEnabled(androidLinkMode)
                ? string.Empty
                : "Android trimming is driven by Link Mode — 'None' disables the linker.";
        if (!selfContained)
            return "Trimming requires a self-contained publish.";
        if (string.Equals(publishPlatform, PublishPlatforms.Ios, StringComparison.Ordinal))
            return "iOS SDK forces PublishTrimmed=true (Xamarin.Shared.Sdk.targets).";
        if (string.Equals(publishPlatform, PublishPlatforms.MacOs, StringComparison.Ordinal)
            && PlatformProfiles.IsMacCatalyst(targetFramework, runtimeIdentifier))
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
        if (string.Equals(publishPlatform, PublishPlatforms.Ios, StringComparison.Ordinal))
            return false;
        return true;
    }

    public static bool IsArchiveOnBuildEnabled(string publishPlatform, string runtimeIdentifier)
    {
        if (!string.Equals(publishPlatform, PublishPlatforms.Ios, StringComparison.Ordinal))
            return false;
        return !runtimeIdentifier.StartsWith("iossimulator-", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsBuildIpaEnabled(string publishPlatform, string runtimeIdentifier)
        => IsArchiveOnBuildEnabled(publishPlatform, runtimeIdentifier);

    public static bool IsAndroidDexToolSupported(string androidDexTool)
        => string.Equals(androidDexTool.Trim(), "d8", StringComparison.OrdinalIgnoreCase);

    public static bool IsAndroidLinkToolSupported(string androidLinkTool, string androidDexTool)
        => IsAndroidDexToolSupported(androidDexTool)
            && string.Equals(androidLinkTool.Trim(), "r8", StringComparison.OrdinalIgnoreCase);

    public static string AndroidToolValidationMessage(string androidLinkTool, string androidDexTool)
    {
        if (!IsAndroidDexToolSupported(androidDexTool))
            return "The .NET 10 Android workload supports D8; the legacy DX compiler is deprecated and is not a supported profile.";

        if (!string.Equals(androidLinkTool.Trim(), "r8", StringComparison.OrdinalIgnoreCase))
            return "D8 requires the R8 code shrinker; ProGuard with D8 is no longer supported.";

        return string.Empty;
    }

    public static bool IsPublishSingleFileEnabled(bool publishAot, string publishPlatform)
    {
        // Native AOT already emits a native executable; adding the single-file
        // bundler on top is redundant and can create extraction/linking surprises.
        if (publishAot)
            return false;
        if (string.Equals(publishPlatform, PublishPlatforms.Android, StringComparison.Ordinal))
            return false;
        if (string.Equals(publishPlatform, PublishPlatforms.Ios, StringComparison.Ordinal))
            return false;
        return true;
    }
}
