using CodePublisher.Features.Publishing.Build;
using CodePublisher.Services;
using CodePublisher.ViewModels;

namespace CodePublisher.Tests.Features.Publishing;

public sealed class PublishDefaultStateTests
{
    [Test]
    public async Task When_StartingAndroid_Should_UseSuccessfulReleaseDefaults()
    {
        var viewModel = CreateViewModel();

        using (Assert.Multiple())
        {
            await Assert.That(viewModel.Configuration).IsEqualTo("Release");
            await Assert.That(viewModel.AndroidLinkMode).IsEqualTo("SdkOnly");
            await Assert.That(viewModel.PublishTrimmed).IsTrue();
            await Assert.That(viewModel.PublishReadyToRun).IsTrue();
            await Assert.That(viewModel.PublishSingleFile).IsFalse();
            await Assert.That(viewModel.IsAndroidMonoAotVisible).IsFalse();
            await Assert.That(viewModel.DeleteBin).IsFalse();
            await Assert.That(viewModel.DeleteObj).IsFalse();
        }
    }

    [Test]
    public async Task When_ResettingDebugOptions_Should_RestoreWholeProfile()
    {
        var viewModel = CreateViewModel();
        viewModel.Configuration = "Debug";
        viewModel.DeleteBin = true;
        viewModel.DeleteObj = true;
        viewModel.PublishSingleFile = true;

        viewModel.ResetToSafeDefaultsCommand.Execute(null);

        using (Assert.Multiple())
        {
            await Assert.That(viewModel.Configuration).IsEqualTo("Release");
            await Assert.That(viewModel.DeleteBin).IsFalse();
            await Assert.That(viewModel.DeleteObj).IsFalse();
            await Assert.That(viewModel.PublishSingleFile).IsFalse();
            await Assert.That(viewModel.AndroidLinkMode).IsEqualTo("SdkOnly");
            await Assert.That(viewModel.PublishReadyToRun).IsTrue();
        }
    }

    [Test]
    public async Task When_ChangingTargetFramework_Should_UpdateAndroidCapabilities()
    {
        var viewModel = CreateViewModel();

        viewModel.TargetFramework = "net10.0-android";

        using (Assert.Multiple())
        {
            await Assert.That(viewModel.IsAndroidMonoAotVisible).IsTrue();
            await Assert.That(viewModel.PublishReadyToRun).IsFalse();
        }

        viewModel.RunAotCompilation = true;
        viewModel.EnableProfiledAot = true;
        viewModel.TargetFramework = "net11.0-android";

        using (Assert.Multiple())
        {
            await Assert.That(viewModel.IsAndroidMonoAotVisible).IsFalse();
            await Assert.That(viewModel.RunAotCompilation).IsFalse();
            await Assert.That(viewModel.EnableProfiledAot).IsFalse();
            await Assert.That(viewModel.PublishReadyToRun).IsTrue();
        }
    }

    [Test]
    public async Task When_TurningOffWindowsExecutable_Should_ProduceValidFrameworkDependentCommand()
    {
        var viewModel = CreateViewModel();
        viewModel.PublishPlatform = PublisherService.WindowsPlatform;

        viewModel.CreateWindowsExecutable = false;
        var configuration = viewModel.CreatePublishConfiguration();
        var command = PublishCommandBuilder.Build("/dotnet", configuration, "/tmp/App.csproj", "/tmp/output", null);

        using (Assert.Multiple())
        {
            await Assert.That(configuration.UseAppHost).IsFalse();
            await Assert.That(configuration.CreateWindowsExecutable).IsFalse();
            await Assert.That(configuration.SelfContained).IsFalse();
            await Assert.That(configuration.PublishSingleFile).IsFalse();
            await Assert.That(command).Contains("-p:UseAppHost=false");
            await Assert.That(command).Contains("-p:SelfContained=false");
            await Assert.That(command).Contains("-p:PublishSingleFile=false");
        }
    }

    [Test]
    public async Task When_ChangingOutputInputs_Should_RecomputeOnlyAutomaticPath()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cp-default-output-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var viewModel = CreateViewModel();
            viewModel.PublishPlatform = PublisherService.LinuxPlatform;
            viewModel.ProjectDirectory = root;

            viewModel.Configuration = "Debug";
            viewModel.RuntimeIdentifier = "linux-arm64";
            var automatic = viewModel.OutputDirectory;
            viewModel.OutputDirectory = Path.Combine(root, "custom");
            viewModel.Configuration = "Release";

            using (Assert.Multiple())
            {
                await Assert.That(automatic).Contains(Path.Combine("Debug", "net11.0", "linux-arm64"));
                await Assert.That(viewModel.OutputDirectory).IsEqualTo(Path.Combine(root, "custom"));
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task When_ChangingConfigurationWithCentralArtifacts_Should_ReevaluateOutputPath()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cp-central-output-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var project = Path.Combine(root, "App.csproj");
            await File.WriteAllTextAsync(
                project,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net11.0</TargetFramework><UseArtifactsOutput>true</UseArtifactsOutput></PropertyGroup></Project>");
            var viewModel = CreateViewModel();
            viewModel.PublishPlatform = PublisherService.LinuxPlatform;
            viewModel.ProjectDirectory = root;
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (!viewModel.ProjectOutputLayoutSummary.Contains("Centralized", StringComparison.OrdinalIgnoreCase)
                   && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }

            await Assert.That(viewModel.ProjectOutputLayoutSummary).Contains("Centralized");
            viewModel.Configuration = "Debug";
            deadline = DateTime.UtcNow.AddSeconds(30);
            while (!viewModel.OutputDirectory.Contains("debug", StringComparison.OrdinalIgnoreCase)
                   && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }

            using (Assert.Multiple())
            {
                await Assert.That(viewModel.OutputDirectory).Contains("debug", StringComparison.OrdinalIgnoreCase);
                await Assert.That(viewModel.PublishCommand.CanExecute(null)).IsTrue();
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static MainViewModel CreateViewModel() =>
        new(new PublisherService(), new DesktopInteractionService());
}
