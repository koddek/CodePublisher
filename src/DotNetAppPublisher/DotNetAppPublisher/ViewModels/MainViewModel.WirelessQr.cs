using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DotNetAppPublisher.Features.Deployment.Android;

namespace DotNetAppPublisher.ViewModels;

/// <summary>
/// Wireless debugging "Pair device with QR code". This computer shows a code carrying a service name and
/// a password it invents; the phone reads it, starts its pairing service under that name, and this
/// computer pairs against it with the same password, so nothing is typed on either side.
///
/// The phone side is Android's own QR pairing handler, so the work here is: start from a clean slate,
/// show the code, watch for the phone's pairing service, pair, then connect to the debugging port the
/// phone publishes afterwards — and only report success once <c>adb devices</c> lists a usable target.
/// </summary>
public partial class MainViewModel
{
    private static readonly TimeSpan QrPairingPollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan QrPairingTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan QrConnectTimeout = TimeSpan.FromSeconds(75);

    /// <summary>
    /// How many failed reachability probes the phone's pairing port may fail before the attempt is
    /// called dead. The advertisement is read again on every poll, so this is a time window in polls.
    /// </summary>
    private const int UnreachablePairingPolls = 10;

    /// <summary>The host of the last QR attempt, so a retry can clear what that attempt left behind.</summary>
    private string? _lastQrPairingHost;

    private CancellationTokenSource? _wirelessQrCancellation;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelWirelessQrPairingCommand))]
    private bool _isWirelessQrVisible;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartWirelessQrPairingCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelWirelessQrPairingCommand))]
    private bool _isWirelessQrWaiting;

    [ObservableProperty]
    private Bitmap? _wirelessQrImage;

    [ObservableProperty]
    private string _wirelessQrServiceName = string.Empty;

    [ObservableProperty]
    private string _wirelessQrPassword = string.Empty;

    [ObservableProperty]
    private string _wirelessQrPayload = string.Empty;

    [ObservableProperty]
    private string _wirelessQrStatus = string.Empty;

    /// <summary>
    /// How far the pairing has come: 1 the code is on screen, 2 the phone has scanned it, 3 this
    /// computer has paired, 4 the debugging connection is live. A live count is what keeps the panel
    /// from looking frozen while the phone takes its time to scan.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWirelessQrStepWaiting))]
    [NotifyPropertyChangedFor(nameof(IsWirelessQrStepScanned))]
    [NotifyPropertyChangedFor(nameof(IsWirelessQrStepPaired))]
    [NotifyPropertyChangedFor(nameof(IsWirelessQrStepConnected))]
    private int _wirelessQrStep;

    /// <summary>Steps in one pairing, which the panel shows as a progress bar.</summary>
    public int WirelessQrStepCount => 4;

    public bool IsWirelessQrStepWaiting => WirelessQrStep == 1;
    public bool IsWirelessQrStepScanned => WirelessQrStep == 2;
    public bool IsWirelessQrStepPaired => WirelessQrStep == 3;
    public bool IsWirelessQrStepConnected => WirelessQrStep >= 4;

    public bool CanStartWirelessQrPairing => CanUseWirelessDebugging() && !IsWirelessQrWaiting;

    public bool CanCancelWirelessQrPairing => IsWirelessQrVisible;

    [RelayCommand(CanExecute = nameof(CanStartWirelessQrPairing))]
    private async Task StartWirelessQrPairingAsync()
    {
        if (OperatingSystem.IsBrowser() || _publisherService.AdbPath is null)
        {
            StatusMessage = "QR pairing needs the desktop app with adb installed.";
            return;
        }

        // A fresh invitation replaces any previous one: the phone only accepts the code it just scanned.
        StopWirelessQrPairing();
        try
        {
            // 1. Immediately create and show the QR code on screen without blocking delays:
            var request = AndroidQrPairing.Create();
            WirelessQrServiceName = request.ServiceName;
            WirelessQrPassword = request.Password;
            WirelessQrPayload = request.Payload;
            ShowWirelessQrImage(AndroidQrPairingImage.CreatePng(request.Payload));
            IsWirelessQrVisible = true;
            IsWirelessQrWaiting = true;
            WirelessQrStep = 1;
            WirelessQrStatus = $"Waiting for the phone to scan the code ({QrPairingTimeout.TotalMinutes:0} min)…";
            StatusMessage = "Pair with QR code: scan it on the phone under Developer options > Wireless debugging > Pair device with QR code.";

            // 2. Automatically clear any cache, any old connections, and daemon transports:
            _pairedWirelessHosts.Clear();
            _wirelessReconnectLastAttempt.Clear();
            _wirelessReconnectFailures.Clear();
            _recentWirelessAddresses.Clear();
            RecentWirelessDevices.Clear();
            _lastQrPairingHost = null;
            OnPropertyChanged(nameof(IsWirelessAddressPaired));
            OnPropertyChanged(nameof(HasRecentWirelessDevices));
            OnPropertyChanged(nameof(HasPairedWirelessDevices));

            var cleanup = await _publisherService.PrepareAndroidWirelessPairingAsync(null, CancellationToken.None);
            AppendLog($"{Environment.NewLine}[Pair with QR code] {cleanup}{Environment.NewLine}");

            var cancellation = new CancellationTokenSource();
            _wirelessQrCancellation = cancellation;
            await WaitForQrPairingAsync(request, [], cancellation);
        }
        catch (Exception ex)
        {
            IsWirelessQrWaiting = false;
            StatusMessage = "QR pairing failed.";
            PlayCompletionAlert(failed: true);
            await _desktopInteractionService.ShowErrorAsync("Pair with QR code", ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelWirelessQrPairing))]
    private void CancelWirelessQrPairing()
    {
        StopWirelessQrPairing();
        WirelessQrStatus = string.Empty;
        StatusMessage = "QR pairing cancelled. Start again for a fresh code.";
    }

    [RelayCommand]
    private async Task CopyWirelessQrPayloadAsync()
    {
        if (string.IsNullOrWhiteSpace(WirelessQrPayload))
        {
            return;
        }

        await _desktopInteractionService.CopyTextToClipboardAsync(WirelessQrPayload);
        StatusMessage = "Pairing code text copied.";
    }

    private async Task<IReadOnlyList<string>> ReadAdvertisedPairingNamesAsync()
    {
        try
        {
            var services = await _publisherService.DiscoverAndroidWirelessPairingServicesAsync(CancellationToken.None);
            return services
                .Where(service => service.IsPairingService)
                .Select(service => service.Name)
                .ToArray();
        }
        catch (Exception)
        {
            // A snapshot that could not be read only costs the name-independent fallback.
            return [];
        }
    }

    private async Task WaitForQrPairingAsync(
        AndroidQrPairingRequest request,
        IReadOnlyList<string> seenPairingServices,
        CancellationTokenSource cancellation)
    {
        var cancellationToken = cancellation.Token;
        var deadline = DateTimeOffset.UtcNow + QrPairingTimeout;
        try
        {
            while (DateTimeOffset.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
            {
                var services = await DiscoverPairingServicesAsync(cancellationToken);
                if (AndroidQrPairing.TryFindPairingService(
                        services,
                        request.ServiceName,
                        seenPairingServices,
                        out var pairingService)
                    && pairingService is not null)
                {
                    WirelessQrStep = 2;
                    WirelessQrStatus = $"The phone scanned the code ({pairingService.Address}). Pairing…";
                    try
                    {
                        var message = await _publisherService.PairAndroidWirelessSecretAsync(
                            pairingService.Address,
                            request.Password,
                            cancellationToken);
                        AppendLog($"{Environment.NewLine}[Pair with QR code] {message}{Environment.NewLine}");
                    }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                    {
                        // The code is spent once the pairing handshake runs, so waiting longer cannot
                        // help. The message names the real cause (newer adb, or the phone forgot this
                        // computer), and the panel stays up so it can be read in place.
                        WirelessQrStatus = $"{ex.Message}{Environment.NewLine}Press Connect with QR code for a fresh code.";
                        StatusMessage = "QR pairing failed. Start again for a fresh code.";
                        PlayCompletionAlert(failed: true);
                        IsWirelessQrWaiting = false;
                        return;
                    }

                    AndroidWirelessDebug.TryParseEndpoint(pairingService.Address, out var pairingHost, out _);
                    await OnPairedAsync(pairingHost, cancellationToken);
                    return;
                }

                // The phone publishes its `_adb-tls-connect` service while its dialog is open, which
                // only shows that wireless debugging is on and the phone is visible on this network.
                var advertising = services.FirstOrDefault(service => !service.IsPairingService);
                var remaining = (int)Math.Max(0, (deadline - DateTimeOffset.UtcNow).TotalSeconds);
                WirelessQrStatus = advertising is null
                    ? $"Waiting for the phone to scan the QR code ({remaining} s left)…"
                    : $"The phone is visible at {advertising.Address}. Scan the code on the phone ({remaining} s left)…";

                await Task.Delay(QrPairingPollInterval, cancellationToken);
            }

            WirelessQrStatus = "No pairing request arrived for this code. Start again for a fresh one, and leave the " +
                "phone's Pair device with QR code screen open until it pairs.";
            StatusMessage = "QR pairing timed out. The code only works for the attempt that showed it.";
            PlayCompletionAlert(failed: true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            WirelessQrStatus = "Pairing failed — see the message.";
            StatusMessage = "QR pairing failed.";
            PlayCompletionAlert(failed: true);
            await _desktopInteractionService.ShowErrorAsync("Pair with QR code", ex.Message);
        }
        finally
        {
            // Keep the invitation on screen for a retry unless this attempt is still the current one.
            if (ReferenceEquals(_wirelessQrCancellation, cancellation))
            {
                IsWirelessQrWaiting = false;
            }
        }
    }

    private async Task<IReadOnlyList<AndroidWirelessService>> DiscoverPairingServicesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _publisherService.DiscoverAndroidWirelessPairingServicesAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            WirelessQrStatus = $"Looking for the phone failed: {ex.Message} Trying again…";

            return [];
        }
    }

    private async Task OnPairedAsync(string host, CancellationToken cancellationToken)
    {
        _lastQrPairingHost = host;
        if (!string.IsNullOrWhiteSpace(host))
        {
            // Recorded as paired before the connection attempt so the phone shows up under
            // "Paired devices (not connected)" with its own Connect action if connecting stalls.
            _pairedWirelessHosts[host] = null;
            OnPropertyChanged(nameof(IsWirelessAddressPaired));
        }

        WirelessQrStep = 3;
        WirelessQrStatus = "Paired with the phone. Looking for its debugging port…";
        await ConnectPairedTargetAsync(host, cancellationToken);
    }

    /// <summary>
    /// After pairing, the phone publishes its debugging port under `_adb-tls-connect._tcp`. That port
    /// is picked up live on every poll — adb's own auto-connect only works when its mDNS backend can
    /// see the network, and on hosts where it cannot, waiting for `adb devices` alone would leave a
    /// paired phone unconnected forever.
    ///
    /// Success is only declared when adb lists the endpoint as a usable target; `adb connect` alone
    /// exits 0 and prints a banner whether or not anything connected.
    /// </summary>
    private async Task ConnectPairedTargetAsync(string host, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + QrConnectTimeout;
        AndroidWirelessConnectResult? lastResult = null;
        while (DateTimeOffset.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            // 1. adb connects on its own after a pairing handshake when its own discovery works.
            var connected = await FindConnectedTargetAsync(host, cancellationToken);
            if (connected is not null)
            {
                await CompleteQrPairingAsync(connected, host, cancellationToken);
                return;
            }

            // 2. Otherwise the debugging port the phone publishes now is connected to explicitly, and
            // every endpoint listed for the host is tried because a cached record can carry the port
            // from before wireless debugging restarted.
            WirelessQrStep = 3;
            WirelessQrStatus = "Paired. Looking for the phone's debugging port…";
            AndroidWirelessConnectResult result;
            try
            {
                result = await _publisherService.ConnectAndroidWirelessHostAsync(host, null, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                AppendLog($"{Environment.NewLine}[Pair with QR code] Connect failed: {ex.Message}{Environment.NewLine}");
                await Task.Delay(QrPairingPollInterval, cancellationToken);
                continue;
            }

            AppendLog($"{Environment.NewLine}[Connect over Wi-Fi] {result.Message}{Environment.NewLine}");
            if (result.Connected)
            {
                await CompleteQrPairingAsync(result.Address, host, cancellationToken);
                return;
            }

            lastResult = result;
            WirelessQrStatus = DescribeTryingConnect(result);
            await Task.Delay(QrPairingPollInterval, cancellationToken);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        // Pairing itself succeeded, so the trust step is done; only the live connection is missing.
        // The host stays under "Paired devices (not connected)" so Connect is one click away.
        WirelessSummary = string.IsNullOrWhiteSpace(host)
            ? "Paired over Wi-Fi, but the phone did not publish a debugging port."
            : $"Paired with {host}, but it is not accepting a connection. {DescribeStalledConnection(lastResult, host)}";
        StatusMessage = "Paired over Wi-Fi, but not connected yet. See the QR panel for the next step.";
        WirelessQrStatus = WirelessSummary;
        PlayCompletionAlert(failed: true);
        IsWirelessQrWaiting = false;
    }

    private async Task<string?> FindConnectedTargetAsync(string host, CancellationToken cancellationToken)
    {
        try
        {
            var entries = await _publisherService.ProbeAndroidDevicesAsync(cancellationToken);
            return entries.FirstOrDefault(entry =>
                entry.IsWirelessSerial
                && string.Equals(entry.State, AndroidDeviceState.Ready, StringComparison.Ordinal)
                && AndroidWirelessDebug.TryParseEndpoint(entry.Serial, out var entryHost, out _)
                && string.Equals(entryHost, host, StringComparison.OrdinalIgnoreCase))?.Serial;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>
    /// The short line shown while the connect loop keeps trying. The full diagnosis is in the log.
    /// </summary>
    private static string DescribeTryingConnect(AndroidWirelessConnectResult result)
    {
        if (result.NeedsApproval)
        {
            return "The phone answered but has not approved this computer. Unlock it and accept the debugging prompt, " +
                "or forget this computer on the phone and pair again.";
        }

        return result.State == "not listed"
            ? "The phone is not publishing a debugging port that accepts a connection yet. Keep Wireless debugging " +
              "open on the phone; retrying…"
            : $"Paired. {result.Address} is listed as `{result.State}`; retrying…";
    }

    private static string DescribeStalledConnection(AndroidWirelessConnectResult? result, string host)
    {
        if (result is null)
        {
            return $"The phone published no debugging port after pairing. Open Developer options > Wireless debugging on " +
                $"the phone, keep the screen on, then press Connect next to {host} under Paired devices.";
        }

        return result.NeedsApproval
            ? "Unlock the phone and accept the debugging prompt, or forget this computer on the phone and pair again."
            : "On the phone, turn Wireless debugging off and on, keep its screen on, then press Connect next to " +
              $"{host} under Paired devices.";
    }

    private async Task CompleteQrPairingAsync(string address, string host, CancellationToken cancellationToken)
    {
        WirelessAddress = address;
        if (!string.IsNullOrWhiteSpace(host))
        {
            _pairedWirelessHosts[host] = address;
        }

        OnPropertyChanged(nameof(IsWirelessAddressPaired));
        await RefreshDevicesSilentlyAsync(CancellationToken.None);

        var matchedDevice = AvailableAndroidDevices.FirstOrDefault(d =>
            string.Equals(d.Serial, address, StringComparison.OrdinalIgnoreCase) ||
            (AndroidWirelessDebug.TryParseEndpoint(d.Serial, out var dHost, out _) && string.Equals(dHost, host, StringComparison.OrdinalIgnoreCase)));
        if (matchedDevice is not null)
        {
            SelectedDevice = matchedDevice;
        }

        WirelessQrStep = 4;
        WirelessQrStatus = $"Connected to {address}! Ready for deployment.";
        WirelessSummary = $"Paired and connected to {address}. It appears under Connected devices.";
        StatusMessage = $"Paired over Wi-Fi and connected to {address}.";
        PlayCompletionAlert(failed: false);

        try
        {
            await Task.Delay(1500, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        StopWirelessQrPairing();
    }

    private void ShowWirelessQrImage(byte[] png)
    {
        var previous = WirelessQrImage;
        using var stream = new MemoryStream(png);
        WirelessQrImage = new Bitmap(stream);
        previous?.Dispose();
    }

    private void StopWirelessQrPairing()
    {
        var cancellation = _wirelessQrCancellation;
        _wirelessQrCancellation = null;
        cancellation?.Cancel();
        cancellation?.Dispose();

        IsWirelessQrWaiting = false;
        IsWirelessQrVisible = false;
        WirelessQrStep = 0;
        WirelessQrStatus = string.Empty;
        WirelessQrServiceName = string.Empty;
        WirelessQrPassword = string.Empty;
        WirelessQrPayload = string.Empty;
        var image = WirelessQrImage;
        WirelessQrImage = null;
        image?.Dispose();
    }
}
