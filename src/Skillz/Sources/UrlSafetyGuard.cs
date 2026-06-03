using System.Net;
using System.Net.Sockets;

namespace Skillz.Sources;

/// <summary>
/// Guards outbound HTTP fetches against server-side request forgery (SSRF). Well-known
/// discovery and index-derived URLs are attacker-influenced, so before any request fires the
/// target must be HTTPS and must not point at loopback, private, link-local, or cloud-metadata
/// hosts.
/// </summary>
/// <remarks>
/// Hostname (non-literal) targets can only be guarded by rejecting literal private IPs and
/// requiring HTTPS here; this cannot fully prevent DNS rebinding, where a hostname resolves to a
/// private address at fetch time. That residual risk is accepted at this layer.
/// </remarks>
internal static class UrlSafetyGuard
{
    /// <summary>
    /// Returns whether <paramref name="uri"/> is a safe outbound fetch target: it must use the
    /// HTTPS scheme and, when its host is an IP literal, must not be a loopback, private,
    /// link-local, unique-local, or unspecified address.
    /// </summary>
    public static bool IsSafeFetchTarget(Uri uri)
    {
        if (uri?.IsAbsoluteUri != true)
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return IsSafeHost(uri.Host);
    }

    /// <summary>
    /// Returns whether <paramref name="host"/> is a permitted target host. Rejects
    /// <c>localhost</c> and any IP literal that is loopback, private, link-local, unique-local, or
    /// unspecified.
    /// </summary>
    public static bool IsSafeHost(string? host)
    {
        if (string.IsNullOrEmpty(host))
        {
            return false;
        }

        if (host.EqualsOrdinalIgnoreCase("localhost"))
        {
            return false;
        }

        // Uri.Host wraps IPv6 literals in brackets; strip them before parsing.
        var candidate = host;
        if (candidate.StartsWith('[') && candidate.EndsWith(']'))
        {
            candidate = candidate[1..^1];
        }

        if (IPAddress.TryParse(candidate, out var address))
        {
            return !IsBlockedAddress(address);
        }

        // Not an IP literal: a hostname. We cannot resolve DNS here, so allow it (see remarks).
        return true;
    }

    private static bool IsBlockedAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return IsBlockedIPv4(address);
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // IPv4-mapped IPv6 (e.g. ::ffff:169.254.169.254) must be checked as IPv4.
            if (address.IsIPv4MappedToIPv6)
            {
                return IsBlockedIPv4(address.MapToIPv4());
            }

            return IsBlockedIPv6(address);
        }

        return true;
    }

    private static bool IsBlockedIPv4(IPAddress address)
    {
        var bytes = address.GetAddressBytes();

        // 0.0.0.0/8 (unspecified / "this network")
        if (bytes[0] == 0)
        {
            return true;
        }

        // 10.0.0.0/8 (private)
        if (bytes[0] == 10)
        {
            return true;
        }

        // 127.0.0.0/8 (loopback)
        if (bytes[0] == 127)
        {
            return true;
        }

        // 169.254.0.0/16 (link-local, includes cloud metadata 169.254.169.254)
        if (bytes[0] == 169 && bytes[1] == 254)
        {
            return true;
        }

        // 172.16.0.0/12 (private)
        if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
        {
            return true;
        }

        // 192.168.0.0/16 (private)
        if (bytes[0] == 192 && bytes[1] == 168)
        {
            return true;
        }

        return false;
    }

    private static bool IsBlockedIPv6(IPAddress address)
    {
        if (address.IsIPv6LinkLocal)
        {
            // fe80::/10
            return true;
        }

        var bytes = address.GetAddressBytes();

        // :: (unspecified)
        if (address.Equals(IPAddress.IPv6Any))
        {
            return true;
        }

        // fc00::/7 (unique local addresses)
        if ((bytes[0] & 0xFE) == 0xFC)
        {
            return true;
        }

        return false;
    }
}
