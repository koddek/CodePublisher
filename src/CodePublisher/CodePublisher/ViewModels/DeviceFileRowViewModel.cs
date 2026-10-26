using CommunityToolkit.Mvvm.Input;
using CodePublisher.Features.Deployment.Android;

namespace CodePublisher.ViewModels;

/// <summary>
/// One row in the device file browser. Keeps the per-row actions next to the entry they act on so the
/// view never has to resolve the parent view model from inside an item template.
/// </summary>
public sealed partial class DeviceFileRowViewModel : ViewModelBase
{
    private readonly Func<Task> _open;
    private readonly Func<Task> _delete;
    private readonly Func<Task> _save;

    public DeviceFileRowViewModel(AndroidDeviceFileEntry entry, Func<Task> open, Func<Task> delete, Func<Task> save)
    {
        Entry = entry;
        _open = open;
        _delete = delete;
        _save = save;
    }

    public AndroidDeviceFileEntry Entry { get; }

    public string Name => Entry.Name;

    public string IconData => Entry.IconData;

    public string DetailText => Entry.DetailText;

    public bool IsDirectory => Entry.IsDirectory;

    public bool IsFile => Entry.IsFile;

    [RelayCommand]
    private Task OpenAsync() => _open();

    [RelayCommand]
    private Task DeleteAsync() => _delete();

    [RelayCommand]
    private Task SaveAsync() => _save();
}
