namespace DotNetAppPublisher.Features.Deployment.CrashLogs;

/// <summary>
/// One crash report that was found for an app: where it came from, where it lives, and the text
/// that can be pasted into an AI agent. The text is already loaded, because a crash report is
/// small and the user wants to read and copy it without another round trip.
/// </summary>
public sealed record CrashLogArtifact(
    string Name,
    CrashLogSource Source,
    string Location,
    string Description,
    long SizeBytes,
    DateTimeOffset? Modified,
    string Content,
    bool CanShowText)
{
    public string SizeText => SizeBytes switch
    {
        <= 0 => string.Empty,
        < 1024 => $"{SizeBytes} B",
        < 1024L * 1024 => $"{SizeBytes / 1024d:0.#} KB",
        < 1024L * 1024 * 1024 => $"{SizeBytes / (1024d * 1024):0.#} MB",
        _ => $"{SizeBytes / (1024d * 1024 * 1024):0.##} GB"
    };

    public string ModifiedText => Modified is { } modified
        ? modified.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
        : string.Empty;

    /// <summary>Short label for where the report came from, shown next to its name.</summary>
    public string SourceText => Source switch
    {
        CrashLogSource.AppData => "App data folder",
        CrashLogSource.AndroidCrashStore => "Android crash store",
        CrashLogSource.DeviceLog => "Device log",
        CrashLogSource.SystemCrashLog => "System crash report",
        CrashLogSource.LocalDump => "Local dump",
        _ => "Crash report"
    };

    /// <summary>One line per row: the size, the time, and where it came from.</summary>
    public string DetailText
    {
        get
        {
            var parts = new[] { SourceText, ModifiedText, SizeText }
                .Where(text => !string.IsNullOrEmpty(text))
                .ToArray();
            return parts.Length == 0 ? Description : string.Join(" · ", parts);
        }
    }

    /// <summary>The text a user copies into an AI agent.</summary>
    public string CopyableContent => CanShowText
        ? Content
        : $"{Description}{Environment.NewLine}{Environment.NewLine}" +
          "This file is a binary dump. It cannot be read as text; open it in the crash analysis tool " +
          "that produced it.";
}
