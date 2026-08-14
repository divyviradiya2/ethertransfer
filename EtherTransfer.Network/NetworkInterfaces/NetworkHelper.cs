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
    /// <summary>
    /// Returns all physical Ethernet interfaces that have a valid IPv4 address and subnet mask.
    /// </summary>
    public static IEnumerable<InterfaceAddressInfo> GetEthernetInterfaces()
    {
        var interfaces = CrossPlatformNetworkDetector.GetInterfaces()
            .Where(ni => ni.IsEthernet && ni.IsPhysical && ni.OperationalStatus == OperationalStatus.Up);

        foreach (var ni in interfaces)
        {
            // We still need the original NetworkInterface to get subnet masks
            // NetworkInterfaceInfo doesn't store subnet mask, so we re-fetch IP properties.
            var origNi = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n => n.Id == ni.Id);
            if (origNi == null) continue;

            var ipProps = origNi.GetIPProperties();
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

        var interfaces = CrossPlatformNetworkDetector.GetInterfaces()
            .Where(ni => ni.IsEthernet && ni.IsPhysical);

        foreach (var ni in interfaces)
        {
            if (ni.OperationalStatus == OperationalStatus.Up)
            {
                var ipv4Addrs = ni.Ipv4Addresses.Select(a => a.ToString()).ToList();

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
                results.Add($"❌ {ni.Name}: Cable not connected");
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
        if (targetIp.AddressFamily != AddressFamily.InterNetwork) return false; // Only support IPv4

        var targetBytes = targetIp.GetAddressBytes();

        var interfaces = CrossPlatformNetworkDetector.GetInterfaces()
            .Where(ni => ni.IsEthernet && ni.IsPhysical && ni.OperationalStatus == OperationalStatus.Up);

        foreach (var ni in interfaces)
        {
            var origNi = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n => n.Id == ni.Id);
            if (origNi == null) continue;

            foreach (var ip in origNi.GetIPProperties().UnicastAddresses)
            {
                if (ip.Address.AddressFamily == AddressFamily.InterNetwork && ip.IPv4Mask != null)
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
            }
        }
        return false;
    }
}
