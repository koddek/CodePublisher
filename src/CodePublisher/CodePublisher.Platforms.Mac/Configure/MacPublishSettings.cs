namespace CodePublisher.Features.Publishing.Configure;

/// <summary>
/// Normalizes the macOS share of a publish settings snapshot. The macOS bundle toggle is honored
/// and every other platform's knobs are zeroed out; the graph owns cross-option relationships.
/// </summary>
public static class MacPublishSettings
{
    public static PublishSettingsState Apply(PublishSettingsState state)
        => state with
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
