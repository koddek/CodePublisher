namespace CodePublisher.Features.Publishing.Configure;

/// <summary>
/// Normalizes the Linux share of a publish settings snapshot. Native AOT always trims; no bundle
/// or launcher rename applies and every other platform's knobs are zeroed out.
/// </summary>
public static class LinuxPublishSettings
{
    public static PublishSettingsState Apply(PublishSettingsState state)
        => DesktopPublishSettings.Normalize(state) with
        {
            CreateWindowsExecutable = false,
            CreateMacAppBundle = false,
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
