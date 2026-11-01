using CodePublisher.Services;

namespace CodePublisher.ViewModels;

/// <summary>
/// The one policy that decides which workspace tabs exist for a publish platform, which phase each
/// tab belongs to, and where to land when the selected tab disappears. The navigation list, the
/// detail pane, and the fallback after a platform switch all read this policy, so they cannot
/// disagree about what the current platform offers.
/// </summary>
public static class WorkspaceNavigation
{
    private const string Prepare = "Prepare";
    private const string Ship = "Ship";
    private const string Inspect = "Inspect";

    private const string ProjectIcon =
        "M10 4H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2h-8l-2-2z";

    private const string BuildIcon =
        "M9.4 16.6L4.8 12l4.6-4.6L8 6l-6 6 6 6 1.4-1.4zm5.2 0l4.6-4.6-4.6-4.6L16 6l6 6-6 6-1.4-1.4z";

    private const string SigningIcon =
        "M18 8h-1V6c0-2.76-2.24-5-5-5S7 3.24 7 6v2H6c-1.1 0-2 .9-2 2v10c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2V10c0-1.1-.9-2-2-2zm-6 9c-1.1 0-2-.9-2-2s.9-2 2-2 2 .9 2 2-.9 2-2 2zm3.1-9H8.9V6c0-1.71 1.39-3.1 3.1-3.1 1.71 0 3.1 1.39 3.1 3.1v2z";

    private const string DeployIcon = "M8 5v14l11-7z";

    private const string GooglePlayIcon =
        "M3.6 1.8 13.2 12l-9.6 10.2A2.1 2.1 0 0 1 3 20.7V3.3c0-.7.2-1.1.6-1.5Zm11.1 8.7L5.2 1.1c.3-.1.7-.2 1.1-.2 2.9 0 5.9 2.1 8.1 5.1L6.7 10.5Zm0 3.6 7.7 4.5C13.1 20.6 10.1 22.7 7.2 22.7c-.4 0-.8 0-1.1-.2l9.6-9.4Zm3.4-2L15.4 12l2.7-1.6c.6.5 1.1 1 1.5 1.5.8 1.4.4 3.2-1 4.1l-2.5-1.5Z";

    private const string NuGetIcon =
        "M20 4H4c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V6c0-1.1-.9-2-2-2zm-9 3h2v3h3v2h-3v3h-2v-3H8v-2h3V7zm9 11H4V6h16v12zM6 8h2v2H6V8z";

    private const string OutputIcon =
        "M14 2H6c-1.1 0-1.99.9-1.99 2L4 20c0 1.1.89 2 1.99 2H18c1.1 0 2-.9 2-2V8l-6-6zm2 16H8v-2h8v2zm0-4H8v-2h8v2zm-3-5V3.5L18.5 9H13z";

    /// <summary>The tabs for a platform, in phase order, each carrying the phase label it starts.</summary>
    public static IReadOnlyList<NavItem> GetTabs(string platform)
    {
        var tabs = new List<NavItem>();
        Add(PublishSection.Project, Prepare, "Project", ProjectIcon);

        // A NuGet package has no app build: the pack workspace replaces Build and Deploy.
        if (!IsNuGet(platform))
        {
            Add(PublishSection.Build, Prepare, "Build", BuildIcon);
        }

        if (IsAndroid(platform))
        {
            Add(PublishSection.Signing, Prepare, "Signing", SigningIcon);
        }

        // Every app platform can reach a target or read crash logs, so Deploy stays. NuGet has no
        // running app, so its package workspace takes the Ship slot instead.
        if (!IsNuGet(platform))
        {
            Add(PublishSection.Deploy, Ship, "Deploy", DeployIcon);
        }

        if (IsAndroid(platform))
        {
            Add(PublishSection.GooglePlay, Ship, "Google Play", GooglePlayIcon);
        }

        if (IsNuGet(platform))
        {
            Add(PublishSection.NuGet, Ship, "NuGet", NuGetIcon);
        }

        Add(PublishSection.Output, Inspect, "Output", OutputIcon);
        return tabs;

        void Add(PublishSection section, string group, string label, string icon)
        {
            var startsGroup = tabs.Count == 0 || tabs[^1].Group != group;
            tabs.Add(new NavItem(section, label, icon, group, startsGroup));
        }
    }

    /// <summary>
    /// The visible tab to select when the current one is not offered by the new platform. The
    /// fallback keeps the user in the same phase when that phase exists, and otherwise lands on
    /// the workspace that replaces the old one.
    /// </summary>
    public static PublishSection GetFallbackSection(string platform, PublishSection current)
    {
        var tabs = GetTabs(platform);
        if (tabs.Any(tab => tab.Section == current))
        {
            return current;
        }

        if (IsNuGet(platform))
        {
            return PublishSection.NuGet;
        }

        return current switch
        {
            PublishSection.Build => PublishSection.Build,
            PublishSection.Signing or PublishSection.GooglePlay => PublishSection.Deploy,
            PublishSection.NuGet => PublishSection.Build,
            _ => PublishSection.Output
        };
    }

    private static bool IsAndroid(string platform) =>
        string.Equals(platform, PublisherService.AndroidPlatform, StringComparison.Ordinal);

    private static bool IsNuGet(string platform) =>
        string.Equals(platform, PublisherService.NuGetPlatform, StringComparison.Ordinal);
}
