namespace DotNetAppPublisher.Models;

public sealed record PublishConfiguration
{
    public required string ProjectDirectory { get; init; }

    public required string PublishPlatform { get; init; }

    public required string TargetFramework { get; init; }

    public required string RuntimeIdentifier { get; init; }

    public required string Configuration { get; init; }

    public required string OutputDirectory { get; init; }

    public required string PackageId { get; init; }

    public required bool IncludeApk { get; init; }

    public required bool IncludeAab { get; init; }

    public required bool SelfContained { get; init; }

    public required bool PublishTrimmed { get; init; }

    public required bool PublishAot { get; init; }

    public required bool PublishReadyToRun { get; init; }

    public required bool PublishSingleFile { get; init; }

    public required bool UseAppHost { get; init; }

    public required bool CreateMacAppBundle { get; init; }

    public required bool CreateWindowsExecutable { get; init; }

    public required bool BuildIpa { get; init; }

    public required bool ArchiveOnBuild { get; init; }

    public required bool RunAotCompilation { get; init; }

    public required bool EnableProfiledAot { get; init; }

    public required string AndroidLinkMode { get; init; }

    public required string AndroidLinkTool { get; init; }

    public required string AndroidDexTool { get; init; }

    public required bool CreateMappingFile { get; init; }

    public required bool EnableMultiDex { get; init; }

    public required bool UseAapt2 { get; init; }

    public required bool EnableDesugar { get; init; }

    public required bool DeleteBin { get; init; }

    public required bool DeleteObj { get; init; }

    public required string SignMode { get; init; }

    public required string KeystorePath { get; init; }

    public required string KeyAlias { get; init; }

    public required string KeystorePassword { get; init; }

    public required string KeyPassword { get; init; }
}
