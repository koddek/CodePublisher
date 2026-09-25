using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DotNetAppPublisher.Features.GooglePlay.Models;

namespace DotNetAppPublisher.Features.GooglePlay.Signing;

internal sealed partial class AndroidSigningKeyService
{
    public async Task<GooglePlaySigningKeyInfo> CreateAsync(
        string path,
        string alias,
        string storePassword,
        string keyPassword,
        string ownerName,
        CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsBrowser())
        {
            throw new PlatformNotSupportedException("Android signing key creation is available in the desktop app only.");
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("Choose where the upload keystore should be created.");
        }

        if (File.Exists(path))
        {
            throw new InvalidOperationException("The selected keystore already exists. Choose a new file or inspect the existing key.");
        }

        var keytool = ResolveKeytool();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var arguments = new List<string>
        {
            "-genkeypair",
            "-keystore", path,
            "-storetype", "PKCS12",
            "-storepass:env", "DOTNET_APP_PUBLISHER_KEYSTORE_PASSWORD",
            "-keypass:env", "DOTNET_APP_PUBLISHER_KEY_PASSWORD",
            "-alias", alias,
            "-keyalg", "RSA",
            "-keysize", "4096",
            "-sigalg", "SHA256withRSA",
            "-validity", "10000",
            "-dname", BuildDistinctName(ownerName)
        };
        var (exitCode, output) = await RunAsync(
            keytool,
            arguments,
            new Dictionary<string, string>
            {
                ["DOTNET_APP_PUBLISHER_KEYSTORE_PASSWORD"] = storePassword,
                ["DOTNET_APP_PUBLISHER_KEY_PASSWORD"] = keyPassword
            },
            cancellationToken);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"Android signing key creation failed: {output}");
        }

        return await InspectAsync(path, alias, storePassword, cancellationToken);
    }

    public async Task<GooglePlaySigningKeyInfo> InspectAsync(
        string path,
        string alias,
        string storePassword,
        CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsBrowser())
        {
            throw new PlatformNotSupportedException("Android signing key inspection is available in the desktop app only.");
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The selected Android keystore does not exist.", path);
        }

        var keytool = ResolveKeytool();
        var arguments = new List<string>
        {
            "-list", "-v",
            "-keystore", path,
            "-storetype", "PKCS12",
            "-storepass:env", "DOTNET_APP_PUBLISHER_KEYSTORE_PASSWORD",
            "-alias", alias
        };
        var (exitCode, output) = await RunAsync(
            keytool,
            arguments,
            new Dictionary<string, string>
            {
                ["DOTNET_APP_PUBLISHER_KEYSTORE_PASSWORD"] = storePassword
            },
            cancellationToken);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"The Android keystore could not be opened: {output}");
        }

        return new GooglePlaySigningKeyInfo(
            path,
            alias,
            StoreTypePattern().Match(output) is { Success: true } storeMatch ? storeMatch.Groups[1].Value : "PKCS12",
            AlgorithmPattern().Match(output) is { Success: true } algorithmMatch ? algorithmMatch.Groups[1].Value : "Unknown",
            int.TryParse(KeySizePattern().Match(output) is { Success: true } sizeMatch
                ? sizeMatch.Groups[1].Value
                : string.Empty, out var keySize)
                ? keySize
                : 0,
            FingerprintPattern("SHA1").Match(output) is { Success: true } sha1 ? FormatFingerprint(sha1.Groups[1].Value) : "Unavailable",
            FingerprintPattern("SHA256").Match(output) is { Success: true } sha256 ? FormatFingerprint(sha256.Groups[1].Value) : "Unavailable",
            DateTimeOffset.TryParse(CreatedAtPattern().Match(output) is { Success: true } created
                ? created.Groups[1].Value
                : string.Empty, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var createdAt)
                ? createdAt
                : null,
            DateTimeOffset.TryParse(ExpiresAtPattern().Match(output) is { Success: true } expires
                ? expires.Groups[1].Value
                : string.Empty, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var expiresAt)
                ? expiresAt
                : null);
    }

    public string CreateStrongPassword()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', 'A')
            .Replace('/', 'B')
            .TrimEnd('=');
    }

    private static string ResolveKeytool()
    {
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome))
        {
            var candidate = Path.Combine(javaHome, "bin", OperatingSystem.IsWindows() ? "keytool.exe" : "keytool");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return OperatingSystem.IsWindows() ? "keytool.exe" : "keytool";
    }

    private static string BuildDistinctName(string ownerName)
    {
        var safeName = string.IsNullOrWhiteSpace(ownerName) ? ".NET App Publisher" : ownerName.Trim();
        return $"CN={safeName}, OU=Android, O={safeName}, L=NA, ST=NA, C=US";
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var pair in environment)
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start keytool.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return (process.ExitCode, await outputTask + await errorTask);
    }

    private static string FormatFingerprint(string value) =>
        value.Replace(":", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant()
            .Chunk(2)
            .Select(chunk => new string(chunk))
            .Aggregate(new StringBuilder(), (builder, chunk) => builder.Append(chunk).Append(':'))
            .ToString()
            .TrimEnd(':');

    [GeneratedRegex(@"Keystore type:\s*(.+)", RegexOptions.IgnoreCase)]
    private static partial Regex StoreTypePattern();
    [GeneratedRegex(@"Algorithm:\s*(.+)", RegexOptions.IgnoreCase)]
    private static partial Regex AlgorithmPattern();
    [GeneratedRegex(@"key size is\s*(\d+)\s*bit", RegexOptions.IgnoreCase)]
    private static partial Regex KeySizePattern();
    [GeneratedRegex("SHA1:\\s*([0-9A-F: ]+)", RegexOptions.IgnoreCase)]
    private static partial Regex Sha1FingerprintPattern();
    [GeneratedRegex("SHA256:\\s*([0-9A-F: ]+)", RegexOptions.IgnoreCase)]
    private static partial Regex Sha256FingerprintPattern();
    [GeneratedRegex(@"Created on:\s*(.+)", RegexOptions.IgnoreCase)]
    private static partial Regex CreatedAtPattern();
    [GeneratedRegex(@"Valid until:\s*(.+)", RegexOptions.IgnoreCase)]
    private static partial Regex ExpiresAtPattern();
    private static Regex FingerprintPattern(string name) =>
        name == "SHA1" ? Sha1FingerprintPattern() : Sha256FingerprintPattern();
}
