using CodePublisher.Features.Deployment.CrashLogs;

namespace CodePublisher.Tests.Features.Deployment.CrashLogs;

public sealed class CrashLogLocatorTests
{
    [Test]
    [Arguments("crash-2026-10-20.log", true)]
    [Arguments("CrashReport.txt", true)]
    [Arguments("app.exception.log", true)]
    [Arguments("stacktrace.txt", true)]
    [Arguments("crash.dmp", true)]
    [Arguments("heap.hprof", true)]
    [Arguments("MyApp.ips", true)]
    [Arguments("notes.txt", false)]
    [Arguments("settings.db", false)]
    [Arguments("logger.log", false)]
    [Arguments("", false)]
    public async Task LooksLikeCrashFile_FindsTheNamesThatMarkACrashReport(string name, bool expected)
    {
        await Assert.That(CrashLogLocator.LooksLikeCrashFile(name)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("crash.dmp", true)]
    [Arguments("heap.hprof", true)]
    [Arguments("crash.log", false)]
    public async Task IsBinaryDump_MarksTheFilesThatCannotBeReadAsText(string name, bool expected)
    {
        await Assert.That(CrashLogLocator.IsBinaryDump(name)).IsEqualTo(expected);
    }

    [Test]
    public async Task AndroidAppDataFolders_StartWithTheLogsFolderOfTheAppDataFolder()
    {
        using (Assert.Multiple())
        {
            await Assert.That(CrashLogLocator.AndroidAppDataFolders[0]).IsEqualTo("files/logs");
            await Assert.That(CrashLogLocator.AndroidAppDataFolders).Contains("files");
            await Assert.That(CrashLogLocator.AndroidAppDataFolders).Contains("cache");
        }
    }

    [Test]
    public async Task IosAppDataFolders_HoldTheLogsFolderOfTheAppContainer()
    {
        using (Assert.Multiple())
        {
            await Assert.That(CrashLogLocator.IosAppDataFolders[0]).IsEqualTo("Library/Logs");
            await Assert.That(CrashLogLocator.IosAppDataFolders).Contains("Library/Caches");
        }
    }

    [Test]
    public async Task DescribeAppDataFolder_NamesTheFolderInTheWordsOfThePlatform()
    {
        using (Assert.Multiple())
        {
            await Assert.That(CrashLogLocator.DescribeAppDataFolder("Android")).Contains("/data/data/<package>");
            await Assert.That(CrashLogLocator.DescribeAppDataFolder("iOS")).Contains("Library/Logs");
        }
    }
}
