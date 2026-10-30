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
