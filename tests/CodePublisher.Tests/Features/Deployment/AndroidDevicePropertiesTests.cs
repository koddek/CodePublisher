using CodePublisher.Features.Deployment.Android;

namespace CodePublisher.Tests.Features.Deployment;

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
    public async Task Parse_ReadsThePrimaryAbi()
    {
        const string output =
            """
            [ro.product.model]: [Pixel 8]
            [ro.product.cpu.abi]: [arm64-v8a]
            [ro.product.cpu.abilist]: [arm64-v8a,armeabi-v7a,armeabi]
            """;

        await Assert.That(AndroidDeviceProperties.Parse(output).Abi).IsEqualTo("arm64-v8a");
    }

    [Test]
    public async Task Parse_FallsBackToTheFirstAbiOfTheAbiList()
    {
        const string output =
            """
            [ro.system.product.cpu.abilist]: [x86_64,x86,arm64-v8a]
            """;

        await Assert.That(AndroidDeviceProperties.Parse(output).Abi).IsEqualTo("x86_64");
    }

    [Test]
    public async Task Parse_HandlesEmptyAndMalformedOutput()
    {
        using (Assert.Multiple())
        {
            await Assert.That(AndroidDeviceProperties.Parse(null).Model).IsNull();
            await Assert.That(AndroidDeviceProperties.Parse(null).Abi).IsNull();
            await Assert.That(AndroidDeviceProperties.Parse("noise without brackets").IsEmulator).IsFalse();
        }
    }
}
