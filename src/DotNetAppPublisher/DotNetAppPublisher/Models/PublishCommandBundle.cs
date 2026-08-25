namespace DotNetAppPublisher.Models;

public sealed record PublishCommandBundle(
    IReadOnlyList<string> CommandArguments,
    string PreviewText,
    string BaselinePreviewText,
    string ProjectFilePath,
    string OutputDirectory);
