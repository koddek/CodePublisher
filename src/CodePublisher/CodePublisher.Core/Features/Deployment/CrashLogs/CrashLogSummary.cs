namespace CodePublisher.Features.Deployment.CrashLogs;

/// <summary>
/// The one line of a crash report that says what the crash was about. Shared by every platform,
/// because the question the user asks is the same one everywhere: what went wrong.
/// </summary>
public static class CrashLogSummary
{
    private static readonly string[] ExceptionMarkers =
    [
        "Exception",
        "Error:",
        "Fatal error",
        "Fatal signal",
        "Unhandled exception",
        "Terminating app",
        "abort message",
        "SIGSEGV",
        "SIGABRT",
        "panic:",
        "Assertion failed",
        "crashed"
    ];

    /// <summary>
    /// The first exception, error, or signal in the report. Empty when the report holds nothing
    /// that names a cause.
    /// </summary>
    public static string Summarize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (ExceptionMarkers.Any(marker => trimmed.Contains(marker, StringComparison.Ordinal)))
            {
                return trimmed;
            }
        }

        return string.Empty;
    }
}
