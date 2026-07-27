using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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
    
    public event EventHandler? DevicesChanged;

    public DeviceService()
    {
        _discoveryService = new DiscoveryService();
        _discoveryService.PeerDiscovered += OnPeerDiscovered;
    }

    public void Start(string computerName, int tcpPort)
    {
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

    private void OnPeerDiscovered(object? sender, PeerDiscoveredEventArgs e)
    {
        var addressStr = e.SourceAddress.ToString();
        
        // Ignore our own broadcasts
        if (NetworkHelper.GetLocalIPAddresses().Contains(addressStr))
        {
            return;
        }

        var updated = false;
        _devices.AddOrUpdate(addressStr, 
            _ => 
            {
                updated = true;
                return new DiscoveredDevice 
                { 
                    Name = e.Message.ComputerName, 
                    Address = addressStr, 
                    LastSeen = DateTime.UtcNow 
                };
            }, 
            (_, existing) => 
            {
                existing.LastSeen = DateTime.UtcNow;
                // If name changed for some reason
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
                    if (_devices.TryRemove(key, out _))
                    {
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
        catch (OperationCanceledException)
        {
            // Expected
        }
    }

    public void Dispose()
    {
        Stop();
        _discoveryService.Dispose();
    }
}
