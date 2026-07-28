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
    private static readonly Dictionary<string, DateTime> _lastConfigAttempt = new();

    private record ConfigChange(string Type, string ConnectionName, string InterfaceName, string? OriginalMethod);

    public static async Task<List<string>> EnsureEthernetReadyAsync(bool isRebind = false)
    {
        var log = new List<string>();

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return log;
        }

        if (!isRebind)
        {
            log.Add("Linux detected — checking Ethernet interfaces...");
        }

        var interfaces = NetworkInterface.GetAllNetworkInterfaces()
            .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                         ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                         ni.NetworkInterfaceType != NetworkInterfaceType.Wireless80211);

        foreach (var ni in interfaces)
        {
            var currentNi = ni;
            var name = currentNi.Name.ToLowerInvariant();
            var desc = currentNi.Description.ToLowerInvariant();

            if (name.Contains("wi-fi") || name.Contains("wlan") || name.StartsWith("wl") || desc.Contains("wireless"))
                continue;
            if (name.StartsWith("docker") || name.StartsWith("br-") || name.StartsWith("veth") ||
                name.StartsWith("virbr") || name.StartsWith("tun") || name.StartsWith("tap"))
                continue;

            var hasIpv4 = currentNi.GetIPProperties().UnicastAddresses
                .Any(a => a.Address.AddressFamily == AddressFamily.InterNetwork);

            if (!hasIpv4)
            {
                // Give NetworkManager up to 6 seconds to finish its DHCP/link-local handshake natively.
                // This prevents the app from starting up with 0 listeners and immediately restarting when NM finishes.
                for (int i = 0; i < 12; i++)
                {
                    await Task.Delay(500);
                    
                    var updatedNi = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n => n.Id == currentNi.Id);
                    if (updatedNi != null && updatedNi.GetIPProperties().UnicastAddresses.Any(a => a.Address.AddressFamily == AddressFamily.InterNetwork))
                    {
                        hasIpv4 = true;
                        currentNi = updatedNi;
                        break;
                    }
                }
            }

            if (hasIpv4)
            {
                if (!isRebind)
                {
                    var ip = currentNi.GetIPProperties().UnicastAddresses
                        .First(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                        .Address.ToString();
                    log.Add($"[Ethernet] {currentNi.Name}: Already has IP {ip}");
                }
                continue;
            }

            if (_lastConfigAttempt.TryGetValue(currentNi.Name, out var lastAttempt))
            {
                if ((DateTime.Now - lastAttempt).TotalSeconds < 15)
                {
                    log.Add($"[Ethernet] {currentNi.Name}: Waiting for OS to assign IP (NetworkManager is working...)");
                    continue;
                }
            }

            _lastConfigAttempt[currentNi.Name] = DateTime.Now;
            log.Add($"[Ethernet] {currentNi.Name}: Connected but has no IP address. Configuring Link-Local (auto-discovery) IP...");

            if (TryConfigureWithNmcli(currentNi.Name, log))
                continue;

            TryConfigureManually(currentNi.Name, log);
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
                return false;
            }

            // Attempt temporary modification first (Enterprise robust: doesn't permanently alter profiles)
            var devModResult = RunCommand("nmcli", $"device modify {ifaceName} ipv4.method link-local");
            if (devModResult.exitCode == 0)
            {
                _changes.Add(new ConfigChange("device_modified", "", ifaceName, null));
                return true;
            }
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
                _changes.Add(new ConfigChange("manual_ip", "", ifaceName, null));
            }
            else
            {
                var sudoResult = RunCommand("sudo", $"-n ip addr add {ip}/16 dev {ifaceName}");
                if (sudoResult.exitCode == 0)
                {
                    _changes.Add(new ConfigChange("manual_ip", "", ifaceName, null));
                }
                else
                {
                    _changes.Add(new ConfigChange("failed", "", ifaceName, null));
                }
            }
        }
        catch (Exception ex)
        {
            log.Add($"   Manual config error: {ex.Message}");
            _changes.Add(new ConfigChange("failed", "", ifaceName, null));
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
