using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Net.NetworkInformation;
using EtherTransfer.Core.Models;
using EtherTransfer.Network.UdpDiscovery;
using EtherTransfer.Network.NetworkInterfaces;

namespace EtherTransfer.Services.DeviceManager;

public class DeviceService : IDisposable
{
    private readonly DiscoveryService _discoveryService;
    private readonly ConcurrentDictionary<string, DiscoveredDevice> _devices = new();
    private CancellationTokenSource? _cts;
    private string _computerName = string.Empty;
    private int _tcpPort;
    private CancellationTokenSource? _debounceCts;

    public event EventHandler? DevicesChanged;
    public event EventHandler? NetworkChanged;
    public event EventHandler<string>? DebugLog;

    public DeviceService()
    {
        _discoveryService = new DiscoveryService();
        _discoveryService.PeerDiscovered += OnPeerDiscovered;
        _discoveryService.DebugLog += (_, msg) => DebugLog?.Invoke(this, msg);
    }

    public void Start(string computerName, int tcpPort)
    {
        _computerName = computerName;
        _tcpPort = tcpPort;
        _cts = new CancellationTokenSource();
        _discoveryService.Start(computerName, tcpPort);

        NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAddressChanged;

        // Start cleanup task for stale devices
        _ = Task.Run(() => CleanupLoopAsync(_cts.Token));
    }

    private void OnNetworkAddressChanged(object? sender, EventArgs e)
    {
        _debounceCts?.Cancel();
        _debounceCts = new CancellationTokenSource();
        var token = _debounceCts.Token;

        Task.Run(async () =>
        {
            try
            {
                // Unplug events: audit if any configured interfaces dropped
                if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux))
                {
                    EthernetConfigurator.AuditInterfaces();
                }

                // Debounce rapid network state changes (DHCP, link up/down)
                await Task.Delay(1000, token);
                if (token.IsCancellationRequested) return;

                DebugLog?.Invoke(this, $"[{DateTime.Now:HH:mm:ss}] Network state settled. Re-binding sockets...");
                _devices.Clear();
                DevicesChanged?.Invoke(this, EventArgs.Empty);
                NetworkChanged?.Invoke(this, EventArgs.Empty);

                // Restart discovery to bind UDP sockets to new interfaces
                _discoveryService.Stop();
                _discoveryService.Start(_computerName, _tcpPort);
            }
            catch { }
        });
    }

    public void UpdateComputerName(string newName)
    {
        _computerName = newName;
        _discoveryService.UpdateComputerName(newName);
    }

    public void Stop()
    {
        NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
        _cts?.Cancel();
        _discoveryService.Stop();
    }

    public IEnumerable<DiscoveredDevice> GetActiveDevices()
    {
        // Group by Name to deduplicate devices broadcasting from multiple network interfaces 
        // (e.g. Wi-Fi and Ethernet simultaneously). Pick the most recently seen IP address.
        return _devices.Values
            .GroupBy(d => d.Name)
            .Select(g => g.OrderByDescending(d => d.LastSeen).First())
            .ToList();
    }

    private static HashSet<string> GetCurrentLocalIps()
    {
        var ips = new HashSet<string>();
        foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
        {
            foreach (var addr in ni.GetIPProperties().UnicastAddresses)
            {
                if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    ips.Add(addr.Address.ToString());
                }
            }
        }
        return ips;
    }

    private void OnPeerDiscovered(object? sender, PeerDiscoveredEventArgs e)
    {
        var sourceIp = e.SourceAddress.ToString();

        // Dynamically check local IPs each time — critical on Linux where the
        // link-local IP is assigned AFTER startup by EthernetConfigurator.
        if (GetCurrentLocalIps().Contains(sourceIp))
        {
            return;
        }

        var updated = false;

        if (e.Message.Type == "BYE")
        {
            if (_devices.TryRemove(sourceIp, out var removed))
            {
                DebugLog?.Invoke(this, $"[{DateTime.Now:HH:mm:ss}] DEVICE WENT OFFLINE: {removed.Name} at {sourceIp}");
                DevicesChanged?.Invoke(this, EventArgs.Empty);
            }
            return;
        }
        _devices.AddOrUpdate(sourceIp,
            _ =>
            {
                updated = true;
                DebugLog?.Invoke(this, $"[{DateTime.Now:HH:mm:ss}] NEW DEVICE: {e.Message.ComputerName} at {sourceIp}");
                return new DiscoveredDevice
                {
                    Name = e.Message.ComputerName,
                    Address = sourceIp,
                    OS = e.Message.OS,
                    LastSeen = DateTime.UtcNow
                };
            },
            (_, existing) =>
            {
                existing.LastSeen = DateTime.UtcNow;
                if (existing.Name != e.Message.ComputerName || existing.OS != e.Message.OS)
                {
                    existing.Name = e.Message.ComputerName;
                    existing.OS = e.Message.OS;
                    updated = true;
                }
                return existing;
            });

        if (updated)
        {
            DevicesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task CleanupLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var now = DateTime.UtcNow;
                // Increased from 10s to 45s. During extremely heavy TCP file transfers that saturate 
                // the NIC, UDP broadcast packets are frequently dropped by network switch buffers.
                // 45 seconds ensures that even with 95% UDP packet loss, the device stays active.
                // Graceful closures will still be instant thanks to the "BYE" broadcast packet.
                var staleThreshold = TimeSpan.FromSeconds(45);
                var removedAny = false;

                var keysToRemove = _devices.Where(kvp => now - kvp.Value.LastSeen > staleThreshold).Select(kvp => kvp.Key).ToList();
                foreach (var key in keysToRemove)
                {
                    if (_devices.TryRemove(key, out var removed))
                    {
                        DebugLog?.Invoke(this, $"[{DateTime.Now:HH:mm:ss}] REMOVED stale: {removed.Name} at {key}");
                        removedAny = true;
                    }
                }

                if (removedAny)
                {
                    DevicesChanged?.Invoke(this, EventArgs.Empty);
                }

                if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux))
                {
                    EthernetConfigurator.AuditInterfaces();
                    CheckForUnconfiguredLinuxEthernet();
                }

                await Task.Delay(2000, cancellationToken);
            }
        }
        catch (OperationCanceledException) { }
    }

    private void CheckForUnconfiguredLinuxEthernet()
    {
        try
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
                    continue;
                if (name.StartsWith("docker") || name.StartsWith("br-") || name.StartsWith("veth") ||
                    name.StartsWith("virbr") || name.StartsWith("tun") || name.StartsWith("tap"))
                    continue;

                var hasIpv4 = ni.GetIPProperties().UnicastAddresses
                    .Any(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);

                if (!hasIpv4)
                {
                    DebugLog?.Invoke(this, $"[{DateTime.Now:HH:mm:ss}] Found unconfigured Ethernet '{ni.Name}'. Forcing network refresh...");
                    // Trigger the network change logic which restarts Discovery and runs EnsureEthernetReady()
                    OnNetworkAddressChanged(this, EventArgs.Empty);
                    break;
                }
            }
        }
        catch { }
    }

    public void Dispose()
    {
        Stop();
        _discoveryService.Dispose();
    }
}
