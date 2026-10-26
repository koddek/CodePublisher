using CodePublisher.Features.Deployment.Android;

namespace CodePublisher.Tests.Features.Deployment;

public sealed class AndroidSigningInspectorTests
{
    private const string DigestA = "a32a43cd459f7ce6b82c552f5d876989d53e6af1c0b2d35fb677b51a1dc4f0e5";
    private const string DigestB = "00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff";

    private const string TypicalApkSignerOutput =
        """
        Signer #1 certificate DN: CN=release, OU=dev, O=example, L=City, ST=ST, C=us
        Signer #1 certificate SHA-256 digest: a32a43cd459f7ce6b82c552f5d876989d53e6af1c0b2d35fb677b51a1dc4f0e5
        Signer #1 certificate SHA-1 digest: 9f86d081884c7d659a2feaa0c55ad015a3bf4f1b
        Signer #1 certificate MD5 digest: 900150983cd24fb0d6963f7d28e17f72
        Verifies
        Verified using v1 scheme (JAR signing): true
        Verified using v2 scheme (APK Signature Scheme v2): true
        """;

    [Test]
    public async Task ParseApkSignerDigests_ReadsSha256AndIgnoresOtherDigests()
    {
        var digests = AndroidSigningInspector.ParseApkSignerDigests(TypicalApkSignerOutput);

        using (Assert.Multiple())
        {
            await Assert.That(digests.Count).IsEqualTo(1);
            await Assert.That(digests[0]).IsEqualTo(DigestA);
        }
    }

    [Test]
    public async Task ParseApkSignerDigests_NormalizesUppercaseColonsAndNextLineValues()
    {
        const string output =
            """
            Signer #1 certificate SHA-256 digest:
            A3:2A:43:CD:45:9F:7C:E6:B8:2C:55:2F:5D:87:69:89:D5:3E:6A:F1:C0:B2:D3:5F:B6:77:B5:1A:1D:C4:F0:E5
            Signer #2 certificate SHA-256 digest: 00112233445566778899AABBCCDDEEFF00112233445566778899AABBCCDDEEFF
            """;

        var digests = AndroidSigningInspector.ParseApkSignerDigests(output);

        using (Assert.Multiple())
        {
            await Assert.That(digests.Count).IsEqualTo(2);
            await Assert.That(digests).Contains(DigestA);
            await Assert.That(digests).Contains(DigestB);
        }
    }

    [Test]
    public async Task ParseApkSignerDigests_ReturnsEmptyForMissingOrInvalidOutput()
    {
        using (Assert.Multiple())
        {
            await Assert.That(AndroidSigningInspector.ParseApkSignerDigests(null).Count).IsEqualTo(0);
            await Assert.That(AndroidSigningInspector.ParseApkSignerDigests("  ").Count).IsEqualTo(0);
            await Assert.That(AndroidSigningInspector.ParseApkSignerDigests("Performing Streamed Install\nSuccess").Count).IsEqualTo(0);
            await Assert.That(AndroidSigningInspector.ParseApkSignerDigests("Signer #1 certificate SHA-256 digest: not-a-digest").Count).IsEqualTo(0);
        }
    }

    [Test]
    public async Task CertificatesMatch_ComparesNormalizedSets()
    {
        using (Assert.Multiple())
        {
            await Assert.That(AndroidSigningInspector.CertificatesMatch([DigestA], [DigestA])).IsTrue();
            await Assert.That(AndroidSigningInspector.CertificatesMatch(
                ["00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF:00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF"],
                [DigestB])).IsTrue();
            await Assert.That(AndroidSigningInspector.CertificatesMatch([DigestA], [DigestB])).IsFalse();
            await Assert.That(AndroidSigningInspector.CertificatesMatch([DigestA], [])).IsFalse();
            await Assert.That(AndroidSigningInspector.CertificatesMatch(null, [DigestA])).IsFalse();
            await Assert.That(AndroidSigningInspector.CertificatesMatch([DigestA], null)).IsFalse();
        }
    }

    [Test]
    public async Task ParseBaseApkPath_PrefersBaseApkAmongSplits()
    {
        const string pmPathOutput =
            """
            package:/data/app/com.example.app-AbCdEf==/split_config.arm64_v8a.apk
            package:/data/app/com.example.app-AbCdEf==/split_config.en.apk
            package:/data/app/com.example.app-AbCdEf==/base.apk
            """;

        var path = AndroidSigningInspector.ParseBaseApkPath(pmPathOutput);

        await Assert.That(path).IsEqualTo("/data/app/com.example.app-AbCdEf==/base.apk");
    }

    [Test]
    public async Task ParseBaseApkPath_ReturnsNullWhenPackageIsNotInstalled()
    {
        using (Assert.Multiple())
        {
            await Assert.That(AndroidSigningInspector.ParseBaseApkPath(string.Empty)).IsNull();
            await Assert.That(AndroidSigningInspector.ParseBaseApkPath("Error: could not find package: com.example.app")).IsNull();
            await Assert.That(AndroidSigningInspector.ParseBaseApkPath(null)).IsNull();
        }
    }

    [Test]
    public async Task ParseInstallerPackage_ReadsStoreAttribution()
    {
        using (Assert.Multiple())
        {
            await Assert.That(AndroidSigningInspector.ParseInstallerPackage(
                "package:com.example.app installer=com.android.vending",
                "com.example.app")).IsEqualTo("com.android.vending");
            await Assert.That(AndroidSigningInspector.ParseInstallerPackage(
                "package:com.example.app installer=null",
                "com.example.app")).IsNull();
            await Assert.That(AndroidSigningInspector.ParseInstallerPackage(
                "package:com.example.app  installer=com.example.distributor",
                "com.example.app")).IsEqualTo("com.example.distributor");

            // pm filters match substrings; only the exact package line may be used.
            await Assert.That(AndroidSigningInspector.ParseInstallerPackage(
                "package:com.example.app.debug installer=com.android.vending",
                "com.example.app")).IsNull();
            await Assert.That(AndroidSigningInspector.ParseInstallerPackage(
                "package:com.other.app installer=com.android.vending",
                "com.example.app")).IsNull();
            await Assert.That(AndroidSigningInspector.ParseInstallerPackage(null, "com.example.app")).IsNull();
        }
    }

    [Test]
    public async Task IsGooglePlayInstaller_MatchesOnlyPlayStore()
    {
        using (Assert.Multiple())
        {
            await Assert.That(AndroidSigningInspector.IsGooglePlayInstaller("com.android.vending")).IsTrue();
            await Assert.That(AndroidSigningInspector.IsGooglePlayInstaller("Com.Android.Vending")).IsTrue();
            await Assert.That(AndroidSigningInspector.IsGooglePlayInstaller("com.example.distributor")).IsFalse();
            await Assert.That(AndroidSigningInspector.IsGooglePlayInstaller(null)).IsFalse();
        }
    }
}
