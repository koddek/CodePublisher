using DotNetAppPublisher.Features.Publishing.Configure;
using DotNetAppPublisher.Services;

namespace DotNetAppPublisher.Tests.Features.Publishing;

public sealed class PlatformProfileTests
{
    [Test]
    [Arguments(PublisherService.AndroidPlatform, "net10.0-android", "android-arm64")]
    [Arguments(PublisherService.MacOsPlatform, "net10.0", "osx-arm64")]
    [Arguments(PublisherService.WindowsPlatform, "net10.0-windows", "win-x64")]
    [Arguments(PublisherService.IosPlatform, "net10.0-ios", "ios-arm64")]
    [Arguments(PublisherService.LinuxPlatform, "net10.0", "linux-x64")]
    public async Task GetDefaults_ReturnsExpectedFrameworkAndRuntime(
        string platform,
        string expectedFramework,
        string expectedRuntime)
    {
        var profile = PlatformProfiles.GetDefaults(platform);

        using (Assert.Multiple())
        {
            await Assert.That(profile.TargetFramework).IsEqualTo(expectedFramework);
            await Assert.That(profile.RuntimeIdentifier).IsEqualTo(expectedRuntime);
            await Assert.That(profile.Configuration).IsEqualTo("Release");
        }
    }

    [Test]
    public async Task GetDefaults_DisablesUnsupportedIosOptions()
    {
        var profile = PlatformProfiles.GetDefaults(PublisherService.IosPlatform);

        using (Assert.Multiple())
        {
            await Assert.That(profile.PublishTrimmed).IsTrue();
            await Assert.That(profile.UseAppHost).IsFalse();
            await Assert.That(profile.PublishAot).IsFalse();
            await Assert.That(profile.PublishReadyToRun).IsFalse();
        }
    }
}
