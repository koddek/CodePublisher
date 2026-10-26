using CommunityToolkit.Mvvm.Input;

namespace DotNetAppPublisher.ViewModels;

/// <summary>
/// One device that is not connected right now: a host this computer has paired with, or a Wi-Fi target
/// adb lists that cannot accept work yet. Each row offers only the next action for that device (Connect)
/// and a way to forget the pairing, so pair and connect are never offered side by side for the same device.
/// </summary>
public sealed partial class WirelessPairedDeviceRowViewModel : ViewModelBase
{
    private readonly Func<Task> _connect;
    private readonly Func<Task> _unpair;

    public WirelessPairedDeviceRowViewModel(string host, Func<Task> connect, Func<Task>? unpair = null)
    {
        Host = host;
        _connect = connect;
        _unpair = unpair ?? (() => Task.CompletedTask);
    }

    public string Host { get; }

    /// <summary>Shown instead of the host when the row stands for an address adb lists right now.</summary>
    public string? TitleOverride { get; init; }

    public string Title => string.IsNullOrWhiteSpace(TitleOverride) ? Host : TitleOverride!;

    public string Subtitle { get; init; } = "Paired, not connected";

    [RelayCommand]
    private Task ConnectAsync() => _connect();

    [RelayCommand]
    private Task UnpairAsync() => _unpair();
}