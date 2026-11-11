namespace CodePublisher.Features.Publishing.Configure;

/// <summary>
/// The full set of publish options as an immutable snapshot.
///
/// Every option the dependency graph can read or write is in here, so the graph never has to
/// guess at a value and the UI never has to keep a second copy of the truth.
/// </summary>
public sealed record PublishSettingsState(
    string PublishPlatform,
    string TargetFramework,
    string RuntimeIdentifier,
    bool SelfContained,
    bool PublishAot,
    bool PublishReadyToRun,
    bool PublishSingleFile,
    bool PublishTrimmed,
    string AndroidLinkMode,
    string AndroidLinkTool,
    string AndroidDexTool,
    bool RunAotCompilation,
    bool EnableProfiledAot,
    bool IncludeApk,
    bool IncludeAab,
    bool UseAppHost = true,
    bool CreateMacAppBundle = true,
    bool CreateWindowsExecutable = true,
    bool BuildIpa = false,
    bool ArchiveOnBuild = true,
    bool CreateMappingFile = false,
    bool EnableMultiDex = false,
    bool UseAapt2 = true,
    bool EnableDesugar = true);
