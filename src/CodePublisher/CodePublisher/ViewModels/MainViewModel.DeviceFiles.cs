using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CodePublisher.Features.Deployment.Android;

namespace CodePublisher.ViewModels;

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

    /// <summary>The last split of the live adb target list, so every connected claim in the panel reads one state.</summary>
    private AndroidWirelessPanelState _wirelessPanel = AndroidWirelessPanel.Describe(null);

    /// <summary>The connected Wi-Fi device the user clicked, for context-aware options.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedWirelessDevice))]
    [NotifyPropertyChangedFor(nameof(SelectedWirelessDeviceOptions))]
    private AndroidDeviceInfo? _selectedWirelessDevice;

    /// <summary>True when a connected device is selected and context options are available.</summary>
    public bool HasSelectedWirelessDevice => SelectedWirelessDevice is not null && SelectedWirelessDevice.IsWireless;

    /// <summary>Context-aware options for the selected connected device.</summary>
    public IReadOnlyList<WirelessDeviceOption> SelectedWirelessDeviceOptions
    {
        get
        {
            var device = SelectedWirelessDevice;
            if (device is null || !device.IsWireless)
            {
                return [];
            }

            var options = new List<WirelessDeviceOption>
            {
                new("Disconnect", "End the live debugging session. The device stays paired.",
                    () => DisconnectWirelessDeviceAsync(device.Serial), true)
            };

            var host = AndroidWirelessDebug.TryParseEndpoint(device.Serial, out var parsedHost, out _) ? parsedHost : device.Serial;
            var isPaired = _pairedWirelessHosts.ContainsKey(host);
            if (isPaired)
            {
                options.Add(new("Unpair (delete completely)",
                    "Remove the pairing and all saved configuration for this device. Connecting again starts from scratch.",
                    async () => await DeleteWirelessDeviceAsync(device), true));
            }

            options.Add(new("Diagnose connection",
                "Check why this device may not connect reliably and what to fix.",
                () => DiagnoseSelectedWirelessDeviceAsync(), false));

            // The phone is the only place a paired computer is forgotten and the debugging
            // authorizations revoked, and no adb command does either, so this puts the phone's
            // Developer options on its screen instead of leaving the user to hunt for the toggle.
            options.Add(new("Open Developer options on the phone",
                "Opens Developer options on the phone, where you can forget this computer or revoke adb " +
                "debugging authorizations. Use this when pairing keeps failing because the phone no " +
                "longer recognises this computer.",
                () => OpenAndroidDeveloperOptionsAsync(device.Serial), false));

            return options;
        }
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PairWirelessDeviceCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConnectWirelessDeviceCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectWirelessDeviceCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshWirelessPeersCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartWirelessQrPairingCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetWirelessDebuggingCommand))]
    private bool _isWirelessBusy;

    public bool IsDeviceFilesAvailable => !OperatingSystem.IsBrowser() && IsAndroidPlatform;

    /// <summary>True while adb reports at least one Wi-Fi target as usable. A Wi-Fi endpoint adb lists as
    /// `offline` is not a connection: `adb connect` leaves that row behind when the session never became
    /// usable, so it must not read as connected here either.</summary>
    public bool IsWirelessConnected => _wirelessPanel.HasConnected;

    /// <summary>True when the machine has already paired with the host in the address box this session.
    /// Pairing is a one-time trust step; once paired, only Connect is offered for that host.</summary>
    public bool IsWirelessAddressPaired => !string.IsNullOrWhiteSpace(WirelessAddress)
        && AndroidWirelessDebug.TryParseEndpoint(WirelessAddress, out var host, out _)
        && !string.IsNullOrWhiteSpace(host)
        && _pairedWirelessHosts.ContainsKey(host);

    public bool HasConnectedWirelessDevices => ConnectedWirelessDevices.Count > 0;

    public bool HasPairedWirelessDevices => PairedWirelessDevices.Count > 0;

    public bool HasRecentWirelessDevices => RecentWirelessDevices.Count > 0;

    /// <summary>One-line state shown at the top of the wireless card, from the same split as the lists below it.</summary>
    public string WirelessStatusLine => _wirelessPanel.StatusLine;

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
            if (peers.Count > 0)
            {
                WirelessSummary = $"Found {peers.Count} wireless debugging service(s) on this network.";
            }
            else
            {
                // An empty scan is a dead end without saying which half is broken, so the adb
                // server's own checks are read before the message is written.
                var server = await _publisherService.ReadAndroidAdbServerStatusAsync(CancellationToken.None);
                var baseMessage = server.IsKnown && !server.IsHealthy
                    ? $"No wireless debugging services found, and this computer's adb cannot look for them: " +
                      $"{string.Join(' ', server.FailedChecks)} {server.FixCommands}"
                    : "No wireless debugging services found. Turn on Developer options > Wireless debugging on the device, " +
                      "keep both machines on the same network, and allow mDNS (some guest networks block it).";
                WirelessSummary = string.IsNullOrWhiteSpace(AdbInstallationNote)
                    ? baseMessage
                    : $"{baseMessage} {AdbInstallationNote}";
            }

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

    /// <summary>
    /// Connects to a Wi-Fi device. <paramref name="address"/> is either the full IP:PORT the user is
    /// working with or just the host of a device that is already paired; either way every debugging
    /// port advertised for the host is tried, with the address itself as the fallback, because the
    /// connect port changes whenever wireless debugging restarts on the phone.
    /// </summary>
    private async Task ConnectWirelessToAsync(string address, string? fallbackAddress = null)
    {
        if (!AndroidWirelessDebug.TryParseEndpoint(address, out var host, out _) || string.IsNullOrWhiteSpace(host))
        {
            StatusMessage = $"`{address}` is not an address with a port. Use the IP:PORT shown under Wireless debugging.";
            return;
        }

        // A bare host is a valid starting point: the ports the phone publishes now are tried first. Only
        // a full endpoint is worth keeping as the last-resort address, because a host with no port is not
        // something `adb connect` accepts.
        var fallback = fallbackAddress
            ?? (AndroidWirelessDebug.TryParseEndpoint(address, out _, out _) ? address : null);

        AndroidWirelessConnectResult result;
        try
        {
            IsWirelessBusy = true;
            result = await _publisherService.ConnectAndroidWirelessHostAsync(host, fallback, CancellationToken.None);
            AppendLog(Environment.NewLine + $"[Connect over Wi-Fi] {result.Message}" + Environment.NewLine);
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

        WirelessSummary = result.Message;
        StatusMessage = result.Connected
            ? $"Connected to {result.Address}."
            : $"Could not connect to {result.Address}.";
        PlayCompletionAlert(failed: !result.Connected);

        if (result.Connected)
        {
            _pairedWirelessHosts[host] = result.Address;
            OnPropertyChanged(nameof(IsWirelessAddressPaired));
        }
        else if (result.NeedsApproval)
        {
            // adb listed the phone but the phone has not trusted this computer's key, so the pairing
            // record is not proof of connection and the address is left as it was.
            await _desktopInteractionService.ShowErrorAsync("Connect over Wi-Fi", result.Message);
        }

        await RefreshDevicesAsync();
    }

    /// <summary>
    /// Clears this computer's Wi-Fi debugging state: every wireless transport is dropped, the app's
    /// pairing and recent records are cleared, and with nothing else attached the adb server is
    /// restarted so its cached mDNS list is empty again. Pairing keys are left alone; a phone that
    /// still does not recognise this computer needs Forget or Revoke on the phone.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanUseWirelessDebugging))]
    private async Task ResetWirelessDebuggingAsync()
    {
        try
        {
            IsWirelessBusy = true;
            CancelWirelessQrPairingCommand.Execute(null);
            var reset = await _publisherService.ResetAndroidWirelessStateAsync(
                host: null,
                restartServerWhenIdle: true,
                CancellationToken.None);
            AppendLog(Environment.NewLine + $"[Reset Wi-Fi debugging] {reset.Message}" + Environment.NewLine);

            _pairedWirelessHosts.Clear();
            _wirelessReconnectFailures.Clear();
            _wirelessReconnectLastAttempt.Clear();
            _recentWirelessAddresses.Clear();
            RecentWirelessDevices.Clear();
            _lastQrPairingHost = null;
            WirelessAddress = string.Empty;
            SelectedWirelessDevice = null;
            WirelessPeers.Clear();
            HasWirelessPeers = false;
            OnPropertyChanged(nameof(IsWirelessAddressPaired));
            OnPropertyChanged(nameof(HasRecentWirelessDevices));
            OnPropertyChanged(nameof(HasPairedWirelessDevices));

            WirelessSummary = reset.Message;
            StatusMessage = reset.Message;
            PlayCompletionAlert(failed: false);
        }
        catch (Exception ex)
        {
            StatusMessage = "Resetting Wi-Fi debugging failed.";
            PlayCompletionAlert(failed: true);
            await _desktopInteractionService.ShowErrorAsync("Reset Wi-Fi debugging", ex.Message);
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
    /// Rebuilds the wireless panel from the live adb target list and drops recent entries that are
    /// connected again (a connected target is not "recent"). Only targets adb reports as usable count as
    /// connected: a failed or half-open <c>adb connect</c> leaves an `offline` row behind while the phone
    /// is not connected, so that row is shown as a target that is not connected and keeps its Connect
    /// action instead of being presented as a session. Runs after every device-list replacement, so the
    /// panel always reflects what adb actually reports.
    /// </summary>
    private void RebuildWirelessLists()
    {
        var panel = AndroidWirelessPanel.Describe(AvailableAndroidDevices);
        _wirelessPanel = panel;

        ConnectedWirelessDevices.Clear();
        foreach (var device in panel.Connected)
        {
            var deviceHost = AndroidWirelessDebug.TryParseEndpoint(device.Serial, out var parsedHost, out _) ? parsedHost : device.Serial;
            ConnectedWirelessDevices.Add(
                new WirelessConnectedDeviceRowViewModel(
                    device,
                    async () => await DisconnectWirelessDeviceAsync(device.Serial),
                    async () => await UnpairWirelessHostAsync(deviceHost),
                    async () =>
                    {
                        // Clicking the same device again clears the context selection.
                        if (SelectedWirelessDevice is not null &&
                            string.Equals(SelectedWirelessDevice.Serial, device.Serial, StringComparison.Ordinal))
                        {
                            SelectedWirelessDevice = null;
                        }
                        else
                        {
                            SelectedWirelessDevice = device;
                        }
                    }));
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

        // Everything that is not connected is listed separately so each host gets its own Connect /
        // Unpair actions rather than the generic Add-a-device buttons: the hosts this computer paired
        // with, and every Wi-Fi target adb lists that cannot accept work yet.
        var connectedHosts = panel.Connected
            .Select(device => AndroidWirelessPanel.HostOf(device.Serial))
            .Where(host => host.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        PairedWirelessDevices.Clear();
        var notConnectedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // A target adb still lists comes first: it carries adb's own state, which says more than the
        // pairing record does. The address adb lists is handed over as the last resort, so Connect tries
        // every port the phone publishes now and then the one adb already holds a (not yet usable) row for.
        foreach (var target in panel.NotUsable.Where(target => !connectedHosts.Contains(target.Host)))
        {
            if (target.Host.Length == 0 || !notConnectedHosts.Add(target.Host))
            {
                continue;
            }

            var listedAddress = target.Address;
            PairedWirelessDevices.Add(
                new WirelessPairedDeviceRowViewModel(
                    target.Host,
                    async () => await ConnectPairedWirelessHostAsync(target.Host, listedAddress),
                    async () => await UnpairWirelessHostAsync(target.Host))
                {
                    TitleOverride = listedAddress,
                    Subtitle = target.Hint
                });
        }

        foreach (var host in _pairedWirelessHosts.Keys.Where(host => !string.IsNullOrWhiteSpace(host) && !connectedHosts.Contains(host)))
        {
            if (!notConnectedHosts.Add(host))
            {
                continue;
            }

            PairedWirelessDevices.Add(
                new WirelessPairedDeviceRowViewModel(
                    host,
                    async () => await ConnectPairedWirelessHostAsync(host),
                    async () => await UnpairWirelessHostAsync(host)));
        }

        // A host that is usable again is not a failing one, so a connection the user made by hand also
        // resets the background retry for it.
        foreach (var host in ReadyWirelessHosts())
        {
            _wirelessReconnectFailures.Remove(host);
        }

        OnPropertyChanged(nameof(IsWirelessConnected));
        OnPropertyChanged(nameof(HasConnectedWirelessDevices));
        OnPropertyChanged(nameof(HasRecentWirelessDevices));
        OnPropertyChanged(nameof(HasPairedWirelessDevices));
        OnPropertyChanged(nameof(WirelessStatusLine));

        // Clear the context selection if the selected device is no longer connected.
        if (SelectedWirelessDevice is not null &&
            !ConnectedWirelessDevices.Any(row => string.Equals(row.Address, SelectedWirelessDevice.Serial, StringComparison.OrdinalIgnoreCase)))
        {
            SelectedWirelessDevice = null;
            OnPropertyChanged(nameof(SelectedWirelessDevice));
        }
    }

    /// <summary>
    /// Connects a host listed as not connected. <paramref name="listedAddress"/> is the address adb
    /// lists for it right now, when there is one; otherwise the address that worked earlier is the
    /// starting point.
    /// </summary>
    private Task ConnectPairedWirelessHostAsync(string host, string? listedAddress = null)
    {
        var stored = _pairedWirelessHosts.TryGetValue(host, out var saved) ? saved : null;
        return ConnectWirelessToAsync(host, listedAddress ?? stored);
    }

    /// <summary>
    /// The Wi-Fi target adb lists right now for a host, whether or not it is usable. Used by the
    /// actions that have to drop a connection, because an `offline` row is a transport adb still holds
    /// and only a disconnect removes it.
    /// </summary>
    private AndroidDeviceInfo? FindListedWirelessTarget(string host, string? address = null) =>
        AvailableAndroidDevices.FirstOrDefault(device =>
            device.IsWireless
            && (string.Equals(device.Serial, address, StringComparison.OrdinalIgnoreCase)
                || (AndroidWirelessDebug.TryParseEndpoint(device.Serial, out var deviceHost, out _)
                    && string.Equals(deviceHost, host, StringComparison.OrdinalIgnoreCase))));

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
            var listedTarget = FindListedWirelessTarget(host, debugAddress);
            if (listedTarget is not null)
            {
                // A target adb lists is dropped whether or not it is usable, so an `offline` row left
                // behind by a failed connect does not outlive the unpair.
                await _publisherService.DisconnectAndroidWirelessAsync(listedTarget.Serial, CancellationToken.None);
                await RefreshDevicesAsync();
            }

            _pairedWirelessHosts.Remove(host);
            _wirelessReconnectFailures.Remove(host);
            _wirelessReconnectLastAttempt.Remove(host);
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

        var host = AndroidWirelessDebug.TryParseEndpoint(address, out var parsedHost, out _) ? parsedHost : address;
        RecentWirelessDevices.Add(
            new WirelessRecentDeviceRowViewModel(
                address,
                () => ReconnectRecentWirelessAsync(host, address),
                () => ForgetRecentWirelessDevice(address)));
        OnPropertyChanged(nameof(HasRecentWirelessDevices));
    }

    /// <summary>
    /// Reconnects a row under Recent devices. The saved address is a starting point, not the truth:
    /// the debugging port changes when wireless debugging restarts, so the port the phone publishes
    /// now is tried first.
    /// </summary>
    private Task ReconnectRecentWirelessAsync(string host, string savedAddress) =>
        ConnectWirelessToAsync(host, savedAddress);

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
        var targetDevice = SelectedDevice;
        if (targetDevice is null || targetDevice.IsEmulator || targetDevice.IsWireless || string.IsNullOrWhiteSpace(targetDevice.Serial))
        {
            var autoUsb = AvailableAndroidDevices.FirstOrDefault(d => !d.IsEmulator && !d.IsWireless && d.IsReady);
            if (autoUsb is not null)
            {
                SelectedDevice = autoUsb;
                targetDevice = autoUsb;
            }
        }

        if (targetDevice is null || targetDevice.IsEmulator || string.IsNullOrWhiteSpace(targetDevice.Serial))
        {
            StatusMessage = "Connect the device over USB, select it, and then enable Wi-Fi debugging.";

            return;
        }

        try
        {
            IsWirelessBusy = true;
            var result = await _publisherService.EnableAndroidWirelessDebuggingAsync(
                targetDevice.Serial,
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

    /// <summary>
    /// Completely deletes a wireless device: disconnects if connected, removes the pairing,
    /// removes it from recent devices, and clears all saved configuration. Connecting again
    /// starts from a clean slate with no residue.
    /// </summary>
    private async Task DeleteWirelessDeviceAsync(AndroidDeviceInfo device)
    {
        if (IsWirelessBusy || device is null || !device.IsWireless)
        {
            return;
        }

        var host = AndroidWirelessDebug.TryParseEndpoint(device.Serial, out var parsedHost, out _) ? parsedHost : device.Serial;
        var address = device.Serial;

        try
        {
            IsWirelessBusy = true;

            // 1. Disconnect while adb still lists the endpoint, usable or not, so nothing is left behind.
            var listedTarget = FindListedWirelessTarget(host, address);
            if (listedTarget is not null)
            {
                await _publisherService.DisconnectAndroidWirelessAsync(listedTarget.Serial, CancellationToken.None);
                await RefreshDevicesAsync();
            }

            // 2. Remove the pairing (unpair) and stop retrying it in the background.
            _pairedWirelessHosts.Remove(host);
            _wirelessReconnectFailures.Remove(host);
            _wirelessReconnectLastAttempt.Remove(host);

            // 3. Remove from recent devices.
            var recentRow = RecentWirelessDevices.FirstOrDefault(row =>
                string.Equals(row.Address, address, StringComparison.OrdinalIgnoreCase));
            if (recentRow is not null)
            {
                _recentWirelessAddresses.Remove(recentRow.Address);
                RecentWirelessDevices.Remove(recentRow);
            }

            // 4. Clear the context selection if it pointed at this device.
            if (SelectedWirelessDevice is not null &&
                string.Equals(SelectedWirelessDevice.Serial, device.Serial, StringComparison.Ordinal))
            {
                SelectedWirelessDevice = null;
                OnPropertyChanged(nameof(SelectedWirelessDevice));
            }

            WirelessSummary = $"Deleted {host}. All connection configuration was wiped.";
            StatusMessage = WirelessSummary;
            PlayCompletionAlert(failed: false);

            RebuildWirelessLists();
        }
        catch (Exception ex)
        {
            StatusMessage = "Delete device failed.";
            PlayCompletionAlert(failed: true);
            await _desktopInteractionService.ShowErrorAsync("Delete wireless device", ex.Message);
        }
        finally
        {
            IsWirelessBusy = false;
        }
    }

    /// <summary>
    /// Runs diagnostics on the selected connected device and shows the result to the user.
    /// </summary>
    /// <summary>
    /// Opens the phone's Developer options screen, so a failed pairing can be started fresh from the
    /// phone side without hunting through its settings.
    /// </summary>
    private async Task OpenAndroidDeveloperOptionsAsync(string serial)
    {
        if (IsWirelessBusy)
        {
            return;
        }

        try
        {
            IsWirelessBusy = true;
            var message = await _publisherService.OpenAndroidDeveloperOptionsAsync(serial, CancellationToken.None);
            AppendLog(Environment.NewLine + $"[Open Developer options] {message}" + Environment.NewLine);
            WirelessSummary = "Developer options are open on the phone. Forget this computer or revoke adb " +
                "debugging authorizations to start a fresh pairing.";
            StatusMessage = "Developer options opened on the phone.";
            PlayCompletionAlert(failed: false);
        }
        catch (Exception ex)
        {
            StatusMessage = "Could not open Developer options on the phone.";
            PlayCompletionAlert(failed: true);
            await _desktopInteractionService.ShowErrorAsync("Open Developer options", ex.Message);
        }
        finally
        {
            IsWirelessBusy = false;
        }
    }

    private async Task DiagnoseSelectedWirelessDeviceAsync()
    {
        var device = SelectedWirelessDevice;
        if (device is null || !device.IsWireless)
        {
            return;
        }

        var address = device.Serial;
        AndroidAdbServerStatus server = new(null, null, false, false);
        try
        {
            server = await _publisherService.ReadAndroidAdbServerStatusAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            // An adb server that cannot be read is reported as "unknown", which keeps the check out
            // of the result instead of making it the only failure.
        }

        var result = AndroidWirelessDiagnostics.Diagnose(
            address,
            AvailableAndroidDevices,
            _pairedWirelessHosts,
            WirelessPeers.Select(p => p.Service).ToList(),
            device.AndroidVersion,
            int.TryParse(device.ApiLevel, out var level) ? level : null,
            server);

        var failed = result.Checks.Where(c => !c.Passed).ToList();
        var details = string.Join(Environment.NewLine,
            failed.Select(c => $"• {c.Title}: {c.Detail ?? "(no detail)"}"));

        var message = result.Summary;
        if (failed.Count > 0)
        {
            message += Environment.NewLine + Environment.NewLine + details;
        }

        StatusMessage = message;
        _ = _desktopInteractionService.ShowInfoAsync("Wireless connection diagnosis", message);
    }

    private bool CanDeleteSelectedWirelessDevice() => SelectedWirelessDevice is { IsWireless: true } && !IsWirelessBusy;
}

/// <summary>
/// One context-aware action offered for a selected connected wireless device. The action is offered as a
/// command because a Button binds its Command to an ICommand: a Func&lt;Task&gt; is a runtime binding error
/// ("Command is expected"), not a callable button.
/// </summary>
public sealed partial class WirelessDeviceOption : ViewModelBase
{
    private readonly Func<Task> _action;

    public WirelessDeviceOption(string label, string description, Func<Task> action, bool isDestructive)
    {
        Label = label;
        Description = description;
        _action = action;
        IsDestructive = isDestructive;
    }

    public string Label { get; }

    public string Description { get; }

    public bool IsDestructive { get; }

    [RelayCommand]
    private Task InvokeAsync() => _action();
}
