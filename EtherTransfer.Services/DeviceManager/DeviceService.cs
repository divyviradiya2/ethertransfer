using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
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
    
    public event EventHandler? DevicesChanged;
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
        _cts = new CancellationTokenSource();
        _discoveryService.Start(computerName, tcpPort);
        
        // Start cleanup task for stale devices
        _ = Task.Run(() => CleanupLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _discoveryService.Stop();
    }

    public IEnumerable<DiscoveredDevice> GetActiveDevices()
    {
        return _devices.Values.ToList();
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
        _devices.AddOrUpdate(sourceIp, 
            _ => 
            {
                updated = true;
                DebugLog?.Invoke(this, $"[{DateTime.Now:HH:mm:ss}] NEW DEVICE: {e.Message.ComputerName} at {sourceIp}");
                return new DiscoveredDevice 
                { 
                    Name = e.Message.ComputerName, 
                    Address = sourceIp, 
                    LastSeen = DateTime.UtcNow 
                };
            }, 
            (_, existing) => 
            {
                existing.LastSeen = DateTime.UtcNow;
                if (existing.Name != e.Message.ComputerName)
                {
                    existing.Name = e.Message.ComputerName;
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
                var staleThreshold = TimeSpan.FromSeconds(10);
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

                await Task.Delay(2000, cancellationToken);
            }
        }
        catch (OperationCanceledException) { }
    }

    public void Dispose()
    {
        Stop();
        _discoveryService.Dispose();
    }
}
