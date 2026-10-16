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
/// The phone side is Android's own QR pairing handler, so the only work here is: show the code, watch for
/// the named `_adb-tls-pairing._tcp` service, pair, then pick up the debugging port that appears.
/// </summary>
public partial class MainViewModel
{
    private static readonly TimeSpan QrPairingPollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan QrPairingTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan QrDebugPortTimeout = TimeSpan.FromSeconds(45);

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
            var request = AndroidQrPairing.Create();
            WirelessQrServiceName = request.ServiceName;
            WirelessQrPassword = request.Password;
            WirelessQrPayload = request.Payload;
            ShowWirelessQrImage(AndroidQrPairingImage.CreatePng(request.Payload));
            IsWirelessQrVisible = true;
            IsWirelessQrWaiting = true;
            WirelessQrStatus = $"Waiting for the phone to scan the code ({QrPairingTimeout.TotalMinutes:0} min)…";
            StatusMessage = "Pair with QR code: scan it on the phone under Developer options > Wireless debugging > Pair device with QR code.";

            var cancellation = new CancellationTokenSource();
            _wirelessQrCancellation = cancellation;
            await WaitForQrPairingAsync(request, cancellation);
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

    private async Task WaitForQrPairingAsync(AndroidQrPairingRequest request, CancellationTokenSource cancellation)
    {
        var cancellationToken = cancellation.Token;
        var deadline = DateTimeOffset.UtcNow + QrPairingTimeout;
        try
        {
            while (DateTimeOffset.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
            {
                var services = await _publisherService.DiscoverAndroidWirelessServicesAsync(cancellationToken);
                if (AndroidQrPairing.TryFindPairingService(services, request.ServiceName, out var pairingService)
                    && pairingService is not null)
                {
                    WirelessQrStatus = $"{pairingService.Address} — pairing…";
                    var message = await _publisherService.PairAndroidWirelessSecretAsync(
                        pairingService.Address,
                        request.Password,
                        cancellationToken);
                    AppendLog($"{Environment.NewLine}[Pair with QR code] {message}{Environment.NewLine}");
                    if (AndroidWirelessDebug.TryParseEndpoint(pairingService.Address, out var qrHost, out _) && !string.IsNullOrWhiteSpace(qrHost))
                    {
                        _pairedWirelessHosts[qrHost] = null;
                        OnPropertyChanged(nameof(IsWirelessAddressPaired));
                    }
                    WirelessQrStatus = "Paired. Looking for the debugging port…";
                    await ConnectPairedTargetAsync(pairingService, cancellationToken);
                    return;
                }

                var remaining = (int)Math.Max(0, (deadline - DateTimeOffset.UtcNow).TotalSeconds);
                WirelessQrStatus = $"Waiting for the phone to scan the code ({remaining} s left)…";
                await Task.Delay(QrPairingPollInterval, cancellationToken);
            }

            WirelessQrStatus = "No pairing request arrived. Start again for a fresh code.";
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

    /// <summary>
    /// After pairing, the phone publishes its debugging port under `_adb-tls-connect._tcp` on the same
    /// host. Connecting to that port is what makes the target show up in the device list and in
    /// `adb devices`, so the feature ends the same way the manual flow does.
    /// </summary>
    private async Task ConnectPairedTargetAsync(AndroidWirelessService pairingService, CancellationToken cancellationToken)
    {
        AndroidWirelessDebug.TryParseEndpoint(pairingService.Address, out var host, out _);
        var deadline = DateTimeOffset.UtcNow + QrDebugPortTimeout;
        while (DateTimeOffset.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            var services = await _publisherService.DiscoverAndroidWirelessServicesAsync(cancellationToken);
            var debuggingPort = services.FirstOrDefault(service =>
                !service.IsPairingService
                && AndroidWirelessDebug.TryParseEndpoint(service.Address, out var candidateHost, out _)
                && string.Equals(candidateHost, host, StringComparison.OrdinalIgnoreCase));
            if (debuggingPort is not null)
            {
                var message = await _publisherService.ConnectAndroidWirelessAsync(debuggingPort.Address, cancellationToken);
                AppendLog($"{Environment.NewLine}[Connect over Wi-Fi] {message}{Environment.NewLine}");
                WirelessAddress = debuggingPort.Address;
                if (AndroidWirelessDebug.TryParseEndpoint(debuggingPort.Address, out var connectedHost, out _) && !string.IsNullOrWhiteSpace(connectedHost))
                {
                    _pairedWirelessHosts[connectedHost] = debuggingPort.Address;
                }

                WirelessSummary = $"Paired and connected to {debuggingPort.Address}. It appears under Connected devices.";

                // The silent refresh puts the device in the list and selects it, so pairing ends with the
                // target ready to use rather than with instructions to go and look for it.
                await RefreshDevicesSilentlyAsync(cancellationToken);
                StopWirelessQrPairing();
                StatusMessage = $"Paired over Wi-Fi and connected to {debuggingPort.Address}.";
                PlayCompletionAlert(failed: false);
                return;
            }

            await Task.Delay(QrPairingPollInterval, cancellationToken);
        }

        WirelessSummary = "Paired over Wi-Fi. Scan for devices and connect using the debugging port shown on the phone.";
        StatusMessage = "Paired over Wi-Fi. The device appears once its debugging port is published.";
        PlayCompletionAlert(failed: false);
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
        WirelessQrStatus = string.Empty;
        WirelessQrServiceName = string.Empty;
        WirelessQrPassword = string.Empty;
        WirelessQrPayload = string.Empty;
        var image = WirelessQrImage;
        WirelessQrImage = null;
        image?.Dispose();
    }
}
