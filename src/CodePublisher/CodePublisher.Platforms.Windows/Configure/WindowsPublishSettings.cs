namespace CodePublisher.Features.Publishing.Configure;

/// <summary>
/// Normalizes the Windows share of a publish settings snapshot. UseAppHost is the single source
/// of truth for the .exe launcher; the view-model alias mirrors it and other platforms are zeroed.
/// </summary>
public static class WindowsPublishSettings
{
    public static PublishSettingsState Apply(PublishSettingsState state)
        => state with
        {
            CreateWindowsExecutable = state.UseAppHost,
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
