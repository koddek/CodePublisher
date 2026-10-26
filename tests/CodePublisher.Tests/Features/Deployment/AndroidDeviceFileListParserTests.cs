using CodePublisher.Features.Deployment.Android;

namespace CodePublisher.Tests.Features.Deployment;

public sealed class AndroidDeviceFileListParserTests
{
    private const string LsOutput =
        """
        total 24
        drwxrwx--x 2 root sdcard_rw 4096 2025-01-02 03:04 .
        drwxrwx--x 2 root sdcard_rw 4096 2025-01-02 03:04 ..
        drwxrwx--x 2 root sdcard_rw 4096 2025-01-02 03:04 Android
        drwxrwx--x 3 root sdcard_rw 4096 2025-01-02 03:04 Download
        -rw-rw---- 1 u0_a123 media_rw 12345 2025-01-02 03:04 report name.pdf
        -rw-rw---- 1 u0_a123 media_rw 512 2024-12-31 23:59 notes.txt
        lrwxrwxrwx 1 root root 21 2025-01-02 03:04 sdcard -> /storage/self/primary
        ls: /sdcard/Download/missing: No such file or directory
        """;

    [Test]
    public async Task Parse_ListsFoldersBeforeFilesAndSkipsDotEntries()
    {
        var entries = AndroidDeviceFileListParser.Parse(LsOutput, "/sdcard/Download");

        using (Assert.Multiple())
        {
            await Assert.That(entries.Count).IsEqualTo(5);
            await Assert.That(entries[0].Name).IsEqualTo("Android");
            await Assert.That(entries[0].IsDirectory).IsTrue();
            await Assert.That(entries[1].Name).IsEqualTo("Download");
            await Assert.That(entries.Any(entry => entry.Name is "." or "..")).IsFalse();
        }
    }

    [Test]
    public async Task Parse_KeepsSpacesInNamesAndBuildsFullPaths()
    {
        var entries = AndroidDeviceFileListParser.Parse(LsOutput, "/sdcard/Download");
        var report = entries.Single(entry => entry.Name == "report name.pdf");

        using (Assert.Multiple())
        {
            await Assert.That(report.IsDirectory).IsFalse();
            await Assert.That(report.SizeBytes).IsEqualTo(12345);
            await Assert.That(report.FullPath).IsEqualTo("/sdcard/Download/report name.pdf");
            await Assert.That(report.Modified!.Value.Year).IsEqualTo(2025);
        }
    }

    [Test]
    public async Task Parse_StripsSymlinkTargets()
    {
        var entries = AndroidDeviceFileListParser.Parse(LsOutput, "/sdcard");

        await Assert.That(entries.Any(entry => entry.Name == "sdcard")).IsTrue();
        await Assert.That(entries.Any(entry => entry.Name.Contains("->"))).IsFalse();
    }

    [Test]
    public async Task Parse_IgnoresErrorLinesAndEmptyInput()
    {
        using (Assert.Multiple())
        {
            await Assert.That(AndroidDeviceFileListParser.Parse("ls: /sdcard/Nope: No such file or directory", "/sdcard").Count).IsEqualTo(0);
            await Assert.That(AndroidDeviceFileListParser.Parse(null, "/sdcard").Count).IsEqualTo(0);
        }
    }

    [Test]
    [Arguments(0L, "0 B")]
    [Arguments(512L, "512 B")]
    [Arguments(2048L, "2 KB")]
    [Arguments(5242880L, "5 MB")]
    [Arguments(3758096384L, "3.5 GB")]
    public async Task FormatSize_UsesReadableUnits(long bytes, string expected)
    {
        await Assert.That(AndroidDeviceFileEntry.FormatSize(bytes)).IsEqualTo(expected);
    }

    [Test]
    public async Task Entry_ShowsFolderWithoutSize()
    {
        var folder = new AndroidDeviceFileEntry("Download", "/sdcard/Download", true, 0, null);

        using (Assert.Multiple())
        {
            await Assert.That(folder.SizeText).IsEmpty();
            await Assert.That(folder.DetailText).IsEqualTo("Folder");
            await Assert.That(folder.IconData).IsEqualTo(AndroidDeviceFileEntry.FolderIconData);
        }
    }
}
