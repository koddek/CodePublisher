using CodePublisher.Features.Deployment.Android;

namespace CodePublisher.Tests.Features.Deployment;

public sealed class AndroidDeviceListParserTests
{
    private const string MixedDeviceOutput =
        """
        * daemon not running; starting now at tcp:5037
        * daemon started successfully
        List of devices attached
        R5CT123456A            device usb:1-2 product:device model:SM_S921B device:e3q transport_id:3
        emulator-5554          device product:sdk_gphone64_x86_64 model:Pixel_8_API_35 device:emu64xa
        192.168.1.42:37123     device product:redfin model:Pixel_5 device:redfin
        """;

    [Test]
    public async Task Parse_KeepsUsbWirelessAndEmulatorTargets()
    {
        var entries = AndroidDeviceListParser.Parse(MixedDeviceOutput);

        using (Assert.Multiple())
        {
            await Assert.That(entries.Count).IsEqualTo(3);
            await Assert.That(entries[0].Serial).IsEqualTo("R5CT123456A");
            await Assert.That(entries[0].IsWirelessSerial).IsFalse();
            await Assert.That(entries[0].Model).IsEqualTo("SM S921B");
            await Assert.That(entries[0].UsbId).IsEqualTo("1-2");
            await Assert.That(entries[1].IsEmulatorSerial).IsTrue();
            await Assert.That(entries[2].IsWirelessSerial).IsTrue();
        }
    }

    [Test]
    public async Task Parse_KeepsUnusableTargetsWithTheirState()
    {
        const string output =
            """
            List of devices attached
            R5CT123456A     unauthorized usb:1-2 transport_id:3
            emulator-5554   offline
            """;

        var entries = AndroidDeviceListParser.Parse(output);

        using (Assert.Multiple())
        {
            await Assert.That(entries.Count).IsEqualTo(2);
            await Assert.That(entries[0].State).IsEqualTo("unauthorized");
            await Assert.That(entries[1].State).IsEqualTo("offline");
        }
    }

    [Test]
    public async Task Parse_ReportsDevicesWithoutUsbPermissions()
    {
        const string output =
            """
            List of devices attached
            ????????????    no permissions (user in plugdev group; are your udev rules wrong?)
            """;

        var entries = AndroidDeviceListParser.Parse(output);

        using (Assert.Multiple())
        {
            await Assert.That(entries.Count).IsEqualTo(1);
            await Assert.That(entries[0].State).IsEqualTo("no permissions");
        }
    }

    [Test]
    public async Task Parse_IgnoresLinesThatAreNotTargets()
    {
        const string output =
            """
            List of devices attached

            not-a-device-line
            """;

        await Assert.That(AndroidDeviceListParser.Parse(output).Count).IsEqualTo(0);
    }

    [Test]
    [Arguments("192.168.1.42:37123", true)]
    [Arguments("192.168.1.42:5555", true)]
    [Arguments("emulator-5554", false)]
    [Arguments("R5CT123456A", false)]
    [Arguments("192.168.1.42", false)]
    [Arguments("192.168.1.42:", false)]
    [Arguments("192.168.1.42:0", false)]
    [Arguments("192.168.1.42:99999", false)]
    public async Task IsWirelessSerial_DetectsHostPortSerialsOnly(string serial, bool expected)
    {
        await Assert.That(AndroidDeviceListParser.IsWirelessSerial(serial)).IsEqualTo(expected);
    }
}
