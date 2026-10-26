using CodePublisher.Features.Deployment.Android;

namespace CodePublisher.Tests.Features.Deployment;

public sealed class AndroidWirelessPanelTests
{
    private static AndroidDeviceInfo Wireless(string serial, string state, bool ready) => new(
        serial,
        "Pixel 8",
        "device",
        ready,
        state,
        AndroidDeviceConnection.Wireless,
        "Pixel 8",
        "14",
        "34",
        "arm64-v8a");

    private static readonly AndroidDeviceInfo Usb = new(
        "R5CN20DFRKA",
        "SM-G986U1",
        "device",
        true,
        AndroidDeviceState.Ready,
        AndroidDeviceConnection.Usb,
        "SM-G986U1",
        "13",
        "33",
        "arm64-v8a");

    [Test]
    public async Task Describe_CountsOnlyUsableTargetsAsConnected()
    {
        var panel = AndroidWirelessPanel.Describe(
        [
            Wireless("192.168.1.42:37123", AndroidDeviceState.Ready, true),
            Wireless("192.168.1.43:42857", AndroidDeviceState.Offline, false),
            Usb,
        ]);

        using (Assert.Multiple())
        {
            await Assert.That(panel.Connected.Count).IsEqualTo(1);
            await Assert.That(panel.Connected[0].Serial).IsEqualTo("192.168.1.42:37123");
            await Assert.That(panel.HasConnected).IsTrue();

            await Assert.That(panel.NotUsable.Count).IsEqualTo(1);
            await Assert.That(panel.NotUsable[0].Address).IsEqualTo("192.168.1.43:42857");
            await Assert.That(panel.NotUsable[0].Host).IsEqualTo("192.168.1.43");

            // USB is not a Wi-Fi target, so it never shows up in either half of the wireless panel.
            await Assert.That(panel.Connected.Any(device => device.Serial == Usb.Serial)).IsFalse();
            await Assert.That(panel.NotUsable.Any(target => target.Address == Usb.Serial)).IsFalse();
        }
    }

    [Test]
    public async Task Describe_NamesTheListedButUnusableTargetInsteadOfReportingConnected()
    {
        var panel = AndroidWirelessPanel.Describe([Wireless("192.168.1.43:42857", AndroidDeviceState.Offline, false)]);

        using (Assert.Multiple())
        {
            await Assert.That(panel.HasConnected).IsFalse();
            await Assert.That(panel.StatusLine).Contains("No device is connected over Wi-Fi");
            await Assert.That(panel.StatusLine).Contains("192.168.1.43:42857");
            await Assert.That(panel.StatusLine).Contains("offline");
            await Assert.That(panel.StatusLine).Contains("Connect");
        }
    }

    [Test]
    public async Task Describe_ReportsConnectedWhenAtLeastOneTargetIsUsable()
    {
        var panel = AndroidWirelessPanel.Describe([Wireless("192.168.1.42:37123", AndroidDeviceState.Ready, true)]);

        await Assert.That(panel.StatusLine).Contains("1 device connected over Wi-Fi");
    }

    [Test]
    public async Task Describe_ReportsOnlyUsbAndEmulatorsAsNothingConnected()
    {
        var emulator = new AndroidDeviceInfo(
            "emulator-5554",
            "Pixel_9",
            "emulator",
            true,
            AndroidDeviceState.Ready,
            AndroidDeviceConnection.Emulator);

        var panel = AndroidWirelessPanel.Describe([Usb, emulator]);

        using (Assert.Multiple())
        {
            await Assert.That(panel.HasConnected).IsFalse();
            await Assert.That(panel.HasNotUsable).IsFalse();
            await Assert.That(panel.StatusLine).IsEqualTo("No devices connected over Wi-Fi.");
        }
    }

    [Test]
    public async Task Describe_CountsSeveralListedButUnusableTargets()
    {
        var panel = AndroidWirelessPanel.Describe(
        [
            Wireless("192.168.1.43:42857", AndroidDeviceState.Offline, false),
            Wireless("192.168.1.44:42857", "connecting", false),
        ]);

        using (Assert.Multiple())
        {
            await Assert.That(panel.NotUsable.Count).IsEqualTo(2);
            await Assert.That(panel.StatusLine).Contains("2 Wi-Fi targets are listed but none is usable yet");
        }
    }

    [Test]
    public async Task Hint_TellsTheNextStepForTheReportedState()
    {
        var offline = AndroidWirelessPanel.Hint(Wireless("192.168.1.43:42857", AndroidDeviceState.Offline, false));
        var unauthorized = AndroidWirelessPanel.Hint(Wireless("192.168.1.43:42857", AndroidDeviceState.Unauthorized, false));
        var connecting = AndroidWirelessPanel.Hint(Wireless("192.168.1.43:42857", "connecting", false));

        using (Assert.Multiple())
        {
            await Assert.That(offline).Contains("offline");
            await Assert.That(offline).Contains("Connect");
            await Assert.That(unauthorized).Contains("unauthorized");
            await Assert.That(unauthorized).Contains("debugging prompt");
            await Assert.That(connecting).Contains("connecting");
        }
    }

    [Test]
    public async Task HostOf_ReturnsTheHostPartOfAnEndpoint()
    {
        using (Assert.Multiple())
        {
            await Assert.That(AndroidWirelessPanel.HostOf("192.168.1.43:42857")).IsEqualTo("192.168.1.43");
            await Assert.That(AndroidWirelessPanel.HostOf(" R5CN20DFRKA ")).IsEqualTo("R5CN20DFRKA");
            await Assert.That(AndroidWirelessPanel.HostOf(null)).IsEqualTo(string.Empty);
        }
    }

    [Test]
    public async Task Describe_IgnoresAnEmptyTargetList()
    {
        var panel = AndroidWirelessPanel.Describe(null);

        using (Assert.Multiple())
        {
            await Assert.That(panel.HasConnected).IsFalse();
            await Assert.That(panel.HasNotUsable).IsFalse();
        }
    }
}
