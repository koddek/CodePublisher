using DotNetAppPublisher.Features.Deployment.CrashLogs;

namespace DotNetAppPublisher.Tests.Features.Deployment.CrashLogs;

public sealed class CrashLogReportTests
{
    private static CrashLogArtifact CreateArtifact(
        string name,
        string content,
        DateTimeOffset? modified = null,
        CrashLogSource source = CrashLogSource.AppData) =>
        new(
            name,
            source,
            "/data/data/com.example.app/files/logs/crash.log",
            "Written by the app",
            content.Length,
            modified ?? new DateTimeOffset(2026, 10, 20, 20, 35, 8, TimeSpan.Zero),
            content,
            true);

    [Test]
    public async Task BuildEntry_NamesTheReportTheSourceAndTheTime()
    {
        var entry = CrashLogReport.BuildEntry(CreateArtifact("crash.log", "java.lang.RuntimeException: boom"));

        using (Assert.Multiple())
        {
            await Assert.That(entry).Contains("=== Crash report: crash.log ===");
            await Assert.That(entry).Contains("Source: App data folder");
            await Assert.That(entry).Contains("Location: /data/data/com.example.app/files/logs/crash.log");
            await Assert.That(entry).Contains("java.lang.RuntimeException: boom");
        }
    }

    [Test]
    public async Task BuildEntry_SaysWhatTheCrashWasAbout()
    {
        var entry = CrashLogReport.BuildEntry(CreateArtifact("crash.log", "java.lang.RuntimeException: boom"));

        await Assert.That(entry).Contains("Summary: java.lang.RuntimeException: boom");
    }

    [Test]
    public async Task BuildAll_PutsTheNewestReportFirstAndCountsThem()
    {
        var older = CreateArtifact("older.log", "java.lang.RuntimeException: older", new DateTimeOffset(2026, 10, 19, 10, 0, 0, TimeSpan.Zero));
        var newer = CreateArtifact("newer.log", "java.lang.RuntimeException: newer", new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero));

        var text = CrashLogReport.BuildAll([older, newer], "Crash reports of com.example.app.");

        using (Assert.Multiple())
        {
            await Assert.That(text).Contains("Reports included: 2");
            await Assert.That(text.IndexOf("newer.log", StringComparison.Ordinal))
                .IsLessThan(text.IndexOf("older.log", StringComparison.Ordinal));
        }
    }

    [Test]
    public async Task BuildAll_ReturnsNothingWhenThereIsNoReport()
    {
        await Assert.That(CrashLogReport.BuildAll([], "Crash reports.")).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task BuildAll_KeepsToTheRequestedNumberOfReports()
    {
        var artifacts = Enumerable.Range(0, 12)
            .Select(index => CreateArtifact($"crash-{index}.log", $"java.lang.RuntimeException: {index}"))
            .ToArray();

        var text = CrashLogReport.BuildAll(artifacts, "Crash reports.", maxArtifacts: 3);

        await Assert.That(text).Contains("Reports included: 3");
    }

    [Test]
    public async Task CopyableContent_SaysThatABinaryDumpCannotBeReadAsText()
    {
        var artifact = new CrashLogArtifact(
            "crash.dmp",
            CrashLogSource.LocalDump,
            "/tmp/crash.dmp",
            "The memory dump Windows wrote",
            1024,
            null,
            string.Empty,
            false);

        using (Assert.Multiple())
        {
            await Assert.That(artifact.CopyableContent).Contains("binary dump");
            await Assert.That(artifact.CopyableContent).Contains("The memory dump Windows wrote");
        }
    }
}
