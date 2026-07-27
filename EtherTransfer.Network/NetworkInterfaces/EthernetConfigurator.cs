using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace EtherTransfer.Network.NetworkInterfaces;

/// <summary>
/// Automatically configures Ethernet interfaces for link-local addressing on Linux.
/// On Windows, APIPA does this automatically. On Linux, NetworkManager doesn't by default.
/// </summary>
public static class EthernetConfigurator
{
    public static List<string> EnsureEthernetReady()
    {
        var log = new List<string>();

        // Only needed on Linux
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return log;
        }

        log.Add("Linux detected — checking Ethernet interfaces...");

        var interfaces = NetworkInterface.GetAllNetworkInterfaces()
            .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                         ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                         ni.NetworkInterfaceType != NetworkInterfaceType.Wireless80211);

        foreach (var ni in interfaces)
        {
            var name = ni.Name.ToLowerInvariant();
            var desc = ni.Description.ToLowerInvariant();

            // Skip wireless
            if (name.Contains("wi-fi") || name.Contains("wlan") || name.StartsWith("wl") || desc.Contains("wireless"))
                continue;

            // Skip virtual/docker/bridge
            if (name.StartsWith("docker") || name.StartsWith("br-") || name.StartsWith("veth") ||
                name.StartsWith("virbr") || name.StartsWith("tun") || name.StartsWith("tap"))
                continue;

            // Check if this interface has any IPv4 address
            var hasIpv4 = ni.GetIPProperties().UnicastAddresses
                .Any(a => a.Address.AddressFamily == AddressFamily.InterNetwork);

            if (hasIpv4)
            {
                var ip = ni.GetIPProperties().UnicastAddresses
                    .First(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Address.ToString();
                log.Add($"✅ {ni.Name}: already has IP {ip}");
                continue;
            }

            log.Add($"⚠️ {ni.Name}: UP but no IPv4 — configuring link-local...");

            // Try nmcli
            if (TryConfigureWithNmcli(ni.Name, log))
                continue;

            // Fallback: assign a random 169.254.x.x directly
            TryConfigureManually(ni.Name, log);
        }

        return log;
    }

    private static bool TryConfigureWithNmcli(string ifaceName, List<string> log)
    {
        try
        {
            // Check if nmcli exists
            var whichResult = RunCommand("which", "nmcli");
            if (whichResult.exitCode != 0)
            {
                log.Add("   nmcli not found, trying manual fallback...");
                return false;
            }

            // Find existing connection for this interface
            var listResult = RunCommand("nmcli", $"-t -f NAME,DEVICE connection show");
            string? connName = null;

            if (listResult.exitCode == 0)
            {
                var lines = listResult.output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var parts = line.Trim().Split(':');
                    if (parts.Length >= 2 && parts[^1] == ifaceName)
                    {
                        connName = parts[0];
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(connName))
            {
                // Create a new connection
                connName = $"EtherTransfer-{ifaceName}";
                log.Add($"   Creating connection '{connName}'...");
                var addResult = RunCommand("nmcli", $"connection add type ethernet con-name \"{connName}\" ifname {ifaceName} ipv4.method link-local");
                if (addResult.exitCode != 0)
                {
                    log.Add($"   Failed to create connection: {addResult.output}");
                    return false;
                }
            }
            else
            {
                // Modify existing connection
                log.Add($"   Setting '{connName}' to link-local...");
                var modResult = RunCommand("nmcli", $"connection modify \"{connName}\" ipv4.method link-local");
                if (modResult.exitCode != 0)
                {
                    log.Add($"   Failed to modify: {modResult.output}");
                    return false;
                }
            }

            // Bring it up
            var upResult = RunCommand("nmcli", $"connection up \"{connName}\"");
            if (upResult.exitCode == 0)
            {
                log.Add($"   ✅ Configured link-local via nmcli");
                return true;
            }
            else
            {
                log.Add($"   Failed to bring up: {upResult.output}");
                return false;
            }
        }
        catch (Exception ex)
        {
            log.Add($"   nmcli error: {ex.Message}");
            return false;
        }
    }

    private static void TryConfigureManually(string ifaceName, List<string> log)
    {
        try
        {
            var rng = new Random();
            var ip = $"169.254.{rng.Next(1, 255)}.{rng.Next(1, 255)}";
            log.Add($"   Assigning {ip}/16 to {ifaceName}...");

            var result = RunCommand("ip", $"addr add {ip}/16 dev {ifaceName}");
            if (result.exitCode == 0)
            {
                log.Add($"   ✅ Manually assigned {ip}");
            }
            else
            {
                // Might need sudo
                var sudoResult = RunCommand("sudo", $"-n ip addr add {ip}/16 dev {ifaceName}");
                if (sudoResult.exitCode == 0)
                {
                    log.Add($"   ✅ Assigned {ip} (via sudo)");
                }
                else
                {
                    log.Add($"   ❌ Could not assign IP. Run app with: sudo dotnet run --project EtherTransfer.UI");
                }
            }
        }
        catch (Exception ex)
        {
            log.Add($"   Manual config error: {ex.Message}");
        }
    }

    private static (int exitCode, string output) RunCommand(string command, string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = command,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
                return (-1, "Failed to start process");

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit(10000);

            return (process.ExitCode, string.IsNullOrEmpty(output) ? error : output);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }
}
