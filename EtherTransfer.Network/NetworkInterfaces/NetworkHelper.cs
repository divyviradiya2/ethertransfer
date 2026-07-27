using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace EtherTransfer.Network.NetworkInterfaces;

public static class NetworkHelper
{
    /// <summary>
    /// Gets a list of IPv4 addresses assigned ONLY to physical/wired Ethernet adapters.
    /// Excludes Wi-Fi, Loopback, and virtual adapters where possible.
    /// </summary>
    public static IEnumerable<IPAddress> GetEthernetIPAddresses()
    {
        var ethernetInterfaces = NetworkInterface.GetAllNetworkInterfaces()
            .Where(ni => ni.OperationalStatus == OperationalStatus.Up && 
                         ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                         ni.NetworkInterfaceType != NetworkInterfaceType.Wireless80211);

        foreach (var ni in ethernetInterfaces)
        {
            // Extra safety to ignore explicitly named Wi-Fi adapters on Linux/Windows
            var name = ni.Name.ToLowerInvariant();
            var desc = ni.Description.ToLowerInvariant();
            if (name.Contains("wi-fi") || name.Contains("wlan") || name.StartsWith("wl") || desc.Contains("wireless"))
            {
                continue;
            }

            var ipProps = ni.GetIPProperties();
            foreach (var ip in ipProps.UnicastAddresses)
            {
                if (ip.Address.AddressFamily == AddressFamily.InterNetwork) // IPv4
                {
                    yield return ip.Address;
                }
            }
        }
    }

    /// <summary>
    /// Determines if a given IP address is reachable on the same subnet as one of our Ethernet adapters.
    /// </summary>
    public static bool IsOnEthernetSubnet(IPAddress remoteIp)
    {
        var ethernetInterfaces = NetworkInterface.GetAllNetworkInterfaces()
            .Where(ni => ni.OperationalStatus == OperationalStatus.Up && 
                         ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                         ni.NetworkInterfaceType != NetworkInterfaceType.Wireless80211);

        bool hasEthernet = false;
        foreach (var ni in ethernetInterfaces)
        {
            var name = ni.Name.ToLowerInvariant();
            var desc = ni.Description.ToLowerInvariant();
            if (name.Contains("wi-fi") || name.Contains("wlan") || name.StartsWith("wl") || desc.Contains("wireless"))
            {
                continue;
            }
            
            hasEthernet = true;

            var ipProps = ni.GetIPProperties();
            foreach (var ip in ipProps.UnicastAddresses)
            {
                if (ip.Address.AddressFamily == AddressFamily.InterNetwork && ip.IPv4Mask != null && remoteIp.AddressFamily == AddressFamily.InterNetwork)
                {
                    if (IsInSameSubnet(ip.Address, remoteIp, ip.IPv4Mask))
                    {
                        return true;
                    }
                }
            }
        }
        
        // Fallback for direct Ethernet cables without a DHCP router:
        // Allow APIPA (169.254.x.x) and IPv6 Link-Local (fe80::) if an Ethernet cable is plugged in.
        if (hasEthernet)
        {
            if (remoteIp.AddressFamily == AddressFamily.InterNetworkV6 && remoteIp.IsIPv6LinkLocal)
            {
                return true;
            }
                
            if (remoteIp.AddressFamily == AddressFamily.InterNetwork)
            {
                var bytes = remoteIp.GetAddressBytes();
                if (bytes[0] == 169 && bytes[1] == 254)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsInSameSubnet(IPAddress address1, IPAddress address2, IPAddress subnetMask)
    {
        var ip1Bytes = address1.GetAddressBytes();
        var ip2Bytes = address2.GetAddressBytes();
        var maskBytes = subnetMask.GetAddressBytes();

        if (ip1Bytes.Length != ip2Bytes.Length || ip1Bytes.Length != maskBytes.Length)
            return false;

        for (int i = 0; i < ip1Bytes.Length; i++)
        {
            if ((ip1Bytes[i] & maskBytes[i]) != (ip2Bytes[i] & maskBytes[i]))
                return false;
        }

        return true;
    }
}
