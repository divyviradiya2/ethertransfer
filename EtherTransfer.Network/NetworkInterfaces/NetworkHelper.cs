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
                else if (ip.Address.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    // For IPv6, broadcast does not exist in the same way, but we can return the local address 
                    // and use multicast (FF02::1) as the 'broadcast' target equivalent if needed by the caller,
                    // but for UDP discovery binding, returning the local address is the primary goal.
                    // The multicast address is standard for all-nodes link-local.
                    yield return new InterfaceAddressInfo(ip.Address, IPAddress.Parse("ff02::1"));
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

    /// <summary>
    /// Checks if a given IP address is reachable via any of our active Ethernet subnets.
    /// Useful for quickly evicting peers when a network link drops.
    /// </summary>
    public static bool IsIpInActiveSubnets(string ipAddress)
    {
        if (!IPAddress.TryParse(ipAddress, out var targetIp)) return false;

        var targetBytes = targetIp.GetAddressBytes();
        bool isIpv6 = targetIp.AddressFamily == AddressFamily.InterNetworkV6;

        var interfaces = NetworkInterface.GetAllNetworkInterfaces()
            .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                         ni.NetworkInterfaceType != NetworkInterfaceType.Loopback);

        foreach (var ni in interfaces)
        {
            var name = ni.Name.ToLowerInvariant();
            var desc = ni.Description.ToLowerInvariant();
            if (name.Contains("wi-fi") || name.Contains("wlan") || name.StartsWith("wl") || desc.Contains("wireless"))
            {
                continue;
            }

            foreach (var ip in ni.GetIPProperties().UnicastAddresses)
            {
                if (!isIpv6 && ip.Address.AddressFamily == AddressFamily.InterNetwork && ip.IPv4Mask != null)
                {
                    var maskBytes = ip.IPv4Mask.GetAddressBytes();
                    var localBytes = ip.Address.GetAddressBytes();

                    if (maskBytes.Length == 4 && localBytes.Length == 4)
                    {
                        bool matches = true;
                        for (int i = 0; i < 4; i++)
                        {
                            if ((localBytes[i] & maskBytes[i]) != (targetBytes[i] & maskBytes[i]))
                            {
                                matches = false;
                                break;
                            }
                        }
                        if (matches) return true;
                    }
                }
                else if (isIpv6 && ip.Address.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    // For IPv6, we can do a simple prefix match based on the PrefixLength.
                    // For link-local (fe80::/10), it's reachable on the same interface.
                    // Simplified: assume reachable if we have an IPv6 address on this interface 
                    // and the scope matches or it's on the same subnet.
                    if (targetIp.IsIPv6LinkLocal && ip.Address.IsIPv6LinkLocal)
                    {
                        return true; 
                    }
                    
                    // Simple full subnet check based on prefix length
                    int prefixBits = ip.PrefixLength;
                    var localBytes = ip.Address.GetAddressBytes();
                    if (localBytes.Length == 16 && targetBytes.Length == 16)
                    {
                        bool matches = true;
                        for (int i = 0; i < 16; i++)
                        {
                            if (prefixBits >= 8)
                            {
                                if (localBytes[i] != targetBytes[i]) { matches = false; break; }
                                prefixBits -= 8;
                            }
                            else if (prefixBits > 0)
                            {
                                byte mask = (byte)(0xFF << (8 - prefixBits));
                                if ((localBytes[i] & mask) != (targetBytes[i] & mask)) { matches = false; break; }
                                prefixBits = 0;
                            }
                            else
                            {
                                break; // Checked all prefix bits
                            }
                        }
                        if (matches) return true;
                    }
                }
            }
        }
        return false;
    }
}
