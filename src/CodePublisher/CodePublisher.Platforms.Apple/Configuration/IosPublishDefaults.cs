namespace CodePublisher.Features.Publishing.Configure;

/// <summary>
/// The default publish profile for the Ios platform. Every setting the platform
/// ships with lives here, not in a shared table, so a profile change on this platform cannot
/// bleed into another platform's defaults.
/// </summary>
public static class IosPublishDefaults
{
    public static PlatformProfile Default { get; } = new(
                TargetFramework: "net11.0-ios",
                RuntimeIdentifier: "ios-arm64",
                SelfContained: true,
                PublishTrimmed: true,
                PublishAot: false,
                PublishReadyToRun: false,
                PublishSingleFile: true,
                UseAppHost: false,
                CreateMacAppBundle: true,
                CreateWindowsExecutable: true,
                BuildIpa: false,
                ArchiveOnBuild: true,
                RunAotCompilation: false,
                EnableProfiledAot: false,
                AndroidLinkMode: "None",
                AndroidLinkTool: "r8",
                AndroidDexTool: "d8",
                CreateMappingFile: false,
                EnableMultiDex: false,
                UseAapt2: true,
                EnableDesugar: true,
                IncludeApk: true,
                IncludeAab: false,
                SignMode: "Auto",
                Configuration: "Release");
}
