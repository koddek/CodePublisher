using CodePublisher.Features.Publishing.Inspect;

namespace CodePublisher.Features.Publishing.Configure;

/// <summary>
/// What Android in this app can and cannot do, and why not. The desktop platforms use
/// <c>DesktopPublishPolicy</c>; iOS and Catalyst use <c>ApplePublishPolicy</c>. Reasons are
/// written for the Build tab's tooltips, so each disabled option says why in plain English.
/// </summary>
public static class AndroidPublishPolicy
{
    public static bool IsAndroidAotEnabled(string androidLinkMode)
        => !string.Equals(androidLinkMode, "None", StringComparison.Ordinal);

    public static string AndroidAotDisabledReason(string androidLinkMode)
        => IsAndroidAotEnabled(androidLinkMode)
            ? string.Empty
            : "RunAOT requires linking — set Link Mode to SdkOnly or Full first.";

    /// <summary>
    /// ReadyToRun stays off for Android on every target framework this app emits: .NET 10 and
    /// earlier use Mono (RunAOTCompilation is the AOT switch there), and .NET 11's CoreCLR compiles
    /// partial ReadyToRun in Release builds when trimming is enabled — no MSBuild switch exists
    /// for it other than turning trimming on.
    /// </summary>
    public static bool IsReadyToRunEnabled() => false;

    public static string ReadyToRunDisabledReason(string? targetFramework)
        => ProjectInspector.GetTargetFrameworkVersion(targetFramework) >= 11
            ? "Android on .NET 11 targets CoreCLR, which compiles partial ReadyToRun in Release builds when trimming is enabled. Turn on Link Mode (SdkOnly) to keep it; the switch is not applicable."
            : "ReadyToRun is not supported for Android — use RunAOT instead.";

    /// <summary>
    /// Native Android AOT (PublishAot) selects the Experimental CoreCLR NativeAOT runtime in
    /// .NET 11. It is not offered: it is opt-in at the SDK level, not a shipping profile.
    /// </summary>
    public static bool IsPublishAotEnabled() => false;

    public static string PublishAotDisabledReason(string? targetFramework)
        => ProjectInspector.GetTargetFrameworkVersion(targetFramework) >= 11
            ? "Android NativeAOT is experimental in .NET 11; this app keeps the CoreCLR default instead."
            : "Android uses RunAOTCompilation instead of PublishAot.";

    public static bool IsPublishTrimmedEnabled(bool selfContained, string androidLinkMode)
        => selfContained && IsAndroidAotEnabled(androidLinkMode);

    public static string PublishTrimmedDisabledReason(bool selfContained, string androidLinkMode)
    {
        if (!selfContained)
        {
            return "Trimming requires a self-contained publish.";
        }

        return IsAndroidAotEnabled(androidLinkMode)
            ? string.Empty
            : "Android trimming is driven by Link Mode — 'None' disables the linker.";
    }

    public static bool IsPublishSingleFileEnabled() => false;

    public static bool IsUseAppHostEnabled() => true;

    public static bool IsShrinkerSettingsEnabled(string androidLinkMode)
        => !string.Equals(androidLinkMode, "None", StringComparison.Ordinal);

    public static bool IsProfiledAotEnabled(bool runAotCompilation) => runAotCompilation;

    public static string ProfiledAotDisabledReason(bool runAotCompilation)
        => runAotCompilation ? string.Empty : "Enable RunAOT first; profiled AOT requires AOT.";

    public static bool IsAndroidDexToolSupported(string androidDexTool)
        => string.Equals(androidDexTool.Trim(), "d8", StringComparison.OrdinalIgnoreCase);

    public static bool IsAndroidLinkToolSupported(string androidLinkTool, string androidDexTool)
        => IsAndroidDexToolSupported(androidDexTool)
            && string.Equals(androidLinkTool.Trim(), "r8", StringComparison.OrdinalIgnoreCase);

    public static string AndroidToolValidationMessage(string androidLinkTool, string androidDexTool)
    {
        if (!IsAndroidDexToolSupported(androidDexTool))
            return "The .NET for Android workload supports D8; the legacy DX compiler is deprecated and is not a supported profile.";

        if (!string.Equals(androidLinkTool.Trim(), "r8", StringComparison.OrdinalIgnoreCase))
            return "D8 requires the R8 code shrinker; ProGuard with D8 is no longer supported.";

        return string.Empty;
    }

    /// <summary>
    /// Whether the Mono AOT switches exist on this target framework. .NET 11 removed the Mono
    /// runtime for Android (NETSDK1242), and the properties are rejected on CoreCLR (XA1044).
    /// </summary>
    public static bool SupportsMonoAot(string? targetFramework)
        => ProjectInspector.GetTargetFrameworkVersion(targetFramework) < 11;
}
