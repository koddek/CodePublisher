using System.IO.Compression;
using CodePublisher.Features.NuGet.Feeds;
using CodePublisher.Features.NuGet.Models;
using CodePublisher.Features.NuGet.Services;

namespace CodePublisher.Tests.Features.NuGet;

public sealed class NuGetPackageServiceTests
{
    private sealed record Invocation(IReadOnlyList<string> Arguments, string? WorkingDirectory);

    private sealed class ImposterRunner
    {
        public List<Invocation> Calls { get; } = [];
        public Func<IReadOnlyList<string>, int>? ExitCodeFor;
        public Action<IReadOnlyList<string>, Action<string>>? Emit;

        public Task<int> Run(IReadOnlyList<string> arguments, Action<string> writeOutput, CancellationToken cancellationToken, string? workingDirectory)
        {
            Calls.Add(new Invocation(arguments, workingDirectory));
            writeOutput("imposter output" + Environment.NewLine);
            Emit?.Invoke(arguments, writeOutput);
            return Task.FromResult(ExitCodeFor?.Invoke(arguments) ?? 0);
        }
    }

    private const string EmptyFeedIndex =
        """{ "version": "3.0.0", "resources": [ { "@id": "https://api.nuget.org/v3-flatcontainer/", "@type": "PackageBaseAddress" } ] }""";

    private static NuGetPackageService CreateService(ImposterRunner runner, HttpClient? httpClient = null) =>
        new("/dotnet", new NuGetFeedClient(httpClient ?? new HttpClient(new StubHandler(request =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(request.RequestUri!.AbsolutePath.Contains("flatcontainer")
                    ? """{ "versions": [] }"""
                    : EmptyFeedIndex)
            }))), runner.Run);

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }

    private static void WriteNupkg(string folder, string id, string version, bool withLib = true)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"{id}.{version}.nupkg");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        var nuspec = archive.CreateEntry($"{id}.nuspec");
        using (var writer = new StreamWriter(nuspec.Open()))
        {
            writer.Write($"""
                <?xml version="1.0"?>
                <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
                  <metadata><id>{id}</id><version>{version}</version></metadata>
                </package>
                """);
        }

        if (withLib)
        {
            // A real managed assembly, so the validator's CLR-header check passes.
            var entry = archive.CreateEntry("lib/net10.0/Lib.dll");
            using var entryStream = entry.Open();
            using var dll = File.OpenRead(typeof(object).Assembly.Location);
            dll.CopyTo(entryStream);
        }

        var readme = archive.CreateEntry("readme.md");
        using (var writer = new StreamWriter(readme.Open()))
        {
            writer.Write("# Lib");
        }
    }

    [Test]
    public async Task Pack_Success_ReturnsNewestNupkgWithIdentity()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cp-svc-{Guid.NewGuid():N}");
        try
        {
            var projectDir = Path.Combine(root, "proj");
            Directory.CreateDirectory(projectDir);
            var projectFile = Path.Combine(projectDir, "Lib.csproj");
            File.WriteAllText(projectFile, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            var output = Path.Combine(projectDir, "nupkgs");

            var runner = new ImposterRunner
            {
                ExitCodeFor = args => args.Contains("pack")
                    ? CreatePackedNupkg(args, output)
                    : 0
            };
            var service = CreateService(runner);

            var outcome = await service.PackAsync(projectFile, "Release", output, null, false, _ => { }, CancellationToken.None);

            using (Assert.Multiple())
            {
                await Assert.That(outcome.Success).IsTrue();
                await Assert.That(outcome.Package).IsNotNull();
                await Assert.That(outcome.Package!.Validation.Identity!.Id).IsEqualTo("Lib");
                await Assert.That(outcome.Package.Validation.Identity.Version).IsEqualTo("1.0.0");
                await Assert.That(runner.Calls[0].Arguments).Contains("pack");
                await Assert.That(runner.Calls[0].Arguments).Contains("-c");
                await Assert.That(runner.Calls[0].Arguments).Contains("Release");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }

        static int CreatePackedNupkg(IReadOnlyList<string> args, string output)
        {
            WriteNupkg(output, "Lib", "1.0.0");
            return 0;
        }
    }

    [Test]
    public async Task Pack_NU5026_ForcesRebuild_ThenRepacksWithNoBuild()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cp-svc-{Guid.NewGuid():N}");
        try
        {
            var projectDir = Path.Combine(root, "proj");
            Directory.CreateDirectory(projectDir);
            var projectFile = Path.Combine(projectDir, "Lib.csproj");
            File.WriteAllText(projectFile, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            var output = Path.Combine(projectDir, "nupkgs");

            var runner = new ImposterRunner
            {
                Emit = (args, write) =>
                {
                    if (args.Contains("pack") && !args.Contains("--no-build"))
                    {
                        write("error NU5026: The file 'bin/Release/net10.0/Lib.dll' to be packed was not found on disk." + Environment.NewLine);
                    }
                },
                ExitCodeFor = args =>
                {
                    if (args.Contains("pack") && args.Contains("--no-build"))
                    {
                        WriteNupkg(output, "Lib", "1.0.0");
                        return 0;
                    }

                    return args.Contains("pack") ? 1 : 0;
                }
            };
            var service = CreateService(runner);

            var outcome = await service.PackAsync(projectFile, "Release", output, null, false, _ => { }, CancellationToken.None);

            using (Assert.Multiple())
            {
                await Assert.That(outcome.Success).IsTrue();
                await Assert.That(outcome.Package!.Validation.Identity!.Version).IsEqualTo("1.0.0");
                // pack (fails NU5026) -> forced build -> pack --no-build
                await Assert.That(runner.Calls.Count()).IsEqualTo(3);
                await Assert.That(runner.Calls[0].Arguments).Contains("pack");
                await Assert.That(runner.Calls[0].Arguments).DoesNotContain("--no-build");
                await Assert.That(runner.Calls[1].Arguments).Contains("build");
                await Assert.That(runner.Calls[1].Arguments).Contains("--no-incremental");
                await Assert.That(runner.Calls[2].Arguments).Contains("pack");
                await Assert.That(runner.Calls[2].Arguments).Contains("--no-build");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task Pack_FailingBuild_ReturnsErrorAndNoPackage()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cp-svc-{Guid.NewGuid():N}");
        try
        {
            var projectFile = Path.Combine(root, "Lib.csproj");
            Directory.CreateDirectory(root);
            File.WriteAllText(projectFile, "<Project />");
            var runner = new ImposterRunner { ExitCodeFor = _ => 1 };
            var service = CreateService(runner);

            var outcome = await service.PackAsync(projectFile, "Release", Path.Combine(root, "nupkgs"), null, false, _ => { }, CancellationToken.None);

            using (Assert.Multiple())
            {
                await Assert.That(outcome.Success).IsFalse();
                await Assert.That(outcome.Package).IsNull();
                await Assert.That(outcome.Message).Contains("exit code 1");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task TestPackage_InvalidArtifact_RunsNoProcesses()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cp-svc-{Guid.NewGuid():N}");
        try
        {
            var folder = Path.Combine(root, "nupkgs");
            WriteNupkg(folder, "Lib", "not-semver");
            var runner = new ImposterRunner();
            var service = CreateService(runner);

            var result = await service.TestPackageAsync(
                new NuGetPackageIdentity("Lib", "not-semver"), folder, _ => { }, CancellationToken.None);

            using (Assert.Multiple())
            {
                await Assert.That(result.Success).IsFalse();
                await Assert.That(result.Message).Contains("validation", StringComparison.OrdinalIgnoreCase);
                await Assert.That(runner.Calls).IsEmpty();
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task TestPackage_HappyPath_RunsLocalFeedStepsInOrder_AndCleansUp()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cp-svc-{Guid.NewGuid():N}");
        try
        {
            var folder = Path.Combine(root, "nupkgs");
            WriteNupkg(folder, "Lib", "1.0.0");
            var runner = new ImposterRunner
            {
                ExitCodeFor = args => args.Contains("locals")
                    ? 0
                    : 0
            };
            var service = CreateService(runner);

            var result = await service.TestPackageAsync(
                new NuGetPackageIdentity("Lib", "1.0.0"), folder, _ => { }, CancellationToken.None);

            var verbs = runner.Calls.Select(call => string.Join(" ", call.Arguments.Skip(1).Take(2))).ToArray();
            using (Assert.Multiple())
            {
                await Assert.That(result.Success).IsTrue();
                await Assert.That(verbs).Contains("nuget locals");
                await Assert.That(verbs).Contains("nuget add");
                await Assert.That(verbs).Contains("new classlib");
                await Assert.That(verbs.Any(v => v.StartsWith("add ", StringComparison.Ordinal))).IsTrue();
                await Assert.That(verbs.Any(v => v.StartsWith("build ", StringComparison.Ordinal))).IsTrue();
                // The local test source is removed again afterwards.
                await Assert.That(verbs).Contains("nuget remove");
                // The add-package step points at the pack output folder as the source.
                var addPackage = runner.Calls.First(call => call.Arguments.Contains("package"));
                await Assert.That(addPackage.Arguments).Contains(folder);
                await Assert.That(addPackage.Arguments).Contains("1.0.0");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task Push_DuplicateVersionOnFeed_IsRefusedWithoutRunningPush()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cp-svc-{Guid.NewGuid():N}");
        try
        {
            var folder = Path.Combine(root, "nupkgs");
            WriteNupkg(folder, "Lib", "1.0.0");
            var packagePath = Path.Combine(folder, "Lib.1.0.0.nupkg");
            var handler = new StubHandler(request =>
            {
                var json = request.RequestUri!.AbsoluteUri.EndsWith("index.json", StringComparison.Ordinal)
                    && request.RequestUri.Host == "api.nuget.org"
                    && request.RequestUri.AbsolutePath.Contains("flatcontainer")
                        ? """{ "versions": [ "1.0.0" ] }"""
                        : """{ "version": "3.0.0", "resources": [ { "@id": "https://api.nuget.org/v3-flatcontainer/", "@type": "PackageBaseAddress" } ] }""";
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(json) };
            });
            var runner = new ImposterRunner();
            var service = CreateService(runner, new HttpClient(handler));
            var target = NuGetFeedCatalog.Targets.First(t => t.Kind == NuGetFeedKind.NuGetOrg);

            var result = await service.PushAsync(packagePath, target, string.Empty, "key", _ => { }, CancellationToken.None);

            using (Assert.Multiple())
            {
                await Assert.That(result.Success).IsFalse();
                await Assert.That(result.Message).Contains("already exists");
                await Assert.That(runner.Calls.Any(call => call.Arguments.Contains("push"))).IsFalse();
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task Push_NewVersion_RunsPushWithSourceAndKey()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cp-svc-{Guid.NewGuid():N}");
        try
        {
            var folder = Path.Combine(root, "nupkgs");
            WriteNupkg(folder, "Lib", "2.0.0");
            var packagePath = Path.Combine(folder, "Lib.2.0.0.nupkg");
            var runner = new ImposterRunner();
            var service = CreateService(runner);
            var target = NuGetFeedCatalog.Targets.First(t => t.Kind == NuGetFeedKind.GitHubPackages);

            var result = await service.PushAsync(packagePath, target, "omoi", "ghp-key", _ => { }, CancellationToken.None);

            var push = runner.Calls.Single(call => call.Arguments.Contains("push"));
            using (Assert.Multiple())
            {
                await Assert.That(result.Success).IsTrue();
                await Assert.That(push.Arguments).Contains("https://nuget.pkg.github.com/omoi/index.json");
                await Assert.That(push.Arguments).Contains("ghp-key");
                await Assert.That(push.Arguments).Contains("--skip-duplicate");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task Push_LocalFolder_CopiesTheNupkg()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cp-svc-{Guid.NewGuid():N}");
        try
        {
            var packFolder = Path.Combine(root, "nupkgs");
            var feedFolder = Path.Combine(root, "feed");
            WriteNupkg(packFolder, "Lib", "1.0.0");
            var packagePath = Path.Combine(packFolder, "Lib.1.0.0.nupkg");
            var runner = new ImposterRunner();
            var service = CreateService(runner);
            var target = NuGetFeedCatalog.Targets.First(t => t.Kind == NuGetFeedKind.LocalFolder);

            var result = await service.PushAsync(packagePath, target, feedFolder, string.Empty, _ => { }, CancellationToken.None);

            using (Assert.Multiple())
            {
                await Assert.That(result.Success).IsTrue();
                await Assert.That(File.Exists(Path.Combine(feedFolder, "Lib.1.0.0.nupkg"))).IsTrue();
                await Assert.That(runner.Calls).IsEmpty();
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task Delete_RunsNonInteractiveDeleteCommand()
    {
        var runner = new ImposterRunner();
        var service = CreateService(runner);
        var target = NuGetFeedCatalog.Targets.First(t => t.Kind == NuGetFeedKind.NuGetOrg);

        var result = await service.DeleteVersionAsync(target, string.Empty, "Lib", "1.0.0", "key", _ => { }, CancellationToken.None);

        var delete = runner.Calls.Single();
        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsTrue();
            await Assert.That(delete.Arguments).Contains("delete");
            await Assert.That(delete.Arguments).Contains("Lib");
            await Assert.That(delete.Arguments).Contains("1.0.0");
            await Assert.That(delete.Arguments).Contains("--non-interactive");
        }
    }

    [Test]
    public async Task Delete_LocalFolder_RemovesTheNupkgFile()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cp-svc-{Guid.NewGuid():N}");
        try
        {
            var feedFolder = Path.Combine(root, "feed");
            WriteNupkg(feedFolder, "Lib", "1.0.0");
            var runner = new ImposterRunner();
            var service = CreateService(runner);
            var target = NuGetFeedCatalog.Targets.First(t => t.Kind == NuGetFeedKind.LocalFolder);

            var result = await service.DeleteVersionAsync(target, feedFolder, "Lib", "1.0.0", string.Empty, _ => { }, CancellationToken.None);

            using (Assert.Multiple())
            {
                await Assert.That(result.Success).IsTrue();
                await Assert.That(Directory.GetFiles(feedFolder, "*.nupkg")).IsEmpty();
                await Assert.That(runner.Calls).IsEmpty();
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task FindNewestNupkg_FiltersByPackageId()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cp-svc-{Guid.NewGuid():N}");
        try
        {
            WriteNupkg(root, "Lib", "1.0.0");
            WriteNupkg(root, "Other", "9.9.9");
            var older = Path.Combine(root, "Lib.0.9.0.nupkg");
            WriteNupkg(root, "Lib", "0.9.0");
            File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddHours(-1));

            var newest = NuGetPackageService.FindNewestNupkg(root, "Lib");

            await Assert.That(Path.GetFileName(newest)).IsEqualTo("Lib.1.0.0.nupkg");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
