namespace CodePublisher.Features.Deployment.CrashLogs;

/// <summary>
/// The result of a crash log scan: the reports that were found, and the notes that say what was
/// looked for and what could not be read. The notes matter as much as the reports, because the
/// place an app keeps its crash files is a choice the app makes, not a platform rule.
/// </summary>
public sealed record CrashLogScan(IReadOnlyList<CrashLogArtifact> Artifacts, IReadOnlyList<string> Notes)
{
    public static CrashLogScan Empty { get; } = new([], []);

    public bool HasArtifacts => Artifacts.Count > 0;

    public bool HasNotes => Notes.Count > 0;
}
