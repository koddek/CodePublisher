using CodePublisher.Features.Publishing.Artifacts;

namespace CodePublisher.Tests.Features.Publishing.Artifacts;

public sealed class ArtifactLocatorTests
{
    [Test]
    public async Task FindBestApk_PrefersSignedProjectArtifact()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"dnap-artifacts-{Guid.NewGuid():N}"));
        try
        {
            var unsigned = new FileInfo(Path.Combine(root.FullName, "App-debug.apk"));
            var signed = new FileInfo(Path.Combine(root.FullName, "App-Signed.apk"));
            await File.WriteAllTextAsync(unsigned.FullName, "unsigned");
            await File.WriteAllTextAsync(signed.FullName, "signed");

            var result = ArtifactLocator.FindBestApk(root.FullName, "App", "com.example.app");

            await Assert.That(result?.FullName).IsEqualTo(signed.FullName);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Test]
    public async Task FindBestAab_ReturnsNewestMatchingArtifact()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"dnap-artifacts-{Guid.NewGuid():N}"));
        try
        {
            var older = new FileInfo(Path.Combine(root.FullName, "App-old.aab"));
            var newer = new FileInfo(Path.Combine(root.FullName, "App-new.aab"));
            await File.WriteAllTextAsync(older.FullName, "older");
            await File.WriteAllTextAsync(newer.FullName, "newer");
            newer.LastWriteTimeUtc = older.LastWriteTimeUtc.AddMinutes(1);

            var result = ArtifactLocator.FindBestAab(root.FullName, "App");

            await Assert.That(result?.FullName).IsEqualTo(newer.FullName);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Test]
    public async Task FindBestPackageFolder_ReturnsTheSingleFolderHoldingThePackages()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"dnap-artifacts-{Guid.NewGuid():N}"));
        try
        {
            var nested = Directory.CreateDirectory(Path.Combine(root.FullName, "android-arm64", "publish"));
            await File.WriteAllTextAsync(Path.Combine(nested.FullName, "App-Signed.apk"), "apk");
            await File.WriteAllTextAsync(Path.Combine(nested.FullName, "App-Signed.aab"), "aab");

            var result = ArtifactLocator.FindBestPackageFolder(root.FullName, "App", "com.example.app");

            await Assert.That(result?.FullName).IsEqualTo(nested.FullName);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Test]
    public async Task FindBestUniversalApk_PrefersSignedApkAcrossRoots()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"dnap-artifacts-{Guid.NewGuid():N}"));
        try
        {
            var build = Directory.CreateDirectory(Path.Combine(root.FullName, "bin", "Release"));
            var other = Directory.CreateDirectory(Path.Combine(root.FullName, "other"));
            await File.WriteAllTextAsync(Path.Combine(other.FullName, "App-debug.apk"), "unsigned");
            var signed = new FileInfo(Path.Combine(build.FullName, "com.example.App-Signed.apk"));
            await File.WriteAllTextAsync(signed.FullName, "signed");

            var result = ArtifactLocator.FindBestUniversalApk(
                [build.FullName, other.FullName, Path.Combine(root.FullName, "missing")],
                "App",
                "com.example");

            await Assert.That(result?.FullName).IsEqualTo(signed.FullName);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Test]
    public async Task FindBestUniversalApk_ReturnsNull_WhenNoSignedApkExists()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"dnap-artifacts-{Guid.NewGuid():N}"));
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root.FullName, "App-debug.apk"), "unsigned");

            var result = ArtifactLocator.FindBestUniversalApk([root.FullName], "App");

            await Assert.That(result).IsNull();
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
