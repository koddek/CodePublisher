namespace CodePublisher.Features.Publishing.Configure;

/// <summary>
/// The full set of publish options as an immutable snapshot.
///
/// Every option the dependency graph can read or write is in here, so the graph never has to
/// guess at a value and the UI never has to keep a second copy of the truth.
/// </summary>
public sealed record PublishSettingsState(
    string PublishPlatform,
    string TargetFramework,
    string RuntimeIdentifier,
    bool SelfContained,
    bool PublishAot,
    bool PublishReadyToRun,
    bool PublishSingleFile,
    bool PublishTrimmed,
    string AndroidLinkMode,
    string AndroidLinkTool,
    string AndroidDexTool,
    bool RunAotCompilation,
    bool EnableProfiledAot,
    bool IncludeApk,
    bool IncludeAab,
    bool UseAppHost = true,
    bool CreateMacAppBundle = true,
    bool CreateWindowsExecutable = true,
    bool BuildIpa = false,
    bool ArchiveOnBuild = true,
    bool CreateMappingFile = false,
    bool EnableMultiDex = false,
    bool UseAapt2 = true,
    bool EnableDesugar = true);

/// <summary>
/// Normalizes a publish settings snapshot so the options agree with each other and with the
/// platform's rules.
/// </summary>
public static class PublishSettingsPolicy
{
    /// <summary>
    /// Normalizes the options: the platform rules and the dependency graph are applied together
    /// until the result settles.
    /// </summary>
    public static PublishSettingsState Normalize(PublishSettingsState state) =>
        PublishOptionResolver.Resolve(state).State;

    /// <summary>
    /// Normalizes the options after the user changed one of them. The changed option is what
    /// settles a mutual exclusion and what tells the resolver whether a prerequisite was turned
    /// off on purpose.
    /// </summary>
    public static PublishSettingsState Normalize(
        PublishSettingsState state,
        PublishOptionId changedOption,
        bool changedValue = true) =>
        PublishOptionResolver.Resolve(state, changedOption, changedValue).State;

    /// <summary>
    /// The platform rules that do not come from the dependency graph: what each platform supports
    /// at all, the values the Android tooling pair is allowed to take, and the options a platform
    /// decides on the user's behalf.
    ///
    /// These rules never settle a cross-option relationship on their own. That is the dependency
    /// graph's job, so the two can never disagree: if the platform rules turned an option off
    /// because a companion was missing, the graph could never turn that companion on.
    /// </summary>
    public static PublishSettingsState ApplyPlatformRules(PublishSettingsState state)
    {
        var isAndroid = string.Equals(state.PublishPlatform, PublishPlatforms.Android, StringComparison.Ordinal);
        var isIos = string.Equals(state.PublishPlatform, PublishPlatforms.Ios, StringComparison.Ordinal);
        var isMacOs = string.Equals(state.PublishPlatform, PublishPlatforms.MacOs, StringComparison.Ordinal);

        // Capability gates: an option the platform cannot do at all is turned off.
        var publishAot = state.PublishAot &&
            PublishOptionPolicy.IsPublishAotEnabled(
                state.PublishPlatform,
                state.TargetFramework,
                state.RuntimeIdentifier);
        var readyToRun = state.PublishReadyToRun &&
            PublishOptionPolicy.IsReadyToRunEnabled(
                publishAot,
                state.PublishPlatform,
                state.TargetFramework,
                state.RuntimeIdentifier);
        var singleFile = state.PublishSingleFile &&
            PublishOptionPolicy.IsPublishSingleFileEnabled(publishAot, state.PublishPlatform);

        // Options that only exist on one platform.
        var runAot = isAndroid && state.RunAotCompilation;
        var profiledAot = isAndroid && state.EnableProfiledAot;
        var createMappingFile = isAndroid && state.CreateMappingFile;
        var enableMultiDex = isAndroid && state.EnableMultiDex;
        var useAapt2 = isAndroid && state.UseAapt2;
        var enableDesugar = isAndroid && state.EnableDesugar;
        var buildIpa = isIos && state.BuildIpa;
        var archiveOnBuild = isIos && state.ArchiveOnBuild;
        var createMacAppBundle = isMacOs && state.CreateMacAppBundle;
        var createWindowsExecutable =
            string.Equals(state.PublishPlatform, PublishPlatforms.Windows, StringComparison.Ordinal) &&
            state.CreateWindowsExecutable;
        var useAppHost = !isIos && state.UseAppHost;

        var trimmed = state.PublishTrimmed;
        if (isIos || (isMacOs && PlatformProfiles.IsMacCatalyst(state.TargetFramework, state.RuntimeIdentifier)))
        {
            // The Apple workload forces trimming on and fails the build otherwise.
            trimmed = true;
        }
        else if (isAndroid)
        {
            // Android trimming follows the linker: a linking mode turns the trimmer on, and the
            // trimmer needs a self-contained publish.
            trimmed = state.SelfContained && PublishOptionPolicy.IsAndroidAotEnabled(state.AndroidLinkMode);
        }
        else if (publishAot)
        {
            // Native AOT always trims. The SDK fails the build with "PublishTrimmed is implied
            // by native compilation and cannot be disabled." when trimming is off.
            trimmed = true;
        }

        // The APK is always built on Android: it is the fast artifact for local testing and
        // device deploys. Checking AAB adds the store bundle to the same publish, so a release
        // build never needs a second run; unchecking it keeps the publish APK-only.
        var includeApk = isAndroid;
        var includeAab = isAndroid && state.IncludeAab;

        var androidLinkTool = state.AndroidLinkTool.Trim();
        var androidDexTool = state.AndroidDexTool.Trim();
        if (isAndroid
            && !string.IsNullOrWhiteSpace(PublishOptionPolicy.AndroidToolValidationMessage(androidLinkTool, androidDexTool)))
        {
            androidLinkTool = "r8";
            androidDexTool = "d8";
        }

        return state with
        {
            SelfContained = state.SelfContained,
            PublishAot = publishAot,
            PublishReadyToRun = readyToRun,
            PublishSingleFile = singleFile,
            PublishTrimmed = trimmed,
            AndroidLinkTool = androidLinkTool,
            AndroidDexTool = androidDexTool,
            RunAotCompilation = runAot,
            EnableProfiledAot = profiledAot,
            CreateMappingFile = createMappingFile,
            EnableMultiDex = enableMultiDex,
            UseAapt2 = useAapt2,
            EnableDesugar = enableDesugar,
            IncludeApk = includeApk,
            IncludeAab = includeAab,
            UseAppHost = useAppHost,
            CreateMacAppBundle = createMacAppBundle,
            CreateWindowsExecutable = createWindowsExecutable,
            BuildIpa = buildIpa,
            ArchiveOnBuild = archiveOnBuild
        };
    }
}
