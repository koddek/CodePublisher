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

    /// <summary>Wi-Fi targets adb actually lists right now, with one Disconnect action each.</summary>
    public ObservableCollection<WirelessConnectedDeviceRowViewModel> ConnectedWirelessDevices { get; } = [];

    /// <summary>Devices that have been paired with this computer but are not currently connected.</summary>
    public ObservableCollection<WirelessPairedDeviceRowViewModel> PairedWirelessDevices { get; } = [];

    /// <summary>Devices connected earlier this session and since disconnected, clearly labeled as recent.</summary>
    public ObservableCollection<WirelessRecentDeviceRowViewModel> RecentWirelessDevices { get; } = [];

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
    [NotifyPropertyChangedFor(nameof(IsWirelessAddressPaired))]
    private string _wirelessAddress = string.Empty;

    [ObservableProperty]
    private string _wirelessPairingCode = string.Empty;

    [ObservableProperty]
    private string _wirelessSummary = "Wireless debugging (Android 11+) pairs over Wi-Fi. USB debugging on Android 10 or older needs the USB step first.";

    /// <summary>Whether the collapsed Manual pairing panel is open. Pairing rows from the nearby list
    /// open it automatically, because those ports need the code typed on the phone.</summary>
    [ObservableProperty]
    private bool _isWirelessManualVisible;

    private readonly Dictionary<string, string?> _pairedWirelessHosts = new(StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> _recentWirelessAddresses = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PairWirelessDeviceCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConnectWirelessDeviceCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectWirelessDeviceCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshWirelessPeersCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartWirelessQrPairingCommand))]
    private bool _isWirelessBusy;

    public bool IsDeviceFilesAvailable => !OperatingSystem.IsBrowser() && IsAndroidPlatform;

    /// <summary>True while at least one wireless (host:port) target is present in the adb device list.
    /// The list is what adb reports, so a failed connect never reports connected and a real disconnect
    /// clears it automatically when the refresh removes the target.</summary>
    public bool IsWirelessConnected => ConnectedWirelessDevices.Count > 0;

    /// <summary>True when the machine has already paired with the host in the address box this session.
    /// Pairing is a one-time trust step; once paired, only Connect is offered for that host.</summary>
    public bool IsWirelessAddressPaired => !string.IsNullOrWhiteSpace(WirelessAddress)
        && AndroidWirelessDebug.TryParseEndpoint(WirelessAddress, out var host, out _)
        && !string.IsNullOrWhiteSpace(host)
        && _pairedWirelessHosts.ContainsKey(host);

    public bool HasConnectedWirelessDevices => ConnectedWirelessDevices.Count > 0;

    public bool HasPairedWirelessDevices => PairedWirelessDevices.Count > 0;

    public bool HasRecentWirelessDevices => RecentWirelessDevices.Count > 0;

    /// <summary>One-line state shown at the top of the wireless card.</summary>
    public string WirelessStatusLine => ConnectedWirelessDevices.Count switch
    {
        0 => "No devices connected over Wi-Fi.",
        1 => "1 device connected over Wi-Fi. It can be disconnected from the list below.",
        _ => $"{ConnectedWirelessDevices.Count} devices connected over Wi-Fi. Each can be disconnected from the list below."
    };

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
            IsWirelessManualVisible = true;
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
            if (AndroidWirelessDebug.TryParseEndpoint(WirelessAddress, out var host, out _) && !string.IsNullOrWhiteSpace(host))
            {
                _pairedWirelessHosts[host] = null;
            }

            OnPropertyChanged(nameof(IsWirelessAddressPaired));
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
            if (AndroidWirelessDebug.TryParseEndpoint(address, out var connectHost, out _) && !string.IsNullOrWhiteSpace(connectHost))
            {
                _pairedWirelessHosts[connectHost] = address;
            }

            OnPropertyChanged(nameof(IsWirelessAddressPaired));
            WirelessSummary = $"Connected to {address}. It appears under Connected devices.";
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
    private async Task DisconnectWirelessDeviceAsync(string address)
    {
        if (IsWirelessBusy || string.IsNullOrWhiteSpace(address))
        {
            return;
        }

        try
        {
            IsWirelessBusy = true;
            var message = await _publisherService.DisconnectAndroidWirelessAsync(address, CancellationToken.None);
            AppendLog(Environment.NewLine + $"[Disconnect over Wi-Fi] {message}" + Environment.NewLine);
            if (string.Equals(WirelessAddress, address, StringComparison.OrdinalIgnoreCase))
            {
                WirelessAddress = string.Empty;
            }

            WirelessSummary = $"Disconnected from {address}.";
            StatusMessage = WirelessSummary;
            AddToRecentWireless(address);
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

    /// <summary>
    /// Rebuilds the Connected devices list from the live adb target list and drops recent entries that
    /// are connected again (a connected target is not "recent"). Runs after every device-list
    /// replacement, so the panel always reflects what adb actually reports.
    /// </summary>
    private void RebuildWirelessLists()
    {
        ConnectedWirelessDevices.Clear();
        foreach (var device in AvailableAndroidDevices.Where(item => item.IsWireless))
        {
            var deviceHost = AndroidWirelessDebug.TryParseEndpoint(device.Serial, out var parsedHost, out _) ? parsedHost : device.Serial;
            ConnectedWirelessDevices.Add(
                new WirelessConnectedDeviceRowViewModel(
                    device,
                    async () => await DisconnectWirelessDeviceAsync(device.Serial),
                    async () => await UnpairWirelessHostAsync(deviceHost)));
        }

        if (_recentWirelessAddresses.Count > 0)
        {
            var connected = ConnectedWirelessDevices
                .Select(row => row.Address)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var recent in RecentWirelessDevices.Where(row => connected.Contains(row.Address)).ToList())
            {
                ForgetRecentWirelessDevice(recent.Address);
            }
        }

        // Paired hosts that are not currently connected are shown as a separate list so each gets its
        // own Connect / Unpair actions rather than the generic Add-a-device buttons.
        var connectedHosts = ConnectedWirelessDevices
            .Select(row => AndroidWirelessDebug.TryParseEndpoint(row.Address, out var parsedHost, out _) ? parsedHost : row.Address)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        PairedWirelessDevices.Clear();
        foreach (var host in _pairedWirelessHosts.Keys.Where(host => !connectedHosts.Contains(host)).ToList())
        {
            var debugAddress = _pairedWirelessHosts[host];
            PairedWirelessDevices.Add(
                new WirelessPairedDeviceRowViewModel(
                    host,
                    async () => await ConnectPairedWirelessHostAsync(host),
                    async () => await UnpairWirelessHostAsync(host)));
        }

        OnPropertyChanged(nameof(IsWirelessConnected));
        OnPropertyChanged(nameof(HasConnectedWirelessDevices));
        OnPropertyChanged(nameof(HasRecentWirelessDevices));
        OnPropertyChanged(nameof(HasPairedWirelessDevices));
        OnPropertyChanged(nameof(WirelessStatusLine));
    }

    private async Task ConnectPairedWirelessHostAsync(string host)
    {
        string? address = null;
        if (_pairedWirelessHosts.TryGetValue(host, out var stored) && !string.IsNullOrWhiteSpace(stored))
        {
            address = stored;
        }
        else
        {
            var peer = WirelessPeers.FirstOrDefault(service =>
                !service.IsPairingService
                && AndroidWirelessDebug.TryParseEndpoint(service.Service.Address, out var peerHost, out _)
                && string.Equals(peerHost, host, StringComparison.OrdinalIgnoreCase));
            address = peer?.Service.Address;
        }

        if (string.IsNullOrWhiteSpace(address))
        {
            StatusMessage = "No debugging port found for this device. Run Scan nearby devices, then its Connect button will appear.";
            return;
        }

        await ConnectWirelessToAsync(address!);
    }

    private async Task UnpairWirelessHostAsync(string host)
    {
        if (IsWirelessBusy)
        {
            return;
        }

        IsWirelessBusy = true;
        try
        {
            var debugAddress = _pairedWirelessHosts.TryGetValue(host, out var stored) ? stored : null;
            var connectedRow = ConnectedWirelessDevices.FirstOrDefault(row =>
                (AndroidWirelessDebug.TryParseEndpoint(row.Address, out var rowHost, out _) && string.Equals(rowHost, host, StringComparison.OrdinalIgnoreCase))
                || string.Equals(row.Address, debugAddress, StringComparison.OrdinalIgnoreCase));
            if (connectedRow is not null)
            {
                await _publisherService.DisconnectAndroidWirelessAsync(connectedRow.Address, CancellationToken.None);
                await RefreshDevicesAsync();
            }

            _pairedWirelessHosts.Remove(host);
            var staleRecent = RecentWirelessDevices.Where(row =>
                (AndroidWirelessDebug.TryParseEndpoint(row.Address, out var recentHost, out _) && string.Equals(recentHost, host, StringComparison.OrdinalIgnoreCase))
                || string.Equals(row.Address, debugAddress, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var recent in staleRecent)
            {
                _recentWirelessAddresses.Remove(recent.Address);
                RecentWirelessDevices.Remove(recent);
            }

            WirelessSummary = $"Unpaired {host}.";
            StatusMessage = WirelessSummary;
            PlayCompletionAlert(failed: false);
            RebuildWirelessLists();
        }
        catch (Exception ex)
        {
            StatusMessage = "Unpair failed.";
            PlayCompletionAlert(failed: true);
            await _desktopInteractionService.ShowErrorAsync("Unpair over Wi-Fi", ex.Message);
        }
        finally
        {
            IsWirelessBusy = false;
        }

        RebuildWirelessLists();
    }

    private void AddToRecentWireless(string address)
    {
        if (string.IsNullOrWhiteSpace(address) || !_recentWirelessAddresses.Add(address))
        {
            return;
        }

        RecentWirelessDevices.Add(
            new WirelessRecentDeviceRowViewModel(
                address,
                () => ConnectWirelessToAsync(address),
                () => ForgetRecentWirelessDevice(address)));
        OnPropertyChanged(nameof(HasRecentWirelessDevices));
    }

    private void ForgetRecentWirelessDevice(string address)
    {
        _recentWirelessAddresses.Remove(address);
        var row = RecentWirelessDevices.FirstOrDefault(
            candidate => string.Equals(candidate.Address, address, StringComparison.OrdinalIgnoreCase));
        if (row is not null)
        {
            RecentWirelessDevices.Remove(row);
        }

        OnPropertyChanged(nameof(HasRecentWirelessDevices));
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
                IsWirelessManualVisible = true;
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
