namespace DotNetAppPublisher.Models;

public sealed record ProjectOutputLayout(
    bool UsesCentralizedArtifacts,
    string DefaultOutputDirectory,
    string? BuildOutputDirectory,
    string? IntermediateOutputDirectory,
    string Description);
