namespace CodePublisher.Features.Publishing.Configure;

/// <summary>
/// The one public surface for publish-option capability queries: "can this platform do X, and if
/// not, why not." The decisions live in the platform libraries (AndroidPublishPolicy,
/// ApplePublishPolicy) and in the desktop policy shared by the native desktop platforms
/// (DesktopPublishPolicy); this facade is the switchboard the UI, the settings normalizer, and
/// the tests call, so they never reach into a platform's library for another platform's answer.
/// </summary>
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
            return AndroidPublishPolicy.IsReadyToRunEnabled();
        if (string.Equals(publishPlatform, PublishPlatforms.Ios, StringComparison.Ordinal))
            return ApplePublishPolicy.IsReadyToRunEnabled();
        if (string.Equals(publishPlatform, PublishPlatforms.MacOs, StringComparison.Ordinal)
            && PublishPlatforms.IsMacCatalyst(targetFramework ?? string.Empty, runtimeIdentifier ?? string.Empty))
            return false;
        return DesktopPublishPolicy.IsReadyToRunEnabled(publishAot: false);
    }

    /// <summary>
    /// True when the SDK turns ReadyToRun on without a manual switch. This is a display concern:
    /// the option is checked and disabled, and no ReadyToRun property is emitted for it.
    /// </summary>
    public static bool IsReadyToRunAutomatic(
        string publishPlatform,
        string? targetFramework,
        string? configuration,
        bool publishTrimmed)
    {
        if (string.Equals(publishPlatform, PublishPlatforms.Android, StringComparison.Ordinal))
            return AndroidPublishPolicy.IsReadyToRunAutomatic(targetFramework, configuration, publishTrimmed);
        if (string.Equals(publishPlatform, PublishPlatforms.Ios, StringComparison.Ordinal))
            return ApplePublishPolicy.IsReadyToRunAutomatic(targetFramework);
        if (string.Equals(publishPlatform, PublishPlatforms.MacOs, StringComparison.Ordinal)
            && PublishPlatforms.IsMacCatalyst(targetFramework ?? string.Empty, string.Empty))
            return ApplePublishPolicy.IsReadyToRunAutomatic(targetFramework);
        return false;
    }

    /// <summary>True when the .NET 11 Apple workload owns trimming and TrimMode controls its granularity.</summary>
    public static bool IsPublishTrimmedAutomatic(string publishPlatform, string? targetFramework) =>
        (string.Equals(publishPlatform, PublishPlatforms.Ios, StringComparison.Ordinal) ||
         (string.Equals(publishPlatform, PublishPlatforms.MacOs, StringComparison.Ordinal)
            && PublishPlatforms.IsMacCatalyst(targetFramework ?? string.Empty, string.Empty))) &&
        ApplePublishPolicy.IsPublishTrimmedAutomatic(targetFramework);

    public static string ReadyToRunDisabledReason(
        bool publishAot,
        string publishPlatform,
        string? targetFramework = null,
        string? runtimeIdentifier = null,
        string? configuration = null,
        bool publishTrimmed = false)
    {
        if (publishAot)
            return "Disabled: AOT and ReadyToRun are mutually exclusive (dotnet/runtime#126598).";
        if (string.Equals(publishPlatform, PublishPlatforms.Android, StringComparison.Ordinal))
            return AndroidPublishPolicy.ReadyToRunAutomaticReason(targetFramework, configuration, publishTrimmed);
        if (string.Equals(publishPlatform, PublishPlatforms.Ios, StringComparison.Ordinal))
            return ApplePublishPolicy.ReadyToRunDisabledReason(publishPlatform, targetFramework);
        if (string.Equals(publishPlatform, PublishPlatforms.MacOs, StringComparison.Ordinal)
            && PublishPlatforms.IsMacCatalyst(targetFramework ?? string.Empty, runtimeIdentifier ?? string.Empty))
            return ApplePublishPolicy.ReadyToRunDisabledReason(publishPlatform, targetFramework);
        return string.Empty;
    }

    public static bool SupportsAndroidMonoAot(string? targetFramework)
        => AndroidPublishPolicy.SupportsMonoAot(targetFramework);

    public static bool IsAndroidAotEnabled(string androidLinkMode)
        => AndroidPublishPolicy.IsAndroidAotEnabled(androidLinkMode);

    public static string AndroidAotDisabledReason(string androidLinkMode)
        => AndroidPublishPolicy.AndroidAotDisabledReason(androidLinkMode);

    public static bool IsPublishAotEnabled(string publishPlatform, string targetFramework, string runtimeIdentifier)
    {
        if (string.Equals(publishPlatform, PublishPlatforms.Android, StringComparison.Ordinal))
            return false;
        if (string.Equals(publishPlatform, PublishPlatforms.Ios, StringComparison.Ordinal))
            return false;
        if (string.Equals(publishPlatform, PublishPlatforms.MacOs, StringComparison.Ordinal)
            && PublishPlatforms.IsMacCatalyst(targetFramework, runtimeIdentifier))
            return false;
        return DesktopPublishPolicy.IsPublishAotEnabled(publishPlatform);
    }

    public static string PublishAotDisabledReason(string publishPlatform, string targetFramework, string runtimeIdentifier)
    {
        if (string.Equals(publishPlatform, PublishPlatforms.Android, StringComparison.Ordinal))
            return AndroidPublishPolicy.PublishAotDisabledReason(targetFramework);
        if (string.Equals(publishPlatform, PublishPlatforms.Ios, StringComparison.Ordinal))
            return ApplePublishPolicy.PublishAotDisabledReason(publishPlatform);
        if (string.Equals(publishPlatform, PublishPlatforms.MacOs, StringComparison.Ordinal)
            && PublishPlatforms.IsMacCatalyst(targetFramework, runtimeIdentifier))
            return ApplePublishPolicy.PublishAotDisabledReason(publishPlatform);
        if (!DesktopPublishPolicy.IsPublishAotEnabled(publishPlatform))
            return DesktopPublishPolicy.PublishAotDisabledReason();
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
            return AndroidPublishPolicy.IsPublishTrimmedEnabled(selfContained, androidLinkMode);
        if (!selfContained)
            return false;
        if (string.Equals(publishPlatform, PublishPlatforms.Ios, StringComparison.Ordinal))
            return false;
        if (string.Equals(publishPlatform, PublishPlatforms.MacOs, StringComparison.Ordinal)
            && PublishPlatforms.IsMacCatalyst(targetFramework, runtimeIdentifier))
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
            return "Native AOT always trims, so this switch is on and cannot be turned off.";
        if (string.Equals(publishPlatform, PublishPlatforms.Android, StringComparison.Ordinal))
            return AndroidPublishPolicy.PublishTrimmedDisabledReason(selfContained, androidLinkMode);
        if (!selfContained)
            return "Trimming requires a self-contained publish.";
        if (string.Equals(publishPlatform, PublishPlatforms.Ios, StringComparison.Ordinal))
            return ApplePublishPolicy.PublishTrimmedDisabledReason(publishPlatform, targetFramework);
        if (string.Equals(publishPlatform, PublishPlatforms.MacOs, StringComparison.Ordinal)
            && PublishPlatforms.IsMacCatalyst(targetFramework, runtimeIdentifier))
            return ApplePublishPolicy.PublishTrimmedDisabledReason(publishPlatform, targetFramework);
        return string.Empty;
    }

    public static bool IsShrinkerSettingsEnabled(string androidLinkMode)
        => AndroidPublishPolicy.IsShrinkerSettingsEnabled(androidLinkMode);

    public static bool IsProfiledAotEnabled(bool runAotCompilation)
        => AndroidPublishPolicy.IsProfiledAotEnabled(runAotCompilation);

    public static string ProfiledAotDisabledReason(bool runAotCompilation)
        => AndroidPublishPolicy.ProfiledAotDisabledReason(runAotCompilation);

    public static bool IsUseAppHostEnabled(string publishPlatform)
        => ApplePublishPolicy.IsUseAppHostEnabled(publishPlatform);

    public static bool IsArchiveOnBuildEnabled(string publishPlatform, string runtimeIdentifier)
        => ApplePublishPolicy.IsArchiveOnBuildEnabled(publishPlatform, runtimeIdentifier);

    public static bool IsBuildIpaEnabled(string publishPlatform, string runtimeIdentifier)
        => ApplePublishPolicy.IsBuildIpaEnabled(publishPlatform, runtimeIdentifier);

    public static bool IsAndroidDexToolSupported(string androidDexTool)
        => AndroidPublishPolicy.IsAndroidDexToolSupported(androidDexTool);

    public static bool IsAndroidLinkToolSupported(string androidLinkTool, string androidDexTool)
        => AndroidPublishPolicy.IsAndroidLinkToolSupported(androidLinkTool, androidDexTool);

    public static string AndroidToolValidationMessage(string androidLinkTool, string androidDexTool)
        => AndroidPublishPolicy.AndroidToolValidationMessage(androidLinkTool, androidDexTool);

    public static void ValidateProjectProperties(
        string publishPlatform,
        string? targetFramework,
        IReadOnlyDictionary<string, string>? evaluated)
    {
        if (string.Equals(publishPlatform, PublishPlatforms.Android, StringComparison.Ordinal))
        {
            AndroidPublishPolicy.ValidateEvaluatedProperties(targetFramework, evaluated);
            return;
        }

        if (string.Equals(publishPlatform, PublishPlatforms.Ios, StringComparison.Ordinal)
            || string.Equals(publishPlatform, PublishPlatforms.MacOs, StringComparison.Ordinal)
                && PublishPlatforms.IsMacCatalyst(targetFramework ?? string.Empty, string.Empty))
        {
            ApplePublishPolicy.ValidateEvaluatedProperties(targetFramework, evaluated);
        }
    }

    public static bool IsPublishSingleFileEnabled(bool publishAot, string publishPlatform) =>
        IsPublishSingleFileEnabled(publishAot, publishPlatform, null, null);

    /// <summary>Context-aware gate. Mac Catalyst rides the macOS platform entry, but the Apple
    /// command never emits a single-file switch, so the option must stay off there.</summary>
    public static bool IsPublishSingleFileEnabled(
        bool publishAot,
        string publishPlatform,
        string? targetFramework,
        string? runtimeIdentifier)
    {
        // Native AOT already emits a native executable; adding the single-file
        // bundler on top is redundant and can create extraction/linking surprises.
        if (publishAot)
            return false;
        if (string.Equals(publishPlatform, PublishPlatforms.Android, StringComparison.Ordinal))
            return AndroidPublishPolicy.IsPublishSingleFileEnabled();
        if (string.Equals(publishPlatform, PublishPlatforms.Ios, StringComparison.Ordinal))
            return ApplePublishPolicy.IsPublishSingleFileEnabled();
        if (string.Equals(publishPlatform, PublishPlatforms.MacOs, StringComparison.Ordinal)
            && PublishPlatforms.IsMacCatalyst(targetFramework ?? string.Empty, runtimeIdentifier ?? string.Empty))
            return false;
        return DesktopPublishPolicy.IsPublishSingleFileEnabled(publishAot: false);
    }
}
