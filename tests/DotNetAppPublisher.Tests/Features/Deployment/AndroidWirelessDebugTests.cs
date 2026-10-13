using DotNetAppPublisher.Features.Deployment.Android;

namespace DotNetAppPublisher.Tests.Features.Deployment;

public sealed class AndroidWirelessDebugTests
{
    private const string MdnsOutput =
        """
        List of discovered mdns services
        adb-2B4C7D._adb-tls-connect._tcp.   192.168.1.42:37123
        adb-9F8E7D._adb-tls-pairing._tcp.   192.168.1.42:39411
        """;

    [Test]
    public async Task ParseMdnsServices_SeparatesPairingFromDebuggingPorts()
    {
        var services = AndroidWirelessDebug.ParseMdnsServices(MdnsOutput);

        using (Assert.Multiple())
        {
            await Assert.That(services.Count).IsEqualTo(2);
            await Assert.That(services[0].Address).IsEqualTo("192.168.1.42:37123");
            await Assert.That(services[0].IsPairingService).IsFalse();
            await Assert.That(services[1].IsPairingService).IsTrue();
            await Assert.That(services[1].Kind).IsEqualTo("pairing");
        }
    }

    [Test]
    public async Task ParseMdnsServices_ReturnsEmptyWhenDiscoveryIsUnavailable()
    {
        var services = AndroidWirelessDebug.ParseMdnsServices("mdns daemon version [x]\n");

        await Assert.That(services.Count).IsEqualTo(0);
    }

    [Test]
    public async Task TryNormalizeAddress_AcceptsPastedServiceRow()
    {
        var accepted = AndroidWirelessDebug.TryNormalizeAddress(
            "adb-2B4C7D._adb-tls-connect._tcp.   192.168.1.42:37123",
            out var address,
            out var error);

        using (Assert.Multiple())
        {
            await Assert.That(accepted).IsTrue();
            await Assert.That(address).IsEqualTo("192.168.1.42:37123");
            await Assert.That(error).IsEmpty();
        }
    }

    [Test]
    public async Task TryNormalizeAddress_RejectsAddressWithoutPort()
    {
        var accepted = AndroidWirelessDebug.TryNormalizeAddress("192.168.1.42", out _, out var error);

        using (Assert.Multiple())
        {
            await Assert.That(accepted).IsFalse();
            await Assert.That(error).Contains("IP:PORT");
        }
    }

    [Test]
    [Arguments("123456", true)]
    [Arguments("1234", true)]
    [Arguments("123", false)]
    [Arguments("123456789", false)]
    [Arguments("12a456", false)]
    [Arguments("", false)]
    public async Task TryNormalizePairingCode_RequiresDigitsOnly(string code, bool expected)
    {
        await Assert.That(AndroidWirelessDebug.TryNormalizePairingCode(code, out _, out _)).IsEqualTo(expected);
    }

    [Test]
    public async Task ExtractWifiIpAddress_ReadsSourceFromRoute()
    {
        const string route =
            """
            default via 192.168.1.1 dev wlan0 proto static
            192.168.1.0/24 dev wlan0 proto kernel scope link src 192.168.1.42
            """;

        await Assert.That(AndroidWirelessDebug.ExtractWifiIpAddress(route)).IsEqualTo("192.168.1.42");
    }

    [Test]
    public async Task ExtractWifiIpAddress_ReturnsNullWhenOnlyMobileDataIsUp()
    {
        const string route = "default via 10.0.0.1 dev rmnet_data0 proto static";

        await Assert.That(AndroidWirelessDebug.ExtractWifiIpAddress(route)).IsNull();
    }

    [Test]
    public async Task ExtractWifiIpAddressFromProperties_ReadsDhcpProperty()
    {
        const string properties = "[dhcp.wlan0.ipaddress]: [192.168.1.77]\n[ro.product.model]: [Pixel 8]\n";

        await Assert.That(AndroidWirelessDebug.ExtractWifiIpAddressFromProperties(properties)).IsEqualTo("192.168.1.77");
    }
}
