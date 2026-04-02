namespace DotNetAppPublisher.Models;

public sealed record ProjectMetadata(
    string ProjectFilePath,
    string DefaultOutputDirectory,
    string? PackageId,
    string? TargetFramework);
