using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DotNetAppPublisher.Features.Deployment.Ios;

namespace DotNetAppPublisher.ViewModels;

/// <summary>
/// Real iOS device support (iPhone/iPad over USB or Wi-Fi). Listing, installing, and launching go
/// through CoreDevice (`xcrun devicectl`); pairing, Developer Mode, and signing stay prerequisites the
/// user owns, and the UI states exactly which one is missing.
/// </summary>
public partial class MainViewModel
{
    public ObservableCollection<IosDeviceInfo> AvailableIosDevices { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallIosDeviceAppCommand))]
    [NotifyCanExecuteChangedFor(nameof(LaunchIosDeviceAppCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopIosDeviceAppCommand))]
    private IosDeviceInfo? _selectedIosDevice;

    public string IosDeviceSupportNote
    {
        get
        {
            if (!OperatingSystem.IsMacOS())
            {
                return "Deploying to a real iPhone or iPad needs macOS with Xcode 15 or newer (CoreDevice).";
            }

            if (_publisherService.XcrunPath is null)
            {
                return "`xcrun` was not found. Install Xcode command line tools to list iOS devices.";
            }

            return "Real devices must be paired once over USB (Trust this computer) with Developer Mode on, " +
                "and the app must be signed with a development profile that includes the device. " +
                "Wi-Fi devices appear here too once Xcode can reach them over the network.";
        }
    }

    public bool CanDiscoverIosDevices => !OperatingSystem.IsBrowser() && OperatingSystem.IsMacOS() && _publisherService.XcrunPath is not null;

    [RelayCommand(CanExecute = nameof(CanDiscoverIosDevices))]
    private async Task RefreshIosDevicesAsync()
    {
        try
        {
            IsBusy = true;
            var devices = await _publisherService.DiscoverIosDevicesAsync(CancellationToken.None);
            var previousSelection = SelectedIosDevice;
            AvailableIosDevices.Clear();
            foreach (var device in devices)
            {
                AvailableIosDevices.Add(device);
            }

            SelectedIosDevice = previousSelection is null
                ? AvailableIosDevices.FirstOrDefault(device => device.IsDeployable)
                : AvailableIosDevices.FirstOrDefault(device => string.Equals(device.Udid, previousSelection.Udid, StringComparison.OrdinalIgnoreCase))
                  ?? AvailableIosDevices.FirstOrDefault(device => device.IsDeployable);

            StatusMessage = AvailableIosDevices.Count == 0
                ? "No real iOS devices found. Connect the device over USB, trust this computer, and enable Developer Mode."
                : $"Discovered {AvailableIosDevices.Count} iOS device(s).";
        }
        catch (Exception ex)
        {
            AvailableIosDevices.Clear();
            SelectedIosDevice = null;
            StatusMessage = "iOS device discovery failed.";
            await _desktopInteractionService.ShowErrorAsync("Discover iOS devices", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Called when the sections that show iOS targets are opened.</summary>
    public Task AutoDiscoverIosDevicesAsync() =>
        CanDiscoverIosDevices && AvailableIosDevices.Count == 0
            ? RefreshIosDevicesAsync()
            : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanInstallIosDeviceApp))]
    private async Task InstallIosDeviceAppAsync()
    {
        if (SelectedIosDevice is null)
        {
            StatusMessage = "Select an iOS device first.";
            return;
        }

        if (!TryValidateOutputDirectory("Install on iOS device"))
        {
            return;
        }

        var udid = SelectedIosDevice.Udid;
        await RunQuickActionAsync(
            async () => await _publisherService.InstallIosDeviceAppAsync(OutputDirectory, udid, CancellationToken.None),
            "Install on iOS device");
    }

    private bool CanInstallIosDeviceApp() =>
        !OperatingSystem.IsBrowser() && IsIosPlatform && !IsBusy
        && !string.IsNullOrWhiteSpace(OutputDirectory)
        && SelectedIosDevice?.IsDeployable == true;

    [RelayCommand(CanExecute = nameof(CanLaunchIosDeviceApp))]
    private async Task LaunchIosDeviceAppAsync()
    {
        if (SelectedIosDevice is null)
        {
            StatusMessage = "Select an iOS device first.";
            return;
        }

        if (!TryValidatePackageId("Launch on iOS device"))
        {
            return;
        }

        var udid = SelectedIosDevice.Udid;
        await RunQuickActionAsync(
            async () => await _publisherService.LaunchIosDeviceAppAsync(PackageId, udid, CancellationToken.None),
            "Launch on iOS device");
    }

    private bool CanLaunchIosDeviceApp() =>
        !OperatingSystem.IsBrowser() && IsIosPlatform && !IsBusy
        && !string.IsNullOrWhiteSpace(PackageId)
        && SelectedIosDevice?.IsDeployable == true;

    /// <summary>
    /// Force-quits the app on the selected real iOS device. CoreDevice has no "terminate by bundle
    /// id" command, so the service resolves the running processes of the installed app and signals
    /// them; the installed app and its data are kept.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStopIosDeviceApp))]
    private async Task StopIosDeviceAppAsync()
    {
        if (SelectedIosDevice is null)
        {
            StatusMessage = "Select an iOS device first.";
            return;
        }

        if (!TryValidatePackageId("Stop on iOS device"))
        {
            return;
        }

        var udid = SelectedIosDevice.Udid;
        await RunQuickActionAsync(
            async () => await _publisherService.StopIosDeviceAppAsync(PackageId, udid, CancellationToken.None),
            "Stop on iOS device");
    }

    private bool CanStopIosDeviceApp() =>
        !OperatingSystem.IsBrowser() && IsIosPlatform && !IsBusy
        && !string.IsNullOrWhiteSpace(PackageId)
        && SelectedIosDevice?.IsDeployable == true;
}
