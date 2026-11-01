using CodePublisher.Services;
using CodePublisher.ViewModels;

namespace CodePublisher.Tests.Views;

public sealed class WorkspaceNavigationTests
{
    private static string Sections(IEnumerable<NavItem> tabs) =>
        string.Join(",", tabs.Select(tab => tab.Section));

    [Test]
    public async Task Android_ShowsSixTabsWithThreePhases()
    {
        var tabs = WorkspaceNavigation.GetTabs(PublisherService.AndroidPlatform);

        using (Assert.Multiple())
        {
            await Assert.That(Sections(tabs)).IsEqualTo("Project,Build,Signing,Deploy,GooglePlay,Output");
            await Assert.That(tabs.Where(tab => tab.StartsGroup).Select(tab => tab.Group))
                .IsEquivalentTo(["Prepare", "Ship", "Inspect"]);
        }
    }

    [Test]
    public async Task Ios_ShowsFourTabsAndNoSigningOrPlay()
    {
        var tabs = WorkspaceNavigation.GetTabs(PublisherService.IosPlatform);

        using (Assert.Multiple())
        {
            await Assert.That(Sections(tabs)).IsEqualTo("Project,Build,Deploy,Output");
            await Assert.That(tabs.Any(tab => tab.Section == PublishSection.Signing)).IsFalse();
            await Assert.That(tabs.Any(tab => tab.Section == PublishSection.GooglePlay)).IsFalse();
        }
    }

    [Test]
    [Arguments(PublisherService.MacOsPlatform)]
    [Arguments(PublisherService.WindowsPlatform)]
    [Arguments(PublisherService.LinuxPlatform)]
    public async Task Desktop_ShowsProjectBuildDeployOutput(string platform)
    {
        var tabs = WorkspaceNavigation.GetTabs(platform);

        using (Assert.Multiple())
        {
            await Assert.That(Sections(tabs)).IsEqualTo("Project,Build,Deploy,Output");
            await Assert.That(tabs.Any(tab => tab.Section == PublishSection.Signing)).IsFalse();
            await Assert.That(tabs.Any(tab => tab.Section == PublishSection.GooglePlay)).IsFalse();
        }
    }

    [Test]
    public async Task NuGet_ShowsOnlyProjectNuGetAndOutput()
    {
        var tabs = WorkspaceNavigation.GetTabs(PublisherService.NuGetPlatform);

        using (Assert.Multiple())
        {
            await Assert.That(Sections(tabs)).IsEqualTo("Project,NuGet,Output");
            await Assert.That(tabs.Any(tab => tab.Section == PublishSection.Deploy)).IsFalse();
            await Assert.That(tabs.Any(tab => tab.Section == PublishSection.Build)).IsFalse();
        }
    }

    [Test]
    [Arguments(PublisherService.AndroidPlatform)]
    [Arguments(PublisherService.IosPlatform)]
    [Arguments(PublisherService.MacOsPlatform)]
    [Arguments(PublisherService.WindowsPlatform)]
    [Arguments(PublisherService.LinuxPlatform)]
    [Arguments(PublisherService.NuGetPlatform)]
    public async Task EveryPlatform_FallsBackToAVisibleTab(string platform)
    {
        var tabs = WorkspaceNavigation.GetTabs(platform);
        var visible = tabs.Select(tab => tab.Section).ToHashSet();

        foreach (var section in Enum.GetValues<PublishSection>())
        {
            var fallback = WorkspaceNavigation.GetFallbackSection(platform, section);
            await Assert.That(visible.Contains(fallback)).IsTrue();
        }
    }

    [Test]
    public async Task SwitchingPlatform_MovesOffAHiddenTab()
    {
        var viewModel = new MainViewModel(new PublisherService(), new DesktopInteractionService());
        viewModel.SelectedSection = PublishSection.Signing;

        viewModel.PublishPlatform = PublisherService.MacOsPlatform;
        var afterDesktop = viewModel.SelectedSection;
        var desktopTabs = viewModel.NavItems.Select(item => item.Section).ToHashSet();

        viewModel.PublishPlatform = PublisherService.NuGetPlatform;
        var afterNuGet = viewModel.SelectedSection;

        using (Assert.Multiple())
        {
            await Assert.That(afterDesktop).IsEqualTo(PublishSection.Deploy);
            await Assert.That(desktopTabs.Contains(afterDesktop)).IsTrue();
            await Assert.That(afterNuGet).IsEqualTo(PublishSection.NuGet);
            await Assert.That(viewModel.NavItems.Any(item => item.Section == afterNuGet)).IsTrue();
        }
    }
}
