using CommunityToolkit.Mvvm.Input;
using DotNetAppPublisher.Features.Deployment.CrashLogs;

namespace DotNetAppPublisher.ViewModels;

/// <summary>
/// One crash report in the list. The actions sit on the row, so the view never has to reach into
/// the parent view model from inside an item template.
/// </summary>
public sealed partial class CrashLogRowViewModel : ViewModelBase
{
    private readonly Func<CrashLogRowViewModel, Task> _show;
    private readonly Func<CrashLogRowViewModel, Task> _copy;

    public CrashLogRowViewModel(
        CrashLogArtifact artifact,
        Func<CrashLogRowViewModel, Task> show,
        Func<CrashLogRowViewModel, Task> copy)
    {
        Artifact = artifact;
        _show = show;
        _copy = copy;
    }

    public CrashLogArtifact Artifact { get; }

    public string Name => Artifact.Name;

    public string DetailText => Artifact.DetailText;

    public string Location => Artifact.Location;

    /// <summary>The one line that says what the crash was about.</summary>
    public string Summary => CrashLogSummary.Summarize(Artifact.Content);

    public bool HasSummary => !string.IsNullOrWhiteSpace(Summary);

    /// <summary>False for a binary dump, which cannot be read as text.</summary>
    public bool CanShowText => Artifact.CanShowText;

    [RelayCommand]
    private Task ShowAsync() => _show(this);

    [RelayCommand]
    private Task CopyAsync() => _copy(this);
}
