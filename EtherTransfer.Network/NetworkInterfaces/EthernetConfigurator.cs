using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using EtherTransfer.Core;
using EtherTransfer.Core.Models;

namespace EtherTransfer.Network.NetworkInterfaces;

/// <summary>
/// Automatically configures Ethernet interfaces for link-local addressing on Linux.
/// Tracks all changes and restores the original configuration on cleanup.
/// </summary>
public static class EthernetConfigurator
{
    private static readonly object _lock = new();

    // Track what we changed so we can undo it
    private static readonly List<ConfigChange> _changes = new();

    private enum ConfigStatus { Pending, Success, Failed }
    private static readonly Dictionary<string, (DateTime AttemptTime, ConfigStatus Status)> _configState = new();

    private record ConfigChange(string Type, string ConnectionName, string InterfaceName, string? OriginalMethod);

    public static async Task<List<StructuredLogMessage>> EnsureEthernetReadyAsync(bool isRebind = false)
    {
        var log = new List<StructuredLogMessage>();

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return log;
        }

        if (!isRebind)
        {
            log.Add(new StructuredLogMessage("ethernet.check", "Linux detected — checking Ethernet interfaces...", LogLevel.Info));
        }

        var interfaces = NetworkInterface.GetAllNetworkInterfaces()
            .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                         ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                         ni.NetworkInterfaceType != NetworkInterfaceType.Wireless80211)
            .ToList();

        var configTasks = interfaces.Select(async currentNi =>
        {
            var taskLog = new List<StructuredLogMessage>();
            var name = currentNi.Name.ToLowerInvariant();
            var desc = currentNi.Description.ToLowerInvariant();

            if (name.Contains("wi-fi") || name.Contains("wlan") || name.StartsWith("wl") || desc.Contains("wireless"))
                return taskLog;
            if (name.StartsWith("docker") || name.StartsWith("br-") || name.StartsWith("veth") ||
                name.StartsWith("virbr") || name.StartsWith("tun") || name.StartsWith("tap"))
                return taskLog;

            var hasIpv4 = currentNi.GetIPProperties().UnicastAddresses
                .Any(a => a.Address.AddressFamily == AddressFamily.InterNetwork);

            if (!hasIpv4)
            {
                // Give NetworkManager time to finish its DHCP/link-local handshake natively.
                for (int i = 0; i < NetworkConfig.Default.IPWaitLoopMaxAttempts; i++)
                {
                    await Task.Delay(NetworkConfig.Default.IPWaitLoopDelay);
                    
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
                    taskLog.Add(new StructuredLogMessage("ethernet.ready", $"[Ethernet] {currentNi.Name}: Already has IP {ip}", LogLevel.Info));
                }
                return taskLog;
            }

            bool shouldAttempt = false;
            lock (_lock)
            {
                if (_configState.TryGetValue(currentNi.Name, out var state))
                {
                    if ((DateTime.Now - state.AttemptTime) < NetworkConfig.Default.ConfigRetryCooldown)
                    {
                        if (state.Status == ConfigStatus.Pending || state.Status == ConfigStatus.Success)
                        {
                            taskLog.Add(new StructuredLogMessage("ethernet.pending", $"[Ethernet] {currentNi.Name}: Waiting for OS to assign IP (NetworkManager is working...)", LogLevel.Info));
                        }
                        else
                        {
                            taskLog.Add(new StructuredLogMessage("ethernet.failed", $"[Ethernet] {currentNi.Name}: Configuration failed recently. Waiting for cooldown.", LogLevel.Warning));
                        }
                        return taskLog; // Skip attempt
                    }
                }
                // Mark as pending
                _configState[currentNi.Name] = (DateTime.Now, ConfigStatus.Pending);
                shouldAttempt = true;
            }

            if (shouldAttempt)
            {
                taskLog.Add(new StructuredLogMessage("ethernet.configuring", $"[Ethernet] {currentNi.Name}: Connected but has no IP address. Configuring Link-Local (auto-discovery) IP...", LogLevel.Info));

                var success = TryConfigureWithNmcli(currentNi.Name, taskLog);
                
                lock (_lock)
                {
                    _configState[currentNi.Name] = (DateTime.Now, success ? ConfigStatus.Success : ConfigStatus.Failed);
                }

                if (!success)
                {
                    taskLog.Add(new StructuredLogMessage("ethernet.config.error", $"[Ethernet] {currentNi.Name}: NetworkManager is required for auto-configuration on this interface, but nmcli failed.", LogLevel.Error));
                }
            }

            return taskLog;
        });

        // Run interface IP-wait loops concurrently
        var taskLogs = await Task.WhenAll(configTasks);
        foreach (var taskLog in taskLogs)
        {
            log.AddRange(taskLog);
        }

        return log;
    }

    /// <summary>
    /// Restores all Ethernet interfaces to their original configuration.
    /// Call this when the app is shutting down.
    /// </summary>
    public static List<StructuredLogMessage> RestoreOriginalConfig()
    {
        var log = new List<StructuredLogMessage>();

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return log;

        List<ConfigChange> changesCopy;
        lock (_lock)
        {
            if (_changes.Count == 0) return log;
            changesCopy = _changes.ToList();
            _changes.Clear();
        }

        log.Add(new StructuredLogMessage("ethernet.restore", "Restoring original Ethernet configuration...", LogLevel.Info));

        foreach (var change in changesCopy)
        {
            try
            {
                switch (change.Type)
                {
                    case "device_modified":
                        log.Add(new StructuredLogMessage("ethernet.restore.device", $"   Reapplying saved profile to {change.InterfaceName}...", LogLevel.Info));
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
                        log.Add(new StructuredLogMessage("ethernet.restore.success", $"   Successfully reapplied in background", LogLevel.Info));
                        break;
                }
            }
            catch (Exception ex)
            {
                log.Add(new StructuredLogMessage("ethernet.restore.error", $"   Restore failed for {change.ConnectionName}: {ex.Message}", LogLevel.Error));
            }
        }

        return log;
    }

    /// <summary>
    /// Checks if tracked interfaces have gone offline, and if so, instantly restores their config.
    /// </summary>
    public static void AuditInterfaces()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return;

        var changesToRestore = new List<ConfigChange>();
        var originalChanges = new List<ConfigChange>();

        lock (_lock)
        {
            if (_changes.Count == 0) return;
            originalChanges = _changes.ToList();
        }

        var allInterfaces = NetworkInterface.GetAllNetworkInterfaces();

        foreach (var change in originalChanges)
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
            lock (_lock)
            {
                // Temporarily replace the global tracking list with only the ones that dropped
                _changes.Clear();
                _changes.AddRange(changesToRestore);
            }

            RestoreOriginalConfig();

            lock (_lock)
            {
                // Put back the ones that are still active
                var activeChanges = originalChanges.Except(changesToRestore).ToList();
                _changes.AddRange(activeChanges);
            }
        }
    }

    private static bool TryConfigureWithNmcli(string ifaceName, List<StructuredLogMessage> log)
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
                lock (_lock)
                {
                    _changes.Add(new ConfigChange("device_modified", "", ifaceName, null));
                }
                return true;
            }
            else
            {
                log.Add(new StructuredLogMessage("nmcli.error", $"   nmcli returned exit code {devModResult.exitCode}: {devModResult.output}", LogLevel.Error));
                return false;
            }
        }
        catch (Exception ex)
        {
            log.Add(new StructuredLogMessage("nmcli.error", $"   nmcli error: {ex.Message}", LogLevel.Error));
            return false;
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
            
            bool exited = process.WaitForExit(NetworkConfig.Default.ProcessTimeoutMs);
            if (!exited)
            {
                try { process.Kill(); } catch { }
                return (-1, $"Process timed out after {NetworkConfig.Default.ProcessTimeoutMs}ms");
            }

            return (process.ExitCode, string.IsNullOrEmpty(output) ? error : output);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }
}
