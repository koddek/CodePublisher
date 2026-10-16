using CommunityToolkit.Mvvm.Input;
using DotNetAppPublisher.Features.Deployment.Android;

namespace DotNetAppPublisher.ViewModels;

/// <summary>
/// One Wi-Fi target currently present in the adb target list. Its only action is Disconnect, so the
/// user always knows the row means "this device is connected right now".
/// </summary>
public sealed partial class WirelessConnectedDeviceRowViewModel : ViewModelBase
{
    private readonly Func<Task> _disconnect;
    private readonly Func<Task> _unpair;

    public WirelessConnectedDeviceRowViewModel(AndroidDeviceInfo device, Func<Task> disconnect, Func<Task>? unpair = null)
    {
        Device = device;
        _disconnect = disconnect;
        _unpair = unpair ?? (() => Task.CompletedTask);
    }

    public AndroidDeviceInfo Device { get; }

    public string Title
    {
        get
        {
            var name = string.IsNullOrWhiteSpace(Device.Model) ? Device.Name : Device.Model;
            return string.IsNullOrWhiteSpace(name) ? Device.Serial : name;
        }
    }

    public string Address => Device.Serial;

    public string Subtitle
    {
        get
        {
            var parts = new List<string> { Address };
            if (Device.PlatformLabel.Length > 0)
            {
                parts.Add(Device.PlatformLabel);
            }

            if (!Device.IsReady)
            {
                parts.Add("not ready");
            }

            return string.Join("  ·  ", parts);
        }
    }

    [RelayCommand]
    private Task DisconnectAsync() => _disconnect();

    [RelayCommand]
    private Task UnpairAsync() => _unpair();
}