using System.Diagnostics;
using DotNetAppPublisher.Features.GooglePlay.Models;

namespace DotNetAppPublisher.Features.GooglePlay.Setup;

internal sealed class EnvironmentVariableService
{
    private const string GoogleClientIdVariableName = "DOTNET_APP_PUBLISHER_GOOGLE_CLIENT_ID";

    public string? GoogleClientId => GetState(GoogleClientIdVariableName).Value;

    public bool IsGoogleClientIdSet => !string.IsNullOrWhiteSpace(GoogleClientId);

    public bool SupportsPersistentUserVariables => !OperatingSystem.IsBrowser() &&
        (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS());

    public string GoogleClientIdStatus => GetState(GoogleClientIdVariableName).Status;

    public GooglePlayEnvironmentVariableState GetState(string name)
    {
        var processValue = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process);
        var userValue = GetUserValue(name);
        var value = !string.IsNullOrWhiteSpace(processValue) ? processValue : userValue;
        var source = !string.IsNullOrWhiteSpace(processValue)
            ? "Configured for this app process"
            : !string.IsNullOrWhiteSpace(userValue)
                ? "Configured for this user profile"
                : "Not configured";
        return new GooglePlayEnvironmentVariableState(name, value, source, !string.IsNullOrWhiteSpace(value));
    }

    private static string? GetUserValue(string name)
    {
        if (OperatingSystem.IsBrowser())
        {
            return null;
        }

        if (OperatingSystem.IsWindows())
        {
            return Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);
        }

        if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var profilePath = string.IsNullOrWhiteSpace(home) ? null : Path.Combine(home, ".zprofile");
            if (profilePath is null || !File.Exists(profilePath))
            {
                return null;
            }

            var prefix = $"export {name}=";
            var line = File.ReadLines(profilePath)
                .LastOrDefault(line => line.TrimStart().StartsWith(prefix, StringComparison.Ordinal));
            if (line is null)
            {
                return null;
            }

            var separatorIndex = line.IndexOf('=', StringComparison.Ordinal);
            var value = line[(separatorIndex + 1)..].Trim();
            return value.Length >= 2 && value[0] == '\'' && value[^1] == '\''
                ? value[1..^1].Replace("'\\''", "'", StringComparison.Ordinal)
                : value;
        }

        return null;
    }

    public void SetForSession(string name, string? value)
    {
        ValidateName(name);
        if (OperatingSystem.IsBrowser())
        {
            return;
        }

        Environment.SetEnvironmentVariable(name, string.IsNullOrWhiteSpace(value) ? null : value.Trim(), EnvironmentVariableTarget.Process);
    }

    public IReadOnlyList<GooglePlayEnvironmentVariableDefinition> GetKnownVariableDefinitions() =>
    [
        new(
            GoogleClientIdVariableName,
            "Public Google Desktop OAuth client ID. No client secret is needed.",
            true,
            false,
            "Google Desktop OAuth client ID",
            "1234567890-abc.apps.googleusercontent.com",
            "In Google Cloud Console, select the project linked to your Play Console app, open APIs & Services → Credentials → Create credentials → OAuth client ID, and select Desktop app. Copy the Client ID; do not enter a client secret.",
            "https://console.cloud.google.com/apis/credentials"),
        new(
            "JAVA_HOME",
            "Optional Java home used to locate keytool when it is not on PATH.",
            false,
            false,
            "Java home",
            "/Library/Java/JavaVirtualMachines/temurin-21.jdk/Contents/Home",
            "Install a JDK 17 or 21 (Temurin is a clear choice). On macOS run /usr/libexec/java_home -v 21 and use the printed folder. On Windows use the JDK folder, for example C:\\Program Files\\Java\\jdk-21. Leave this blank when keytool is already on PATH.",
            "https://adoptium.net/temurin/releases/"),
        new(
            "ANDROID_HOME",
            "Optional Android SDK location. ANDROID_SDK_ROOT is also supported by the publisher.",
            false,
            false,
            "Android SDK location",
            "/Users/you/Library/Android/sdk",
            "Install Android Studio and open SDK Manager. Use the SDK folder shown at the top: macOS ~/Library/Android/sdk, Windows %LOCALAPPDATA%\\Android\\Sdk, or Linux ~/Android/Sdk. If your setup uses ANDROID_SDK_ROOT, enter that same SDK folder here; the publisher also reads ANDROID_SDK_ROOT.",
            "https://developer.android.com/studio")
    ];

    public void SetForUser(string name, string? value)
    {
        ValidateName(name);
        if (OperatingSystem.IsBrowser())
        {
            throw new PlatformNotSupportedException("Persistent environment variables are available in the desktop app only.");
        }

        var normalizedValue = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (OperatingSystem.IsWindows())
        {
            Environment.SetEnvironmentVariable(name, normalizedValue, EnvironmentVariableTarget.User);
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            SetMacLoginEnvironmentVariable(name, normalizedValue);
            return;
        }

        throw new PlatformNotSupportedException("Saving user environment variables is supported on Windows and macOS in this app. Use the session button on Linux.");
    }

    private static void SetMacLoginEnvironmentVariable(string name, string? value)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            throw new InvalidOperationException("The macOS user profile folder could not be found.");
        }

        var profilePath = Path.Combine(home, ".zprofile");
        var marker = $"export {name}=";
        var existingLines = File.Exists(profilePath)
            ? File.ReadAllLines(profilePath)
            : [];
        var updatedLines = existingLines
            .Where(line => !line.TrimStart().StartsWith(marker, StringComparison.Ordinal))
            .ToList();
        if (value is not null)
        {
            updatedLines.Add($"{marker}{ShellQuote(value)}");
        }

        File.WriteAllLines(profilePath, updatedLines);
    }

    private static string ShellQuote(string value) => $"'{value.Replace("'", "'\\''", StringComparison.Ordinal)}'";

    public void RemoveForSession(string name)
    {
        ValidateName(name);
        if (OperatingSystem.IsBrowser())
        {
            return;
        }

        Environment.SetEnvironmentVariable(name, null, EnvironmentVariableTarget.Process);
    }

    public void RemoveForUser(string name)
    {
        ValidateName(name);
        if (OperatingSystem.IsBrowser())
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            Environment.SetEnvironmentVariable(name, null, EnvironmentVariableTarget.User);
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            SetMacLoginEnvironmentVariable(name, null);
            return;
        }

        throw new PlatformNotSupportedException("Removing user environment variables is supported on Windows and macOS in this app. Use the session button on Linux.");
    }

    public void OpenEnvironmentVariableHelp()
    {
        OpenUrl("https://developers.google.com/android-publisher/getting_started");
    }

    public void OpenGoogleCloudCredentials()
    {
        OpenUrl("https://console.cloud.google.com/apis/credentials");
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("Environment variable names cannot contain whitespace.", nameof(name));
        }
    }

    internal static void OpenUrl(string url)
    {
        if (OperatingSystem.IsBrowser())
        {
            return;
        }

        var startInfo = OperatingSystem.IsMacOS()
            ? new ProcessStartInfo("open", url) { UseShellExecute = true }
            : OperatingSystem.IsWindows()
                ? new ProcessStartInfo(url) { UseShellExecute = true }
                : new ProcessStartInfo("xdg-open", url) { UseShellExecute = true };
        _ = Process.Start(startInfo);
    }
}

internal sealed record GooglePlayEnvironmentVariableState(
    string Name,
    string? Value,
    string Status,
    bool IsSet);
