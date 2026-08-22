using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Net.NetworkInformation;
using EtherTransfer.Core;
using EtherTransfer.Core.Models;
using EtherTransfer.Network.UdpDiscovery;
using EtherTransfer.Network.NetworkInterfaces;

namespace EtherTransfer.Services.DeviceManager;

public class DeviceService : IDisposable
{
    private readonly DiscoveryService _discoveryService;
    private readonly ConcurrentDictionary<string, DiscoveredDevice> _devices = new();
    private CancellationTokenSource? _cts;
    
    // Track last refresh attempt per interface to prevent spamming discovery restarts
    private readonly Dictionary<string, DateTime> _lastRefreshAttempt = new();
    private HashSet<string> _lastKnownIps = new();
    private string _computerName = string.Empty;
    private int _tcpPort;
    private CancellationTokenSource? _debounceCts;

    public event EventHandler? DevicesChanged;
    public event EventHandler? NetworkChanged;
    public event EventHandler<PeerDiscoveredEventArgs>? TransferCancelReceived;
    public event EventHandler<StructuredLogMessage>? DebugLog;

    private void Log(string msg, LogLevel level = LogLevel.Info, string eventId = "device.log")
    {
        DebugLog?.Invoke(this, new StructuredLogMessage(eventId, msg, level));
    }

    public DeviceService()
    {
        _discoveryService = new DiscoveryService();
        _discoveryService.PeerDiscovered += OnPeerDiscovered;
        _discoveryService.TransferCancelReceived += (_, e) => TransferCancelReceived?.Invoke(this, e);
        _discoveryService.DebugLog += (_, msg) => DebugLog?.Invoke(this, msg);
    }

    public Task SendTransferCancelAsync(IPAddress targetAddress)
    {
        return _discoveryService.SendTransferCancelAsync(targetAddress);
    }

    public async Task StartAsync(string computerName, int tcpPort)
    {
        _computerName = computerName;
        _tcpPort = tcpPort;
        _cts = new CancellationTokenSource();
        await _discoveryService.StartAsync(computerName, tcpPort);

        NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAddressChanged;

        // Start cleanup task for stale devices
        _ = Task.Run(() => CleanupLoopAsync(_cts.Token));
        _lastKnownIps = GetCurrentLocalIps();
        
        // Initial diagnostic log
        var diags = NetworkHelper.DiagnoseInterfaces();
        foreach (var diag in diags)
        {
            Log(diag, LogLevel.Info, "network.diagnostic");
        }
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
                await Task.Delay(300, token);
                if (token.IsCancellationRequested) return;

                bool vpnStateChanged = false;
                bool oldVpnState = HasActiveVpn;
                
                var currentIps = GetCurrentLocalIps();
                bool ipSetChanged = !_lastKnownIps.SetEquals(currentIps);
                _lastKnownIps = currentIps;
                
                if (oldVpnState != HasActiveVpn)
                {
                    vpnStateChanged = true;
                }

                if (!ipSetChanged && !vpnStateChanged)
                {
                    // Ignore spurious OS routing changes if our IPv4 addresses and VPN state haven't changed
                    return;
                }

                // Log diagnostics
                var diags = NetworkHelper.DiagnoseInterfaces();
                foreach (var diag in diags)
                {
                    Log(diag, LogLevel.Info, "network.diagnostic");
                }

                NetworkChanged?.Invoke(this, EventArgs.Empty);

                // Restart discovery to bind UDP sockets to new interfaces (without sending false BYE packets)
                _discoveryService.Stop(sendBye: false);
                await _discoveryService.StartAsync(_computerName, _tcpPort, isRebind: true);
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
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAddressChanged;
        _cts?.Cancel();
        _discoveryService.Stop(sendBye: true);
    }

    public IEnumerable<DiscoveredDevice> GetActiveDevices()
    {
        // Group by Name to deduplicate devices broadcasting from multiple network interfaces 
        // (e.g. connected to multiple Ethernet networks simultaneously). Pick the most recently seen IP address.
        return _devices.Values
            .GroupBy(d => d.Name)
            .Select(g => g.OrderByDescending(d => d.LastSeen).First())
            .ToList();
    }

    public bool HasActiveVpn { get; private set; }

    private HashSet<string> GetCurrentLocalIps()
    {
        var ips = new HashSet<string>();
        var interfaces = CrossPlatformNetworkDetector.GetInterfaces().ToList();
        
        bool vpnActive = interfaces.Any(n => 
            n.IsVirtual && 
            n.OperationalStatus == OperationalStatus.Up && 
            (n.Description.Contains("cisco", StringComparison.OrdinalIgnoreCase) ||
             n.Description.Contains("anyconnect", StringComparison.OrdinalIgnoreCase) ||
             n.Description.Contains("globalprotect", StringComparison.OrdinalIgnoreCase) ||
             n.Description.Contains("zscaler", StringComparison.OrdinalIgnoreCase) ||
             n.Description.Contains("fortinet", StringComparison.OrdinalIgnoreCase) ||
             n.Description.Contains("pulse secure", StringComparison.OrdinalIgnoreCase) ||
             n.Description.Contains("checkpoint", StringComparison.OrdinalIgnoreCase) ||
             n.Description.Contains("sonicwall", StringComparison.OrdinalIgnoreCase) ||
             n.Description.Contains("f5", StringComparison.OrdinalIgnoreCase) ||
             n.Description.Contains("big-ip", StringComparison.OrdinalIgnoreCase) ||
             n.Description.Contains("netmotion", StringComparison.OrdinalIgnoreCase)));

        if (vpnActive && !HasActiveVpn)
        {
            Log("A Corporate VPN is currently active. If you cannot see other devices, your IT administrator may be blocking local network discovery.", LogLevel.Warning, "network.vpn_warning");
        }
        
        HasActiveVpn = vpnActive;

        foreach (var ni in interfaces.Where(n => n.IsEthernet && n.IsPhysical))
        {
            foreach (var addr in ni.Ipv4Addresses)
            {
                ips.Add(addr.ToString());
            }
        }
        return ips;
    }

    private void OnPeerDiscovered(object? sender, PeerDiscoveredEventArgs e)
    {
        var sourceIp = e.SourceAddress.ToString();

        // Use cached IPs to avoid re-querying OS on every packet
        if (_lastKnownIps.Contains(sourceIp))
        {
            return;
        }
        
        var sessionId = e.Message.SessionId;
        if (string.IsNullOrEmpty(sessionId)) 
        {
            // Fallback for old versions
            sessionId = sourceIp;
        }

        var updated = false;

        if (e.Message.Type == "BYE")
        {
            if (_devices.TryRemove(sessionId, out var removed))
            {
                Log($"DEVICE WENT OFFLINE: {removed.Name} at {sourceIp}", LogLevel.Info, "device.offline");
                DevicesChanged?.Invoke(this, EventArgs.Empty);
            }
            return;
        }
        var isNew = false;
        var devicePort = e.Message.TcpPort > 0 ? e.Message.TcpPort : 55000;
        
        _devices.AddOrUpdate(sessionId,
            _ =>
            {
                updated = true;
                isNew = true;
                return new DiscoveredDevice
                {
                    SessionId = sessionId,
                    Name = e.Message.ComputerName,
                    Address = sourceIp,
                    Port = devicePort,
                    OS = e.Message.OS,
                    LastSeen = DateTime.UtcNow
                };
            },
            (_, existing) =>
            {
                existing.LastSeen = DateTime.UtcNow;
                if (existing.Name != e.Message.ComputerName || existing.OS != e.Message.OS || existing.Address != sourceIp || existing.Port != devicePort)
                {
                    if (existing.Address != sourceIp)
                    {
                        Log($"DEVICE IP CHANGED: {existing.Name} moved from {existing.Address} to {sourceIp}", LogLevel.Info, "device.ip_changed");
                    }
                    existing.Name = e.Message.ComputerName;
                    existing.OS = e.Message.OS;
                    existing.Address = sourceIp;
                    existing.Port = devicePort;
                    updated = true;
                }
                return existing;
            });

        if (isNew)
        {
            Log($"NEW DEVICE: {e.Message.ComputerName} at {sourceIp}", LogLevel.Info, "device.new");
        }

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
                var staleThreshold = NetworkConfig.Default.PeerStaleThreshold;
                var removedAny = false;

                var keysToRemove = _devices.Where(kvp => now - kvp.Value.LastSeen > staleThreshold).Select(kvp => kvp.Key).ToList();
                foreach (var key in keysToRemove)
                {
                    if (_devices.TryRemove(key, out var removed))
                    {
                        Log($"REMOVED stale: {removed.Name} at {removed.Address}", LogLevel.Info, "device.removed.stale");
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
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _cts?.Cancel();
        _cts?.Dispose();
        _discoveryService?.Dispose();
        NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAddressChanged;
    }
}
