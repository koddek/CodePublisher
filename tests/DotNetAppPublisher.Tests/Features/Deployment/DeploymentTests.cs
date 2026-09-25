using DotNetAppPublisher.Features.Deployment.Android;
using DotNetAppPublisher.Features.Deployment.Ios;

namespace DotNetAppPublisher.Tests.Features.Deployment;

public sealed class DeploymentTests
{
    [Test]
    public async Task AndroidDeviceSelection_PrefersPhysicalDeviceThenEmulator()
    {
        var devices = new[]
        {
            new AndroidDeviceInfo("emulator-5554", "Pixel", "emulator", true),
            new AndroidDeviceInfo("device-1", "Phone", "device", true)
        };

        var serial = AndroidDeploymentService.GetFirstRunningDeviceSerial(devices);

        await Assert.That(serial).IsEqualTo("device-1");
    }

    [Test]
    [Arguments("iPhone 15 | ABC-123", "ABC-123")]
    [Arguments("ABC-123", "ABC-123")]
    public async Task IosSimulatorId_UsesFinalIdentifier(string simulator, string expected)
    {
        await Assert.That(IosSimulatorService.ExtractSimulatorId(simulator)).IsEqualTo(expected);
    }
}
