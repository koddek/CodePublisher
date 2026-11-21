using CodePublisher.Features.Publishing.Inspect;

namespace CodePublisher.Features.Publishing.Configure;

/// <summary>
/// Normalizes the Android share of a publish settings snapshot. The shared preamble in
/// <c>PublishSettingsPolicy.ApplyPlatformRules</c> has already applied the capability gates the
/// platforms agree on; this function owns everything else Android decides, and zeroes the knobs
/// that only exist on the other platforms.
/// </summary>
public static class AndroidPublishSettings
{
    public static PublishSettingsState Apply(PublishSettingsState state)
    {
        // .NET 11 removed the Mono runtime for Android: RunAOTCompilation and
        // AndroidEnableProfiledAot are rejected on CoreCLR (XA1044), so they are cleared on
        // net11.0-android target frameworks instead of being passed through.
        var supportsMonoAot = AndroidPublishPolicy.SupportsMonoAot(state.TargetFramework);

        var runAot = supportsMonoAot && state.RunAotCompilation;
        var profiledAot = supportsMonoAot && state.EnableProfiledAot;
        var createMappingFile = state.CreateMappingFile;
        var enableMultiDex = state.EnableMultiDex;
        var useAapt2 = state.UseAapt2;
        var enableDesugar = state.EnableDesugar;

        // The graph settles linking, trimming and self-contained mode before this platform pass.
        // CoreCLR then turns partial ReadyToRun on by itself for a trimmed Release publish.
        var readyToRun = AndroidPublishPolicy.IsReadyToRunAutomatic(state.TargetFramework, state.Configuration, state.PublishTrimmed);

        // The APK is always built on Android: it is the fast artifact for local testing and
        // device deploys. Checking AAB adds the store bundle to the same publish, so a release
        // build never needs a second run; unchecking it keeps the publish APK-only.
        var includeApk = true;
        var includeAab = state.IncludeAab;

        var androidLinkTool = state.AndroidLinkTool.Trim();
        var androidDexTool = state.AndroidDexTool.Trim();
        if (!string.IsNullOrWhiteSpace(AndroidPublishPolicy.AndroidToolValidationMessage(androidLinkTool, androidDexTool)))
        {
            androidLinkTool = "r8";
            androidDexTool = "d8";
        }

        return state with
        {
            AndroidLinkTool = androidLinkTool,
            AndroidDexTool = androidDexTool,
            PublishReadyToRun = readyToRun,
            RunAotCompilation = runAot,
            EnableProfiledAot = profiledAot,
            CreateMappingFile = createMappingFile,
            EnableMultiDex = enableMultiDex,
            UseAapt2 = useAapt2,
            EnableDesugar = enableDesugar,
            IncludeApk = includeApk,
            IncludeAab = includeAab,
            UseAppHost = state.UseAppHost,
            CreateMacAppBundle = false,
            CreateWindowsExecutable = false,
            BuildIpa = false,
            ArchiveOnBuild = false
        };
    }
}
