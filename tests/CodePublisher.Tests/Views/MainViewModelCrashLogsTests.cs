using CodePublisher.Services;
using CodePublisher.ViewModels;

namespace CodePublisher.Tests.Views;

public sealed class MainViewModelCrashLogsTests
{
    private static MainViewModel CreateViewModel() =>
        new(new PublisherService(), new DesktopInteractionService());

    [Test]
    public async Task ScanCrashLogs_IsEnabledForADeviceTarget()
    {
        var viewModel = CreateViewModel();

        // A dead button with no reason is worse than a button that says what is missing, so the
        // scan stays available even before the package id is known.
        await Assert.That(viewModel.ScanCrashLogsCommand.CanExecute(null)).IsTrue();
    }

    [Test]
    public async Task ScanCrashLogs_IsEnabledForADesktopTarget()
    {
        var viewModel = CreateViewModel();

        viewModel.PublishPlatform = PublisherService.MacOsPlatform;

        await Assert.That(viewModel.ScanCrashLogsCommand.CanExecute(null)).IsTrue();
    }

    [Test]
    public async Task ScanCrashLogs_TellsTheButtonToCheckAgainWhileItRuns()
    {
        var viewModel = CreateViewModel();
        var toldToCheckAgain = 0;
        viewModel.ScanCrashLogsCommand.CanExecuteChanged += (_, _) => toldToCheckAgain++;

        viewModel.IsCrashLogsBusy = true;

        using (Assert.Multiple())
        {
            await Assert.That(toldToCheckAgain).IsGreaterThan(0);
            await Assert.That(viewModel.ScanCrashLogsCommand.CanExecute(null)).IsFalse();
        }
    }
}
