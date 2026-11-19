using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;

namespace GSS.Util.Validation;

/// <summary>
/// Validates configured external service base URLs (SSO, MIS, ASM) against SSRF-prone targets.
/// </summary>
public static class UrlValidator
{
    /// <summary>Host names that always mean loopback (compared ordinal, ignore case).</summary>
    private static readonly string[] LoopbackHostNames =
    [
        "localhost",
        "localhost.localdomain",
        "ip6-localhost",
        "ip6-loopback"
    ];

    /// <summary>
    /// True for an absolute HTTPS URL whose host is not loopback, unspecified, private or link-local.
    /// Config-time check only: DNS names are not resolved.
    /// </summary>
    public static bool IsValidExternalUrl(string? url, [NotNullWhen(true)] out Uri? uri)
    {
        uri = null;

        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
        {
            return false;
        }

        // Uri lower-cases the scheme, so ordinal comparison is safe.
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            uri = null;
            return false;
        }

        if (IsBlockedHost(uri))
        {
            uri = null;
            return false;
        }

        return true;
    }

    /// <summary>True for a loopback name or a loopback, unspecified, private or link-local IP literal.</summary>
    private static bool IsBlockedHost(Uri uri)
    {
        string host = uri.Host;

        foreach (string loopbackHostName in LoopbackHostNames)
        {
            if (string.Equals(host, loopbackHostName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // Strip IPv6 brackets; Uri already normalizes alternate IPv4 encodings.
        string hostAddress = uri.HostNameType == UriHostNameType.IPv6 ? host.Trim('[', ']') : host;

        if (!IPAddress.TryParse(hostAddress, out var address))
        {
            // DNS name: not resolved.
            return false;
        }

        // Unwrap IPv4-mapped IPv6 (::ffff:127.0.0.1) so the IPv4 rules below apply to it.
        var candidate = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

        return IPAddress.IsLoopback(candidate)
            || IsUnspecified(candidate)
            || IsPrivateOrLinkLocal(candidate);
    }

    /// <summary>True for 0.0.0.0 or ::.</summary>
    private static bool IsUnspecified(IPAddress address)
    {
        return address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any);
    }

    /// <summary>True for private or link-local IPv4/IPv6 ranges.</summary>
    private static bool IsPrivateOrLinkLocal(IPAddress address)
    {
        byte[] bytes = address.GetAddressBytes();

        if (address.AddressFamily == AddressFamily.InterNetwork && bytes.Length == 4)
        {
            // 10.0.0.0/8
            if (bytes[0] == 10)
            {
                return true;
            }

            // 172.16.0.0/12 (172.16.0.0 - 172.31.255.255)
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            {
                return true;
            }

            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168)
            {
                return true;
            }

            // 169.254.0.0/16 (link-local / cloud metadata)
            return bytes[0] == 169 && bytes[1] == 254;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6 && bytes.Length == 16)
        {
            // fc00::/7 unique local address
            if ((bytes[0] & 0xFE) == 0xFC)
            {
                return true;
            }

            // fe80::/10 link local
            return bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80;
        }

        return false;
    }
}
