using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CodePublisher.Features.NuGet.Models;

namespace CodePublisher.Features.NuGet.Models;

/// <summary>One version on the feed, with the Delete action on the row.</summary>
public sealed partial class NuGetFeedVersionRowViewModel : ObservableObject
{
    private readonly Func<NuGetFeedVersionRowViewModel, Task> _deleteRequested;

    public NuGetFeedVersionRowViewModel(NuGetFeedVersion version, Func<NuGetFeedVersionRowViewModel, Task> deleteRequested)
    {
        Version = version.Version;
        _deleteRequested = deleteRequested;
    }

    public string Version { get; }

    public string DetailText => "listed on the feed";

    [RelayCommand]
    private Task Delete() => _deleteRequested(this);
}
