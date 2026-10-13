using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DotNetAppPublisher.Features.Deployment.Android;

namespace DotNetAppPublisher.ViewModels;

/// <summary>
/// Device Files workspace: browse the public folders of a selected Android device, delete entries,
/// pull them to this computer, and push files (button or drag and drop) into the open folder.
/// </summary>
public partial class MainViewModel
{
    public ObservableCollection<DeviceFileRowViewModel> DeviceFiles { get; } = [];

    public ObservableCollection<WirelessPeerRowViewModel> WirelessPeers { get; } = [];

    public IReadOnlyList<AndroidStorageLocation> DeviceFileLocations => AndroidStorageLocations.QuickLocations;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshDeviceFilesCommand))]
    [NotifyCanExecuteChangedFor(nameof(GoUpDeviceFilesCommand))]
    [NotifyCanExecuteChangedFor(nameof(GoToDeviceLocationCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenDeviceFilesPathCommand))]
    [NotifyCanExecuteChangedFor(nameof(SendFilesToDeviceCommand))]
    private bool _isDeviceFilesBusy;

    [ObservableProperty]
    private string _deviceFilesPath = AndroidStorageLocations.Downloads;

    [ObservableProperty]
    private bool _hasDeviceFiles;

    [ObservableProperty]
    private bool _hasWirelessPeers;

    [ObservableProperty]
    private string _deviceFilesSummary = "Select a device, then open a folder.";

    [ObservableProperty]
    private string _wirelessAddress = string.Empty;

    [ObservableProperty]
    private string _wirelessPairingCode = string.Empty;

    [ObservableProperty]
    private string _wirelessSummary = "Wireless debugging (Android 11+) pairs over Wi-Fi. USB debugging on Android 10 or older needs the USB step first.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PairWirelessDeviceCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConnectWirelessDeviceCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectWirelessDeviceCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshWirelessPeersCommand))]
    private bool _isWirelessBusy;

    public bool IsDeviceFilesAvailable => !OperatingSystem.IsBrowser() && IsAndroidPlatform;

    public string DeviceFilesHint => SelectedDevice switch
    {
        null => "Select an Android device above. USB and Wi-Fi devices both appear there.",
        { IsReady: false } device => $"This target is not usable yet: {device.StateDetail}",
        _ => "Drag files from your computer onto the list to send them to the open folder."
    };

    private bool CanUseDeviceFiles() =>
        !OperatingSystem.IsBrowser() && IsAndroidPlatform && SelectedDevice?.IsReady == true && !IsDeviceFilesBusy;

    [RelayCommand(CanExecute = nameof(CanUseDeviceFiles))]
    private Task RefreshDeviceFilesAsync() => LoadDeviceFilesAsync(DeviceFilesPath);

    [RelayCommand(CanExecute = nameof(CanUseDeviceFiles))]
    private Task GoUpDeviceFilesAsync() => LoadDeviceFilesAsync(AndroidStorageLocations.GetParentRemotePath(DeviceFilesPath));

    [RelayCommand(CanExecute = nameof(CanUseDeviceFiles))]
    private Task GoToDeviceLocationAsync(AndroidStorageLocation? location) =>
        LoadDeviceFilesAsync(location?.RemotePath ?? AndroidStorageLocations.Downloads);

    [RelayCommand(CanExecute = nameof(CanUseDeviceFiles))]
    private Task OpenDeviceFilesPathAsync() => LoadDeviceFilesAsync(DeviceFilesPath);

    [RelayCommand(CanExecute = nameof(CanUseDeviceFiles))]
    private async Task SendFilesToDeviceAsync()
    {
        if (!CanUseDeviceFiles())
        {
            StatusMessage = DeviceFilesHint;
            return;
        }

        var paths = await _desktopInteractionService.PickFilesAsync("Choose files to send to the device");
        if (paths.Count == 0)
        {
            StatusMessage = "Send canceled.";
            return;
        }

        await UploadToDeviceAsync(paths);
    }

    /// <summary>Entry point used by the drop target in the view.</summary>
    public async Task AcceptDroppedFilesAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return;
        }

        if (!CanUseDeviceFiles())
        {
            StatusMessage = DeviceFilesHint;
            return;
        }

        await UploadToDeviceAsync(paths);
    }

    public Task LoadDeviceFilesFromSectionAsync() => CanUseDeviceFiles()
        ? LoadDeviceFilesAsync(DeviceFilesPath)
        : Task.CompletedTask;

    private async Task LoadDeviceFilesAsync(string? path)
    {
        if (SelectedDevice is null || !SelectedDevice.IsReady)
        {
            DeviceFilesSummary = DeviceFilesHint;
            return;
        }

        var target = AndroidStorageLocations.NormalizeRemotePath(path);
        try
        {
            IsDeviceFilesBusy = true;
            var entries = await _publisherService.ListAndroidFilesAsync(SelectedDevice.Serial, target, CancellationToken.None);
            DeviceFilesPath = target;
            DeviceFiles.Clear();
            foreach (var entry in entries)
            {
                DeviceFiles.Add(new DeviceFileRowViewModel(
                    entry,
                    () => OpenDeviceEntryAsync(entry),
                    () => DeleteDeviceEntryAsync(entry),
                    () => SaveDeviceEntryAsync(entry)));
            }

            HasDeviceFiles = DeviceFiles.Count > 0;
            DeviceFilesSummary = entries.Count == 0
                ? $"{target} is empty."
                : $"{entries.Count(entry => entry.IsDirectory)} folder(s) · {entries.Count(entry => entry.IsFile)} file(s) · " +
                  $"{AndroidDeviceFileEntry.FormatSize(entries.Sum(entry => entry.SizeBytes))} in {target}.";
            StatusMessage = $"Loaded {entries.Count} item(s) from {target} on {SelectedDevice.DisplayName}.";
        }
        catch (Exception ex)
        {
            DeviceFiles.Clear();
            HasDeviceFiles = false;
            DeviceFilesSummary = "Could not list this folder.";
            StatusMessage = "Device file listing failed.";
            await _desktopInteractionService.ShowErrorAsync("Device files", ex.Message);
        }
        finally
        {
            IsDeviceFilesBusy = false;
        }
    }

    private Task OpenDeviceEntryAsync(AndroidDeviceFileEntry entry) =>
        entry.IsDirectory
            ? LoadDeviceFilesAsync(entry.FullPath)
            : Task.CompletedTask;

    private async Task DeleteDeviceEntryAsync(AndroidDeviceFileEntry entry)
    {
        if (SelectedDevice is null || !SelectedDevice.IsReady)
        {
            return;
        }

        var confirmed = await _desktopInteractionService.ConfirmAsync(
            "Delete on device",
            $"Delete {(entry.IsDirectory ? "the folder" : "the file")} `{entry.Name}` from the device?\n\n" +
            (entry.IsDirectory ? "Everything inside the folder is deleted too. " : string.Empty) +
            "This cannot be undone.");
        if (!confirmed)
        {
            StatusMessage = "Delete canceled.";
            return;
        }

        var serial = SelectedDevice.Serial;
        var path = entry.FullPath;
        try
        {
            IsDeviceFilesBusy = true;
            var message = await _publisherService.DeleteAndroidFileAsync(serial, path, entry.IsDirectory, CancellationToken.None);
            AppendLog(Environment.NewLine + $"[Delete on device] {message}" + Environment.NewLine);
            StatusMessage = $"Deleted {entry.Name} on the device.";
            PlayCompletionAlert(failed: false);
        }
        catch (Exception ex)
        {
            StatusMessage = "Delete failed.";
            PlayCompletionAlert(failed: true);
            await _desktopInteractionService.ShowErrorAsync("Delete on device", ex.Message);
        }
        finally
        {
            IsDeviceFilesBusy = false;
        }

        await LoadDeviceFilesAsync(DeviceFilesPath);
    }

    private async Task SaveDeviceEntryAsync(AndroidDeviceFileEntry entry)
    {
        if (SelectedDevice is null || !SelectedDevice.IsReady)
        {
            return;
        }

        var folder = await _desktopInteractionService.PickFolderAsync($"Save {entry.Name} to this computer");
        if (string.IsNullOrWhiteSpace(folder))
        {
            StatusMessage = "Save canceled.";
            return;
        }

        try
        {
            IsDeviceFilesBusy = true;
            var message = await _publisherService.DownloadFromAndroidDeviceAsync(
                SelectedDevice.Serial,
                entry.FullPath,
                folder,
                CancellationToken.None);
            AppendLog(Environment.NewLine + $"[Save from device] {message}" + Environment.NewLine);
            StatusMessage = $"Saved {entry.Name} to {folder}.";
            PlayCompletionAlert(failed: false);
        }
        catch (Exception ex)
        {
            StatusMessage = "Save from device failed.";
            PlayCompletionAlert(failed: true);
            await _desktopInteractionService.ShowErrorAsync("Save from device", ex.Message);
        }
        finally
        {
            IsDeviceFilesBusy = false;
        }
    }

    private async Task UploadToDeviceAsync(IReadOnlyList<string> localPaths)
    {
        if (SelectedDevice is null)
        {
            return;
        }

        var serial = SelectedDevice.Serial;
        var directory = DeviceFilesPath;
        try
        {
            IsDeviceFilesBusy = true;
            var message = await _publisherService.UploadToAndroidDeviceAsync(serial, localPaths, directory, CancellationToken.None);
            AppendLog(Environment.NewLine + $"[Send to device] {message}" + Environment.NewLine);
            StatusMessage = $"Sent {localPaths.Count} item(s) to {directory}.";
            PlayCompletionAlert(failed: false);
        }
        catch (Exception ex)
        {
            StatusMessage = "Send to device failed.";
            PlayCompletionAlert(failed: true);
            await _desktopInteractionService.ShowErrorAsync("Send to device", ex.Message);
            return;
        }
        finally
        {
            IsDeviceFilesBusy = false;
        }

        await LoadDeviceFilesAsync(directory);
    }

    // --- Wireless debugging ---

    private bool CanUseWirelessDebugging() =>
        !OperatingSystem.IsBrowser() && IsAndroidPlatform && _publisherService.AdbPath is not null && !IsWirelessBusy;

    [RelayCommand(CanExecute = nameof(CanUseWirelessDebugging))]
    private async Task RefreshWirelessPeersAsync()
    {
        try
        {
            IsWirelessBusy = true;
            var peers = await _publisherService.DiscoverAndroidWirelessServicesAsync(CancellationToken.None);
            WirelessPeers.Clear();
            foreach (var peer in peers)
            {
                WirelessPeers.Add(new WirelessPeerRowViewModel(peer, () => UseWirelessPeerAsync(peer)));
            }

            HasWirelessPeers = peers.Count > 0;
            WirelessSummary = peers.Count == 0
                ? "No wireless debugging services found. Turn on Developer options > Wireless debugging on the device, " +
                  "keep both machines on the same network, and allow mDNS (some guest networks block it)."
                : $"Found {peers.Count} wireless debugging service(s) on this network.";
            StatusMessage = WirelessSummary;
        }
        catch (Exception ex)
        {
            WirelessPeers.Clear();
            HasWirelessPeers = false;
            WirelessSummary = "Wireless scan failed.";
            StatusMessage = "Wireless scan failed.";
            await _desktopInteractionService.ShowErrorAsync("Wireless debugging", ex.Message);
        }
        finally
        {
            IsWirelessBusy = false;
        }
    }

    private async Task UseWirelessPeerAsync(AndroidWirelessService peer)
    {
        WirelessAddress = peer.Address;
        if (peer.IsPairingService)
        {
            WirelessSummary = $"{peer.Address} is the pairing port. Type the code shown on the device, then press Pair, " +
                "then connect using the debugging port.";
            StatusMessage = "Pairing address filled in. Enter the code shown on the device.";
            return;
        }

        await ConnectWirelessToAsync(peer.Address);
    }

    [RelayCommand(CanExecute = nameof(CanUseWirelessDebugging))]
    private async Task PairWirelessDeviceAsync()
    {
        try
        {
            IsWirelessBusy = true;
            var message = await _publisherService.PairAndroidWirelessAsync(WirelessAddress, WirelessPairingCode, CancellationToken.None);
            AppendLog(Environment.NewLine + $"[Pair over Wi-Fi] {message}" + Environment.NewLine);
            WirelessSummary = "Paired. Scan again, then connect using the debugging port shown on the device.";
            StatusMessage = "Paired over Wi-Fi. Now connect to the debugging port.";
            PlayCompletionAlert(failed: false);
        }
        catch (Exception ex)
        {
            StatusMessage = "Pairing failed.";
            PlayCompletionAlert(failed: true);
            await _desktopInteractionService.ShowErrorAsync("Pair over Wi-Fi", ex.Message);
        }
        finally
        {
            IsWirelessBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseWirelessDebugging))]
    private Task ConnectWirelessDeviceAsync() => ConnectWirelessToAsync(WirelessAddress);

    private async Task ConnectWirelessToAsync(string address)
    {
        try
        {
            IsWirelessBusy = true;
            var message = await _publisherService.ConnectAndroidWirelessAsync(address, CancellationToken.None);
            AppendLog(Environment.NewLine + $"[Connect over Wi-Fi] {message}" + Environment.NewLine);
            WirelessSummary = $"Connected to {address}. It now appears in the device list.";
            StatusMessage = $"Connected to {address}.";
            PlayCompletionAlert(failed: false);
        }
        catch (Exception ex)
        {
            StatusMessage = "Wireless connect failed.";
            PlayCompletionAlert(failed: true);
            await _desktopInteractionService.ShowErrorAsync("Connect over Wi-Fi", ex.Message);
            return;
        }
        finally
        {
            IsWirelessBusy = false;
        }

        await RefreshDevicesAsync();
    }

    [RelayCommand(CanExecute = nameof(CanUseWirelessDebugging))]
    private async Task DisconnectWirelessDeviceAsync()
    {
        try
        {
            IsWirelessBusy = true;
            var message = await _publisherService.DisconnectAndroidWirelessAsync(WirelessAddress, CancellationToken.None);
            AppendLog(Environment.NewLine + $"[Disconnect over Wi-Fi] {message}" + Environment.NewLine);
            WirelessSummary = $"Disconnected from {WirelessAddress}.";
            StatusMessage = WirelessSummary;
            PlayCompletionAlert(failed: false);
        }
        catch (Exception ex)
        {
            StatusMessage = "Wireless disconnect failed.";
            PlayCompletionAlert(failed: true);
            await _desktopInteractionService.ShowErrorAsync("Disconnect over Wi-Fi", ex.Message);
        }
        finally
        {
            IsWirelessBusy = false;
        }

        await RefreshDevicesAsync();
    }

    [RelayCommand(CanExecute = nameof(CanUseWirelessDebugging))]
    private async Task EnableWirelessDebuggingAsync()
    {
        if (SelectedDevice is null || SelectedDevice.IsEmulator || string.IsNullOrWhiteSpace(SelectedDevice.Serial))
        {
            StatusMessage = "Connect the device over USB, select it, and then enable Wi-Fi debugging.";
            return;
        }

        try
        {
            IsWirelessBusy = true;
            var result = await _publisherService.EnableAndroidWirelessDebuggingAsync(
                SelectedDevice.Serial,
                AndroidWirelessDebug.LegacyPort,
                CancellationToken.None);
            AppendLog(Environment.NewLine + $"[Enable Wi-Fi debugging] {result.Message}" + Environment.NewLine);
            if (!string.IsNullOrWhiteSpace(result.Address))
            {
                WirelessAddress = result.Address;
            }

            WirelessSummary = result.Message;
            StatusMessage = "Wi-Fi debugging enabled. Unplug USB, then press Connect.";
            PlayCompletionAlert(failed: false);
        }
        catch (Exception ex)
        {
            StatusMessage = "Could not enable Wi-Fi debugging.";
            PlayCompletionAlert(failed: true);
            await _desktopInteractionService.ShowErrorAsync("Enable Wi-Fi debugging", ex.Message);
        }
        finally
        {
            IsWirelessBusy = false;
        }
    }
}
