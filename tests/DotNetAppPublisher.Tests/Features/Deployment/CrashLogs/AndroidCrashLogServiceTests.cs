using DotNetAppPublisher.Features.Deployment.Android;

namespace DotNetAppPublisher.Tests.Features.Deployment.CrashLogs;

public sealed class AndroidCrashLogServiceTests
{
    [Test]
    public async Task ScanAsync_AsksForThePackageIdWhenItIsMissing()
    {
        var service = new AndroidCrashLogService("adb");

        var error = await Assert.That(async () =>
            await service.ScanAsync("emulator-5554", "   ", CancellationToken.None))
            .Throws<InvalidOperationException>();

        await Assert.That(error!.Message).Contains("package id");
    }

    [Test]
    public async Task ScanAsync_AsksForADeviceWhenItIsMissing()
    {
        var service = new AndroidCrashLogService("adb");

        var error = await Assert.That(async () =>
            await service.ScanAsync("  ", "com.example.app", CancellationToken.None))
            .Throws<InvalidOperationException>();

        await Assert.That(error!.Message).Contains("No device selected");
    }
}
