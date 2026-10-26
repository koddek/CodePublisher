namespace DotNetAppPublisher.Features.Deployment.Android;

/// <summary>
/// Pure parsers for the native mDNS resolver output used when the adb server's own mDNS backend
/// cannot see the network.
///
/// On some hosts (observed on macOS 26 with platform-tools 36.x) the adb server's mDNS discovery
/// returns nothing at all, while the operating system's own mDNS responder sees the same service.
/// That makes QR pairing hang: the phone scans the code, starts its pairing server, and waits
/// forever because the host never runs <c>adb pair</c>. These parsers read the OS resolver instead.
/// Pure: no process, no network.
/// </summary>
public static class AndroidMdnsResolve
{
    /// <summary>macOS ships this resolver as <c>dns-sd</c>.</summary>
    public const string DnsSd = "dns-sd";

    /// <summary>The two service types wireless debugging advertises.</summary>
    public const string PairingServiceType = "_adb-tls-pairing._tcp";
    public const string ConnectServiceType = "_adb-tls-connect._tcp";

    private const string ReachedAt = "can be reached at ";

    /// <summary>
    /// Reads one <c>dns-sd -L</c> line and returns the resolved host and port.
    /// The line looks like:
    /// <c>13:03:14.995  name._adb-tls-pairing._tcp.local. can be reached at MacBook-Air.local.:39411 (interface 22)</c>
    /// Returns false when the line is a banner, a failure, or carries no address yet.
    /// </summary>
    public static bool TryParseResolveLine(string? line, out string host, out int port)
    {
        host = string.Empty;
        port = 0;

        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var start = line.IndexOf(ReachedAt, StringComparison.Ordinal);
        if (start < 0)
        {
            return false;
        }

        var rest = line[(start + ReachedAt.Length)..];

        // The address runs until the trailing " (interface n)" / " (interface n) Flags: n" part.
        var end = rest.IndexOf(" (interface", StringComparison.Ordinal);
        var address = (end >= 0 ? rest[..end] : rest).Trim().TrimEnd('.');
        if (address.Length == 0)
        {
            return false;
        }

        var separator = address.LastIndexOf(':');
        if (separator <= 0 || separator == address.Length - 1)
        {
            return false;
        }

        host = address[..separator].Trim().TrimEnd('.');
        return host.Length > 0
            && int.TryParse(address[(separator + 1)..], out port)
            && port is > 0 and <= 65535;
    }

    /// <summary>
    /// Reads one <c>dns-sd -G v4</c> line and returns the IPv4 address it reports. The line looks like:
    /// <c>13:06:35.889  Add  40000003  13  MacBook-Air.local.  192.168.50.158  4500</c>
    /// </summary>
    public static bool TryParseAddressLine(string? line, out string address)
    {
        address = string.Empty;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        // Only "Add" rows carry an address; "Rmv" rows drop one.
        var tokens = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 6 || !string.Equals(tokens[1], "Add", StringComparison.Ordinal))
        {
            return false;
        }

        var candidate = tokens[5].Trim().TrimEnd('.');
        if (IsIpv4(candidate))
        {
            address = candidate;
            return true;
        }

        // Defensive: the hostname column can contain a space on some builds, so fall back to
        // the last token that parses as an IPv4 rather than trusting a fixed column.
        foreach (var token in tokens)
        {
            if (IsIpv4(token))
            {
                address = token;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Picks the address the host can actually reach: never loopback, never link-local. When several
    /// remain, the first wins, so the caller should feed them in interface order.
    /// </summary>
    public static string? PickBestIpv4(IEnumerable<string> addresses)
    {
        foreach (var address in addresses)
        {
            if (!IsIpv4(address))
            {
                continue;
            }

            var octets = address.Split('.');
            var first = byte.TryParse(octets[0], out var a) ? a : (byte)0;
            var second = byte.TryParse(octets[1], out var b) ? b : (byte)0;
            if (first == 127)
            {
                continue; // loopback
            }

            if (first == 169 && second == 254)
            {
                continue; // link-local, auto-assigned when DHCP failed
            }

            return address;
        }

        return null;
    }

    private static bool IsIpv4(string value) =>
        value.Split('.') is { Length: 4 } parts
        && parts.All(part => byte.TryParse(part, out _));

    /// <summary>
    /// Reads one <c>dns-sd -B</c> line and returns the instance name it reports. The columns are
    /// <c>Timestamp A/R Flags if Domain Service Type Instance Name</c>, so a data row looks like
    /// <c>0:50:23.651  Add  2  13  local.  _adb-tls-pairing._tcp.  dotnetapppublisher-test1234</c>.
    ///
    /// The instance name is every column after the service type, because a newer phone appends the
    /// service version to it: <c>adb-R5CN20DFRKA-0z8hFB (2)</c>. Taking only the last column would
    /// return the marker and lose the name. <c>Rmv</c> rows report an instance going away and are
    /// ignored, and only wireless debugging service types are accepted, so an unrelated service on
    /// the network is never picked up.
    /// </summary>
    public static bool TryParseBrowseLine(string? line, out bool added, out string instanceName)
    {
        added = false;
        instanceName = string.Empty;

        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var tokens = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 7)
        {
            return false;
        }

        if (string.Equals(tokens[1], "Add", StringComparison.Ordinal))
        {
            added = true;
        }
        else if (string.Equals(tokens[1], "Rmv", StringComparison.Ordinal))
        {
            added = false;
        }
        else
        {
            return false;
        }

        var serviceType = tokens[5].TrimEnd('.');
        if (!string.Equals(serviceType, PairingServiceType, StringComparison.Ordinal)
            && !string.Equals(serviceType, ConnectServiceType, StringComparison.Ordinal))
        {
            return false;
        }

        var name = string.Join(' ', tokens.Skip(6)).Trim().TrimEnd('.');
        if (name.Length == 0)
        {
            return false;
        }

        instanceName = name;
        return added;
    }

    /// <summary>
    /// Splits the service version off an advertised instance name: <c>adb-R5CN20DFRKA-0z8hFB (2)</c>
    /// becomes the name <c>adb-R5CN20DFRKA-0z8hFB</c> with version 2. A name without the marker keeps
    /// itself and no version.
    /// </summary>
    public static bool TryTrimVersionMarker(string? instanceName, out string name, out int? version)
    {
        name = string.Empty;
        version = null;

        var candidate = instanceName?.Trim().TrimEnd('.') ?? string.Empty;
        if (candidate.EndsWith(')'))
        {
            var separator = candidate.LastIndexOf(' ');
            if (separator > 0
                && candidate[separator + 1] == '('
                && int.TryParse(candidate[(separator + 2)..^1], out var parsedVersion))
            {
                version = parsedVersion;
                candidate = candidate[..separator].Trim();
            }
        }

        name = candidate;
        return name.Length > 0;
    }
}
