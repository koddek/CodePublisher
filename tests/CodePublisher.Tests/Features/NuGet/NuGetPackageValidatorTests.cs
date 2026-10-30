using System.IO.Compression;
using System.Text;
using CodePublisher.Features.NuGet.Models;
using CodePublisher.Features.NuGet.Validation;

namespace CodePublisher.Tests.Features.NuGet;

public sealed class NuGetPackageValidatorTests
{
    private static string CreatePackage(string directory, string? nuspec, params string[] entries)
    {
        var path = Path.Combine(directory, "Test.Lib.1.0.0.nupkg");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        if (nuspec is not null)
        {
            var entry = archive.CreateEntry("Test.Lib.nuspec");
            using var writer = new StreamWriter(entry.Open());
            writer.Write(nuspec);
        }

        foreach (var name in entries)
        {
            archive.CreateEntry(name);
        }

        return path;
    }

    /// <summary>Embeds a real managed assembly (the runtime's own) as a lib entry.</summary>
    private static void AddRealManagedDll(string packagePath, string entryName)
    {
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Update);
        var entry = archive.CreateEntry(entryName);
        using var entryStream = entry.Open();
        using var dll = File.OpenRead(typeof(object).Assembly.Location);
        dll.CopyTo(entryStream);
    }

    private static void AddEntryText(string packagePath, string entryName, string text)
    {
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Update);
        var entry = archive.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(text);
    }

    private static string ValidNuspec(string id = "Test.Lib", string version = "1.0.0") =>
        $"""
        <?xml version="1.0"?>
        <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
          <metadata><id>{id}</id><version>{version}</version></metadata>
        </package>
        """;

    [Test]
    public async Task Validate_ValidPackageWithRealDllAndReadme_PassesWithoutIssues()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"cp-val-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(directory);
            var path = CreatePackage(directory, ValidNuspec());
            AddRealManagedDll(path, "lib/net10.0/Test.Lib.dll");
            AddEntryText(path, "readme.md", "# Test.Lib");

            var report = NuGetPackageValidator.Validate(path);

            using (Assert.Multiple())
            {
                await Assert.That(report.IsValid).IsTrue();
                await Assert.That(report.Issues).IsEmpty();
                await Assert.That(report.Identity).IsNotNull();
                await Assert.That(report.Identity!.Id).IsEqualTo("Test.Lib");
                await Assert.That(report.Identity.Version).IsEqualTo("1.0.0");
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task Validate_LibDllThatIsNotManaged_IsAnError()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"cp-val-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(directory);
            var path = CreatePackage(directory, ValidNuspec());
            AddEntryText(path, "lib/net10.0/Test.Lib.dll", "this is not a dll");
            AddEntryText(path, "readme.md", "# Test.Lib");

            var report = NuGetPackageValidator.Validate(path);

            using (Assert.Multiple())
            {
                await Assert.That(report.IsValid).IsFalse();
                await Assert.That(report.Issues.Any(issue =>
                    issue.IsError && issue.Message.Contains("not a managed .NET assembly", StringComparison.OrdinalIgnoreCase))).IsTrue();
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task Validate_MalformedNuspec_IsAnError()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"cp-val-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(directory);
            var path = CreatePackage(directory, "<package><metadata><id>Test.Lib</id>"); // broken XML
            AddEntryText(path, "readme.md", "# x");

            var report = NuGetPackageValidator.Validate(path);

            using (Assert.Multiple())
            {
                await Assert.That(report.IsValid).IsFalse();
                await Assert.That(report.Issues.Any(issue => issue.IsError)).IsTrue();
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task Validate_MissingReadme_IsAWarning()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"cp-val-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(directory);
            var path = CreatePackage(directory, ValidNuspec());
            AddRealManagedDll(path, "lib/net10.0/Test.Lib.dll");

            var report = NuGetPackageValidator.Validate(path);

            using (Assert.Multiple())
            {
                await Assert.That(report.IsValid).IsTrue();
                await Assert.That(report.Issues.Any(issue =>
                    issue.Severity == NuGetIssueSeverity.Warning && issue.Message.Contains("readme", StringComparison.OrdinalIgnoreCase))).IsTrue();
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task Validate_MissingFile_IsAnError()
    {
        var report = NuGetPackageValidator.Validate(Path.Combine(Path.GetTempPath(), "does-not-exist.nupkg"));

        using (Assert.Multiple())
        {
            await Assert.That(report.IsValid).IsFalse();
            await Assert.That(report.Issues[0].IsError).IsTrue();
        }
    }

    [Test]
    public async Task Validate_BadVersion_IsAnError_AndNoIdentity()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"cp-val-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(directory);
            var path = CreatePackage(directory, ValidNuspec(version: "1.0"), "lib/net10.0/Test.Lib.dll");

            var report = NuGetPackageValidator.Validate(path);

            using (Assert.Multiple())
            {
                await Assert.That(report.IsValid).IsFalse();
                await Assert.That(report.Issues.Any(issue => issue.IsError && issue.Message.Contains("version", StringComparison.OrdinalIgnoreCase))).IsTrue();
                await Assert.That(report.Identity).IsNull();
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task Validate_PackageWithoutAssets_IsValidWithWarning()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"cp-val-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(directory);
            var path = CreatePackage(directory, ValidNuspec());
            AddEntryText(path, "readme.md", "# x");

            var report = NuGetPackageValidator.Validate(path);

            using (Assert.Multiple())
            {
                await Assert.That(report.IsValid).IsTrue();
                await Assert.That(report.Issues.Count()).IsEqualTo(1);
                await Assert.That(report.Issues[0].Severity).IsEqualTo(NuGetIssueSeverity.Warning);
                await Assert.That(report.Issues[0].Message).Contains("no lib/");
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task Validate_EscapingEntry_IsAnError()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"cp-val-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "Test.Lib.1.0.0.nupkg");
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                var nuspec = archive.CreateEntry("Test.Lib.nuspec");
                using (var writer = new StreamWriter(nuspec.Open()))
                {
                    writer.Write(ValidNuspec());
                }

                archive.CreateEntry("../evil.dll");
            }

            var report = NuGetPackageValidator.Validate(path);

            using (Assert.Multiple())
            {
                await Assert.That(report.IsValid).IsFalse();
                await Assert.That(report.Issues.Any(issue => issue.IsError && issue.Message.Contains("escape", StringComparison.OrdinalIgnoreCase))).IsTrue();
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
