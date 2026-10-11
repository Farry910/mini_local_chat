using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace MiniLocalChat.Network;

public static class NetUtil
{
    private static readonly string[] VirtualAdapterHints = ["virtual", "vethernet", "vmware", "virtualbox", "hyper-v", "wsl", "docker", "vpn", "tailscale", "zerotier", "wireguard"];

    public static string? Normalize(string? ip) =>
        IPAddress.TryParse(ip, out var address) && address.AddressFamily == AddressFamily.InterNetwork
            ? address.ToString()
            : null;

    public static HashSet<string> GetLocalIPv4Strings() =>
        GetCandidates().Select(c => c.Address.ToString()).Append(IPAddress.Loopback.ToString()).ToHashSet();

    public static List<string> GetSelectableIPv4() =>
        GetCandidates().OrderByDescending(c => c.Score).Select(c => c.Address.ToString()).Distinct().ToList();

    /// <summary>Best guess for "my LAN address": an adapter with a gateway, private range, not a VM/VPN adapter.</summary>
    public static string? DetectPrimaryIPv4() =>
        GetCandidates().OrderByDescending(c => c.Score).Select(c => c.Address.ToString()).FirstOrDefault();

    public static List<IPAddress> GetBroadcastAddresses()
    {
        var result = new List<IPAddress>();
        foreach (var (address, mask) in GetUpInterfaces().SelectMany(n => n.GetIPProperties().UnicastAddresses)
                     .Where(u => u.Address.AddressFamily == AddressFamily.InterNetwork && u.IPv4Mask is not null)
                     .Select(u => (u.Address, u.IPv4Mask)))
        {
            var ip = address.GetAddressBytes();
            var m = mask.GetAddressBytes();
            var broadcast = new byte[4];
            for (var i = 0; i < 4; i++)
            {
                broadcast[i] = (byte)(ip[i] | ~m[i]);
            }

            result.Add(new IPAddress(broadcast));
        }

        return result;
    }

    /// <summary>
    /// Every host address on the local subnets, for direct "hello"s when broadcasts don't get through
    /// (common on Wi-Fi). Big subnets are narrowed to the /24 around our own address.
    /// </summary>
    public static List<IPAddress> GetSubnetHosts()
    {
        const long maxHostsPerSubnet = 1022;
        var hosts = new HashSet<IPAddress>();
        foreach (var unicast in GetUpInterfaces().Where(n => !IsVirtual(n)).SelectMany(n => n.GetIPProperties().UnicastAddresses))
        {
            if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || unicast.IPv4Mask is null || IsLinkLocal(unicast.Address))
            {
                continue;
            }

            var ip = ToUInt(unicast.Address);
            var mask = ToUInt(unicast.IPv4Mask);
            var hostCount = (long)~mask - 1;
            if (hostCount < 1)
            {
                continue; // /31 or /32, e.g. a VPN tunnel address
            }

            if (hostCount > maxHostsPerSubnet)
            {
                mask = 0xFFFFFF00;
            }

            var network = ip & mask;
            var broadcast = network | ~mask;
            for (var host = network + 1; host < broadcast; host++)
            {
                if (host != ip)
                {
                    hosts.Add(FromUInt(host));
                }
            }
        }

        return [.. hosts];
    }

    private static uint ToUInt(IPAddress address) => BinaryPrimitives.ReadUInt32BigEndian(address.GetAddressBytes());

    private static IPAddress FromUInt(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return new IPAddress(bytes);
    }

    private static IEnumerable<NetworkInterface> GetUpInterfaces()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                            && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel));
        }
        catch (NetworkInformationException)
        {
            return [];
        }
    }

    private static IEnumerable<(IPAddress Address, int Score)> GetCandidates()
    {
        foreach (var nic in GetUpInterfaces())
        {
            var props = nic.GetIPProperties();
            var hasGateway = props.GatewayAddresses.Any(g =>
                g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
            var isVirtual = IsVirtual(nic);

            foreach (var unicast in props.UnicastAddresses)
            {
                var address = unicast.Address;
                if (address.AddressFamily != AddressFamily.InterNetwork || IsLinkLocal(address))
                {
                    continue;
                }

                var score = (hasGateway ? 4 : 0) + (IsPrivate(address) ? 2 : 0) + (isVirtual ? -5 : 0);
                yield return (address, score);
            }
        }
    }

    /// <summary>VM, container and VPN adapters: never the LAN the chat runs on.</summary>
    private static bool IsVirtual(NetworkInterface nic)
    {
        var text = (nic.Name + " " + nic.Description).ToLowerInvariant();
        return VirtualAdapterHints.Any(text.Contains);
    }

    private static bool IsLinkLocal(IPAddress a) => a.GetAddressBytes() is [169, 254, ..];

    private static bool IsPrivate(IPAddress a) => a.GetAddressBytes() switch
    {
        [10, ..] => true,
        [172, >= 16 and <= 31, ..] => true,
        [192, 168, ..] => true,
        _ => false,
    };
}
