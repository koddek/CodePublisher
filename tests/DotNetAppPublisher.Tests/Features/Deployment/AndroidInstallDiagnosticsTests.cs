using DotNetAppPublisher.Features.Deployment.Android;

namespace DotNetAppPublisher.Tests.Features.Deployment;

public sealed class AndroidInstallDiagnosticsTests
{
    [Test]
    public async Task DescribeFailure_ExplainsSignatureClashWithoutSuggestingADataWipe()
    {
        const string output = "Failure [INSTALL_FAILED_UPDATE_INCOMPATIBLE: Package com.example.app signatures do not match]";

        var guidance = AndroidInstallDiagnostics.DescribeFailure(output);

        using (Assert.Multiple())
        {
            await Assert.That(guidance).IsNotNull();
            await Assert.That(guidance!).Contains("Uninstall");
            await Assert.That(guidance!).Contains("Install never uninstalls for you");
            await Assert.That(guidance!).Contains("Google Play");
        }
    }

    [Test]
    public async Task DescribeSignatureClash_WarnsThatThePlayCopyMustNotBeUninstalled()
    {
        const string apkDigest = "a32a43cd459f7ce6b82c552f5d876989d53e6af1c0b2d35fb677b51a1dc4f0e5";
        const string installedDigest = "00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff";

        var guidance = AndroidInstallDiagnostics.DescribeSignatureClash(
            [apkDigest],
            [installedDigest],
            "com.android.vending");

        using (Assert.Multiple())
        {
            await Assert.That(guidance).Contains("Google Play");
            await Assert.That(guidance).Contains("App not installed");
            await Assert.That(guidance).Contains(apkDigest);
            await Assert.That(guidance).Contains(installedDigest);
            await Assert.That(guidance).Contains("App integrity");

            // The Play-installed copy must never be steered toward a wipe.
            await Assert.That(guidance).DoesNotContain("press Uninstall");
        }
    }

    [Test]
    public async Task DescribeSignatureClash_KeepsManualUninstallPathForLocalCopies()
    {
        var guidance = AndroidInstallDiagnostics.DescribeSignatureClash(
            ["a32a43cd459f7ce6b82c552f5d876989d53e6af1c0b2d35fb677b51a1dc4f0e5"],
            ["00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff"],
            null);

        using (Assert.Multiple())
        {
            await Assert.That(guidance).Contains("press Uninstall");
            await Assert.That(guidance).Contains("Install never uninstalls for you");
        }
    }

    [Test]
    public async Task DescribeFailure_ExplainsVersionDowngrade()
    {
        const string output = "Failure [INSTALL_FAILED_VERSION_DOWNGRADE: Downgrade detected]";

        await Assert.That(AndroidInstallDiagnostics.DescribeFailure(output)!).Contains("version code");
    }

    [Test]
    public async Task DescribeFailure_ExplainsWrongAbi()
    {
        const string output = "Failure [INSTALL_FAILED_NO_MATCHING_ABIS: Failed to extract native libraries]";

        await Assert.That(AndroidInstallDiagnostics.DescribeFailure(output)!).Contains("android-arm64");
    }

    [Test]
    public async Task DescribeFailure_ReturnsNullForUnknownOutput()
    {
        await Assert.That(AndroidInstallDiagnostics.DescribeFailure("Performing Streamed Install\nSuccess")).IsNull();
        await Assert.That(AndroidInstallDiagnostics.DescribeFailure(null)).IsNull();
    }

    [Test]
    public async Task IsStreamingFailure_OnlyMatchesStreamingProblems()
    {
        using (Assert.Multiple())
        {
            await Assert.That(AndroidInstallDiagnostics.IsStreamingFailure("adb: failed to install: streaming not supported")).IsTrue();
            await Assert.That(AndroidInstallDiagnostics.IsStreamingFailure("Failure [INSTALL_FAILED_UPDATE_INCOMPATIBLE]")).IsFalse();
        }
    }
}
