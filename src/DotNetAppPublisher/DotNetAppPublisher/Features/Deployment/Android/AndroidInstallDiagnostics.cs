namespace DotNetAppPublisher.Features.Deployment.Android;

/// <summary>
/// Turns raw `adb install` output into the next action a user can actually take. Pure so the mapping
/// is unit tested instead of discovered on a real phone.
/// </summary>
public static class AndroidInstallDiagnostics
{
    /// <summary>Returns guidance for a known Package Manager failure, or null when nothing is recognised.</summary>
    public static string? DescribeFailure(string? adbOutput)
    {
        if (string.IsNullOrWhiteSpace(adbOutput))
        {
            return null;
        }

        if (Contains(adbOutput, "INSTALL_FAILED_UPDATE_INCOMPATIBLE"))
        {
            return "The app already installed on this device is signed with a different key than this APK, " +
                "so Android will not update it in place. Install never uninstalls for you: press Uninstall " +
                "(this deletes the app and its data), then Install to do a clean install. If that copy came " +
                "from Google Play, do not uninstall it: a replacement signed with a different key can never " +
                "be updated by Google Play again, so the next Play Store update fails. Publish with the " +
                "signing key Play uses (its SHA-256 fingerprint is shown in Play Console > Release > App " +
                "integrity) and install that build instead.";
        }


        if (Contains(adbOutput, "INSTALL_FAILED_VERSION_DOWNGRADE"))
        {
            return "This APK has a lower version code than the app already on the device. Raise the version " +
                "code (Directory.Version.props / ApplicationVersion) and install again, or uninstall the newer " +
                "build first (that deletes its data).";
        }

        if (Contains(adbOutput, "INSTALL_FAILED_INSUFFICIENT_STORAGE"))
        {
            return "The device does not have enough free space for this APK. Free up space on the device and try again.";
        }

        if (Contains(adbOutput, "INSTALL_FAILED_NO_MATCHING_ABIS"))
        {
            return "This APK has no native libraries for the device's CPU. For a real phone, publish with " +
                "`android-arm64`; use `android-x64` only for emulators and x86 tablets.";
        }

        if (Contains(adbOutput, "INSTALL_FAILED_OLDER_SDK"))
        {
            return "The device's Android version is older than this APK supports.";
        }

        if (Contains(adbOutput, "INSTALL_FAILED_TEST_ONLY"))
        {
            return "This APK is marked test-only, so Android refuses a normal install. Publish without the " +
                "test-only flag (a Release publish is not test-only).";
        }

        if (Contains(adbOutput, "INSTALL_FAILED_USER_RESTRICTED"))
        {
            return "The device blocks installs over USB for this user. Enable Install via USB in Developer " +
                "options (some OEMs also need the SIM/eSIM restriction lifted).";
        }

        if (Contains(adbOutput, "INSTALL_FAILED_VERIFICATION_FAILURE"))
        {
            return "The device's verification (Play Protect) blocked this APK. Allow the install on the device, " +
                "or temporarily disable Play Protect scanning, then install again.";
        }

        if (Contains(adbOutput, "INSTALL_PARSE_FAILED"))
        {
            return "Android could not parse this APK. Rebuild it (the file may be truncated or unsigned).";
        }

        if (Contains(adbOutput, "device offline"))
        {
            return "The device reported `offline` during the install. Reconnect the cable or run `adb reconnect` and try again.";
        }

        if (Contains(adbOutput, "device unauthorized"))
        {
            return "The device has not authorized this computer. Approve the `Allow USB debugging?` prompt on the device, then install again.";
        }

        if (Contains(adbOutput, "not enough space"))
        {
            return "The device ran out of space while installing. Free up space and try again.";
        }

        return null;
    }

    /// <summary>
    /// Guidance for a signature clash found by the install preflight, before anything is written to
    /// the device. The Google Play installer case must never steer toward uninstall: replacing the
    /// Play copy with a differently-signed build destroys its data and permanently breaks Play
    /// Store updates for that package.
    /// </summary>
    public static string DescribeSignatureClash(
        IReadOnlyList<string> apkDigests,
        IReadOnlyList<string> installedDigests,
        string? installerPackage)
    {
        var apkFingerprint = string.Join(", ", apkDigests);
        var installedFingerprint = string.Join(", ", installedDigests);
        var fromGooglePlay = AndroidSigningInspector.IsGooglePlayInstaller(installerPackage);

        var guidance = "This APK is signed with a different key than the app already installed on the device, " +
            "so Android refuses to update it (INSTALL_FAILED_UPDATE_INCOMPATIBLE). " +
            "Install never uninstalls for you.";

        if (fromGooglePlay)
        {
            guidance += " The installed copy came from Google Play, which signs downloads with the app's Play " +
                "signing key. Uninstalling it would delete its data and leave a build Google Play can never " +
                "update: every future Play Store update fails with \"App not installed\".";
        }

        guidance += " To keep Play Store updates working, install an APK signed with the same certificate Play " +
            "delivers: publish with Signing mode Sign and the keystore registered in Play Console, and compare " +
            "its fingerprint with Play Console > Release > App integrity." +
            $" APK certificate SHA-256: {apkFingerprint}." +
            $" Installed app certificate SHA-256: {installedFingerprint}.";

        if (!fromGooglePlay)
        {
            guidance += " If the installed copy is an old local build you do not need, press Uninstall " +
                "(this deletes the app and its data), then Install.";
        }

        return guidance;
    }

    /// <summary>
    /// True when adb could not stream the APK into Package Manager, which older devices reject. The
    /// install is retried once with `--no-streaming`, which copies the APK first.
    /// </summary>
    public static bool IsStreamingFailure(string? adbOutput) =>
        Contains(adbOutput, "streaming") || Contains(adbOutput, "--streaming not supported");

    private static bool Contains(string? value, string candidate) =>
        value is not null && value.Contains(candidate, StringComparison.OrdinalIgnoreCase);
}
