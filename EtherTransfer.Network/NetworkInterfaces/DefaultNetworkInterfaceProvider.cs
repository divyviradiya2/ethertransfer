using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace EtherTransfer.Network.NetworkInterfaces;

public class DefaultNetworkInterfaceProvider : INetworkInterfaceProvider
{
    public IEnumerable<EthernetInterfaceState> GetEthernetInterfaces()
    {
        var interfaces = NetworkInterface.GetAllNetworkInterfaces()
            .Where(ni => ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                         ni.NetworkInterfaceType != NetworkInterfaceType.Wireless80211);

        foreach (var ni in interfaces)
        {
            var name = ni.Name.ToLowerInvariant();
            var desc = ni.Description.ToLowerInvariant();
            
            // Strictly filter out Wi-Fi adapters
            if (name.Contains("wi-fi") || name.Contains("wlan") || name.StartsWith("wl") || desc.Contains("wireless"))
            {
                continue;
            }

            // Skip virtual/WAN miniport adapters on Windows, Docker/VM bridges on Linux
            if (name.Contains("local area connection") || desc.Contains("wan miniport") ||
                desc.Contains("filter") || desc.Contains("scheduler") ||
                name.StartsWith("docker") || name.StartsWith("br-") || name.StartsWith("veth") ||
                name.StartsWith("virbr") || name.StartsWith("tun") || name.StartsWith("tap"))
            {
                continue;
            }

            var hasIpv4 = false;
            
            // Only check for IP if it's up
            if (ni.OperationalStatus == OperationalStatus.Up)
            {
                hasIpv4 = ni.GetIPProperties().UnicastAddresses
                    .Any(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
            }

            yield return new EthernetInterfaceState(
                Id: ni.Id,
                Name: ni.Name,
                Description: ni.Description,
                OperationalStatus: ni.OperationalStatus,
                HasIpv4Address: hasIpv4
            );
        }
    }
}
