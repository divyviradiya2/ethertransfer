using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace EtherTransfer.Network.NetworkInterfaces;

public static class NetworkHelper
{
    public static IEnumerable<string> GetLocalIPAddresses()
    {
        var activeInterfaces = NetworkInterface.GetAllNetworkInterfaces()
            .Where(ni => ni.OperationalStatus == OperationalStatus.Up && 
                         ni.NetworkInterfaceType != NetworkInterfaceType.Loopback);

        foreach (var ni in activeInterfaces)
        {
            var ipProps = ni.GetIPProperties();
            foreach (var ip in ipProps.UnicastAddresses)
            {
                if (ip.Address.AddressFamily == AddressFamily.InterNetwork) // IPv4 only for now
                {
                    yield return ip.Address.ToString();
                }
            }
        }
    }

    public static IEnumerable<IPAddress> GetBroadcastAddresses()
    {
        var activeInterfaces = NetworkInterface.GetAllNetworkInterfaces()
            .Where(ni => ni.OperationalStatus == OperationalStatus.Up && 
                         ni.NetworkInterfaceType != NetworkInterfaceType.Loopback);

        foreach (var ni in activeInterfaces)
        {
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
                        
                        // Ignore 0.0.0.0 masks which can happen on some disconnected interfaces
                        if (!maskBytes.All(b => b == 0))
                        {
                            yield return new IPAddress(broadcastBytes);
                        }
                    }
                }
            }
        }
        
        // Always include the global broadcast address as a fallback
        yield return IPAddress.Broadcast;
    }
}
