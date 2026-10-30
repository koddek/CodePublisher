using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CodePublisher.Features.NuGet.Models;

namespace CodePublisher.Features.NuGet.Models;

/// <summary>One version on the feed, with the Delete action on the row.</summary>
public sealed partial class NuGetFeedVersionRowViewModel : ObservableObject
{
    private readonly Action<NuGetFeedVersionRowViewModel> _deleteRequested;

    public NuGetFeedVersionRowViewModel(NuGetFeedVersion version, Action<NuGetFeedVersionRowViewModel> deleteRequested)
    {
        Version = version.Version;
        _deleteRequested = deleteRequested;
    }

    public string Version { get; }

    public string DetailText => "listed on the feed";

    [RelayCommand]
    private void Delete() => _deleteRequested(this);
}
