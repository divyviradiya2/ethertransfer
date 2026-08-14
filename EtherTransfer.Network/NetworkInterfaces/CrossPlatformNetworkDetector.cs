using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace EtherTransfer.Network.NetworkInterfaces;

public static class CrossPlatformNetworkDetector
{
    public static IEnumerable<NetworkInterfaceInfo> GetInterfaces()
    {
        var interfaces = NetworkInterface.GetAllNetworkInterfaces();
        var env = new DefaultPlatformEnvironment();

        foreach (var ni in interfaces)
        {
            var isPhysical = false;
            var isVirtual = true;
            var isWifi = false;

            if (env.IsLinux)
            {
                (isPhysical, isVirtual, isWifi) = LinuxNetworkInterfaceDetector.Analyze(ni, env);
            }
            else if (env.IsWindows)
            {
                (isPhysical, isVirtual, isWifi) = WindowsNetworkInterfaceDetector.Analyze(ni, env);
            }
            else
            {
                // Fallback for macOS or unknown
                isVirtual = false; // Assume physical by default
                isPhysical = ni.NetworkInterfaceType != NetworkInterfaceType.Loopback;
                isWifi = ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
            }

            var isEthernet = !isWifi && !isVirtual && isPhysical &&
                             ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                             ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel;

            var ipv4 = new List<IPAddress>();

            try
            {
                if (ni.OperationalStatus == OperationalStatus.Up)
                {
                    var ipProps = ni.GetIPProperties();
                    foreach (var ip in ipProps.UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                            ipv4.Add(ip.Address);
                    }
                }
            }
            catch
            {
                // Ignore if we can't get IPs
            }

            yield return new NetworkInterfaceInfo(
                Id: ni.Id,
                Name: ni.Name,
                Description: ni.Description,
                InterfaceType: ni.NetworkInterfaceType,
                OperationalStatus: ni.OperationalStatus,
                IsPhysical: isPhysical,
                IsEthernet: isEthernet,
                IsWifi: isWifi,
                IsVirtual: isVirtual,
                MacAddress: ni.GetPhysicalAddress().GetAddressBytes(),
                Ipv4Addresses: ipv4);
        }
    }
}
