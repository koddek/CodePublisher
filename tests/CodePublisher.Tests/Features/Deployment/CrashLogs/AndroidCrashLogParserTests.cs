using CodePublisher.Features.Deployment.Android;

namespace CodePublisher.Tests.Features.Deployment.CrashLogs;

public sealed class AndroidCrashLogParserTests
{
    private const string DropBoxOutput =
        """
        Drop box contents: 2 entries
        Max entries: 1000
        Low priority rate limit period: 2000 ms

        ========================================
        2026-10-20 20:35:08 system_app_crash (text, 1285 bytes)
        SystemUptimeMs: 47523
        Process: com.example.app
        PID: 1193
        UID: 1000
        Package: com.example.app v37 (17)
        Timestamp: 2026-10-20 20:35:08.846-0400
        Dropped-Count: 0

        android.app.RemoteServiceException$CrashedByAdbException: shell-induced crash
        	at android.app.ActivityThread.throwRemoteServiceException(ActivityThread.java:2595)
        	at android.app.ActivityThread.main(ActivityThread.java:9569)

        ========================================
        2026-10-20 20:36:00 system_app_crash (text, 640 bytes)
        Process: com.other.app
        PID: 2222

        java.lang.IllegalStateException: other app
        """;

    private const string LogcatOutput =
        """
        10-20 20:35:08.840  1193  1193 E AndroidRuntime: FATAL EXCEPTION: main
        10-20 20:35:08.840  1193  1193 E AndroidRuntime: Process: com.example.app, PID: 1193
        10-20 20:35:08.840  1193  1193 E AndroidRuntime: java.lang.RuntimeException: boom
        10-20 20:35:08.840  1193  1193 E AndroidRuntime: 	at com.example.app.MainActivity.onCreate(MainActivity.java:42)
        10-20 20:35:08.840  1193  1193 E AndroidRuntime: Caused by: java.lang.NullPointerException: missing
        10-20 20:35:08.900  1194  1194 I ActivityManager: Displayed com.example.app/.MainActivity
        10-20 20:35:09.100  1300  1300 D SomeOther: nothing to do with the crash
        """;

    [Test]
    public async Task ParseCrashStore_KeepsOnlyTheEntriesOfTheApp()
    {
        var entries = AndroidCrashLogParser.ParseCrashStore(DropBoxOutput, "com.example.app");

        using (Assert.Multiple())
        {
            await Assert.That(entries.Count).IsEqualTo(1);
            await Assert.That(entries[0].Tag).IsEqualTo("system_app_crash");
            await Assert.That(entries[0].Timestamp).IsEqualTo("2026-10-20 20:35:08");
            await Assert.That(entries[0].Body).Contains("shell-induced crash");
        }
    }

    [Test]
    public async Task ParseCrashStore_ReadsTheHeaderShapeTheDeviceProduces()
    {
        var entries = AndroidCrashLogParser.ParseCrashStore(DropBoxOutput, "com.example.app");

        await Assert.That(entries[0].Body).DoesNotContain("com.other.app");
    }

    [Test]
    public async Task ParseCrashStore_IgnoresEmptyInput()
    {
        using (Assert.Multiple())
        {
            await Assert.That(AndroidCrashLogParser.ParseCrashStore(null, "com.example.app").Count).IsEqualTo(0);
            await Assert.That(AndroidCrashLogParser.ParseCrashStore("   ", "com.example.app").Count).IsEqualTo(0);
        }
    }

    [Test]
    public async Task ExtractCrashLines_KeepsTheStackTraceAndDropsTheNoise()
    {
        var lines = AndroidCrashLogParser.ExtractCrashLines(LogcatOutput, "com.example.app");

        using (Assert.Multiple())
        {
            await Assert.That(lines.Count).IsEqualTo(5);
            await Assert.That(lines[0]).Contains("FATAL EXCEPTION");
            await Assert.That(lines.Any(line => line.Contains("MainActivity.onCreate"))).IsTrue();
            await Assert.That(lines.Any(line => line.Contains("SomeOther"))).IsFalse();
            await Assert.That(lines.Any(line => line.Contains("Displayed com.example.app"))).IsFalse();
        }
    }

    [Test]
    public async Task ExtractCrashLines_LeavesOutTheCrashOfAnotherApp()
    {
        var lines = AndroidCrashLogParser.ExtractCrashLines(LogcatOutput, "com.other.app");

        await Assert.That(lines.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ExtractCrashLines_KeepsTheStackTraceOfANetAppThatTheSystemAlsoReports()
    {
        const string logcat =
            """
            10-20 20:41:04.512  6189  6189 E mono    : [ERROR] FATAL UNHANDLED EXCEPTION: System.InvalidOperationException: boom
            10-20 20:41:04.512  6189  6189 E mono    :   at MyApp.Services.Settings.Load()
            10-20 20:41:04.512  6189  6189 E AndroidRuntime: FATAL EXCEPTION: main
            10-20 20:41:04.512  6189  6189 E AndroidRuntime: Process: com.example.app, PID: 6189
            """;

        var lines = AndroidCrashLogParser.ExtractCrashLines(logcat, "com.example.app");

        using (Assert.Multiple())
        {
            await Assert.That(lines.Count).IsEqualTo(4);
            await Assert.That(lines[0]).Contains("System.InvalidOperationException");
            await Assert.That(lines.Any(line => line.Contains("Settings.Load"))).IsTrue();
        }
    }

    [Test]
    public async Task ExtractCrashLines_KeepsTheOrderOfTheBuffer()
    {
        var lines = AndroidCrashLogParser.ExtractCrashLines(LogcatOutput, "com.example.app");

        await Assert.That(lines[1]).Contains("Process: com.example.app");
    }

    [Test]
    public async Task ExtractCrashLines_ReturnsNothingWhenThereIsNoCrash()
    {
        var lines = AndroidCrashLogParser.ExtractCrashLines(
            "10-20 20:35:08.840  1193  1193 I ActivityManager: Displayed com.example.app/.MainActivity",
            "com.example.app");

        await Assert.That(lines.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Summarize_NamesTheExceptionThatCausedTheCrash()
    {
        using (Assert.Multiple())
        {
            await Assert.That(AndroidCrashLogParser.Summarize("java.lang.RuntimeException: boom"))
                .IsEqualTo("java.lang.RuntimeException: boom");
            await Assert.That(AndroidCrashLogParser.Summarize("nothing useful here")).IsEqualTo(string.Empty);
            await Assert.That(AndroidCrashLogParser.Summarize(null)).IsEqualTo(string.Empty);
        }
    }
}
