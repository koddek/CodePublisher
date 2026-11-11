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

        return ApplyCommon(state, buildIpa, archiveOnBuild, createMacAppBundle: false);
    }

    /// <summary>
    /// The Mac Catalyst branch: the Apple SDK with the shared desktop option surface — Catalyst
    /// rides the macOS platform entry, so the macOS-only bundle toggle is honored and the iOS-only
    /// archive/IPA toggles are not.
    /// </summary>
    public static PublishSettingsState ApplyCatalyst(PublishSettingsState state)
        => ApplyCommon(state, buildIpa: false, archiveOnBuild: false, createMacAppBundle: state.CreateMacAppBundle);

    private static PublishSettingsState ApplyCommon(PublishSettingsState state, bool buildIpa, bool archiveOnBuild, bool createMacAppBundle)
        => state with
        {
            // The Apple workload forces trimming on and fails the build otherwise.
            PublishTrimmed = true,
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
