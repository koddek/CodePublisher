using CommunityToolkit.Mvvm.Input;

namespace DotNetAppPublisher.ViewModels;

/// <summary>
/// One device that has been paired with this computer but is not currently connected. Each row offers
/// only the next action for that device (Connect) and a way to forget the pairing, so pair and connect
/// are never offered side by side for the same device.
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

    public string Title => Host;

    public string Subtitle { get; init; } = "Paired, not connected";

    [RelayCommand]
    private Task ConnectAsync() => _connect();

    [RelayCommand]
    private Task UnpairAsync() => _unpair();
}