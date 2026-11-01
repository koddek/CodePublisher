using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CodePublisher.ViewModels;

public enum PublishSection
{
    Project,
    Build,
    Signing,
    Deploy,
    GooglePlay,
    NuGet,
    Output
}

public partial class MainViewModel
{
    [ObservableProperty]
    private PublishSection _selectedSection = PublishSection.Project;

    [ObservableProperty]
    private NavItem? _selectedNavItem;

    /// <summary>
    /// The tabs the selected publish platform offers. <see cref="WorkspaceNavigation"/> owns the
    /// list, and this collection is rebuilt whenever the platform changes.
    /// </summary>
    public ObservableCollection<NavItem> NavItems { get; } = [];

    public bool IsProjectSection => SelectedSection == PublishSection.Project;
    public bool IsBuildSection => SelectedSection == PublishSection.Build;
    public bool IsSigningSection => SelectedSection == PublishSection.Signing;
    public bool IsDeploySection => SelectedSection == PublishSection.Deploy;
    public bool IsGooglePlaySection => SelectedSection == PublishSection.GooglePlay;
    public bool IsNuGetSection => SelectedSection == PublishSection.NuGet;
    public bool IsOutputSection => SelectedSection == PublishSection.Output;

    /// <summary>Rebuilds the tab list and moves off a tab the new platform does not offer.</summary>
    private void RebuildNavigation()
    {
        var tabs = WorkspaceNavigation.GetTabs(PublishPlatform);
        NavItems.Clear();
        foreach (var tab in tabs)
        {
            NavItems.Add(tab);
        }

        var section = WorkspaceNavigation.GetFallbackSection(PublishPlatform, SelectedSection);
        if (SelectedSection != section)
        {
            SelectedSection = section;
        }

        SelectedNavItem = NavItems.FirstOrDefault(item => item.Section == section) ?? NavItems.FirstOrDefault();
    }

    partial void OnSelectedSectionChanged(PublishSection value)
    {
        OnPropertyChanged(nameof(IsProjectSection));
        OnPropertyChanged(nameof(IsBuildSection));
        OnPropertyChanged(nameof(IsSigningSection));
        OnPropertyChanged(nameof(IsDeploySection));
        OnPropertyChanged(nameof(IsGooglePlaySection));
        OnPropertyChanged(nameof(IsNuGetSection));
        OnPropertyChanged(nameof(IsOutputSection));

        var nav = NavItems.FirstOrDefault(n => n.Section == value);
        if (nav is not null && !Equals(SelectedNavItem, nav))
        {
            SelectedNavItem = nav;
        }

        OnSectionEntered(value);
    }

    partial void OnSelectedNavItemChanged(NavItem? value)
    {
        if (value is not null && SelectedSection != value.Section)
        {
            SelectedSection = value.Section;
        }
    }
}

/// <summary>One workspace tab. <paramref name="Group"/> is its phase, and only the first tab of a phase starts it.</summary>
public sealed record NavItem(
    PublishSection Section,
    string Label,
    string IconData,
    string Group,
    bool StartsGroup);
