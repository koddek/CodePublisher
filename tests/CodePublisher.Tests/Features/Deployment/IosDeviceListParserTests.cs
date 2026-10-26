using CodePublisher.Features.Deployment.Ios;

namespace CodePublisher.Tests.Features.Deployment;

public sealed class IosDeviceListParserTests
{
    private const string DevicectlJson =
        """
        {
          "info": { "commandType": "devicectl.list.devices", "jsonVersion": 2, "outcome": "success" },
          "result": {
            "devices": [
              {
                "identifier": "6E1B1C4A-0000-1111-2222-333344445555",
                "hardwareProperties": { "deviceType": "iPhone", "udid": "00008030-000A1D3C0C88802E", "marketingName": "iPhone 15 Pro" },
                "deviceProperties": { "name": "Dev iPhone", "osVersionNumber": "18.1.1", "developerModeStatus": "enabled" },
                "connectionProperties": { "pairingState": "paired", "transportType": "wired" }
              },
              {
                "identifier": "6E1B1C4A-0000-1111-2222-333344445566",
                "hardwareProperties": { "deviceType": "iPad", "udid": "00008110-000F1D2E3F4G5061", "marketingName": "iPad Pro" },
                "deviceProperties": { "name": "Lab iPad", "osVersionNumber": "17.5", "developerModeStatus": "disabled" },
                "connectionProperties": { "pairingState": "paired", "transportType": "localNetwork" }
              },
              {
                "identifier": "6E1B1C4A-0000-1111-2222-333344445577",
                "hardwareProperties": { "deviceType": "iPhone", "udid": "00008020-000B2C3D4E5F6071" },
                "deviceProperties": { "name": "Old iPhone", "osVersionNumber": "16.7" },
                "connectionProperties": { "pairingState": "unpaired" }
              }
            ]
          }
        }
        """;

    [Test]
    public async Task ParseDevicectlJson_ReadsIdentityTransportAndState()
    {
        var devices = IosDeviceListParser.ParseDevicectlJson(DevicectlJson);

        using (Assert.Multiple())
        {
            await Assert.That(devices.Count).IsEqualTo(3);
            await Assert.That(devices[0].Name).IsEqualTo("Dev iPhone");
            await Assert.That(devices[0].Udid).IsEqualTo("00008030-000A1D3C0C88802E");
            await Assert.That(devices[0].Transport).IsEqualTo(IosDeviceTransport.Usb);
            await Assert.That(devices[0].IsDeployable).IsTrue();
            await Assert.That(devices[0].OsVersion).IsEqualTo("18.1.1");
        }
    }

    [Test]
    public async Task ParseDevicectlJson_MarksWiFiDevicesAndMissingDeveloperMode()
    {
        var devices = IosDeviceListParser.ParseDevicectlJson(DevicectlJson);
        var ipad = devices.Single(device => device.Name == "Lab iPad");

        using (Assert.Multiple())
        {
            await Assert.That(ipad.Transport).IsEqualTo(IosDeviceTransport.Network);
            await Assert.That(ipad.TransportLabel).IsEqualTo("Wi-Fi");
            await Assert.That(ipad.IsDeveloperModeEnabled).IsFalse();
            await Assert.That(ipad.DisplayName).Contains("Developer Mode is off");
        }
    }

    [Test]
    public async Task ParseDevicectlJson_ExplainsUnpairedDevicesAndOrdersThemLast()
    {
        var devices = IosDeviceListParser.ParseDevicectlJson(DevicectlJson);

        using (Assert.Multiple())
        {
            await Assert.That(devices[^1].Name).IsEqualTo("Old iPhone");
            await Assert.That(devices[^1].IsDeployable).IsFalse();
            await Assert.That(devices[^1].DisplayName).Contains("not paired");
        }
    }

    [Test]
    public async Task ParseDevicectlJson_ReturnsEmptyForUnusableInput()
    {
        using (Assert.Multiple())
        {
            await Assert.That(IosDeviceListParser.ParseDevicectlJson("not json").Count).IsEqualTo(0);
            await Assert.That(IosDeviceListParser.ParseDevicectlJson(null).Count).IsEqualTo(0);
            await Assert.That(IosDeviceListParser.ParseDevicectlJson("{\"result\":{}}").Count).IsEqualTo(0);
        }
    }

    [Test]
    public async Task ParseXctraceList_ReadsTheDeviceSectionOnly()
    {
        const string output =
            """
            == Devices ==
            John's iPhone (17.0) (00008030-000A1D3C0C88802E)
            Apple TV (18.0) (ABCD1234-5678-90AB-CDEF-1234567890AB)
            == Simulators ==
            iPhone 15 (17.0) (ABC-123)
            """;

        var devices = IosDeviceListParser.ParseXctraceList(output);

        using (Assert.Multiple())
        {
            await Assert.That(devices.Count).IsEqualTo(2);
            await Assert.That(devices[0].Name).IsEqualTo("John's iPhone");
            await Assert.That(devices[0].OsVersion).IsEqualTo("17.0");
            await Assert.That(devices[0].Udid).IsEqualTo("00008030-000A1D3C0C88802E");
            await Assert.That(devices.Any(device => device.Name.Contains("iPhone 15"))).IsFalse();
        }
    }
}
