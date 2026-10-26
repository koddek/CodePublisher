using CodePublisher.Features.Deployment.Android;
using CodePublisher.Features.Deployment.Ios;

namespace CodePublisher.Tests.Features.Deployment;

public sealed class DeploymentTests
{
    [Test]
    public async Task AndroidDeviceSelection_PrefersPhysicalDeviceThenEmulator()
    {
        var devices = new[]
        {
            new AndroidDeviceInfo("emulator-5554", "Pixel", "emulator", true, Connection: AndroidDeviceConnection.Emulator),
            new AndroidDeviceInfo("device-1", "Phone", "device", true)
        };

        var serial = AndroidDeploymentService.GetFirstRunningDeviceSerial(devices);

        await Assert.That(serial).IsEqualTo("device-1");
    }

    [Test]
    public async Task AndroidDeviceSelection_PrefersReadyDeviceOverUnusableTargets()
    {
        var devices = new[]
        {
            new AndroidDeviceInfo("R5CT123456A", "Galaxy S21", "device", false, AndroidDeviceState.Unauthorized),
            new AndroidDeviceInfo("emulator-5554", "Pixel", "emulator", true, Connection: AndroidDeviceConnection.Emulator),
            new AndroidDeviceInfo("192.168.1.42:37123", "Pixel 5", "device", true, Connection: AndroidDeviceConnection.Wireless)
        };

        var serial = AndroidDeploymentService.SelectPreferredSerial(devices);

        await Assert.That(serial).IsEqualTo("192.168.1.42:37123");
    }

    [Test]
    public async Task AndroidDeviceSelection_ReturnsNullWhenNothingIsReady()
    {
        var devices = new[]
        {
            new AndroidDeviceInfo(string.Empty, "Pixel_8_API_35", "emulator", false, AndroidDeviceState.Offline)
        };

        await Assert.That(AndroidDeploymentService.SelectPreferredSerial(devices)).IsNull();
    }

    [Test]
    public async Task AndroidDeviceRank_OrdersReadyDevicesThenRunningEmulatorsThenTheRest()
    {
        var ready = new AndroidDeviceInfo("device-1", "Phone", "device", true);
        var emulator = new AndroidDeviceInfo("emulator-5554", "Pixel", "emulator", true, Connection: AndroidDeviceConnection.Emulator);
        var offline = new AndroidDeviceInfo("device-2", "Tablet", "device", false, AndroidDeviceState.Offline);

        using (Assert.Multiple())
        {
            await Assert.That(AndroidDeviceInfo.Rank(ready)).IsEqualTo(0);
            await Assert.That(AndroidDeviceInfo.Rank(emulator)).IsEqualTo(1);
            await Assert.That(AndroidDeviceInfo.Rank(offline)).IsEqualTo(2);
        }
    }

    [Test]
    public async Task AndroidDeviceInfo_DisplayNameExplainsConnectionAndState()
    {
        var phone = new AndroidDeviceInfo(
            "R5CT123456A",
            "Galaxy S21",
            "device",
            true,
            AndroidDeviceState.Ready,
            AndroidDeviceConnection.Usb,
            "Galaxy S21",
            "14",
            "34");
        var unauthorized = new AndroidDeviceInfo(
            "R5CT123456B",
            "Galaxy S21",
            "device",
            false,
            AndroidDeviceState.Unauthorized);
        var wireless = new AndroidDeviceInfo(
            "192.168.1.42:37123",
            "Pixel 5",
            "device",
            true,
            AndroidDeviceState.Ready,
            AndroidDeviceConnection.Wireless);

        using (Assert.Multiple())
        {
            await Assert.That(phone.DisplayName).IsEqualTo("Galaxy S21 (R5CT123456A, USB) · Android 14 (API 34)");
            await Assert.That(unauthorized.DisplayName).Contains("unauthorized");
            await Assert.That(wireless.DisplayName).Contains("Wi-Fi");
            await Assert.That(wireless.IsReady).IsTrue();
            await Assert.That(unauthorized.IsReady).IsFalse();
        }
    }

    [Test]
    public async Task AndroidDeviceInfo_DetailSummaryShowsVersionApiAbiTransportAndReadiness()
    {
        var phone = new AndroidDeviceInfo(
            "R5CT123456A",
            "Galaxy S21",
            "device",
            true,
            AndroidDeviceState.Ready,
            AndroidDeviceConnection.Usb,
            "SM-G991B",
            "14",
            "34",
            "arm64-v8a");
        var wireless = new AndroidDeviceInfo(
            "192.168.1.42:37123",
            "Pixel 5",
            "device",
            true,
            AndroidDeviceState.Ready,
            AndroidDeviceConnection.Wireless,
            "Pixel 5",
            "13",
            "33",
            "arm64-v8a");
        var unauthorized = new AndroidDeviceInfo(
            "R5CT123456B",
            "Galaxy S21",
            "device",
            false,
            AndroidDeviceState.Unauthorized);
        var offlineAvd = new AndroidDeviceInfo(
            string.Empty,
            "Pixel_8_API_35",
            "emulator",
            false,
            AndroidDeviceState.Offline,
            AndroidDeviceConnection.Emulator);

        using (Assert.Multiple())
        {
            await Assert.That(phone.DetailSummary).IsEqualTo("SM-G991B  ·  Android 14 (API 34)  ·  arm64-v8a  ·  USB  ·  ready");
            await Assert.That(wireless.DetailSummary).IsEqualTo("Pixel 5  ·  Android 13 (API 33)  ·  arm64-v8a  ·  Wi-Fi  ·  ready");
            await Assert.That(unauthorized.DetailSummary).Contains("unauthorized");
            await Assert.That(offlineAvd.DetailSummary).Contains("offline");
            await Assert.That(offlineAvd.HasDetail).IsTrue();
        }
    }

    [Test]
    public async Task ResolveLauncherComponent_ReadsTheComponentFromResolveActivity()
    {
        const string output =
            """
            priority=0 preferredOrder=0 match=0x108000 specificIndex=-1 isDefault=false
            com.example.app/.MainActivity
            """;

        using (Assert.Multiple())
        {
            await Assert.That(AndroidDeploymentService.ResolveLauncherComponent(output)).IsEqualTo("com.example.app/.MainActivity");
            await Assert.That(AndroidDeploymentService.ResolveLauncherComponent("No activity found")).IsNull();
            await Assert.That(AndroidDeploymentService.ResolveLauncherComponent(null)).IsNull();
        }
    }

    [Test]
    [Arguments("iPhone 15 | ABC-123", "ABC-123")]
    [Arguments("ABC-123", "ABC-123")]
    public async Task IosSimulatorId_UsesFinalIdentifier(string simulator, string expected)
    {
        await Assert.That(IosSimulatorService.ExtractSimulatorId(simulator)).IsEqualTo(expected);
    }
}
