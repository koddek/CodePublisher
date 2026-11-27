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
    /// ReadyToRun has no manual Android switch in this app: .NET 10 and earlier use Mono
    /// (RunAOTCompilation is the AOT switch there), and .NET 11 CoreCLR compiles partial
    /// ReadyToRun in trimmed Release builds. The app preserves the SDK's automatic setting.
    /// </summary>
    public static bool IsReadyToRunEnabled() => false;

    /// <summary>
    /// Whether CoreCLR turns ReadyToRun on by itself. Verified with SDK 11.0.100: for a
    /// net11.0-android Release publish, <c>PublishReadyToRun</c> evaluates to true whenever
    /// <c>PublishTrimmed</c> is true, with no explicit property on the command line. Debug builds
    /// and untrimmed builds leave it off. The app surfaces this as a checked but read-only option.
    /// </summary>
    public static bool IsReadyToRunAutomatic(string? targetFramework, string? configuration, bool publishTrimmed)
        => ProjectInspector.GetTargetFrameworkVersion(targetFramework) >= 11
            && publishTrimmed
            && !string.Equals(configuration?.Trim(), "Debug", StringComparison.OrdinalIgnoreCase);

    public static string ReadyToRunDisabledReason(string? targetFramework)
        => ProjectInspector.GetTargetFrameworkVersion(targetFramework) >= 11
            ? "ReadyToRun is on automatically: CoreCLR compiles partial ReadyToRun in trimmed Release builds. The SDK sets it, so it cannot be toggled here."
            : "ReadyToRun is not supported for Android — use RunAOT instead.";

    public static string ReadyToRunAutomaticReason(string? targetFramework, string? configuration, bool publishTrimmed)
        => IsReadyToRunAutomatic(targetFramework, configuration, publishTrimmed)
            ? ReadyToRunDisabledReason(targetFramework)
            : "ReadyToRun is off: CoreCLR only compiles partial ReadyToRun in trimmed Release builds. Turn on Link Mode (SdkOnly or Full) to get it.";

    /// <summary>
    /// Native Android AOT (PublishAot) selects the Experimental CoreCLR NativeAOT runtime in
    /// .NET 11. It is not offered: it is opt-in at the SDK level, not a shipping profile.
    /// </summary>
    public static bool IsPublishAotEnabled() => false;

    public static string PublishAotDisabledReason(string? targetFramework)
        => ProjectInspector.GetTargetFrameworkVersion(targetFramework) >= 11
            ? "Android NativeAOT is experimental in .NET 11; this app keeps the CoreCLR default instead."
            : "Android uses RunAOTCompilation instead of PublishAot.";

    /// <summary>
    /// Link Mode and trimming form a cycle in the option graph: either one can turn the other on.
    /// The checkbox therefore stays enabled whenever trimming is possible. When Link Mode is
    /// 'None', checking Publish trimmed makes the graph select SdkOnly; disabling the checkbox
    /// here would strand the user with no in-app route back to a trimmed publish.
    /// </summary>
    public static bool IsPublishTrimmedEnabled(bool selfContained, string androidLinkMode)
        => selfContained;

    public static string PublishTrimmedDisabledReason(bool selfContained, string androidLinkMode)
        => selfContained
            ? string.Empty
            : "Trimming requires a self-contained publish.";

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

    /// <summary>
    /// Mono-only properties the .NET 11 Android SDK rejects at any value (XA1044). The publish
    /// command clears these with an empty command-line override, so a declaration in a csproj or
    /// any imported props file cannot stop the publish.
    /// </summary>
    public static IReadOnlyList<string> PropertiesToClear(string? targetFramework)
        => SupportsMonoAot(targetFramework) ? [] : ["RunAOTCompilation", "AndroidEnableProfiledAot", "EnableLLVM"];

    public static IReadOnlyList<string> IncompatibleEvaluatedProperties(
        string? targetFramework,
        IReadOnlyDictionary<string, string>? evaluated)
    {
        if (SupportsMonoAot(targetFramework) || evaluated is null)
        {
            return [];
        }

        return PropertiesToClear(targetFramework)
            .Where(property => evaluated.TryGetValue(property, out var value) && !string.IsNullOrWhiteSpace(value))
            .ToArray();
    }

    public static void ValidateEvaluatedProperties(string? targetFramework, IReadOnlyDictionary<string, string>? evaluated)
    {
        var property = IncompatibleEvaluatedProperties(targetFramework, evaluated).FirstOrDefault();
        if (property is null)
        {
            return;
        }

        evaluated!.TryGetValue(property, out var value);
        throw new InvalidOperationException(
            $"`{property}` is set to `{value}` in the project or an imported props file. .NET 11 Android rejects this Mono-only property even when it is false (XA1044). Remove it from the project, then retry.");
    }
}
