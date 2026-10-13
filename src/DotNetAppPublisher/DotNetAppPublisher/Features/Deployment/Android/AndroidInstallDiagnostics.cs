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
                "(this deletes the app and its data), then Install to do a clean install, or publish with the " +
                "same signing key that was used before.";
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
    /// True when adb could not stream the APK into Package Manager, which older devices reject. The
    /// install is retried once with `--no-streaming`, which copies the APK first.
    /// </summary>
    public static bool IsStreamingFailure(string? adbOutput) =>
        Contains(adbOutput, "streaming") || Contains(adbOutput, "--streaming not supported");

    private static bool Contains(string? value, string candidate) =>
        value is not null && value.Contains(candidate, StringComparison.OrdinalIgnoreCase);
}
