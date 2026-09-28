using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace ControlFS.Infrastructure.Remote;

/// <summary>
/// Endereços IPv4 privados das redes ativas do PC (#223): o servidor do celular escuta só num deles, nunca em todas as
/// interfaces nem em IP público. Redes com gateway (a Wi-Fi/cabo de casa) vêm primeiro; adaptadores virtuais depois.
/// </summary>
internal static class LanAddresses
{
    /// <summary>10/8, 172.16/12 e 192.168/16 (RFC 1918). Fora: loopback, link-local (169.254), CGNAT e públicos.</summary>
    public static bool IsPrivate(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = address.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168);
    }

    public static IReadOnlyList<IPAddress> Discover()
    {
        var found = new List<(IPAddress Address, int Rank)>();
        NetworkInterface[] interfaces;
        try
        {
            interfaces = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (NetworkInformationException)
        {
            return [];
        }
        foreach (var nic in interfaces)
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            IPInterfaceProperties properties;
            try
            {
                properties = nic.GetIPProperties();
            }
            catch (NetworkInformationException)
            {
                continue;
            }
            var hasGateway = properties.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
            var looksVirtual = nic.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase) || nic.Name.StartsWith("vEthernet", StringComparison.OrdinalIgnoreCase);
            var rank = (hasGateway ? 0 : 2) + (looksVirtual ? 1 : 0);
            foreach (var unicast in properties.UnicastAddresses)
                if (IsPrivate(unicast.Address)) found.Add((unicast.Address, rank));
        }
        return [.. found.OrderBy(f => f.Rank).Select(f => f.Address).Distinct()];
    }
}
