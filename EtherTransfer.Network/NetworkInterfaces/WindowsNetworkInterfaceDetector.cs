using System;
using System.Net.NetworkInformation;

namespace EtherTransfer.Network.NetworkInterfaces;

public static class WindowsNetworkInterfaceDetector
{
    public static (bool isPhysical, bool isVirtual, bool isWifi) Analyze(NetworkInterface ni, IPlatformEnvironment env)
    {
        bool isPhysical = false;
        bool isVirtual = false;
        bool isWifi = ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;

        if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
        {
            return (false, true, false);
        }

        try
        {
            string keyPath = $@"SYSTEM\CurrentControlSet\Control\Network\{{4D36E972-E325-11CE-BFC1-08002BE10318}}\{ni.Id}\Connection";
            var pnpInstanceId = env.GetRegistryValue(keyPath, "PnpInstanceID");
            
            if (!string.IsNullOrEmpty(pnpInstanceId))
            {
                if (pnpInstanceId.StartsWith(@"PCI\", StringComparison.OrdinalIgnoreCase) ||
                    pnpInstanceId.StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase) ||
                    pnpInstanceId.StartsWith(@"BTH\", StringComparison.OrdinalIgnoreCase))
                {
                    isPhysical = true;
                    isVirtual = false;
                }
                else
                {
                    isPhysical = false;
                    isVirtual = true;
                }
            }
        }
        catch
        {
            // Ignore registry access errors
        }

        // Fallback heuristics if registry check failed or didn't match
        if (!isPhysical && !isVirtual)
        {
            var desc = ni.Description.ToLowerInvariant();
            var name = ni.Name.ToLowerInvariant();

            // Enterprise VPNs, tunnels, virtual adapters, container networks, and packet filters
            if (desc.Contains("virtual") || desc.Contains("pseudo") || desc.Contains("vpn") ||
                desc.Contains("tunnel") || desc.Contains("tap") || desc.Contains("tun") || desc.Contains("wintun") ||
                desc.Contains("tailscale") || desc.Contains("wireguard") || desc.Contains("zerotier") ||
                desc.Contains("openvpn") || desc.Contains("nord") || desc.Contains("expressvpn") ||
                desc.Contains("cisco") || desc.Contains("anyconnect") || desc.Contains("secure client") ||
                desc.Contains("fortinet") || desc.Contains("forticlient") || desc.Contains("fortissl") ||
                desc.Contains("globalprotect") || desc.Contains("palo alto") || desc.Contains("panos") ||
                desc.Contains("checkpoint") || desc.Contains("pulse secure") || desc.Contains("ivanti") ||
                desc.Contains("softether") || desc.Contains("cloudflare") || desc.Contains("warp") ||
                desc.Contains("hamachi") || desc.Contains("radmin") || desc.Contains("ztna") ||
                desc.Contains("hyper-v") || desc.Contains("vethernet") || desc.Contains("vmware") ||
                desc.Contains("virtualbox") || desc.Contains("vbox") || desc.Contains("wsl") ||
                desc.Contains("docker") || desc.Contains("container") || desc.Contains("multipass") ||
                desc.Contains("wan miniport") || desc.Contains("wfp ") || desc.Contains("qos ") || desc.Contains("filter") ||
                name.Contains("virtual") || name.Contains("vpn") || name.Contains("tailscale") || name.Contains("wireguard") ||
                name.Contains("zerotier") || name.Contains("openvpn") || name.Contains("cisco") || name.Contains("anyconnect") ||
                name.Contains("forticlient") || name.Contains("globalprotect") || name.Contains("wsl") || name.Contains("docker") ||
                name.Contains("wfp ") || name.Contains("qos ") || name.Contains("filter"))
            {
                isVirtual = true;
                isPhysical = false;
            }
            else
            {
                // Optimistic fallback
                isPhysical = true;
                isVirtual = false;
            }
        }

        return (isPhysical, isVirtual, isWifi);
    }
}
