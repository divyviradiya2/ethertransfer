using System.Net.NetworkInformation;

namespace EtherTransfer.Network.NetworkInterfaces;

public static class LinuxNetworkInterfaceDetector
{
    public static (bool isPhysical, bool isVirtual, bool isWifi) Analyze(NetworkInterface ni, IPlatformEnvironment env)
    {
        bool isPhysical = false;
        bool isVirtual = false;
        bool isWifi = false;

        if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
        {
            return (false, true, false);
        }

        string sysfsPath = $"/sys/class/net/{ni.Name}";
        
        try
        {
            if (env.DirectoryExists(sysfsPath))
            {
                string? target = env.GetSymlinkTarget(sysfsPath);

                if (target != null)
                {
                    if (target.Contains("/virtual/"))
                    {
                        isVirtual = true;
                        isPhysical = false;
                    }
                    else
                    {
                        isVirtual = false;
                        isPhysical = true;
                    }
                }
                else
                {
                    isVirtual = !env.DirectoryExists($"{sysfsPath}/device");
                    isPhysical = !isVirtual;
                }

                if (env.DirectoryExists($"{sysfsPath}/wireless") || 
                    env.DirectoryExists($"{sysfsPath}/phy80211") ||
                    ni.Name.StartsWith("wl", StringComparison.OrdinalIgnoreCase) ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ||
                    ni.Description.Contains("wireless", StringComparison.OrdinalIgnoreCase) ||
                    ni.Description.Contains("wlan", StringComparison.OrdinalIgnoreCase) ||
                    ni.Description.Contains("wifi", StringComparison.OrdinalIgnoreCase))
                {
                    isWifi = true;
                }
            }
            else
            {
                if (ni.Name.StartsWith("wl", StringComparison.OrdinalIgnoreCase) ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                {
                    isWifi = true;
                }
            }
        }
        catch
        {
            isWifi = ni.Name.StartsWith("wl", StringComparison.OrdinalIgnoreCase) ||
                     ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
            isVirtual = false;
            isPhysical = true;
        }

        return (isPhysical, isVirtual, isWifi);
    }
}
