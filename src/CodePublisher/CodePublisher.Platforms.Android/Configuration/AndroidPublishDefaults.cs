namespace CodePublisher.Features.Publishing.Configure;

/// <summary>
/// The default publish profile for the Android platform. Every setting the platform
/// ships with lives here, not in a shared table, so a profile change on this platform cannot
/// bleed into another platform's defaults.
/// </summary>
public static class AndroidPublishDefaults
{
    public static PlatformProfile Default { get; } = new(
                TargetFramework: "net11.0-android",
                RuntimeIdentifier: "android-arm64",
                SelfContained: true,
                PublishTrimmed: true,
                PublishAot: false,
                PublishReadyToRun: false,
                PublishSingleFile: false,
                UseAppHost: true,
                CreateMacAppBundle: false,
                CreateWindowsExecutable: false,
                BuildIpa: false,
                ArchiveOnBuild: false,
                RunAotCompilation: true,
                EnableProfiledAot: true,
                AndroidLinkMode: "SdkOnly",
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
