using System.IO.Compression;
using System.Text;
using CodePublisher.Features.NuGet.Inspect;

namespace CodePublisher.Tests.Features.NuGet;

public sealed class NuGetPackageReaderTests
{
    private static string CreateNupkg(string directory, string nuspec, params string[] extraEntries)
    {
        var path = Path.Combine(directory, "Test.Lib.1.2.3.nupkg");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            var nuspecEntry = archive.CreateEntry("Test.Lib.nuspec");
            using (var writer = new StreamWriter(nuspecEntry.Open()))
            {
                writer.Write(nuspec);
            }

            foreach (var entry in extraEntries)
            {
                archive.CreateEntry(entry);
            }
        }

        return path;
    }

    private const string Nuspec2013 =
        """
        <?xml version="1.0" encoding="utf-8"?>
        <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
          <metadata>
            <id>Test.Lib</id>
            <version>1.2.3</version>
            <description>A test package.</description>
          </metadata>
        </package>
        """;

    [Test]
    public async Task Read_ExtractsIdAndVersion_FromNamespacedNuspec()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"cp-nupkg-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(directory);
            var path = CreateNupkg(directory, Nuspec2013, "lib/net10.0/Test.Lib.dll", "build/Test.Lib.targets");

            var contents = NuGetPackageReader.Read(path);

            using (Assert.Multiple())
            {
                await Assert.That(contents.Id).IsEqualTo("Test.Lib");
                await Assert.That(contents.Version).IsEqualTo("1.2.3");
                await Assert.That(contents.HasLibAssets).IsTrue();
                await Assert.That(contents.HasBuildAssets).IsTrue();
                await Assert.That(contents.HasRefAssets).IsFalse();
                await Assert.That(contents.Entries).Contains("lib/net10.0/Test.Lib.dll");
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task Read_WithoutNuspec_ReportsNullIdentity()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"cp-nupkg-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(directory);
            var path = CreateNupkg(directory, Nuspec2013);
            // Overwrite with an archive that has no nuspec.
            File.Delete(path);
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                archive.CreateEntry("lib/net10.0/Test.Lib.dll");
            }

            var contents = NuGetPackageReader.Read(path);

            using (Assert.Multiple())
            {
                await Assert.That(contents.Id).IsNull();
                await Assert.That(contents.Version).IsNull();
                await Assert.That(contents.NuspecEntryName).IsNull();
                await Assert.That(contents.HasLibAssets).IsTrue();
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
