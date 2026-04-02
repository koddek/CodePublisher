namespace MauiAppPublisher.Models;

public sealed record PublishCommandBundle(
    IReadOnlyList<string> CommandArguments,
    string PreviewText,
    string VerifiedApkPreviewText,
    string ProjectFilePath,
    string OutputDirectory);
