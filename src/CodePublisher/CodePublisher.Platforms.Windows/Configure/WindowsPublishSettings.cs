namespace CodePublisher.Features.Publishing.Configure;

/// <summary>
/// Normalizes the Windows share of a publish settings snapshot. Native AOT always trims; the
/// Windows launcher toggle is honored and every other platform's knobs are zeroed out.
/// </summary>
public static class WindowsPublishSettings
{
    public static PublishSettingsState Apply(PublishSettingsState state)
        => DesktopPublishSettings.Normalize(state) with
        {
            CreateWindowsExecutable = state.CreateWindowsExecutable,
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
