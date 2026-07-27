using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace EtherTransfer.Network.NetworkInterfaces;

public class InterfaceAddressInfo
{
    public IPAddress LocalAddress { get; }
    public IPAddress BroadcastAddress { get; }

    public InterfaceAddressInfo(IPAddress localAddress, IPAddress broadcastAddress)
    {
        LocalAddress = localAddress;
        BroadcastAddress = broadcastAddress;
    }
}

public static class NetworkHelper
{
    public static IEnumerable<InterfaceAddressInfo> GetEthernetInterfaces()
    {
        var interfaces = NetworkInterface.GetAllNetworkInterfaces()
            .Where(ni => ni.OperationalStatus == OperationalStatus.Up && 
                         ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                         ni.NetworkInterfaceType != NetworkInterfaceType.Wireless80211);

        foreach (var ni in interfaces)
        {
            var name = ni.Name.ToLowerInvariant();
            var desc = ni.Description.ToLowerInvariant();
            if (name.Contains("wi-fi") || name.Contains("wlan") || name.StartsWith("wl") || desc.Contains("wireless"))
            {
                continue;
            }

            var ipProps = ni.GetIPProperties();
            foreach (var ip in ipProps.UnicastAddresses)
            {
                if (ip.Address.AddressFamily == AddressFamily.InterNetwork && ip.IPv4Mask != null)
                {
                    var ipBytes = ip.Address.GetAddressBytes();
                    var maskBytes = ip.IPv4Mask.GetAddressBytes();
                    
                    if (maskBytes.Length == 4 && ipBytes.Length == 4)
                    {
                        var broadcastBytes = new byte[4];
                        for (int i = 0; i < 4; i++)
                        {
                            broadcastBytes[i] = (byte)(ipBytes[i] | ~maskBytes[i]);
                        }
                        
                        if (!maskBytes.All(b => b == 0))
                        {
                            yield return new InterfaceAddressInfo(ip.Address, new IPAddress(broadcastBytes));
                        }
                    }
                }
            }
        }
    }

    public static bool IsOnEthernetSubnet(IPAddress remoteIp)
    {
        var interfaces = GetEthernetInterfaces();
        bool hasEthernet = false;
        
        foreach (var netIf in interfaces)
        {
            hasEthernet = true;
            
            // Check if remote IP is in the same IPv4 subnet
            if (remoteIp.AddressFamily == AddressFamily.InterNetwork)
            {
                var localBytes = netIf.LocalAddress.GetAddressBytes();
                var remoteBytes = remoteIp.GetAddressBytes();
                var broadcastBytes = netIf.BroadcastAddress.GetAddressBytes();
                
                // We can derive the mask from the broadcast and local address
                var maskBytes = new byte[4];
                for (int i = 0; i < 4; i++)
                {
                    maskBytes[i] = (byte)~(localBytes[i] ^ broadcastBytes[i]);
                }
                
                bool match = true;
                for (int i = 0; i < 4; i++)
                {
                    if ((localBytes[i] & maskBytes[i]) != (remoteBytes[i] & maskBytes[i]))
                    {
                        match = false;
                        break;
                    }
                }
                
                if (match) return true;
            }
        }
        
        // Fallback for direct cables
        if (hasEthernet && remoteIp.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = remoteIp.GetAddressBytes();
            if (bytes[0] == 169 && bytes[1] == 254) // APIPA
            {
                return true;
            }
        }

        return false;
    }
}
