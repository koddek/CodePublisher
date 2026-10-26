using CodePublisher.Features.Deployment.Android;

namespace CodePublisher.Tests.Features.Deployment;

public sealed class AndroidWirelessDiagnosticsTests
{
    private static readonly AndroidDeviceInfo ConnectedWirelessDevice = new(
        "192.168.1.42:37123",
        "Pixel 8",
        "device",
        true,
        AndroidDeviceState.Ready,
        AndroidDeviceConnection.Wireless,
        "Pixel 8",
        "14",
        "34",
        "arm64-v8a");

    private static readonly AndroidDeviceInfo UnpairedWirelessDevice = new(
        "192.168.1.43:37123",
        "Pixel 7",
        "device",
        true,
        AndroidDeviceState.Ready,
        AndroidDeviceConnection.Wireless,
        "Pixel 7",
        "13",
        "33",
        "arm64-v8a");

    private static readonly Dictionary<string, string?> EmptyPairedHosts = new(StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyList<AndroidWirelessService> EmptyMdns = [];

    [Test]
    public async Task Diagnose_ReturnsConnected_WhenDeviceIsInTargetList()
    {
        var paired = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["192.168.1.42"] = null };

        var result = AndroidWirelessDiagnostics.Diagnose(
            "192.168.1.42:37123",
            [ConnectedWirelessDevice],
            paired,
            EmptyMdns,
            "14",
            34);

        using (Assert.Multiple())
        {
            await Assert.That(result.IsConnected).IsTrue();
            await Assert.That(result.IsPaired).IsTrue();
            await Assert.That(result.AllPassed).IsTrue();
        }
    }

    [Test]
    public async Task Diagnose_ReturnsNotConnected_WhenDeviceNotInList()
    {
        var result = AndroidWirelessDiagnostics.Diagnose(
            "192.168.1.99:37123",
            [ConnectedWirelessDevice],
            EmptyPairedHosts,
            EmptyMdns,
            "14",
            34);

        using (Assert.Multiple())
        {
            await Assert.That(result.IsConnected).IsFalse();
            await Assert.That(result.IsPaired).IsFalse();
            await Assert.That(result.Summary).Contains("Connection not ready");
        }
    }

    [Test]
    public async Task Diagnose_FlagsBadAddressFormat()
    {
        var result = AndroidWirelessDiagnostics.Diagnose(
            "192.168.1.42",
            [ConnectedWirelessDevice],
            EmptyPairedHosts,
            EmptyMdns,
            "14",
            34);

        using (Assert.Multiple())
        {
            var addressCheck = result.Checks.First(c => c.Id == "address-format");
            await Assert.That(addressCheck.Passed).IsFalse();
            await Assert.That(addressCheck.Detail).Contains("IP:PORT");
        }
    }

    [Test]
    public async Task Diagnose_FlagsPairingPortUsedAsDebugPort()
    {
        var mdns = new List<AndroidWirelessService>
        {
            new("adb-xxx", "192.168.1.42:39411", true),
            new("adb-yyy", "192.168.1.42:37123", false),
        };
        var paired = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["192.168.1.42"] = null };

        // The debugging port 37123 is also advertised as a debugging service, so it's fine.
        // But if we connect to the pairing port 39411:
        var result = AndroidWirelessDiagnostics.Diagnose(
            "192.168.1.42:39411",
            [ConnectedWirelessDevice],
            paired,
            mdns,
            "14",
            34);

        var portCheck = result.Checks.First(c => c.Id == "port-sanity");
        using (Assert.Multiple())
        {
            await Assert.That(portCheck.Passed).IsFalse();
            await Assert.That(portCheck.Detail).Contains("pairing port");
        }
    }

    [Test]
    public async Task Diagnose_FlagsNotPaired()
    {
        var result = AndroidWirelessDiagnostics.Diagnose(
            "192.168.1.42:37123",
            [ConnectedWirelessDevice],
            EmptyPairedHosts,
            EmptyMdns,
            "14",
            34);

        var pairCheck = result.Checks.First(c => c.Id == "paired");
        using (Assert.Multiple())
        {
            await Assert.That(pairCheck.Passed).IsFalse();
            await Assert.That(pairCheck.Detail).Contains("not paired");
        }
    }

    [Test]
    public async Task Diagnose_FlagsNotConnected()
    {
        var paired = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["192.168.1.42"] = null };

        var result = AndroidWirelessDiagnostics.Diagnose(
            "192.168.1.42:37123",
            [], // no connected devices
            paired,
            EmptyMdns,
            "14",
            34);

        var connectCheck = result.Checks.First(c => c.Id == "connected");
        using (Assert.Multiple())
        {
            await Assert.That(connectCheck.Passed).IsFalse();
            await Assert.That(connectCheck.Detail).Contains("not listed as a connected");
        }
    }

    [Test]
    public async Task Diagnose_FlagsTargetNotReady()
    {
        var notReady = new AndroidDeviceInfo(
            "192.168.1.42:37123",
            "Pixel 8",
            "device",
            true,
            AndroidDeviceState.Unauthorized,
            AndroidDeviceConnection.Wireless,
            "Pixel 8",
            "14",
            "34",
            "arm64-v8a");
        var paired = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["192.168.1.42"] = null };

        var result = AndroidWirelessDiagnostics.Diagnose(
            "192.168.1.42:37123",
            [notReady],
            paired,
            EmptyMdns,
            "14",
            34);

        var readyCheck = result.Checks.First(c => c.Id == "target-ready");
        using (Assert.Multiple())
        {
            await Assert.That(readyCheck.Passed).IsFalse();
            await Assert.That(readyCheck.Detail).Contains("unauthorized");
        }
    }

    [Test]
    public async Task Diagnose_TreatsAListedButOfflineTargetAsNotConnected()
    {
        // `adb connect` leaves this row behind when the session never became usable, so it must not be
        // reported as a connection: that is the row the panel used to present as connected.
        var offline = new AndroidDeviceInfo(
            "192.168.1.42:37123",
            "Pixel 8",
            "device",
            false,
            AndroidDeviceState.Offline,
            AndroidDeviceConnection.Wireless,
            "Pixel 8",
            "14",
            "34",
            "arm64-v8a");
        var paired = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["192.168.1.42"] = null };

        var result = AndroidWirelessDiagnostics.Diagnose(
            "192.168.1.42:37123",
            [offline],
            paired,
            EmptyMdns,
            "14",
            34);

        var connectCheck = result.Checks.First(c => c.Id == "connected");
        var readyCheck = result.Checks.First(c => c.Id == "target-ready");
        using (Assert.Multiple())
        {
            await Assert.That(result.IsConnected).IsFalse();
            await Assert.That(connectCheck.Passed).IsFalse();
            await Assert.That(connectCheck.Detail).Contains("offline");
            await Assert.That(readyCheck.Passed).IsFalse();
            await Assert.That(readyCheck.Detail).Contains("offline");
        }
    }

    [Test]
    public async Task Diagnose_FlagsAndroid10AndOlder()
    {
        var result = AndroidWirelessDiagnostics.Diagnose(
            "192.168.1.42:37123",
            [],
            EmptyPairedHosts,
            EmptyMdns,
            "10",
            29);

        var versionCheck = result.Checks.First(c => c.Id == "android-version");
        using (Assert.Multiple())
        {
            await Assert.That(versionCheck.Passed).IsFalse();
            await Assert.That(versionCheck.Detail).Contains("API 29");
            await Assert.That(versionCheck.Fix).Contains("Wi-Fi debugging over USB");
        }
    }

    [Test]
    public async Task Diagnose_AcceptsAndroid11Through13()
    {
        var result = AndroidWirelessDiagnostics.Diagnose(
            "192.168.1.42:37123",
            [],
            EmptyPairedHosts,
            EmptyMdns,
            "12",
            31);

        var versionCheck = result.Checks.First(c => c.Id == "android-version");
        await Assert.That(versionCheck.Passed).IsTrue();
    }

    [Test]
    public async Task Diagnose_AcceptsAndroid14AndNewer()
    {
        var result = AndroidWirelessDiagnostics.Diagnose(
            "192.168.1.42:37123",
            [],
            EmptyPairedHosts,
            EmptyMdns,
            "15",
            35);

        var versionCheck = result.Checks.First(c => c.Id == "android-version");
        await Assert.That(versionCheck.Passed).IsTrue();
    }

    [Test]
    public async Task Diagnose_SummaryReportsAllChecksPassed()
    {
        var paired = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["192.168.1.42"] = null };

        var result = AndroidWirelessDiagnostics.Diagnose(
            "192.168.1.42:37123",
            [ConnectedWirelessDevice],
            paired,
            EmptyMdns,
            "14",
            34);

        await Assert.That(result.Summary).Contains("Everything checks out");
    }

    [Test]
    public async Task Diagnose_EmptyAddressFails()
    {
        var result = AndroidWirelessDiagnostics.Diagnose(
            "",
            [],
            EmptyPairedHosts,
            EmptyMdns,
            "14",
            34);

        var addressCheck = result.Checks.First(c => c.Id == "address-format");
        await Assert.That(addressCheck.Passed).IsFalse();
    }
}
