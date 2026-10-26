using CodePublisher.Features.Deployment.Android;

namespace CodePublisher.Tests.Features.Deployment;

public sealed class AndroidMdnsResolveTests
{
    [Test]
    public async Task TryParseResolveLine_ReadsHostAndPort()
    {
        const string line =
            "13:03:14.995  codepublisher-abc12345._adb-tls-pairing._tcp.local. " +
            "can be reached at Pixel-8.local.:39411 (interface 22) Flags: 1";

        var parsed = AndroidMdnsResolve.TryParseResolveLine(line, out var host, out var port);

        using (Assert.Multiple())
        {
            await Assert.That(parsed).IsTrue();
            await Assert.That(host).IsEqualTo("Pixel-8.local");
            await Assert.That(port).IsEqualTo(39411);
        }
    }

    [Test]
    public async Task TryParseResolveLine_ReadsWithoutTheFlagsSuffix()
    {
        const string line =
            "13:03:14.995  codepublisher-abc12345._adb-tls-pairing._tcp.local. " +
            "can be reached at Pixel-8.local.:39411 (interface 22)";

        var parsed = AndroidMdnsResolve.TryParseResolveLine(line, out var host, out var port);

        using (Assert.Multiple())
        {
            await Assert.That(parsed).IsTrue();
            await Assert.That(host).IsEqualTo("Pixel-8.local");
            await Assert.That(port).IsEqualTo(39411);
        }
    }

    [Test]
    public async Task TryParseResolveLine_RejectsBannersAndFailures()
    {
        using (Assert.Multiple())
        {
            await Assert.That(AndroidMdnsResolve.TryParseResolveLine(
                "Lookup codepublisher-abc12345._adb-tls-pairing._tcp.local", out _, out _)).IsFalse();
            await Assert.That(AndroidMdnsResolve.TryParseResolveLine(
                "DATE: ---Fri 23 Oct 2026---", out _, out _)).IsFalse();
            await Assert.That(AndroidMdnsResolve.TryParseResolveLine(
                "13:03:14.995  ...STARTING...", out _, out _)).IsFalse();
            await Assert.That(AndroidMdnsResolve.TryParseResolveLine(
                "DNSServiceResolve failed -65540", out _, out _)).IsFalse();
            await Assert.That(AndroidMdnsResolve.TryParseResolveLine("", out _, out _)).IsFalse();
        }
    }

    [Test]
    public async Task TryParseAddressLine_ReadsTheReachableAddress()
    {
        const string line =
            "13:06:35.889  Add  40000003      13  Pixel-8.local.                     192.168.50.158                           4500";

        var parsed = AndroidMdnsResolve.TryParseAddressLine(line, out var address);

        using (Assert.Multiple())
        {
            await Assert.That(parsed).IsTrue();
            await Assert.That(address).IsEqualTo("192.168.50.158");
        }
    }

    [Test]
    public async Task TryParseAddressLine_IgnoresRemovalsAndBanners()
    {
        using (Assert.Multiple())
        {
            await Assert.That(AndroidMdnsResolve.TryParseAddressLine(
                "13:06:35.889  Rmv  40000003      13  Pixel-8.local.  192.168.50.158  0", out _)).IsFalse();
            await Assert.That(AndroidMdnsResolve.TryParseAddressLine(
                "Timestamp     A/R  Flags         IF  Hostname   Address   TTL", out _)).IsFalse();
            await Assert.That(AndroidMdnsResolve.TryParseAddressLine("", out _)).IsFalse();
        }
    }

    [Test]
    [Arguments("127.0.0.1", null)]
    [Arguments("169.254.2.3", null)]
    [Arguments("192.168.1.5", "192.168.1.5")]
    [Arguments("10.0.0.7", "10.0.0.7")]
    public async Task PickBestIpv4_SkipsLoopbackAndLinkLocal(string? input, string? expected)
    {
        await Assert.That(AndroidMdnsResolve.PickBestIpv4([input!])).IsEqualTo(expected);
    }

    [Test]
    public async Task PickBestIpv4_PrefersTheFirstReachableAddress()
    {
        await Assert.That(AndroidMdnsResolve.PickBestIpv4(
            ["127.0.0.1", "192.168.50.158", "192.168.64.1"])).IsEqualTo("192.168.50.158");
    }

    [Test]
    public async Task PickBestIpv4_ReturnsNullWhenNothingIsReachable()
    {
        await Assert.That(AndroidMdnsResolve.PickBestIpv4(["127.0.0.1", "169.254.1.1"])).IsNull();
    }

    [Test]
    public async Task TryParseBrowseLine_ReadsAnAddedInstance()
    {
        const string line =
            "12:27:26.660  Add        3   1 local.               _adb-tls-pairing._tcp. codepublisher-abc12345";

        var parsed = AndroidMdnsResolve.TryParseBrowseLine(line, out var added, out var name);

        using (Assert.Multiple())
        {
            await Assert.That(parsed).IsTrue();
            await Assert.That(added).IsTrue();
            await Assert.That(name).IsEqualTo("codepublisher-abc12345");
        }
    }

    [Test]
    public async Task TryParseBrowseLine_IgnoresRemovals()
    {
        const string line =
            "12:27:26.660  Rmv        3   1 local.               _adb-tls-pairing._tcp. codepublisher-abc12345";

        await Assert.That(AndroidMdnsResolve.TryParseBrowseLine(line, out _, out _)).IsFalse();
    }

    [Test]
    public async Task TryParseBrowseLine_IgnoresServicesThatAreNotWirelessDebugging()
    {
        const string line = "12:27:26.660  Add        3   1 local.               _airplay._tcp. LivingRoom";

        await Assert.That(AndroidMdnsResolve.TryParseBrowseLine(line, out _, out _)).IsFalse();
    }

    [Test]
    public async Task TryParseBrowseLine_AcceptsTheConnectServiceType()
    {
        const string line =
            "12:27:26.660  Add        3   1 local.               _adb-tls-connect._tcp. adb-PIXEL8-TnSdi9";

        var parsed = AndroidMdnsResolve.TryParseBrowseLine(line, out var added, out var name);

        using (Assert.Multiple())
        {
            await Assert.That(parsed).IsTrue();
            await Assert.That(added).IsTrue();
            await Assert.That(name).IsEqualTo("adb-PIXEL8-TnSdi9");
        }
    }

    [Test]
    public async Task TryParseBrowseLine_KeepsTheServiceVersionInsideTheName()
    {
        // A device running Wi-Fi debugging 2.0 appends the service version to the instance name, so
        // the name is every column after the service type rather than only the last one.
        const string line =
            "0:50:23.651  Add        2  13 local.               _adb-tls-connect._tcp. adb-R5CN20DFRKA-0z8hFB (2)";

        var parsed = AndroidMdnsResolve.TryParseBrowseLine(line, out var added, out var name);

        using (Assert.Multiple())
        {
            await Assert.That(parsed).IsTrue();
            await Assert.That(added).IsTrue();
            await Assert.That(name).IsEqualTo("adb-R5CN20DFRKA-0z8hFB (2)");
        }
    }

    [Test]
    public async Task TryParseBrowseLine_IgnoresTheResolverHeader()
    {
        await Assert.That(AndroidMdnsResolve.TryParseBrowseLine(
            "Timestamp     A/R    Flags  if Domain               Service Type         Instance Name",
            out _, out _)).IsFalse();
    }

    [Test]
    public async Task TryTrimVersionMarker_SplitsNameAndVersion()
    {
        var parsed = AndroidMdnsResolve.TryTrimVersionMarker("adb-R5CN20DFRKA-0z8hFB (2)", out var name, out var version);

        using (Assert.Multiple())
        {
            await Assert.That(parsed).IsTrue();
            await Assert.That(name).IsEqualTo("adb-R5CN20DFRKA-0z8hFB");
            await Assert.That(version).IsEqualTo(2);
        }
    }

    [Test]
    public async Task TryTrimVersionMarker_LeavesANameWithoutTheMarkerAlone()
    {
        var parsed = AndroidMdnsResolve.TryTrimVersionMarker("adb-PIXEL8-TnSdi9", out var name, out var version);

        using (Assert.Multiple())
        {
            await Assert.That(parsed).IsTrue();
            await Assert.That(name).IsEqualTo("adb-PIXEL8-TnSdi9");
            await Assert.That(version).IsNull();
        }
    }

    [Test]
    public async Task ResolveNativeMdnsTool_IsOnlyOfferedWhereDnsSdExists()
    {
        // macOS ships dns-sd, so the resolver fallback must be available there. Linux does not
        // ship it (avahi-browse speaks a different syntax), and the browser build never reaches
        // this code because the wireless panel is desktop-only.
        var tool = AndroidDeploymentService.ResolveNativeMdnsTool();
        if (OperatingSystem.IsMacOS())
        {
            await Assert.That(tool).IsEqualTo("dns-sd");
        }
        else
        {
            await Assert.That(tool).IsNull();
        }
    }

    [Test]
    public async Task ResolveNativeMdnsServiceAsync_ReturnsNullWhenNothingIsAdvertised()
    {
        // A short window keeps the test fast: nothing publishes this name, so the resolver is
        // stopped by the timeout and no exception may escape.
        var resolved = await AndroidDeploymentService.ResolveNativeMdnsServiceAsync(
            "codepublisher-does-not-exist",
            pairingService: true,
            TimeSpan.FromMilliseconds(150),
            CancellationToken.None);

        await Assert.That(resolved).IsNull();
    }
}
