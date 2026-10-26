using CommunityToolkit.Mvvm.Input;
using CodePublisher.Features.Deployment.Android;

namespace CodePublisher.ViewModels;

/// <summary>
/// One Wireless debugging service found through mDNS. Pairing ports need the code shown on the device;
/// debugging ports connect straight away.
/// </summary>
public sealed partial class WirelessPeerRowViewModel : ViewModelBase
{
    private readonly Func<Task> _use;

    public WirelessPeerRowViewModel(AndroidWirelessService service, Func<Task> use)
    {
        Service = service;
        _use = use;
    }

    public AndroidWirelessService Service { get; }

    public string Title => Service.Address;

    public string KindText => Service.IsPairingService
        ? "Pairing port — needs the code shown on the device"
        : "Debugging port — ready to connect";

    public bool IsPairingService => Service.IsPairingService;

    public string ActionLabel => Service.IsPairingService ? "Enter code" : "Connect";

    [RelayCommand]
    private Task UseAsync() => _use();
}
