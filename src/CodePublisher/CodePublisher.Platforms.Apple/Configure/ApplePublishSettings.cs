namespace CodePublisher.Features.Publishing.Configure;

/// <summary>
/// Normalizes the Apple share of a publish settings snapshot: iOS proper and Mac Catalyst (which
/// rides the macOS platform entry). Everything Android or desktop owns is zeroed out.
/// </summary>
public static class ApplePublishSettings
{
    /// <summary>The iOS branch: archiving and the .ipa, with trimming forced on.</summary>
    public static PublishSettingsState Apply(PublishSettingsState state)
    {
        var buildIpa = state.BuildIpa;
        var archiveOnBuild = state.ArchiveOnBuild;
        var readyToRun = ApplePublishPolicy.IsReadyToRunAutomatic(state.TargetFramework) || state.PublishReadyToRun;

        return ApplyCommon(state with { PublishReadyToRun = readyToRun }, buildIpa, archiveOnBuild, createMacAppBundle: false);
    }

    /// <summary>
    /// The Mac Catalyst branch: the Apple SDK with the shared desktop option surface — Catalyst
    /// rides the macOS platform entry, so the macOS-only bundle toggle is honored and the iOS-only
    /// archive/IPA toggles are not.
    /// </summary>
    public static PublishSettingsState ApplyCatalyst(PublishSettingsState state)
    {
        var readyToRun = ApplePublishPolicy.IsReadyToRunAutomatic(state.TargetFramework) || state.PublishReadyToRun;
        return ApplyCommon(
            state with { PublishReadyToRun = readyToRun },
            buildIpa: false,
            archiveOnBuild: false,
            createMacAppBundle: state.CreateMacAppBundle);
    }

    private static PublishSettingsState ApplyCommon(PublishSettingsState state, bool buildIpa, bool archiveOnBuild, bool createMacAppBundle)
        => state with
        {
            // Apple application publishes are self-contained and always run the trimmer.
            // The Apple commands never emit PublishSingleFile, including on Mac Catalyst.
            SelfContained = true,
            PublishTrimmed = true,
            PublishSingleFile = false,
            RunAotCompilation = false,
            EnableProfiledAot = false,
            CreateMappingFile = false,
            EnableMultiDex = false,
            UseAapt2 = false,
            EnableDesugar = false,
            IncludeApk = false,
            IncludeAab = false,
            CreateWindowsExecutable = false,
            CreateMacAppBundle = createMacAppBundle,
            BuildIpa = buildIpa,
            ArchiveOnBuild = archiveOnBuild,
            UseAppHost = state.UseAppHost
        };
}
