using DotNetAppPublisher.Features.Deployment.Android;

namespace DotNetAppPublisher.Tests.Features.Deployment;

public sealed class AndroidQrPairingTests
{
    [Test]
    public async Task BuildPayload_UsesTheFormatAndroidScans()
    {
        var payload = AndroidQrPairing.BuildPayload("studio-abc123", "(Aq+v9>Cx>!/");

        await Assert.That(payload).IsEqualTo("WIFI:T:ADB;S:studio-abc123;P:(Aq+v9>Cx>!/;;");
    }

    [Test]
    public async Task Create_GeneratesAnInvitationThePhoneCanActOn()
    {
        var request = AndroidQrPairing.Create();

        using (Assert.Multiple())
        {
            await Assert.That(request.ServiceName).StartsWith(AndroidQrPairing.ServiceNamePrefix);
            await Assert.That(request.Password.Length).IsEqualTo(AndroidQrPairing.PasswordLength);
            await Assert.That(request.Password.All(char.IsLetterOrDigit)).IsTrue();
            await Assert.That(request.Payload).IsEqualTo(
                $"WIFI:T:ADB;S:{request.ServiceName};P:{request.Password};;");
        }
    }

    [Test]
    public async Task Create_DoesNotRepeatItself()
    {
        var names = Enumerable.Range(0, 25).Select(_ => AndroidQrPairing.Create()).ToArray();

        await Assert.That(names.Select(request => request.ServiceName).Distinct().Count()).IsEqualTo(names.Length);
        await Assert.That(names.Select(request => request.Password).Distinct().Count()).IsEqualTo(names.Length);
    }

    [Test]
    public async Task Create_KeepsExplicitValues()
    {
        var request = AndroidQrPairing.Create("dotnetapppublisher-fixed", "Ab3Xy7Zq1Wm4");

        using (Assert.Multiple())
        {
            await Assert.That(request.ServiceName).IsEqualTo("dotnetapppublisher-fixed");
            await Assert.That(request.Password).IsEqualTo("Ab3Xy7Zq1Wm4");
            await Assert.That(request.Payload).IsEqualTo("WIFI:T:ADB;S:dotnetapppublisher-fixed;P:Ab3Xy7Zq1Wm4;;");
        }
    }

    [Test]
    [Arguments("Ab3Xy7Zq1Wm4", true)]
    [Arguments("123456", true)]
    [Arguments("a-b_c.d@e", true)]
    [Arguments("", false)]
    [Arguments("   ", false)]
    [Arguments("abc", false)]
    [Arguments("has space", false)]
    [Arguments("semi;colon", false)]
    [Arguments("quote\"inside", false)]
    [Arguments("back\\slash", false)]
    public async Task TryNormalizeSecret_AcceptsPassphrasesAndRejectsFieldBreakingInput(string input, bool expected)
    {
        await Assert.That(AndroidQrPairing.TryNormalizeSecret(input, out _, out _)).IsEqualTo(expected);
    }

    [Test]
    public async Task TryNormalizeSecret_TrimsSurroundingSpace()
    {
        var accepted = AndroidQrPairing.TryNormalizeSecret("  Ab3Xy7Zq1Wm4 ", out var secret, out _);

        using (Assert.Multiple())
        {
            await Assert.That(accepted).IsTrue();
            await Assert.That(secret).IsEqualTo("Ab3Xy7Zq1Wm4");
        }
    }

    [Test]
    public async Task IsMatch_RequiresThePairingServiceAndTheRequestedName()
    {
        var requested = AndroidQrPairing.Create("dotnetapppublisher-abcdefgh12", "Ab3Xy7Zq1Wm4");

        var pairing = new AndroidWirelessService("dotnetapppublisher-abcdefgh12", "192.168.1.42:39411", true);
        var differentName = new AndroidWirelessService("dotnetapppublisher-zzzzzzzz99", "192.168.1.42:39412", true);
        var debuggingPort = new AndroidWirelessService("dotnetapppublisher-abcdefgh12", "192.168.1.42:37123", false);

        using (Assert.Multiple())
        {
            await Assert.That(AndroidQrPairing.IsMatch(pairing, requested.ServiceName)).IsTrue();
            await Assert.That(AndroidQrPairing.IsMatch(pairing, requested.ServiceName.ToUpperInvariant())).IsTrue();
            await Assert.That(AndroidQrPairing.IsMatch(differentName, requested.ServiceName)).IsFalse();
            await Assert.That(AndroidQrPairing.IsMatch(debuggingPort, requested.ServiceName)).IsFalse();
        }
    }

    [Test]
    public async Task IsMatch_ToleratesABuildThatPrefixesTheNameWithAdb()
    {
        var requested = AndroidQrPairing.Create("dotnetapppublisher-abcdefgh12", "Ab3Xy7Zq1Wm4");
        var prefixed = new AndroidWirelessService(
            "adb-14141FDF600081-dotnetapppublisher-abcdefgh12",
            "192.168.86.38:33861",
            true);

        await Assert.That(AndroidQrPairing.IsMatch(prefixed, requested.ServiceName)).IsTrue();
    }

    [Test]
    public async Task IsMatch_DoesNotSuffixMatchAShortName()
    {
        var service = new AndroidWirelessService("adb-abc", "192.168.1.42:39411", true);

        await Assert.That(AndroidQrPairing.IsMatch(service, "abc")).IsFalse();
    }

    [Test]
    public async Task TryFindPairingService_PrefersTheExactNameOverAPrefixedOne()
    {
        var services = new List<AndroidWirelessService>
        {
            new("adb-14141FDF600081-dotnetapppublisher-abcdefgh12", "192.168.1.42:33861", true),
            new("dotnetapppublisher-abcdefgh12", "192.168.1.42:39411", true),
            new("other-host", "192.168.1.43:39411", true)
        };

        var found = AndroidQrPairing.TryFindPairingService(services, "dotnetapppublisher-abcdefgh12", out var match);

        using (Assert.Multiple())
        {
            await Assert.That(found).IsTrue();
            await Assert.That(match!.Address).IsEqualTo("192.168.1.42:39411");
        }
    }

    [Test]
    public async Task TryFindPairingService_ReportsNothingWhileThePhoneHasNotScanned()
    {
        var services = new List<AndroidWirelessService>
        {
            new("dotnetapppublisher-abcdefgh12", "192.168.1.42:37123", false)
        };

        await Assert.That(AndroidQrPairing.TryFindPairingService(services, "dotnetapppublisher-abcdefgh12", out _)).IsFalse();
    }

    [Test]
    public async Task CreatePng_ProducesASquarePngMadeOfWholeModules()
    {
        var png = AndroidQrPairingImage.CreatePng("WIFI:T:ADB;S:dotnetapppublisher-abcdefgh12;P:Ab3Xy7Zq1Wm4;;", 8);

        var signature = Convert.ToHexString(png.AsSpan(0, 8));
        var width = ReadBigEndianInt(png, 16);
        var height = ReadBigEndianInt(png, 20);

        using (Assert.Multiple())
        {
            await Assert.That(signature).IsEqualTo("89504E470D0A1A0A");
            await Assert.That(width).IsEqualTo(height);
            await Assert.That(width % 8).IsEqualTo(0);
            await Assert.That(width).IsGreaterThan(8 * 21);
        }
    }

    [Test]
    public async Task CreatePng_DiffersWithThePayload()
    {
        var first = AndroidQrPairingImage.CreatePng("WIFI:T:ADB;S:dotnetapppublisher-aaaaaaaaaa;P:Ab3Xy7Zq1Wm4;;");
        var second = AndroidQrPairingImage.CreatePng("WIFI:T:ADB;S:dotnetapppublisher-bbbbbbbbbb;P:Ab3Xy7Zq1Wm4;;");

        await Assert.That(first.SequenceEqual(second)).IsFalse();
    }

    private static int ReadBigEndianInt(byte[] bytes, int offset) =>
        (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
}
