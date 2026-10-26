using System.Text;

namespace CodePublisher.Features.Deployment.CrashLogs;

/// <summary>
/// Builds the text a user pastes into an AI agent. One block per crash report, each with a
/// header that says which report it is, where it came from, and when it happened, so the answer
/// the agent gives is about the crash the user is looking at and not about some other one.
/// </summary>
public static class CrashLogReport
{
    /// <summary>The text of one report on its own, with its header.</summary>
    public static string BuildEntry(CrashLogArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        var builder = new StringBuilder();
        builder.AppendLine($"=== Crash report: {artifact.Name} ===");
        builder.AppendLine($"Source: {artifact.SourceText}");
        if (!string.IsNullOrWhiteSpace(artifact.Location))
        {
            builder.AppendLine($"Location: {artifact.Location}");
        }

        if (artifact.Modified is { } modified)
        {
            builder.AppendLine($"Time: {modified.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz}");
        }

        if (!string.IsNullOrWhiteSpace(artifact.SizeText))
        {
            builder.AppendLine($"Size: {artifact.SizeText}");
        }

        var summary = CrashLogSummary.Summarize(artifact.Content);
        if (!string.IsNullOrWhiteSpace(summary))
        {
            builder.AppendLine($"Summary: {summary}");
        }

        builder.AppendLine();
        builder.AppendLine(artifact.CopyableContent.TrimEnd());
        return builder.ToString();
    }

    /// <summary>
    /// The text of every report, newest first, in one block. This is what the user pastes into an
    /// AI agent: one paste, every report, each one labelled.
    /// </summary>
    public static string BuildAll(
        IEnumerable<CrashLogArtifact> artifacts,
        string header,
        int maxArtifacts = 10)
    {
        ArgumentNullException.ThrowIfNull(artifacts);

        var selected = artifacts
            .OrderByDescending(artifact => artifact.Modified ?? DateTimeOffset.MinValue)
            .Take(Math.Max(maxArtifacts, 1))
            .ToArray();

        if (selected.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine(header);
        builder.AppendLine($"Reports included: {selected.Length}");
        builder.AppendLine();

        foreach (var artifact in selected)
        {
            builder.AppendLine(BuildEntry(artifact));
            builder.AppendLine();
        }

        return builder.ToString();
    }
}
