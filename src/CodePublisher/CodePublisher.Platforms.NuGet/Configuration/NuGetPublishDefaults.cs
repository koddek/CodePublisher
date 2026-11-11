namespace CodePublisher.Features.Publishing.Configure;

/// <summary>
/// The default publish profile for the Nuget platform. Every setting the platform
/// ships with lives here, not in a shared table, so a profile change on this platform cannot
/// bleed into another platform's defaults.
/// </summary>
public static class NuGetPublishDefaults
{
    public static PlatformProfile Default { get; } = new(
                TargetFramework: "net11.0",
                RuntimeIdentifier: "any",
                SelfContained: false,
                PublishTrimmed: false,
                PublishAot: false,
                PublishReadyToRun: false,
                PublishSingleFile: false,
                UseAppHost: false,
                CreateMacAppBundle: false,
                CreateWindowsExecutable: false,
                BuildIpa: false,
                ArchiveOnBuild: false,
                RunAotCompilation: false,
                EnableProfiledAot: false,
                AndroidLinkMode: "None",
                AndroidLinkTool: "r8",
                AndroidDexTool: "d8",
                CreateMappingFile: false,
                EnableMultiDex: false,
                UseAapt2: true,
                EnableDesugar: true,
                IncludeApk: false,
                IncludeAab: false,
                SignMode: "Auto",
                Configuration: "Release");
}
