using CodePublisher.Features.Deployment.Android;

namespace CodePublisher.Tests.Features.Deployment;

public sealed class AndroidStorageLocationsTests
{
    [Test]
    [Arguments("", "/sdcard")]
    [Arguments("/sdcard/", "/sdcard")]
    [Arguments("sdcard/Download", "/sdcard/Download")]
    [Arguments("/sdcard/Download/", "/sdcard/Download")]
    [Arguments("\\sdcard\\Download", "/sdcard/Download")]
    public async Task NormalizeRemotePath_ProducesShellPaths(string input, string expected)
    {
        await Assert.That(AndroidStorageLocations.NormalizeRemotePath(input)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("/sdcard", "/sdcard")]
    [Arguments("/sdcard/Download", "/sdcard")]
    [Arguments("/sdcard/DCIM/Camera", "/sdcard/DCIM")]
    public async Task GetParentRemotePath_StopsAtInternalStorage(string path, string expected)
    {
        await Assert.That(AndroidStorageLocations.GetParentRemotePath(path)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("/sdcard", true)]
    [Arguments("/sdcard/Download", true)]
    [Arguments("/storage/emulated/0/Download", false)]
    [Arguments("/data/data/com.example", false)]
    public async Task IsWithinInternalStorage_OnlyAllowsSharedStorage(string path, bool expected)
    {
        await Assert.That(AndroidStorageLocations.IsWithinInternalStorage(path)).IsEqualTo(expected);
    }

    [Test]
    public async Task QuoteForRemoteShell_EscapesSpacesAndApostrophes()
    {
        var quoted = AndroidStorageLocations.QuoteForRemoteShell("/sdcard/My Files/it's.txt");

        await Assert.That(quoted).IsEqualTo("'/sdcard/My Files/it'\\''s.txt'");
    }

    [Test]
    public async Task CombineRemotePath_JoinsFolderAndName()
    {
        await Assert.That(AndroidStorageLocations.CombineRemotePath("/sdcard/Download/", "notes.txt"))
            .IsEqualTo("/sdcard/Download/notes.txt");
    }

    [Test]
    public async Task AsRemoteDirectory_KeepsTrailingSlashForPush()
    {
        await Assert.That(AndroidStorageLocations.AsRemoteDirectory("/sdcard/Download")).IsEqualTo("/sdcard/Download/");
    }

    [Test]
    public async Task QuickLocations_CoverThePublicUserFolders()
    {
        var labels = AndroidStorageLocations.QuickLocations.Select(location => location.Label).ToArray();

        using (Assert.Multiple())
        {
            await Assert.That(labels).Contains("Downloads");
            await Assert.That(labels).Contains("Pictures");
            await Assert.That(labels).Contains("Camera (DCIM)");
            await Assert.That(labels).Contains("Documents");
            await Assert.That(labels).Contains("Movies");
            await Assert.That(labels).Contains("Music");
            await Assert.That(AndroidStorageLocations.QuickLocations.Any(location => location.RemotePath == AndroidStorageLocations.Downloads)).IsTrue();
        }
    }
}
