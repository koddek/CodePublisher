using CodePublisher.Features.Publishing.Configure;
using CodePublisher.Features.Publishing.Inspect;
using CodePublisher.Models;
using CodePublisher.Services;
using CodePublisher.ViewModels;

namespace CodePublisher.Tests.Features.NuGet;

public sealed class NuGetPlatformTests
{
    private static MainViewModel CreateViewModel() =>
        new(new PublisherService(), new DesktopInteractionService());

    [Test]
    public async Task PublishPlatformOptions_IncludeNuGet()
    {
        var viewModel = CreateViewModel();

        await Assert.That(viewModel.PublishPlatformOptions).Contains(PublisherService.NuGetPlatform);
    }

    [Test]
    public async Task NuGetPlatform_HasNeutralDefaults()
    {
        var defaults = PlatformProfiles.GetDefaults(PublisherService.NuGetPlatform);

        using (Assert.Multiple())
        {
            await Assert.That(defaults.TargetFramework).IsEqualTo("net10.0");
            await Assert.That(defaults.RuntimeIdentifier).IsEqualTo("any");
            await Assert.That(defaults.Configuration).IsEqualTo("Release");
            await Assert.That(defaults.SelfContained).IsFalse();
            await Assert.That(defaults.IncludeApk).IsFalse();
        }
    }

    [Test]
    public async Task ValidateRuntimeForPlatform_NuGetNeedsNoRid()
    {
        var configuration = new PublishConfiguration
        {
            ProjectDirectory = "/tmp",
            PublishPlatform = PublisherService.NuGetPlatform,
            TargetFramework = "net10.0",
            RuntimeIdentifier = "",
            Configuration = "Release",
            OutputDirectory = "/tmp/out",
            PackageId = "Lib",
            IncludeApk = false,
            IncludeAab = false,
            SelfContained = false,
            PublishTrimmed = false,
            PublishAot = false,
            PublishReadyToRun = false,
            PublishSingleFile = false,
            UseAppHost = false,
            CreateMacAppBundle = false,
            CreateWindowsExecutable = false,
            BuildIpa = false,
            ArchiveOnBuild = false,
            RunAotCompilation = false,
            EnableProfiledAot = false,
            AndroidLinkMode = "None",
            AndroidLinkTool = "r8",
            AndroidDexTool = "d8",
            CreateMappingFile = false,
            EnableMultiDex = false,
            UseAapt2 = true,
            EnableDesugar = true,
            DeleteBin = false,
            DeleteObj = false,
            SignMode = "Auto",
            KeystorePath = "",
            KeyAlias = "",
            KeystorePassword = "",
            KeyPassword = ""
        };

        // Must not throw: a NuGet package is platform-neutral.
        ProjectInspector.ValidateRuntimeForPlatform(configuration);
        await Assert.That(configuration.RuntimeIdentifier).IsEmpty();
    }

    [Test]
    public async Task SelectingNuGetPlatform_SwitchesEveryTabToNuGetContext()
    {
        var viewModel = CreateViewModel();

        viewModel.PublishPlatform = PublisherService.NuGetPlatform;

        using (Assert.Multiple())
        {
            await Assert.That(viewModel.IsNuGetPlatform).IsTrue();
            // App-publish build options, Android signing card, crash logs, and the device-files
            // fallback all hide; the NuGet deploy card shows on a desktop session.
            await Assert.That(viewModel.IsAppBuildOptionsVisible).IsFalse();
            await Assert.That(viewModel.IsPlatformSigningCardVisible).IsFalse();
            await Assert.That(viewModel.IsCrashLogsCardVisible).IsFalse();
            await Assert.That(viewModel.IsDeviceFilesFallbackVisible).IsFalse();
            await Assert.That(viewModel.IsNuGetDeployCardVisible).IsTrue();
        }
    }

    [Test]
    public async Task SelectingNuGetPlatform_OutputPreviewShowsThePackCommand_NotAnAppPublishError()
    {
        var viewModel = CreateViewModel();
        viewModel.PublishPlatform = PublisherService.NuGetPlatform;
        viewModel.NuGet.UpdateContext("/proj", "/proj/Lib.csproj", "My.Lib", "1.2.3");
        viewModel.Configuration = "Debug"; // any preview-sensitive change refreshes the preview

        using (Assert.Multiple())
        {
            await Assert.That(viewModel.CommandPreview).Contains("pack");
            await Assert.That(viewModel.CommandPreview).Contains("-p:PackageVersion=1.2.3");
            await Assert.That(viewModel.CommandPreview).DoesNotContain("Invalid config");
            await Assert.That(viewModel.KnownGoodBaselineCommand).IsEmpty();
        }
    }

    [Test]
    public async Task NuGetPlatform_OpenPublishFolder_TargetsTheNupkgFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cp-open-{Guid.NewGuid():N}");
        try
        {
            var nupkgs = Path.Combine(root, "nupkgs");
            Directory.CreateDirectory(nupkgs);
            var packagePath = Path.Combine(nupkgs, "Lib.1.0.0.nupkg");
            File.WriteAllText(packagePath, "x");

            var viewModel = CreateViewModel();
            viewModel.PublishPlatform = PublisherService.NuGetPlatform;
            // Nothing packed yet: the button is enabled via the NuGet output folder default.
            viewModel.NuGet.UpdateContext(root, Path.Combine(root, "Lib.csproj"), "Lib", "1.0.0");
            await Assert.That(viewModel.OpenPublishFolderCommand.CanExecute(null)).IsTrue();

            // After a pack, the button opens the folder that holds the .nupkg.
            viewModel.NuGet.PackedPackagePath = packagePath;
            var folders = viewModel.ResolvePublishArtifactFolders();

            using (Assert.Multiple())
            {
                await Assert.That(folders.Count()).IsEqualTo(1);
                await Assert.That(folders[0]).IsEqualTo(nupkgs);
                await Assert.That(viewModel.OpenPublishFolderCommand.CanExecute(null)).IsTrue();
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task OtherPlatforms_KeepTheirContext()
    {
        var viewModel = CreateViewModel();

        viewModel.PublishPlatform = PublisherService.AndroidPlatform;

        using (Assert.Multiple())
        {
            await Assert.That(viewModel.IsNuGetPlatform).IsFalse();
            await Assert.That(viewModel.IsAppBuildOptionsVisible).IsTrue();
            await Assert.That(viewModel.IsCrashLogsCardVisible).IsTrue();
            await Assert.That(viewModel.IsNuGetDeployCardVisible).IsFalse();
        }
    }
}
