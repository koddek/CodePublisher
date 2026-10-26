using System.Text.RegularExpressions;
using CodePublisher.Features.Deployment.CrashLogs;

namespace CodePublisher.Features.Deployment.Android;

/// <summary>One entry from Android's own crash store (the DropBox service).</summary>
public sealed record AndroidCrashStoreEntry(string Tag, string Timestamp, string Body);

/// <summary>
/// Reads the two places Android itself keeps crash information: the DropBox service, which stores
/// the crash report the system wrote, and the log buffer, which still holds the stack trace of
/// the last crash. Pure, so every output shape is covered by tests.
/// </summary>
public static partial class AndroidCrashLogParser
{
    private static readonly string[] CrashMarkers =
    [
        "FATAL EXCEPTION",
        "FATAL UNHANDLED EXCEPTION",
        "UNHANDLED EXCEPTION",
        "Unhandled exception",
        "Fatal signal",
        "beginning of crash",
        "*** *** ***",
        "ANR in ",
        "has died",
        "Force finishing",
        "signal 11",
        "signal 6",
        "SIGSEGV",
        "SIGABRT",
        "backtrace:"
    ];

    /// <summary>
    /// Splits `dumpsys dropbox --print` output into the entries that belong to the app. An entry
    /// belongs to the app when its body names the package.
    /// </summary>
    public static IReadOnlyList<AndroidCrashStoreEntry> ParseCrashStore(string? text, string? packageId)
    {
        var entries = new List<AndroidCrashStoreEntry>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return entries;
        }

        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        string? tag = null;
        string? timestamp = null;
        var body = new List<string>();

        foreach (var line in lines)
        {
            var header = CrashStoreHeaderRegex().Match(line);
            if (header.Success)
            {
                Flush(entries, tag, timestamp, body, packageId);
                tag = header.Groups["tag"].Value;
                timestamp = $"{header.Groups["date"].Value} {header.Groups["time"].Value}";
                body.Clear();
                continue;
            }

            body.Add(line);
        }

        Flush(entries, tag, timestamp, body, packageId);
        return entries;
    }

    /// <summary>
    /// Keeps the crash of the app out of the log buffer: the crash marker, the stack trace that
    /// follows it, and the lines of the same process that name the app. The order of the original
    /// buffer is kept, so the result reads like the log did.
    ///
    /// A crash of another app is never included, even when it happened on the same device, because
    /// the report has to be about the app the user is looking at.
    /// </summary>
    public static IReadOnlyList<string> ExtractCrashLines(string? logcatText, string? packageId)
    {
        var lines = (logcatText ?? string.Empty).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
        {
            return [];
        }

        var records = new List<LogLine>(lines.Length);
        foreach (var line in lines)
        {
            records.Add(LogLine.Parse(line));
        }

        var blocks = FindCrashBlocks(records);
        if (blocks.Count == 0)
        {
            return [];
        }

        // The blocks of one process belong together: a .NET app writes its own stack trace first
        // and the system writes the Java crash report after it, and only the second one names the
        // app. When any block of a process names the app, the whole crash of that process is kept.
        var keep = new bool[records.Count];
        foreach (var group in blocks.GroupBy(block => block.ProcessId, StringComparer.OrdinalIgnoreCase))
        {
            var namesTheApp = !string.IsNullOrWhiteSpace(packageId)
                && group.Any(block => BlockText(records, block).Contains(packageId, StringComparison.OrdinalIgnoreCase));

            if (!namesTheApp)
            {
                continue;
            }

            foreach (var block in group)
            {
                for (var index = block.Start; index <= block.End; index++)
                {
                    keep[index] = true;
                }
            }
        }

        return keep
            .Select((isKept, index) => (isKept, index))
            .Where(item => item.isKept)
            .Select(item => lines[item.index])
            .ToArray();
    }

    /// <summary>
    /// The crash blocks of the buffer: a crash marker and the lines of the same process that
    /// follow it. A block ends where another process takes over or the level turns back to normal.
    /// </summary>
    private static List<CrashBlock> FindCrashBlocks(IReadOnlyList<LogLine> records)
    {
        var blocks = new List<CrashBlock>();

        for (var index = 0; index < records.Count; index++)
        {
            var record = records[index];
            if (record.IsAdbDaemon || !IsCrashMarker(record.Message))
            {
                continue;
            }

            var end = index;
            for (var next = index + 1; next < records.Count; next++)
            {
                var candidate = records[next];
                if (!candidate.Parsed)
                {
                    // A continuation line of a stack frame that did not repeat the header.
                    end = next;
                    continue;
                }

                if (string.Equals(candidate.ProcessId, record.ProcessId, StringComparison.OrdinalIgnoreCase)
                    && (candidate.Level is "E" or "F"
                        || candidate.Message.StartsWith("at ", StringComparison.Ordinal)
                        || candidate.Message.StartsWith("\tat ", StringComparison.Ordinal)
                        || candidate.Message.Contains("Caused by", StringComparison.Ordinal)))
                {
                    end = next;
                    continue;
                }

                break;
            }

            blocks.Add(new CrashBlock(index, end, record.ProcessId));
            index = end;
        }

        return blocks;
    }

    private static string BlockText(IReadOnlyList<LogLine> records, CrashBlock block) =>
        string.Join(
            Environment.NewLine,
            records.Skip(block.Start).Take(block.End - block.Start + 1).Select(record => record.Text));

    /// <summary>
    /// The one line that says what the crash was about: the first exception, error, or signal in
    /// the report. Empty when the report holds nothing that names a cause.
    /// </summary>
    public static string Summarize(string? text) => CrashLogSummary.Summarize(text);

    private static void Flush(
        List<AndroidCrashStoreEntry> entries,
        string? tag,
        string? timestamp,
        List<string> body,
        string? packageId)
    {
        if (tag is null || body.Count == 0)
        {
            body.Clear();
            return;
        }

        var text = string.Join(Environment.NewLine, body);
        if (!string.IsNullOrWhiteSpace(packageId)
            && !text.Contains(packageId, StringComparison.OrdinalIgnoreCase))
        {
            body.Clear();
            return;
        }

        entries.Add(new AndroidCrashStoreEntry(tag, timestamp ?? string.Empty, text));
        body.Clear();
    }

    private static bool IsCrashMarker(string? message) =>
        !string.IsNullOrEmpty(message)
        && CrashMarkers.Any(marker => message.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static bool IsAdbDaemonTag(string? tag) =>
        string.Equals(tag, "adbd", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads a `logcat -v threadtime` line: `MM-DD HH:MM:SS.mmm  PID  TID LEVEL TAG: message`.
    /// </summary>
    private static bool TryParseThreadTimeLine(
        string line,
        out string pid,
        out string level,
        out string tag,
        out string message)
    {
        pid = string.Empty;
        level = string.Empty;
        tag = string.Empty;
        message = string.Empty;

        var match = ThreadTimeLineRegex().Match(line);
        if (!match.Success)
        {
            return false;
        }

        pid = match.Groups["pid"].Value;
        level = match.Groups["level"].Value;
        tag = match.Groups["tag"].Value.Trim();
        message = match.Groups["message"].Value.Trim();
        return true;
    }

    /// <summary>One line of the log buffer, with the parts the crash rules need.</summary>
    private sealed record LogLine(
        string Text,
        bool Parsed,
        string ProcessId,
        string Level,
        string Tag,
        string Message)
    {
        public bool IsAdbDaemon => Parsed && IsAdbDaemonTag(Tag);

        public static LogLine Parse(string line)
        {
            var parsed = TryParseThreadTimeLine(line, out var pid, out var level, out var tag, out var message);
            return new LogLine(line, parsed, pid, level, tag, message);
        }
    }

    /// <summary>A crash marker and the lines of the same process that follow it.</summary>
    private sealed record CrashBlock(int Start, int End, string ProcessId);

    [GeneratedRegex(@"^(?<date>\d{4}-\d{2}-\d{2})\s+(?<time>\d{2}:\d{2}:\d{2})\s+(?<tag>\S+)\s+\((?<kind>[^,]+),\s*(?<bytes>\d+)\s+bytes?\)\s*$")]
    private static partial Regex CrashStoreHeaderRegex();

    [GeneratedRegex(@"^\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2}\.\d{3}\s+(?<pid>\d+)\s+\d+\s+(?<level>[VDIWEF])\s+(?<tag>[^:]*):\s?(?<message>.*)$")]
    private static partial Regex ThreadTimeLineRegex();
}
