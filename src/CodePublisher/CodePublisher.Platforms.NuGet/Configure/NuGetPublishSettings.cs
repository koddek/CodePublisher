namespace CodePublisher.Features.Publishing.Configure;

/// <summary>
/// Normalizes the NuGet share of a publish settings snapshot. Packing a package carries no
/// platform-specific toggles: every publish-platform knob is zeroed out and the self-contained
/// shape of the pack is left to the SDK.
/// </summary>
public static class NuGetPublishSettings
{
    public static PublishSettingsState Apply(PublishSettingsState state)
        => state with
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
            PublishAot = false,
            PublishSingleFile = false,
            PublishReadyToRun = false
        };
}
