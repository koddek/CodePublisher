using DotNetAppPublisher.Services;

namespace DotNetAppPublisher.Tests.Services;

public sealed class AlertSoundServiceTests
{
    private static readonly Func<string, bool> NoFiles = _ => false;

    private static Func<string, bool> Only(string path) =>
        candidate => string.Equals(candidate, path, StringComparison.Ordinal);

    [Test]
    public async Task BuildPlan_MacOs_PlaysTheSuccessSoundWithAfplay()
    {
        var plan = AlertSoundService.BuildPlan(AlertTone.Success, AlertSoundPlatform.MacOs, File.Exists);

        var command = plan.Single();
        using (Assert.Multiple())
        {
            await Assert.That(command.FileName).IsEqualTo("afplay");
            await Assert.That(command.Arguments.Single()).IsEqualTo("/System/Library/Sounds/Glass.aiff");
        }
    }

    [Test]
    public async Task BuildPlan_MacOs_PlaysADifferentSoundForFailures()
    {
        var plan = AlertSoundService.BuildPlan(AlertTone.Failure, AlertSoundPlatform.MacOs, File.Exists);

        await Assert.That(plan.Single().Arguments.Single()).IsEqualTo("/System/Library/Sounds/Basso.aiff");
    }

    [Test]
    public async Task BuildPlan_MacOs_FallsBackToTheNextInstalledSound()
    {
        var plan = AlertSoundService.BuildPlan(
            AlertTone.Success,
            AlertSoundPlatform.MacOs,
            Only("/System/Library/Sounds/Blow.aiff"));

        await Assert.That(plan.Single().Arguments.Single()).IsEqualTo("/System/Library/Sounds/Blow.aiff");
    }

    [Test]
    public async Task BuildPlan_MacOs_IsEmptyWhenNoSoundFileExists()
    {
        var plan = AlertSoundService.BuildPlan(AlertTone.Success, AlertSoundPlatform.MacOs, NoFiles);

        await Assert.That(plan.Count).IsEqualTo(0);
    }

    [Test]
    public async Task BuildPlan_Windows_PlaysTheSystemSoundThroughPowerShell()
    {
        var plan = AlertSoundService.BuildPlan(
            AlertTone.Success,
            AlertSoundPlatform.Windows,
            Only(@"C:\Windows\Media\Windows Notify System Generic.wav"));

        var command = plan.Single();
        using (Assert.Multiple())
        {
            await Assert.That(command.FileName).IsEqualTo("powershell");
            await Assert.That(command.Arguments).Contains("-NoProfile");
            await Assert.That(command.Arguments.Last()).Contains("Windows Notify System Generic.wav");
            await Assert.That(command.Arguments.Last()).Contains("PlaySync");
        }
    }

    [Test]
    public async Task BuildPlan_Windows_IsEmptyWhenNoSoundFileExists()
    {
        var plan = AlertSoundService.BuildPlan(AlertTone.Failure, AlertSoundPlatform.Windows, NoFiles);

        await Assert.That(plan.Count).IsEqualTo(0);
    }

    [Test]
    public async Task BuildPlan_Linux_PlaysTheSoundFileAndFallsBackToCanberra()
    {
        var withSound = AlertSoundService.BuildPlan(
            AlertTone.Success,
            AlertSoundPlatform.Linux,
            Only("/usr/share/sounds/freedesktop/stereo/complete.oga"));
        var withoutSound = AlertSoundService.BuildPlan(AlertTone.Failure, AlertSoundPlatform.Linux, NoFiles);

        using (Assert.Multiple())
        {
            await Assert.That(string.Join(",", withSound.Select(command => command.FileName)))
                .IsEqualTo("paplay,pw-play,canberra-gtk-play");
            await Assert.That(withSound[0].Arguments.Single())
                .IsEqualTo("/usr/share/sounds/freedesktop/stereo/complete.oga");
            await Assert.That(string.Join(",", withoutSound.Select(command => command.FileName)))
                .IsEqualTo("canberra-gtk-play");
            await Assert.That(withoutSound[0].Arguments).Contains("dialog-error");
        }
    }

    [Test]
    public async Task BuildPlan_UnknownPlatform_HasNoPlayerAndRingsTheTerminalBell()
    {
        var plan = AlertSoundService.BuildPlan(AlertTone.Success, AlertSoundPlatform.Other, File.Exists);

        await Assert.That(plan.Count).IsEqualTo(0);
    }

    [Test]
    public async Task IsCoalesced_TreatsASecondAlertRightAfterTheFirstAsDuplicate()
    {
        using (Assert.Multiple())
        {
            await Assert.That(AlertSoundService.IsCoalesced(1000, 400)).IsTrue();
            await Assert.That(AlertSoundService.IsCoalesced(1000, 350)).IsTrue();
            // Exactly one coalescing window apart is already a new alert.
            await Assert.That(AlertSoundService.IsCoalesced(1000, 300)).IsFalse();
            await Assert.That(AlertSoundService.IsCoalesced(1000, 0)).IsFalse();
            await Assert.That(AlertSoundService.IsCoalesced(1000, long.MinValue)).IsFalse();
        }
    }
}
