using CodePublisher.Features.Deployment.Android;

namespace CodePublisher.Tests.Features.Deployment;

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
    public async Task ParseMdnsServices_KeepsTheServiceVersionOfANewerDevice()
    {
        // The device that hung on its pairing spinner advertised 192.168.50.237:42857 with the
        // service version marked inside the instance name.
        const string services =
            """
            List of discovered mdns services
            adb-R5CN20DFRKA-0z8hFB (2)	_adb-tls-connect._tcp	192.168.50.237:42857
            """;

        var parsed = AndroidWirelessDebug.ParseMdnsServices(services);

        using (Assert.Multiple())
        {
            await Assert.That(parsed.Count).IsEqualTo(1);
            await Assert.That(parsed[0].Name).IsEqualTo("adb-R5CN20DFRKA-0z8hFB");
            await Assert.That(parsed[0].Address).IsEqualTo("192.168.50.237:42857");
            await Assert.That(parsed[0].ServiceVersion).IsEqualTo(2);
            await Assert.That(parsed[0].IsWifiTwo).IsTrue();
        }
    }

    [Test]
    [Arguments("failed to connect to '192.168.50.237:41023': No route to host", true)]
    [Arguments("failed to connect to '192.168.50.237:42857': Operation timed out", true)]
    [Arguments("failed to connect to '192.168.50.237:42857': Connection refused", false)]
    [Arguments("connected to 192.168.50.237:42857", null)]
    public async Task ReadConnectFailure_ExplainsTheSocketErrorAdbReports(string adbOutput, bool? unreachable)
    {
        var failure = AndroidWirelessDebug.ReadConnectFailure(adbOutput);

        using (Assert.Multiple())
        {
            if (unreachable is null)
            {
                await Assert.That(failure).IsNull();
                return;
            }

            await Assert.That(failure).IsNotNull();
            await Assert.That(failure!.IsUnreachable).IsEqualTo(unreachable);
            await Assert.That(failure.Cause.Length > 0).IsTrue();
            await Assert.That(failure.Fix.Length > 0).IsTrue();
        }
    }

    [Test]
    public async Task ReadConnectFailure_NamesTheNetworkWhenThereIsNoRoute()
    {
        var failure = AndroidWirelessDebug.ReadConnectFailure(
            "failed to connect to '192.168.50.237:41023': No route to host");

        using (Assert.Multiple())
        {
            await Assert.That(failure).IsNotNull();
            await Assert.That(failure!.Cause).Contains("no route");
            await Assert.That(failure.Fix).Contains("same Wi-Fi network");
        }
    }

    [Test]
    public async Task ReadConnectFailure_NamesTheMacLocalNetworkBlockForNoRoute()
    {
        // The real failure on macOS 26: the app's own adb was denied local network access, so every
        // address on the network answered "No route to host" while a Terminal-launched process
        // reached the same phone at the same moment.
        var failure = AndroidWirelessDebug.ReadConnectFailure(
            "failed to connect to '192.168.50.237:42857': No route to host");

        using (Assert.Multiple())
        {
            await Assert.That(failure).IsNotNull();
            await Assert.That(failure!.IsUnreachable).IsTrue();
            await Assert.That(failure.Fix).Contains("Local Network");
            await Assert.That(failure.Fix).Contains("System Settings");
        }
    }

    [Test]
    public async Task ReadSocketError_KeepsTheMacLocalNetworkBlockInTheProbeGuidance()
    {
        // The app's own socket probe is denied by the same policy, so the sentence has to survive
        // the stale-address note ReadSocketError appends to the unreachable case.
        var failure = AndroidWirelessDebug.ReadSocketError("No route to host");

        using (Assert.Multiple())
        {
            await Assert.That(failure).IsNotNull();
            await Assert.That(failure!.Fix).Contains("Local Network");
            await Assert.That(failure.Fix).Contains("stale");
        }
    }

    [Test]
    public async Task ReadConnectFailure_PointsAtTheDebuggingPortWhenRefused()
    {
        var failure = AndroidWirelessDebug.ReadConnectFailure(
            "failed to connect to '192.168.50.237:42857': Connection refused");

        // A refused port is what a closed pairing port or a restarted phone answers with, so the
        // fix has to name the port the user should connect to instead.
        using (Assert.Multiple())
        {
            await Assert.That(failure).IsNotNull();
            await Assert.That(failure!.IsUnreachable).IsFalse();
            await Assert.That(failure.Fix).Contains("Wireless debugging");
        }
    }

    [Test]
    [Arguments("Connection refused", false)]
    [Arguments("The operation has timed out.", true)]
    [Arguments("No route to host", true)]
    public async Task ReadSocketError_ExplainsWhyAPhoneDidNotAnswer(string socketError, bool unreachable)
    {
        var failure = AndroidWirelessDebug.ReadSocketError(socketError);

        using (Assert.Multiple())
        {
            await Assert.That(failure).IsNotNull();
            await Assert.That(failure!.IsUnreachable).IsEqualTo(unreachable);
            // A phone the packets never reached can only have moved on, so the staleness warning is
            // what sends the user back to a fresh scan instead of a retry of the same address.
            await Assert.That(failure.Fix.Contains("stale", StringComparison.OrdinalIgnoreCase)).IsEqualTo(unreachable);
        }
    }

    [Test]
    public async Task ReadSocketError_NamesTheStaleAddressForAnUnreachablePhone()
    {
        // The pairing service was advertised a minute ago and the phone has since stopped answering;
        // the socket timeout is the only signal left that the address is no longer good.
        var failure = AndroidWirelessDebug.ReadSocketError("The operation has timed out.");

        using (Assert.Multiple())
        {
            await Assert.That(failure!.IsUnreachable).IsTrue();
            await Assert.That(failure.Fix).Contains("scan again");
        }
    }

    [Test]
    public async Task ReadSocketError_ReturnsNullForOutputItDoesNotKnow()
    {
        await Assert.That(AndroidWirelessDebug.ReadSocketError("something else")).IsNull();
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
    public async Task MergeServices_KeepsThePhoneTheAdbBackendCannotSee()
    {
        // Verified on macOS 26 with platform-tools 37.0.1: `adb mdns services` listed nothing while
        // the system resolver saw the same phone. Reading only adb loses every later step.
        AndroidWirelessService[] fromAdb = [];
        AndroidWirelessService[] fromResolver =
        [
            new("adb-R5CN20DFRKA-0z8hFB", "192.168.50.237:42857", IsPairingService: false)
        ];

        var merged = AndroidWirelessDebug.MergeServices(fromAdb, fromResolver);

        using (Assert.Multiple())
        {
            await Assert.That(merged.Count).IsEqualTo(1);
            await Assert.That(merged[0].Address).IsEqualTo("192.168.50.237:42857");
        }
    }

    [Test]
    public async Task MergeServices_ListsOneRowPerServiceAcrossBothPaths()
    {
        AndroidWirelessService[] fromAdb =
        [
            new("adb-R5CN20DFRKA-0z8hFB", "192.168.50.237:42857", IsPairingService: false)
        ];
        AndroidWirelessService[] fromResolver =
        [
            // The same endpoint under the name the resolver reports, plus a pairing port of its own.
            new("adb-R5CN20DFRKA-0z8hFB (2)", "192.168.50.237:42857", IsPairingService: false),
            new("studio-abc12345", "192.168.50.237:39411", IsPairingService: true)
        ];

        var merged = AndroidWirelessDebug.MergeServices(fromAdb, fromResolver);

        using (Assert.Multiple())
        {
            await Assert.That(merged.Count).IsEqualTo(2);
            // Same order as `adb mdns services` parsing: debugging ports first, pairing ports after.
            await Assert.That(merged[0].IsPairingService).IsFalse();
            await Assert.That(merged[0].Address).IsEqualTo("192.168.50.237:42857");
            await Assert.That(merged[1].Name).IsEqualTo("studio-abc12345");
            await Assert.That(merged[1].IsPairingService).IsTrue();
        }
    }

    [Test]
    public async Task ConnectCandidates_TriesEveryDebuggingPortOfOneHostAndNeverAPairingPort()
    {
        // A cached adb record and the system resolver can list the same phone with different ports;
        // the stale one is only discovered by trying, so both are candidates and the pairing port,
        // which needs the code on the phone, is never one.
        AndroidWirelessService[] services =
        [
            new("studio-abc12345", "192.168.50.237:39411", IsPairingService: true),
            new("adb-a", "192.168.50.237:42857", IsPairingService: false),
            new("adb-b", "192.168.50.237:43111", IsPairingService: false),
            new("adb-c", "192.168.50.99:37123", IsPairingService: false)
        ];

        var candidates = AndroidWirelessDebug.ConnectCandidates(services, "192.168.50.237", "192.168.50.237:42857");

        using (Assert.Multiple())
        {
            await Assert.That(candidates.Count).IsEqualTo(2);
            await Assert.That(candidates[0]).IsEqualTo("192.168.50.237:42857");
            // The saved address is not repeated, and another device's port is never used.
            await Assert.That(candidates[1]).IsEqualTo("192.168.50.237:43111");
        }
    }

    [Test]
    public async Task ConnectCandidates_FallsBackToTheSavedAddressWhenNothingIsAdvertised()
    {
        var candidates = AndroidWirelessDebug.ConnectCandidates(
            [],
            "192.168.50.237",
            "192.168.50.237:42857");

        await Assert.That(candidates.Single()).IsEqualTo("192.168.50.237:42857");
    }

    [Test]
    public async Task FindWirelessTargetState_ReadsWhatAdbListedForTheEndpoint()
    {
        var entries = AndroidDeviceListParser.Parse(
            """
            List of devices attached
            192.168.50.237:42857	offline product:x model:SM_G986U1
            192.168.50.99:37123	device product:y model:Pixel_8
            """);

        using (Assert.Multiple())
        {
            await Assert.That(AndroidWirelessDebug.FindWirelessTargetState(entries, "192.168.50.237:42857"))
                .IsEqualTo(AndroidDeviceState.Offline);
            await Assert.That(AndroidWirelessDebug.FindWirelessTargetState(entries, "192.168.50.99:37123"))
                .IsEqualTo(AndroidDeviceState.Ready);
            // Not listed at all: `adb connect` printing "connected to" is not a success signal.
            await Assert.That(AndroidWirelessDebug.FindWirelessTargetState(entries, "192.168.50.237:40000"))
                .IsNull();
        }
    }

    [Test]
    public async Task ReadConnectFailure_NamesTheMissingPairingWhenTheKeyIsRejected()
    {
        var failure = AndroidWirelessDebug.ReadConnectFailure(
            "failed to authenticate to '192.168.50.237:42857': The connection is not authenticated");

        using (Assert.Multiple())
        {
            await Assert.That(failure).IsNotNull();
            await Assert.That(failure!.Cause).Contains("does not trust");
            await Assert.That(failure.Fix).Contains("Pair again");
            await Assert.That(failure.IsUnreachable).IsFalse();
        }
    }

    [Test]
    public async Task ConnectResult_NeedsApprovalOnlyForAnUnauthorizedTarget()
    {
        using (Assert.Multiple())
        {
            await Assert.That(new AndroidWirelessConnectResult("a:1", false, AndroidDeviceState.Unauthorized, "x").NeedsApproval)
                .IsTrue();
            await Assert.That(new AndroidWirelessConnectResult("a:1", false, "not listed", "x").NeedsApproval)
                .IsFalse();
        }
    }

    [Test]
    public async Task ExtractWifiIpAddressFromProperties_ReadsDhcpProperty()
    {
        const string properties = "[dhcp.wlan0.ipaddress]: [192.168.1.77]\n[ro.product.model]: [Pixel 8]\n";

        await Assert.That(AndroidWirelessDebug.ExtractWifiIpAddressFromProperties(properties)).IsEqualTo("192.168.1.77");
    }

    [Test]
    public async Task ExtractWifiIpAddressFromIpAddr_ExtractsIpv4FromWlanInterface()
    {
        const string output =
            """
            1: lo: <LOOPBACK,UP,LOWER_UP> mtu 65536 qdisc noqueue state UNKNOWN group default qlen 1000
                inet 127.0.0.1/8 scope host lo
                   valid_lft forever preferred_lft forever
            29: wlan0: <BROADCAST,MULTICAST,UP,LOWER_UP> mtu 1500 qdisc mq state UP group default qlen 3000
                inet 192.168.1.145/24 brd 192.168.1.255 scope global wlan0
                   valid_lft forever preferred_lft forever
            """;

        await Assert.That(AndroidWirelessDebug.ExtractWifiIpAddressFromIpAddr(output)).IsEqualTo("192.168.1.145");
    }

    [Test]
    public async Task ExtractWifiIpAddressFromIpAddr_ReturnsNullWhenNoWifiInterface()
    {
        const string output =
            """
            1: lo: <LOOPBACK,UP,LOWER_UP> mtu 65536 qdisc noqueue state UNKNOWN group default qlen 1000
                inet 127.0.0.1/8 scope host lo
                   valid_lft forever preferred_lft forever
            """;

        await Assert.That(AndroidWirelessDebug.ExtractWifiIpAddressFromIpAddr(output)).IsNull();
    }

    [Test]
    public async Task ReadStalledTransport_ExplainsAnOfflineEndpointThatStillAnswers()
    {
        var failure = AndroidWirelessDebug.ReadStalledTransport(AndroidDeviceState.Offline, endpointReachable: true);

        using (Assert.Multiple())
        {
            await Assert.That(failure).IsNotNull();
            await Assert.That(failure!.Cause).Contains("never became usable");
            await Assert.That(failure.Fix).Contains("Forget");
            await Assert.That(failure.Fix).Contains("pair again");
        }
    }

    [Test]
    public async Task ReadStalledTransport_ExplainsAnUnauthorizedEndpointThatAnswers()
    {
        var failure = AndroidWirelessDebug.ReadStalledTransport(AndroidDeviceState.Unauthorized, endpointReachable: true);

        using (Assert.Multiple())
        {
            await Assert.That(failure).IsNotNull();
            await Assert.That(failure!.Cause).Contains("approved");
            await Assert.That(failure.Fix).Contains("debugging prompt");
        }
    }

    [Test]
    public async Task ReadStalledTransport_SaysNothingWhenThePortDoesNotAnswer()
    {
        var offline = AndroidWirelessDebug.ReadStalledTransport(AndroidDeviceState.Offline, endpointReachable: false);
        var connecting = AndroidWirelessDebug.ReadStalledTransport("connecting", endpointReachable: false);

        using (Assert.Multiple())
        {
            await Assert.That(offline).IsNull();
            await Assert.That(connecting).IsNull();
        }
    }

    [Test]
    public async Task ReadStalledTransport_SaysNothingAboutAUsableOrUnknownState()
    {
        using (Assert.Multiple())
        {
            await Assert.That(AndroidWirelessDebug.ReadStalledTransport(AndroidDeviceState.Ready, endpointReachable: true)).IsNull();
            await Assert.That(AndroidWirelessDebug.ReadStalledTransport("no permissions", endpointReachable: true)).IsNull();
            await Assert.That(AndroidWirelessDebug.ReadStalledTransport(null, endpointReachable: true)).IsNull();
            await Assert.That(AndroidWirelessDebug.ReadStalledTransport("connecting", endpointReachable: true)).IsNotNull();
        }
    }
}
