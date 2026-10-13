using DotNetAppPublisher.Features.Deployment.Android;

namespace DotNetAppPublisher.Tests.Features.Deployment;

public sealed class AndroidDevicePropertiesTests
{
    private const string GetpropOutput =
        """
        [ro.product.model]: [Pixel 8]
        [ro.product.vendor.model]: [ignored when the main model exists]
        [ro.build.version.release]: [14]
        [ro.build.version.sdk]: [34]
        [ro.kernel.qemu]: []
        [ro.boot.qemu]: [0]
        """;

    [Test]
    public async Task Parse_ReadsPhysicalDeviceProperties()
    {
        var properties = AndroidDeviceProperties.Parse(GetpropOutput);

        using (Assert.Multiple())
        {
            await Assert.That(properties.Model).IsEqualTo("Pixel 8");
            await Assert.That(properties.AndroidVersion).IsEqualTo("14");
            await Assert.That(properties.ApiLevel).IsEqualTo("34");
            await Assert.That(properties.IsEmulator).IsFalse();
        }
    }

    [Test]
    public async Task Parse_DetectsEmulatorsFromBootProperty()
    {
        const string output =
            """
            [ro.product.model]: [sdk_gphone64_arm64]
            [ro.kernel.qemu]: [1]
            """;

        await Assert.That(AndroidDeviceProperties.Parse(output).IsEmulator).IsTrue();
    }

    [Test]
    public async Task Parse_DetectsEmulatorsThatOnlySetTheBootProperty()
    {
        const string output = "[ro.boot.qemu]: [1]\n[ro.product.model]: [Emulator]\n";

        await Assert.That(AndroidDeviceProperties.Parse(output).IsEmulator).IsTrue();
    }

    [Test]
    public async Task Parse_HandlesEmptyAndMalformedOutput()
    {
        using (Assert.Multiple())
        {
            await Assert.That(AndroidDeviceProperties.Parse(null).Model).IsNull();
            await Assert.That(AndroidDeviceProperties.Parse("noise without brackets").IsEmulator).IsFalse();
        }
    }
}
