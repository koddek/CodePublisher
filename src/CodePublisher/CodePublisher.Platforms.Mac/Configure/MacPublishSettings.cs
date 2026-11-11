namespace CodePublisher.Features.Publishing.Configure;

/// <summary>
/// Normalizes the macOS share of a publish settings snapshot. Native AOT always trims (the Apple
/// workload keeps this honest on its own builds); the macOS bundle toggle is honored and every
/// other platform's knobs are zeroed out.
/// </summary>
public static class MacPublishSettings
{
    public static PublishSettingsState Apply(PublishSettingsState state)
        => DesktopPublishSettings.Normalize(state) with
        {
            CreateMacAppBundle = state.CreateMacAppBundle,
            CreateWindowsExecutable = false,
            BuildIpa = false,
            ArchiveOnBuild = false,
            RunAotCompilation = false,
            EnableProfiledAot = false,
            CreateMappingFile = false,
            EnableMultiDex = false,
            UseAapt2 = false,
            EnableDesugar = false,
            IncludeApk = false,
            IncludeAab = false,
            UseAppHost = state.UseAppHost
        };
}
