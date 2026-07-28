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
                log.Add($"[Ethernet] {ni.Name}: Already has IP {ip}");
                continue;
            }

            if (IsTrackingInterface(ni.Name))
            {
                log.Add($"[Ethernet] {ni.Name}: Waiting for OS to assign IP (configuration pending)...");
                continue;
            }

            log.Add($"[Ethernet] {ni.Name}: Connected but has no IP address. Configuring Link-Local (auto-discovery) IP...");

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
                    case "device_modified":
                        log.Add($"   Reapplying saved profile to {change.InterfaceName}...");
                        // Run in background so it doesn't block UI shutdown if NM hangs on DHCP
                        try
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = "nmcli",
                                Arguments = $"device reapply {change.InterfaceName}",
                                CreateNoWindow = true,
                                UseShellExecute = false
                            });
                        }
                        catch { }
                        log.Add($"   Successfully reapplied in background");
                        break;

                    case "manual_ip":
                        // We manually added an IP — remove it
                        log.Add($"   Flushing manual IP from {change.InterfaceName}...");
                        RunCommand("ip", $"addr flush dev {change.InterfaceName} scope link");
                        log.Add($"   Successfully flushed");
                        break;
                }
            }
            catch (Exception ex)
            {
                log.Add($"   Restore failed for {change.ConnectionName}: {ex.Message}");
            }
        }

        _changes.Clear();
        return log;
    }

    /// <summary>
    /// Checks if we have already issued configuration commands for this interface.
    /// </summary>
    public static bool IsTrackingInterface(string ifaceName)
    {
        return _changes.Any(c => c.InterfaceName == ifaceName);
    }

    /// <summary>
    /// Checks if tracked interfaces have gone offline, and if so, instantly restores their config.
    /// </summary>
    public static void AuditInterfaces()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || _changes.Count == 0)
            return;

        var allInterfaces = NetworkInterface.GetAllNetworkInterfaces();
        var changesToRestore = new List<ConfigChange>();

        foreach (var change in _changes.ToList())
        {
            var ni = allInterfaces.FirstOrDefault(i => i.Name == change.InterfaceName);
            // If interface doesn't exist, or is down, or cable is unplugged
            if (ni == null || ni.OperationalStatus != OperationalStatus.Up)
            {
                changesToRestore.Add(change);
            }
        }

        if (changesToRestore.Count > 0)
        {
            // Temporarily replace the global tracking list with only the ones that dropped
            var originalChanges = _changes.ToList();
            _changes.Clear();
            _changes.AddRange(changesToRestore);

            RestoreOriginalConfig();

            // Put back the ones that are still active
            var activeChanges = originalChanges.Except(changesToRestore).ToList();
            _changes.AddRange(activeChanges);
        }
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

            // Attempt temporary modification first (Enterprise robust: doesn't permanently alter profiles)
            log.Add($"   Attempting temporary link-local config on device {ifaceName}...");
            var devModResult = RunCommand("nmcli", $"device modify {ifaceName} ipv4.method link-local");
            if (devModResult.exitCode == 0)
            {
                log.Add($"   Successfully configured link-local temporarily on device");
                _changes.Add(new ConfigChange("device_modified", "", ifaceName, null));
                return true;
            }

            log.Add($"   nmcli device modify failed: {devModResult.output}. Falling back to manual IP assignment...");
            return false;
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
                log.Add($"   Successfully manually assigned {ip}");
                _changes.Add(new ConfigChange("manual_ip", "", ifaceName, null));
            }
            else
            {
                var sudoResult = RunCommand("sudo", $"-n ip addr add {ip}/16 dev {ifaceName}");
                if (sudoResult.exitCode == 0)
                {
                    log.Add($"   Successfully assigned {ip} (via sudo)");
                    _changes.Add(new ConfigChange("manual_ip", "", ifaceName, null));
                }
                else
                {
                    log.Add($"   Could not assign IP. Try running with sudo.");
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
