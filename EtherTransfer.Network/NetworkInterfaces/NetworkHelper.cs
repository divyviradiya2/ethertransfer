using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace EtherTransfer.Network.NetworkInterfaces;

public static class NetworkHelper
{
    public static IEnumerable<string> GetLocalIPAddresses()
    {
        var activeInterfaces = NetworkInterface.GetAllNetworkInterfaces()
            .Where(ni => ni.OperationalStatus == OperationalStatus.Up && 
                         (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet || 
                          ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211));

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
}
