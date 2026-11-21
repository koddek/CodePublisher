using CommunityToolkit.Mvvm.ComponentModel;
using CodePublisher.Features.NuGet;

namespace CodePublisher.ViewModels;

public partial class MainViewModel
{
    private readonly NuGetViewModel _nugetViewModel;

    /// <summary>The NuGet workspace: pack the project into a .nupkg, test it, push it to a feed.</summary>
    public NuGetViewModel NuGet => _nugetViewModel;

    private void UpdateNuGetContext()
    {
        _nugetViewModel.UpdateContext(ProjectDirectory, SelectedProjectFile, PackageId, ProjectDisplayVersion);
    }

    /// <summary>
    /// The header "Open publish folder" is enabled by the NuGet pack state too, so its CanExecute
    /// is re-checked when the packed package or the NuGet output folder changes.
    /// </summary>
    private void AttachNuGetNotifications()
    {
        _nugetViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(NuGetViewModel.PackedPackagePath) or nameof(NuGetViewModel.OutputDirectory))
            {
                OpenPublishFolderCommand.NotifyCanExecuteChanged();
            }

            if (e.PropertyName == nameof(NuGetViewModel.OutputDirectory))
            {
                OnPropertyChanged(nameof(ActiveOutputDirectory));
                OnPropertyChanged(nameof(ContextOutputText));
            }

            if (e.PropertyName == nameof(NuGetViewModel.PackCommandPreview))
            {
                RefreshCommandPreview();
            }

            if (e.PropertyName is nameof(NuGetViewModel.ProjectFile) or nameof(NuGetViewModel.IsBusy))
            {
                PublishCommand.NotifyCanExecuteChanged();
            }
        };
    }
}
