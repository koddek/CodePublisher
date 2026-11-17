using CodePublisher.Features.Publishing.Configure;
using CodePublisher.Services;

namespace CodePublisher.Tests.Features.Publishing;

/// <summary>
/// Desktop targets keep manual publish options on .NET 11. Property evaluation on SDK 11.0.100
/// showed no automatic ReadyToRun, AOT or single-file for osx/win/linux RIDs, so the app's explicit
/// switches remain the source of truth there.
/// </summary>
public sealed class DesktopNet11Tests
{
    [Test]
    [Arguments(PublisherService.MacOsPlatform, "net11.0", "osx-arm64")]
    [Arguments(PublisherService.WindowsPlatform, "net11.0-windows", "win-x64")]
    [Arguments(PublisherService.LinuxPlatform, "net11.0", "linux-x64")]
    public async Task NoAutomaticReadyToRunOnDesktop(string platform, string targetFramework, string runtimeIdentifier)
    {
        await Assert.That(PublishOptionPolicy.IsReadyToRunAutomatic(platform, targetFramework, "Release", true)).IsFalse();
    }

    [Test]
    [Arguments(PublisherService.MacOsPlatform, "net11.0", "osx-arm64")]
    [Arguments(PublisherService.WindowsPlatform, "net11.0-windows", "win-x64")]
    [Arguments(PublisherService.LinuxPlatform, "net11.0", "linux-x64")]
    public async Task NoAutomaticTrimOnDesktop(string platform, string targetFramework, string runtimeIdentifier)
    {
        await Assert.That(PublishOptionPolicy.IsPublishTrimmedAutomatic(platform, targetFramework)).IsFalse();
    }

    [Test]
    public async Task NativeAot_StillAppliesToDesktop()
    {
        using (Assert.Multiple())
        {
            await Assert.That(DesktopPublishPolicy.IsPublishAotEnabled(PublisherService.MacOsPlatform)).IsTrue();
            await Assert.That(DesktopPublishPolicy.IsPublishAotEnabled(PublisherService.WindowsPlatform)).IsTrue();
            await Assert.That(DesktopPublishPolicy.IsPublishAotEnabled(PublisherService.LinuxPlatform)).IsTrue();
        }
    }

    [Test]
    public async Task SingleFile_StillManualOnDesktop()
    {
        await Assert.That(DesktopPublishPolicy.IsPublishSingleFileEnabled(publishAot: false)).IsTrue();
    }
}
