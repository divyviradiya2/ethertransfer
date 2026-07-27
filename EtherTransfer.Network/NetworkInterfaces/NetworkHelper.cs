using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

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
    /// <summary>
    /// Returns all non-wireless, non-loopback interfaces that have a valid IPv4 address and subnet mask.
    /// </summary>
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

    /// <summary>
    /// Diagnoses Ethernet interfaces and returns human-readable status messages.
    /// Detects interfaces that are UP but have no IP (the Linux link-local problem).
    /// </summary>
    public static List<string> DiagnoseInterfaces()
    {
        var results = new List<string>();
        
        var interfaces = NetworkInterface.GetAllNetworkInterfaces()
            .Where(ni => ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                         ni.NetworkInterfaceType != NetworkInterfaceType.Wireless80211);

        foreach (var ni in interfaces)
        {
            var name = ni.Name.ToLowerInvariant();
            var desc = ni.Description.ToLowerInvariant();
            if (name.Contains("wi-fi") || name.Contains("wlan") || name.StartsWith("wl") || desc.Contains("wireless"))
                continue;
            
            // Skip virtual/WAN miniport adapters on Windows
            if (name.Contains("local area connection") || desc.Contains("wan miniport") || 
                desc.Contains("filter") || desc.Contains("scheduler"))
                continue;

            if (ni.OperationalStatus == OperationalStatus.Up)
            {
                var ipv4Addrs = ni.GetIPProperties().UnicastAddresses
                    .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(a => a.Address.ToString())
                    .ToList();

                if (ipv4Addrs.Count > 0)
                {
                    results.Add($"✅ {ni.Name}: UP, IP = {string.Join(", ", ipv4Addrs)}");
                }
                else
                {
                    results.Add($"⚠️ {ni.Name}: UP but NO IPv4 address!");
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    {
                        results.Add($"   FIX: Run './run-linux.sh' or manually: sudo nmcli connection modify \"Wired connection 1\" ipv4.method link-local && sudo nmcli connection up \"Wired connection 1\"");
                    }
                }
            }
            else if (ni.OperationalStatus == OperationalStatus.Down)
            {
                // Only mention the main Ethernet adapter, not all the virtual stuff
                if (name == "ethernet" || name.StartsWith("enp") || name.StartsWith("eth") || name.StartsWith("eno"))
                {
                    results.Add($"❌ {ni.Name}: Cable not connected");
                }
            }
        }

        return results;
    }
}
