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
/// Tracks all changes and restores the original configuration on cleanup.
/// </summary>
public static class EthernetConfigurator
{
    // Track what we changed so we can undo it
    private static readonly List<ConfigChange> _changes = new();

    private record ConfigChange(string Type, string ConnectionName, string InterfaceName, string? OriginalMethod);

    public static List<string> EnsureEthernetReady()
    {
        var log = new List<string>();

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

            if (name.Contains("wi-fi") || name.Contains("wlan") || name.StartsWith("wl") || desc.Contains("wireless"))
                continue;
            if (name.StartsWith("docker") || name.StartsWith("br-") || name.StartsWith("veth") ||
                name.StartsWith("virbr") || name.StartsWith("tun") || name.StartsWith("tap"))
                continue;

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

            if (TryConfigureWithNmcli(ni.Name, log))
                continue;

            TryConfigureManually(ni.Name, log);
        }

        return log;
    }

    /// <summary>
    /// Restores all Ethernet interfaces to their original configuration.
    /// Call this when the app is shutting down.
    /// </summary>
    public static List<string> RestoreOriginalConfig()
    {
        var log = new List<string>();

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || _changes.Count == 0)
            return log;

        log.Add("Restoring original Ethernet configuration...");

        foreach (var change in _changes)
        {
            try
            {
                switch (change.Type)
                {
                    case "created":
                        // We created a new connection — delete it and bring back the original
                        log.Add($"   Removing '{change.ConnectionName}'...");
                        RunCommand("nmcli", $"connection down \"{change.ConnectionName}\"");
                        RunCommand("nmcli", $"connection delete \"{change.ConnectionName}\"");
                        log.Add($"   ✅ Removed");
                        break;

                    case "modified":
                        // We modified an existing connection — restore original method
                        var method = change.OriginalMethod ?? "auto";
                        log.Add($"   Restoring '{change.ConnectionName}' to ipv4.method={method}...");
                        RunCommand("nmcli", $"connection modify \"{change.ConnectionName}\" ipv4.method {method}");
                        RunCommand("nmcli", $"connection up \"{change.ConnectionName}\"");
                        log.Add($"   ✅ Restored");
                        break;

                    case "manual_ip":
                        // We manually added an IP — remove it
                        log.Add($"   Flushing manual IP from {change.InterfaceName}...");
                        RunCommand("ip", $"addr flush dev {change.InterfaceName} scope link");
                        log.Add($"   ✅ Flushed");
                        break;
                }
            }
            catch (Exception ex)
            {
                log.Add($"   ⚠️ Restore failed for {change.ConnectionName}: {ex.Message}");
            }
        }

        _changes.Clear();
        return log;
    }

    private static bool TryConfigureWithNmcli(string ifaceName, List<string> log)
    {
        try
        {
            var whichResult = RunCommand("which", "nmcli");
            if (whichResult.exitCode != 0)
            {
                log.Add("   nmcli not found, trying manual fallback...");
                return false;
            }

            // Find existing connection for this interface
            var listResult = RunCommand("nmcli", "-t -f NAME,DEVICE connection show");
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
                // Create a new connection — track for cleanup
                connName = $"EtherTransfer-{ifaceName}";
                log.Add($"   Creating connection '{connName}'...");
                var addResult = RunCommand("nmcli", $"connection add type ethernet con-name \"{connName}\" ifname {ifaceName} ipv4.method link-local");
                if (addResult.exitCode != 0)
                {
                    log.Add($"   Failed to create connection: {addResult.output}");
                    return false;
                }
                _changes.Add(new ConfigChange("created", connName, ifaceName, null));
            }
            else
            {
                // Read current method before modifying
                var methodResult = RunCommand("nmcli", $"-t -f ipv4.method connection show \"{connName}\"");
                var originalMethod = "auto"; // default
                if (methodResult.exitCode == 0)
                {
                    var val = methodResult.output.Trim();
                    if (val.Contains(':'))
                        originalMethod = val.Split(':')[^1].Trim();
                }

                // If already link-local, nothing to do
                if (originalMethod == "link-local")
                {
                    log.Add($"   '{connName}' is already link-local, bringing up...");
                    RunCommand("nmcli", $"connection up \"{connName}\"");
                    return true;
                }

                log.Add($"   Setting '{connName}' to link-local (was: {originalMethod})...");
                var modResult = RunCommand("nmcli", $"connection modify \"{connName}\" ipv4.method link-local");
                if (modResult.exitCode != 0)
                {
                    log.Add($"   Failed to modify: {modResult.output}");
                    return false;
                }
                _changes.Add(new ConfigChange("modified", connName, ifaceName, originalMethod));
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
                _changes.Add(new ConfigChange("manual_ip", "", ifaceName, null));
            }
            else
            {
                var sudoResult = RunCommand("sudo", $"-n ip addr add {ip}/16 dev {ifaceName}");
                if (sudoResult.exitCode == 0)
                {
                    log.Add($"   ✅ Assigned {ip} (via sudo)");
                    _changes.Add(new ConfigChange("manual_ip", "", ifaceName, null));
                }
                else
                {
                    log.Add($"   ❌ Could not assign IP. Try running with sudo.");
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
