using CodePublisher.Features.Publishing.Inspect;
using CodePublisher.Services;
using CodePublisher.ViewModels;

namespace CodePublisher.Tests.Features.Publishing.Inspect;

public sealed class ProjectDiscoveryTests
{
    private static string CreateTree()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cp-disc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "src", "MainLib"));
        Directory.CreateDirectory(Path.Combine(root, "src", "Generator"));
        Directory.CreateDirectory(Path.Combine(root, "tests", "MainLib.Tests"));
        Directory.CreateDirectory(Path.Combine(root, "src", "MainLib", "bin", "Release"));
        Directory.CreateDirectory(Path.Combine(root, "src", "MainLib", "obj"));
        File.WriteAllText(Path.Combine(root, "src", "MainLib", "MainLib.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        File.WriteAllText(Path.Combine(root, "src", "Generator", "Generator.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        File.WriteAllText(Path.Combine(root, "tests", "MainLib.Tests", "MainLib.Tests.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        // Build-output copies must not be discovered.
        File.WriteAllText(Path.Combine(root, "src", "MainLib", "bin", "Release", "Stale.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(root, "src", "MainLib", "obj", "Stale.csproj"), "<Project />");
        return root;
    }

    [Test]
    public async Task DiscoverProjectFiles_FindsNestedProjects_AndSkipsBinObj()
    {
        var root = CreateTree();
        try
        {
            var found = ProjectInspector.DiscoverProjectFiles(new DirectoryInfo(root));

            using (Assert.Multiple())
            {
                await Assert.That(found.Count()).IsEqualTo(3);
                await Assert.That(found.Any(f => f.Name == "MainLib.csproj")).IsTrue();
                await Assert.That(found.Any(f => f.Name == "Generator.csproj")).IsTrue();
                await Assert.That(found.Any(f => f.Name == "MainLib.Tests.csproj")).IsTrue();
                await Assert.That(found.Any(f => f.Name == "Stale.csproj")).IsFalse();
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task SelectingSourceRoot_OffersEveryProject_AndPickingOneDrivesThePreview()
    {
        var root = CreateTree();
        try
        {
            var viewModel = new MainViewModel(new PublisherService(), new DesktopInteractionService());
            viewModel.PublishPlatform = PublisherService.NuGetPlatform;
            viewModel.ProjectDirectory = root;

            using (Assert.Multiple())
            {
                await Assert.That(viewModel.AvailableProjectFiles.Count()).IsEqualTo(3);
                await Assert.That(viewModel.HasMultipleProjectFiles).IsTrue();
                // The first discovered project drives the pack preview by default.
                await Assert.That(viewModel.ResolveProjectFilePath()).Contains("Generator.csproj");
            }

            var mainLib = Path.Combine(root, "src", "MainLib", "MainLib.csproj");
            viewModel.SelectedProjectFile = mainLib;

            using (Assert.Multiple())
            {
                await Assert.That(viewModel.ResolveProjectFilePath()).IsEqualTo(mainLib);
                await Assert.That(viewModel.CommandPreview).Contains("MainLib.csproj");
                await Assert.That(viewModel.CommandPreview).Contains("pack");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task SingleProjectFolder_KeepsThePickerHidden()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cp-disc-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "Only.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            var viewModel = new MainViewModel(new PublisherService(), new DesktopInteractionService());

            viewModel.ProjectDirectory = root;

            using (Assert.Multiple())
            {
                await Assert.That(viewModel.AvailableProjectFiles.Count()).IsEqualTo(1);
                await Assert.That(viewModel.HasMultipleProjectFiles).IsFalse();
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
