using System.Net;
using System.Net.Sockets;

namespace RequestGuardMcp.Core.Util;

/// <summary>Ports src/util/net.rs.</summary>
public static class NetUtil
{
    public static IPAddress? ParseIp(string value) => IPAddress.TryParse(value, out var ip) ? ip : null;

    /// <summary>Check if an IP is a known private/loopback address.</summary>
    public static bool IsPrivate(IPAddress ip)
    {
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return IPAddress.IsLoopback(ip);
        }

        var octets = ip.GetAddressBytes();
        return octets[0] == 10 ||
               (octets[0] == 172 && octets[1] is >= 16 and <= 31) ||
               (octets[0] == 192 && octets[1] == 168) ||
               IPAddress.IsLoopback(ip) ||
               (octets[0] == 169 && octets[1] == 254) ||
               (octets[0] == 255 && octets[1] == 255 && octets[2] == 255 && octets[3] == 255);
    }
}
