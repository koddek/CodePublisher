using System.Security.Cryptography;

namespace DotNetAppPublisher.Features.Deployment.Android;

/// <summary>
/// One Wireless debugging pairing invitation made by this computer. The payload is shown as a QR code;
/// the phone that reads it starts its pairing server under <see cref="ServiceName"/> and takes
/// <see cref="Password"/> as the shared secret, which is why the computer can then pair without a code
/// being typed on either side.
/// </summary>
public sealed record AndroidQrPairingRequest(string ServiceName, string Password, string Payload);

/// <summary>
/// The Android "Pair device with QR code" format:
/// <c>WIFI:T:ADB;S:&lt;service name&gt;;P:&lt;password&gt;;;</c> (packages/apps/Settings `WifiQrCode`).
/// `S` is the mDNS instance name the phone advertises under `_adb-tls-pairing._tcp`, `P` is the pairing
/// secret the host sends with `adb pair`. Pure: no adb, no network, no clock.
/// </summary>
public static class AndroidQrPairing
{
    /// <summary>Prefix every QR payload must carry for the phone to treat it as ADB pairing.</summary>
    public const string PayloadPrefix = "WIFI:T:ADB";

    /// <summary>
    /// Instance name asked of the phone. Uses the standard `studio-` prefix Android Studio uses, which
    /// every Android vendor build and QR scanner recognizes and echoes in mDNS advertisements.
    /// </summary>
    public const string ServiceNamePrefix = "studio-";

    /// <summary>
    /// Length of the generated secret. AOSP's architecture document shows a 12-character non-numeric
    /// example (`(Aq+v9>Cx>!/`), which is the shape Android Studio generates, so alphanumerics of the
    /// same length satisfy both the documented example and a digits-only parser.
    /// </summary>
    public const int PasswordLength = 12;

    // Ambiguous characters are left out: the secret is also shown in the UI for manual entry when a
    // camera cannot read the code.
    private const string PasswordAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
    private const string ServiceNameAlphabet = "abcdefghijklmnopqrstuvwxyz0123456789";

    /// <summary>Creates an invitation. Pass explicit values only to reproduce a specific code.</summary>
    public static AndroidQrPairingRequest Create(string? serviceName = null, string? password = null)
    {
        var name = string.IsNullOrWhiteSpace(serviceName)
            ? ServiceNamePrefix + RandomToken(8, ServiceNameAlphabet)
            : serviceName.Trim();
        var secret = string.IsNullOrWhiteSpace(password) ? RandomToken(PasswordLength, PasswordAlphabet) : password;
        return new AndroidQrPairingRequest(name, secret, BuildPayload(name, secret));
    }

    /// <summary>Builds the QR payload exactly as the phone's QR reader expects it.</summary>
    public static string BuildPayload(string serviceName, string password) =>
        $"{PayloadPrefix};S:{serviceName.Trim()};P:{password};;";

    /// <summary>
    /// Validates a pairing secret (`P` in the payload, `PAIRING CODE` argument of `adb pair`). Unlike the
    /// 6-digit code shown on the device, a QR secret is a short passphrase, so letters are allowed while
    /// characters that would break the QR field separators are not.
    /// </summary>
    public static bool TryNormalizeSecret(string? input, out string secret, out string error)
    {
        secret = string.Empty;
        error = string.Empty;

        var candidate = input?.Trim() ?? string.Empty;
        if (candidate.Length == 0)
        {
            error = "Enter the pairing password shown with the QR code.";
            return false;
        }

        if (candidate.Length is < 4 or > 64)
        {
            error = "The pairing password is 4 to 64 characters long.";
            return false;
        }

        if (candidate.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)
                                       || character is ';' or '\\' or '"'))
        {
            error = "The pairing password cannot contain spaces, quotes, backslashes, or semicolons.";
            return false;
        }

        secret = candidate;
        return true;
    }

    /// <summary>
    /// Whether one advertised instance name belongs to the invitation this computer made. Android
    /// leaves the requested name alone, but a few vendor builds advertise it as
    /// `adb-&lt;serial&gt;-&lt;requested name&gt;`, so a suffix match is accepted as well; the random
    /// part keeps that from matching anything else.
    /// </summary>
    public static bool NameMatchesService(string? advertisedName, string? requestedName)
    {
        var advertised = advertisedName?.Trim().TrimEnd('.') ?? string.Empty;
        var requested = requestedName?.Trim() ?? string.Empty;
        if (advertised.Length == 0 || requested.Length == 0)
        {
            return false;
        }

        return string.Equals(advertised, requested, StringComparison.OrdinalIgnoreCase)
            || (requested.Length >= 6 && advertised.EndsWith(requested, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Whether an advertised service is the one this invitation asked for.
    /// </summary>
    public static bool IsMatch(AndroidWirelessService service, string serviceName)
    {
        if (!service.IsPairingService || string.IsNullOrWhiteSpace(serviceName))
        {
            return false;
        }

        return NameMatchesService(service.Name, serviceName);
    }

    /// <summary>Finds the pairing service this invitation is waiting for, preferring an exact name.</summary>
    public static bool TryFindPairingService(
        IReadOnlyList<AndroidWirelessService> services,
        string serviceName,
        out AndroidWirelessService? match)
    {
        match = services
            .Where(service => IsMatch(service, serviceName))
            .OrderByDescending(service => string.Equals(
                service.Name.Trim().TrimEnd('.'),
                serviceName.Trim(),
                StringComparison.OrdinalIgnoreCase))
            .ThenBy(service => service.Address, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        return match is not null;
    }

    /// <summary>
    /// Finds the pairing service an invitation is waiting for, from a snapshot of what was advertised
    /// before the code was shown.
    ///
    /// The exact requested name wins. Failing that, a pairing service that was not advertised before
    /// the attempt began is accepted: a pairing server exists only while a phone has its pairing dialog
    /// open, so a new one appeared because a phone just scanned this code. That keeps pairing working
    /// on builds that ignore or rewrite the requested instance name, where waiting for the name alone
    /// would end in a timeout even though the phone did everything right.
    /// </summary>
    public static bool TryFindPairingService(
        IReadOnlyList<AndroidWirelessService> services,
        string serviceName,
        IReadOnlyCollection<string>? pairingServicesSeenBefore,
        out AndroidWirelessService? match)
    {
        if (TryFindPairingService(services, serviceName, out match))
        {
            return true;
        }

        if (pairingServicesSeenBefore is null)
        {
            return false;
        }

        match = services
            .Where(service => service.IsPairingService)
            .Where(service => !pairingServicesSeenBefore.Contains(service.Name, StringComparer.OrdinalIgnoreCase))
            .OrderBy(service => service.Address, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        return match is not null;
    }

    private static string RandomToken(int length, string alphabet)
    {
        var characters = new char[length];
        for (var index = 0; index < length; index++)
        {
            characters[index] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        }

        return new string(characters);
    }
}
