using CommunityToolkit.Mvvm.Input;

namespace DotNetAppPublisher.ViewModels;

/// <summary>
/// One device that was connected over Wi-Fi and has since been disconnected: still remembered, but
/// clearly labeled as recent rather than connected. It can be reconnected to the same address or
/// forgotten.
/// </summary>
public sealed partial class WirelessRecentDeviceRowViewModel : ViewModelBase
{
    private readonly Func<Task> _reconnect;
    private readonly Action _forget;

    public WirelessRecentDeviceRowViewModel(string address, Func<Task> reconnect, Action forget)
    {
        Address = address;
        _reconnect = reconnect;
        _forget = forget;
    }

    public string Address { get; }

    [RelayCommand]
    private Task ReconnectAsync() => _reconnect();

    [RelayCommand]
    private void Forget() => _forget();
}