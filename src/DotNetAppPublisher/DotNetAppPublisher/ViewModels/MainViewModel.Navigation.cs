using CommunityToolkit.Mvvm.ComponentModel;

namespace DotNetAppPublisher.ViewModels;

public enum PublishSection
{
    Project,
    Platform,
    Build,
    Signing,
    Output,
    Deploy,
    Log
}

public partial class MainViewModel
{
    [ObservableProperty]
    private PublishSection _selectedSection = PublishSection.Project;

    [ObservableProperty]
    private NavItem? _selectedNavItem;

    public IReadOnlyList<NavItem> NavItems { get; } =
    [
        new(PublishSection.Project, "Project", "M10 4H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2h-8l-2-2z"),
        new(PublishSection.Platform, "Platform", "M17 1.01L7 1c-1.1 0-2 .9-2 2v18c0 1.1.9 2 2 2h10c1.1 0 2-.9 2-2V3c0-1.1-.9-1.99-2-1.99zM17 19H7V5h10v14z"),
        new(PublishSection.Build, "Build", "M9.4 16.6L4.8 12l4.6-4.6L8 6l-6 6 6 6 1.4-1.4zm5.2 0l4.6-4.6-4.6-4.6L16 6l6 6-6 6-1.4-1.4z"),
        new(PublishSection.Signing, "Signing", "M18 8h-1V6c0-2.76-2.24-5-5-5S7 3.24 7 6v2H6c-1.1 0-2 .9-2 2v10c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2V10c0-1.1-.9-2-2-2zm-6 9c-1.1 0-2-.9-2-2s.9-2 2-2 2 .9 2 2-.9 2-2 2zm3.1-9H8.9V6c0-1.71 1.39-3.1 3.1-3.1 1.71 0 3.1 1.39 3.1 3.1v2z"),
        new(PublishSection.Output, "Output", "M14 2H6c-1.1 0-1.99.9-1.99 2L4 20c0 1.1.89 2 1.99 2H18c1.1 0 2-.9 2-2V8l-6-6zm2 16H8v-2h8v2zm0-4H8v-2h8v2zm-3-5V3.5L18.5 9H13z"),
        new(PublishSection.Deploy, "Deploy", "M8 5v14l11-7z"),
        new(PublishSection.Log, "Log", "M3 13h2v-2H3v2zm0 4h2v-2H3v2zm0-8h2V7H3v2zm4 4h14v-2H7v2zm0 4h14v-2H7v2zM7 7v2h14V7H7z"),
    ];

    public bool IsProjectSection => SelectedSection == PublishSection.Project;
    public bool IsPlatformSection => SelectedSection == PublishSection.Platform;
    public bool IsBuildSection => SelectedSection == PublishSection.Build;
    public bool IsSigningSection => SelectedSection == PublishSection.Signing;
    public bool IsOutputSection => SelectedSection == PublishSection.Output;
    public bool IsDeploySection => SelectedSection == PublishSection.Deploy;
    public bool IsLogSection => SelectedSection == PublishSection.Log;

    partial void OnSelectedSectionChanged(PublishSection value)
    {
        OnPropertyChanged(nameof(IsProjectSection));
        OnPropertyChanged(nameof(IsPlatformSection));
        OnPropertyChanged(nameof(IsBuildSection));
        OnPropertyChanged(nameof(IsSigningSection));
        OnPropertyChanged(nameof(IsOutputSection));
        OnPropertyChanged(nameof(IsDeploySection));
        OnPropertyChanged(nameof(IsLogSection));

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

public sealed record NavItem(PublishSection Section, string Label, string IconData);
