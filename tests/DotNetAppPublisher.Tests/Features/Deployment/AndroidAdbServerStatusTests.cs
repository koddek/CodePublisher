using DotNetAppPublisher.Features.Deployment.Android;

namespace DotNetAppPublisher.Tests.Features.Deployment;

public sealed class AndroidAdbServerStatusTests
{
    private const string CurrentServer =
        """
        usb_backend: LIBUSB
        mdns_backend: OPENSCREEN
        mdns_backend_forced: true
        version: "36.0.2"
        build: "14143358"
        executable_absolute_path: "/opt/homebrew/bin/adb"
        mdns_enabled: true
        keystore_path: "/Users/omoi/.android/adbkey"
        """;

    private const string HealthyServer =
        """
        usb_backend: LIBUSB
        mdns_backend: LIBADBMDNS
        version: "37.0.0"
        mdns_enabled: true
        """;

    private const string DiscoverySwitchedOff =
        """
        mdns_backend: LIBADBMDNS
        version: "37.0.0"
        mdns_enabled: false
        """;

    [Test]
    public async Task Parse_ReadsTheFieldsGoogleTroubleshootingUses()
    {
        var status = AndroidAdbServerStatus.Parse(CurrentServer);

        using (Assert.Multiple())
        {
            await Assert.That(status.Version).IsEqualTo("36.0.2");
            await Assert.That(status.MdnsBackend).IsEqualTo("OPENSCREEN");
            await Assert.That(status.MdnsEnabled).IsTrue();
            await Assert.That(status.MdnsBackendForced).IsTrue();
            await Assert.That(status.MajorVersion).IsEqualTo(36);
        }
    }

    [Test]
    public async Task IsHealthy_RequiresTheModernBackendAndA37Server()
    {
        using (Assert.Multiple())
        {
            await Assert.That(AndroidAdbServerStatus.Parse(HealthyServer).IsHealthy).IsTrue();
            await Assert.That(AndroidAdbServerStatus.Parse(CurrentServer).IsHealthy).IsFalse();
            await Assert.That(AndroidAdbServerStatus.Parse(DiscoverySwitchedOff).IsHealthy).IsFalse();
            await Assert.That(AndroidAdbServerStatus.Parse("mdns daemon version [x]").IsHealthy).IsFalse();
        }
    }

    [Test]
    public async Task FailedChecks_NameEachDocumentedCheckInOrder()
    {
        var checks = AndroidAdbServerStatus.Parse(CurrentServer).FailedChecks;

        using (Assert.Multiple())
        {
            await Assert.That(checks.Count).IsEqualTo(2);
            await Assert.That(checks[0]).Contains("36.0.2");
            await Assert.That(checks[1]).Contains("OPENSCREEN");
        }
    }

    [Test]
    public async Task FixCommands_ReportTheDocumentedCommands()
    {
        var commands = AndroidAdbServerStatus.Parse(CurrentServer).FixCommands;

        using (Assert.Multiple())
        {
            await Assert.That(commands).Contains("37.0.0");
            await Assert.That(commands).Contains("ADB_MDNS_OPENSCREEN");
            await Assert.That(commands).Contains("adb kill-server");
        }
    }

    [Test]
    public async Task FixCommands_ReportTheVariableForAServerWithDiscoverySwitchedOff()
    {
        var commands = AndroidAdbServerStatus.Parse(DiscoverySwitchedOff).FixCommands;

        using (Assert.Multiple())
        {
            await Assert.That(commands).Contains("ADB_MDNS");
            await Assert.That(commands).DoesNotContain("ADB_MDNS_OPENSCREEN");
        }
    }

    [Test]
    public async Task Parse_LeavesFieldsEmptyForOutputItDoesNotKnow()
    {
        var status = AndroidAdbServerStatus.Parse("mdns daemon version [Openscreen discovery 0.0.0]");

        using (Assert.Multiple())
        {
            await Assert.That(status.IsKnown).IsFalse();
            await Assert.That(status.Version).IsNull();
            await Assert.That(status.MajorVersion).IsEqualTo(0);
        }
    }

    [Test]
    [Arguments("Version 37.0.1-15733141", 37)]
    [Arguments("Version 36.0.2-14143358", 36)]
    [Arguments("Version 35.0.2-12147458", 35)]
    public async Task AdbVersion_ReadsTheBuildAdbReports(string output, int major)
    {
        var version = AndroidAdbVersion.Parse($"Android Debug Bridge version 1.0.41\n{output}\nInstalled as /x/adb");

        using (Assert.Multiple())
        {
            await Assert.That(version.Major).IsEqualTo(major);
            await Assert.That(version.Reported).IsEqualTo(output.Split(' ').Last());
            await Assert.That(version.SupportsWireless).IsEqualTo(major >= 37);
        }
    }

    [Test]
    public async Task AdbVersion_OrdersNewerBuildsFirst()
    {
        var old = AndroidAdbVersion.Parse("Version 36.0.2-14143358");
        var current = AndroidAdbVersion.Parse("Version 37.0.1-15733141");
        var unreadable = AndroidAdbVersion.Parse("something else entirely");

        using (Assert.Multiple())
        {
            await Assert.That(current.CompareTo(old) > 0).IsTrue();
            await Assert.That(old.CompareTo(current) < 0).IsTrue();
            await Assert.That(old.CompareTo(unreadable) > 0).IsTrue();
            await Assert.That(unreadable.SupportsWireless).IsFalse();
        }
    }

    [Test]
    public async Task ReadPairingFailure_NamesTheNewerPairingProtocolForAProtocolFault()
    {
        // The real failure from a pairing against an Android 17 phone on adb 36.0.2.
        var failure = AndroidWirelessDebug.ReadPairingFailure(
            "error: protocol fault (couldn't read status message): Undefined error: 0");

        using (Assert.Multiple())
        {
            await Assert.That(failure).IsNotNull();
            await Assert.That(failure!.Fix).Contains("37.0.0");
            await Assert.That(failure.Fix).Contains("Forget");
            // The same line is printed when the pairing connection never opened, which is what a
            // denied local network produces, so the check that names that cause has to stay.
            await Assert.That(failure.Fix).Contains("Local Network");
        }
    }

    [Test]
    public async Task ReadPairingFailure_NamesThePhoneKeyForATlsAlert()
    {
        var failure = AndroidWirelessDebug.ReadPairingFailure(
            "error: Post-handshake SSL_peek failed [SSLV3_ALERT_CERTIFICATE_UNKNOWN]");

        using (Assert.Multiple())
        {
            await Assert.That(failure).IsNotNull();
            await Assert.That(failure!.Cause).Contains("refused");
        }
    }
}
