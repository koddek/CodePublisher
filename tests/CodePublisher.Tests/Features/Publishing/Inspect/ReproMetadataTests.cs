using CodePublisher.Features.Deployment.Android;
using CodePublisher.Services;
using CodePublisher.ViewModels;

namespace CodePublisher.Tests.Features.Publishing.Inspect;

public sealed class ReproMetadataTests
{
    private static string CreateTree()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cp-repro-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "src", "App"));
        File.WriteAllText(
            Path.Combine(root, "Root.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><PackageId>com.root.lib</PackageId></PropertyGroup></Project>");
        File.WriteAllText(
            Path.Combine(root, "src", "App", "App.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0-android</TargetFramework><ApplicationId>com.example.androidapp</ApplicationId><ApplicationVersion>7</ApplicationVersion></PropertyGroup></Project>");
        return root;
    }

    [Test]
    public async Task SelectingAndroidSubproject_UpdatesPackageId()
    {
        var root = CreateTree();
        try
        {
            var viewModel = new MainViewModel(new PublisherService(), new DesktopInteractionService());
            viewModel.ProjectDirectory = root;
            await viewModel.ReloadProjectMetadataCommand.ExecuteAsync(null);

            var androidProject = Path.Combine(root, "src", "App", "App.csproj");
            viewModel.SelectedProjectFile = androidProject;

            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (viewModel.PackageId != "com.example.androidapp" && DateTime.UtcNow < deadline)
            {
                await Task.Delay(100);
            }

            using (Assert.Multiple())
            {
                await Assert.That(viewModel.PackageId).IsEqualTo("com.example.androidapp");
                await Assert.That(viewModel.SelectedProjectFile).IsEqualTo(androidProject);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task SelectingAndroidSubproject_ThenAndroidPlatform_UpdatesPackageId()
    {
        var root = CreateTree();
        try
        {
            var viewModel = new MainViewModel(new PublisherService(), new DesktopInteractionService());
            viewModel.ProjectDirectory = root;
            await viewModel.ReloadProjectMetadataCommand.ExecuteAsync(null);

            var androidProject = Path.Combine(root, "src", "App", "App.csproj");
            viewModel.SelectedProjectFile = androidProject;
            viewModel.PublishPlatform = PublisherService.AndroidPlatform;

            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (viewModel.PackageId != "com.example.androidapp" && DateTime.UtcNow < deadline)
            {
                await Task.Delay(100);
            }

            using (Assert.Multiple())
            {
                await Assert.That(viewModel.PackageId).IsEqualTo("com.example.androidapp");
                await Assert.That(viewModel.TargetFramework).Contains("android");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task SelectingAndroidSubproject_WithAndroidManifestPackage_UpdatesPackageId()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cp-repro-manifest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "src", "ManifestApp", "Platforms", "Android"));
        try
        {
            File.WriteAllText(
                Path.Combine(root, "Root.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><PackageId>com.root.lib</PackageId></PropertyGroup></Project>");
            File.WriteAllText(
                Path.Combine(root, "src", "ManifestApp", "ManifestApp.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0-android</TargetFramework></PropertyGroup></Project>");
            File.WriteAllText(
                Path.Combine(root, "src", "ManifestApp", "Platforms", "Android", "AndroidManifest.xml"),
                "<?xml version=\"1.0\" encoding=\"utf-8\"?><manifest xmlns:android=\"http://schemas.android.com/apk/res/android\" package=\"com.example.manifestapp\"></manifest>");

            var viewModel = new MainViewModel(new PublisherService(), new DesktopInteractionService());
            viewModel.ProjectDirectory = root;
            await viewModel.ReloadProjectMetadataCommand.ExecuteAsync(null);

            var androidProject = Path.Combine(root, "src", "ManifestApp", "ManifestApp.csproj");
            viewModel.SelectedProjectFile = androidProject;

            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (viewModel.PackageId != "com.example.manifestapp" && DateTime.UtcNow < deadline)
            {
                await Task.Delay(100);
            }

            using (Assert.Multiple())
            {
                await Assert.That(viewModel.PackageId).IsEqualTo("com.example.manifestapp");
                await Assert.That(viewModel.PublishPlatform).IsEqualTo(PublisherService.AndroidPlatform);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task SelectingSubproject_WithoutExplicitId_DerivesIdFromProjectName_NotPredetermined()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cp-repro-name-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "src", "CoolAndroidApp"));
        try
        {
            File.WriteAllText(
                Path.Combine(root, "Root.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><PackageId>com.root.lib</PackageId></PropertyGroup></Project>");
            File.WriteAllText(
                Path.Combine(root, "src", "CoolAndroidApp", "CoolAndroidApp.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0-android</TargetFramework></PropertyGroup></Project>");

            var viewModel = new MainViewModel(new PublisherService(), new DesktopInteractionService());
            viewModel.ProjectDirectory = root;
            await viewModel.ReloadProjectMetadataCommand.ExecuteAsync(null);

            var androidProject = Path.Combine(root, "src", "CoolAndroidApp", "CoolAndroidApp.csproj");
            viewModel.SelectedProjectFile = androidProject;

            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (viewModel.PackageId != "com.coolandroidapp" && DateTime.UtcNow < deadline)
            {
                await Task.Delay(100);
            }

            using (Assert.Multiple())
            {
                await Assert.That(viewModel.PackageId).IsEqualTo("com.coolandroidapp");
                await Assert.That(viewModel.PackageId).IsNotEqualTo("com.codepublisher");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task SelectingSubprojectFromFolderPicker_WithoutManualReload_UpdatesPackageId()
    {
        var root = CreateTree();
        try
        {
            var viewModel = new MainViewModel(new PublisherService(), new DesktopInteractionService());
            viewModel.ProjectDirectory = root;

            var androidProject = Path.Combine(root, "src", "App", "App.csproj");
            viewModel.SelectedProjectFile = androidProject;

            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (viewModel.PackageId != "com.example.androidapp" && DateTime.UtcNow < deadline)
            {
                await Task.Delay(100);
            }

            using (Assert.Multiple())
            {
                await Assert.That(viewModel.PackageId).IsEqualTo("com.example.androidapp");
                await Assert.That(viewModel.SelectedProjectFile).IsEqualTo(androidProject);
                await Assert.That(viewModel.OutputDirectory).Contains("App");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task SelectingSecondProject_WhileFirstMetadataLoads_LoadsTheSecondProject()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cp-repro-race-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "Second"));
        try
        {
            File.WriteAllText(
                Path.Combine(root, "First.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0-android</TargetFramework><ApplicationId>com.example.first</ApplicationId></PropertyGroup></Project>");
            var secondProject = Path.Combine(root, "Second", "Second.csproj");
            File.WriteAllText(
                secondProject,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0-android</TargetFramework><ApplicationId>com.example.second</ApplicationId></PropertyGroup></Project>");

            var viewModel = new MainViewModel(new PublisherService(), new DesktopInteractionService());
            viewModel.ProjectDirectory = root;
            viewModel.SelectedProjectFile = secondProject;

            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (viewModel.PackageId != "com.example.second" && DateTime.UtcNow < deadline)
            {
                await Task.Delay(100);
            }

            using (Assert.Multiple())
            {
                await Assert.That(viewModel.SelectedProjectFile).IsEqualTo(secondProject);
                await Assert.That(viewModel.PackageId).IsEqualTo("com.example.second");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task SelectingFolder_WithOneProject_LoadsMetadataWithoutManualReload()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cp-repro-single-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(
                Path.Combine(root, "SingleApp.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0-android</TargetFramework><ApplicationId>com.example.singleapp</ApplicationId></PropertyGroup></Project>");

            var viewModel = new MainViewModel(new PublisherService(), new DesktopInteractionService());
            viewModel.ProjectDirectory = root;

            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (viewModel.PackageId != "com.example.singleapp" && DateTime.UtcNow < deadline)
            {
                await Task.Delay(100);
            }

            using (Assert.Multiple())
            {
                await Assert.That(viewModel.SelectedProjectFile).IsEqualTo(Path.Combine(root, "SingleApp.csproj"));
                await Assert.That(viewModel.PackageId).IsEqualTo("com.example.singleapp");
                await Assert.That(viewModel.OutputDirectory).IsNotEmpty();
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task SelectingFolder_WithOnePlainLibrary_LoadsPackageIdWithoutManualReload()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cp-repro-library-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(
                Path.Combine(root, "SingleLib.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><PackageId>com.example.singlelib</PackageId></PropertyGroup></Project>");

            var viewModel = new MainViewModel(new PublisherService(), new DesktopInteractionService());
            viewModel.ProjectDirectory = root;

            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (viewModel.PackageId != "com.example.singlelib" && DateTime.UtcNow < deadline)
            {
                await Task.Delay(100);
            }

            using (Assert.Multiple())
            {
                await Assert.That(viewModel.SelectedProjectFile).IsEqualTo(Path.Combine(root, "SingleLib.csproj"));
                await Assert.That(viewModel.PackageId).IsEqualTo("com.example.singlelib");
                await Assert.That(viewModel.OutputDirectory).IsNotEmpty();
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task SelectingAnotherProject_ReplacesAManualOutputFolderWithThatProjectsDefault()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cp-repro-output-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "First"));
        Directory.CreateDirectory(Path.Combine(root, "Second"));
        try
        {
            var firstProject = Path.Combine(root, "First", "First.csproj");
            var secondProject = Path.Combine(root, "Second", "Second.csproj");
            File.WriteAllText(
                firstProject,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0-android</TargetFramework><ApplicationId>com.example.first</ApplicationId></PropertyGroup></Project>");
            File.WriteAllText(
                secondProject,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0-android</TargetFramework><ApplicationId>com.example.second</ApplicationId></PropertyGroup></Project>");

            var viewModel = new MainViewModel(new PublisherService(), new DesktopInteractionService());
            viewModel.ProjectDirectory = root;
            viewModel.SelectedProjectFile = firstProject;

            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (viewModel.PackageId != "com.example.first" && DateTime.UtcNow < deadline)
            {
                await Task.Delay(100);
            }

            var manualFolder = Path.Combine(root, "manual-output");
            viewModel.OutputDirectory = manualFolder;
            viewModel.SelectedProjectFile = secondProject;

            deadline = DateTime.UtcNow.AddSeconds(30);
            while (viewModel.PackageId != "com.example.second" && DateTime.UtcNow < deadline)
            {
                await Task.Delay(100);
            }

            using (Assert.Multiple())
            {
                await Assert.That(viewModel.PackageId).IsEqualTo("com.example.second");
                await Assert.That(viewModel.OutputDirectory).IsNotEqualTo(manualFolder);
                await Assert.That(viewModel.OutputDirectory).Contains("Second");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task EnsurePublishOutputDirectory_ResolvesTheFolderTheCommandWillUse()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cp-repro-publish-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(
                Path.Combine(root, "PublishApp.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0-android</TargetFramework><ApplicationId>com.example.publishapp</ApplicationId></PropertyGroup></Project>");

            var viewModel = new MainViewModel(new PublisherService(), new DesktopInteractionService());
            viewModel.ProjectDirectory = root;
            viewModel.OutputDirectory = string.Empty;
            viewModel.EnsurePublishOutputDirectory();

            using (Assert.Multiple())
            {
                await Assert.That(viewModel.OutputDirectory).IsNotEmpty();
                await Assert.That(viewModel.OutputDirectory).Contains("net10.0-android");
                await Assert.That(viewModel.OutputDirectory).Contains("android-arm64");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task ResolvePublishArtifactFolder_ReturnsTheFolderHoldingTheSelectedProjectsPackage()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cp-repro-folder-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(
                Path.Combine(root, "Selected.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0-android</TargetFramework><ApplicationId>com.example.selected</ApplicationId></PropertyGroup></Project>");

            var viewModel = new MainViewModel(new PublisherService(), new DesktopInteractionService());
            viewModel.ProjectDirectory = root;

            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (viewModel.PackageId != "com.example.selected" && DateTime.UtcNow < deadline)
            {
                await Task.Delay(100);
            }

            Directory.CreateDirectory(viewModel.OutputDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(viewModel.OutputDirectory, "com.example.selected.apk"),
                "selected");
            var other = Directory.CreateDirectory(Path.Combine(viewModel.OutputDirectory, "other"));
            await File.WriteAllTextAsync(Path.Combine(other.FullName, "com.example.other-Signed.apk"), "other");

            await Assert.That(viewModel.ResolvePublishArtifactFolder()).IsEqualTo(viewModel.OutputDirectory);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task DeployPackageId_IsReadOnlyWhileAProjectIsSelected()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cp-repro-readonly-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(
                Path.Combine(root, "DeployApp.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0-android</TargetFramework><ApplicationId>com.example.deployapp</ApplicationId></PropertyGroup></Project>");

            var viewModel = new MainViewModel(new PublisherService(), new DesktopInteractionService());
            await Assert.That(viewModel.IsPackageIdReadOnly).IsFalse();

            viewModel.ProjectDirectory = root;

            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (viewModel.PackageId != "com.example.deployapp" && DateTime.UtcNow < deadline)
            {
                await Task.Delay(100);
            }

            await Assert.That(viewModel.IsPackageIdReadOnly).IsTrue();

            viewModel.ProjectDirectory = string.Empty;
            await Assert.That(viewModel.IsPackageIdReadOnly).IsFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task OfflineEmulator_DisplayName_DoesNotHaveEmptySerialComma()
    {
        var offlineEmulator = new AndroidDeviceInfo(
            Serial: string.Empty,
            Name: "Pixel_7",
            Type: "emulator",
            IsRunning: false,
            State: AndroidDeviceState.Offline,
            Connection: AndroidDeviceConnection.Emulator);

        await Assert.That(offlineEmulator.DisplayName).IsEqualTo("Pixel_7 (emulator) — offline — launch to start");
        await Assert.That(offlineEmulator.StateDetail).IsEqualTo("offline — launch to start");
    }
}
