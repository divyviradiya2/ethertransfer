using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EtherTransfer.Core.Models;
using EtherTransfer.Network.NetworkInterfaces;

namespace EtherTransfer.Network.UdpDiscovery;

public class PeerDiscoveredEventArgs : EventArgs
{
    public DiscoveryMessage Message { get; }
    public IPAddress SourceAddress { get; }

    public PeerDiscoveredEventArgs(DiscoveryMessage message, IPAddress sourceAddress)
    {
        Message = message;
        SourceAddress = sourceAddress;
    }
}

public class DiscoveryService : IDisposable
{
    private const int DiscoveryPort = 50000;
    private const string AppId = "EtherTransferApp-V1";
    
    private CancellationTokenSource? _cts;
    private UdpClient? _globalListener;
    private string _computerName = string.Empty;
    
    public event EventHandler<PeerDiscoveredEventArgs>? PeerDiscovered;
    
    // Debug log for the UI
    public event EventHandler<string>? DebugLog;

    private void Log(string msg)
    {
        DebugLog?.Invoke(this, $"[{DateTime.Now:HH:mm:ss}] {msg}");
    }

    public void Start(string computerName, int tcpPort)
    {
        _computerName = computerName;
        _cts = new CancellationTokenSource();
        
        Log($"Starting discovery as '{computerName}' on port {DiscoveryPort}");
        
        // Auto-configure Ethernet on Linux (assign link-local IP if missing)
        var configLog = EthernetConfigurator.EnsureEthernetReady();
        foreach (var line in configLog)
        {
            Log(line);
        }
        
        // Run diagnostics
        var diag = NetworkHelper.DiagnoseInterfaces();
        foreach (var line in diag)
        {
            Log(line);
        }
        
        // Global listener on 0.0.0.0:50000 to receive ALL broadcast packets
        try
        {
            _globalListener = new UdpClient();
            _globalListener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _globalListener.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
            Log("Listener bound to 0.0.0.0:" + DiscoveryPort);
            _ = Task.Run(() => ListenAsync(_globalListener, _cts.Token));
        }
        catch (Exception ex)
        {
            Log($"FAILED to bind listener: {ex.Message}");
        }
        
        // Start broadcast loop
        _ = Task.Run(() => BroadcastLoopAsync(tcpPort, _cts.Token));
    }

    public void UpdateComputerName(string newName)
    {
        _computerName = newName;
        Log($"Discovery name updated to '{newName}'");
    }

    public void Stop()
    {
        if (_cts != null && !_cts.IsCancellationRequested)
        {
            SendBye();
        }
        _cts?.Cancel();
        
        try
        {
            _globalListener?.Dispose();
            _globalListener = null;
        }
        catch { }
    }

    private void SendBye()
    {
        try
        {
            var message = new DiscoveryMessage
            {
                Type = "BYE",
                ComputerName = _computerName,
                TcpPort = 0,
                Id = AppId,
                OS = GetCurrentOS()
            };
            var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
            
            var ethInterfaces = NetworkHelper.GetEthernetInterfaces().ToList();
            foreach (var netIf in ethInterfaces)
            {
                try
                {
                    using var sender = new UdpClient();
                    sender.Client.Bind(new IPEndPoint(netIf.LocalAddress, 0));
                    sender.EnableBroadcast = true;
                    var target = new IPEndPoint(netIf.BroadcastAddress, DiscoveryPort);
                    sender.Send(payload, payload.Length, target);
                }
                catch { }
            }
            
            try
            {
                using var globalSender = new UdpClient();
                globalSender.EnableBroadcast = true;
                var globalTarget = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);
                globalSender.Send(payload, payload.Length, globalTarget);
            }
            catch { }
            
            Log("Broadcasted BYE message.");
        }
        catch { }
    }

    private async Task BroadcastLoopAsync(int tcpPort, CancellationToken ct)
    {
        // One persistent sender for the global 255.255.255.255 broadcast
        UdpClient? globalSender = null;
        try
        {
            globalSender = new UdpClient();
            globalSender.EnableBroadcast = true;
            Log("Global broadcast sender created");
        }
        catch (Exception ex)
        {
            Log($"Failed to create global sender: {ex.Message}");
        }

        bool wasEmpty = false;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Get all Ethernet interface broadcast addresses
                var ethInterfaces = NetworkHelper.GetEthernetInterfaces().ToList();
                
                if (ethInterfaces.Count == 0)
                {
                    if (!wasEmpty)
                    {
                        Log("No Ethernet interfaces found. Waiting...");
                        wasEmpty = true;
                    }
                }
                else
                {
                    wasEmpty = false;
                }

                // Re-build message each loop to pick up any custom name changes
                var message = new DiscoveryMessage
                {
                    Type = "HELLO",
                    ComputerName = _computerName,
                    TcpPort = tcpPort,
                    Id = AppId,
                    OS = GetCurrentOS()
                };
                var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));

                // Strategy 1: Send to each Ethernet interface's subnet broadcast
                foreach (var netIf in ethInterfaces)
                {
                    try
                    {
                        using var sender = new UdpClient();
                        sender.Client.Bind(new IPEndPoint(netIf.LocalAddress, 0));
                        sender.EnableBroadcast = true;
                        var target = new IPEndPoint(netIf.BroadcastAddress, DiscoveryPort);
                        await sender.SendAsync(payload, payload.Length, target);
                    }
                    catch (Exception ex)
                    {
                        Log($"Send failed on {netIf.LocalAddress}: {ex.Message}");
                    }
                }
                
                // Strategy 2: Also send global 255.255.255.255 broadcast as fallback
                if (globalSender != null)
                {
                    try
                    {
                        var globalTarget = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);
                        await globalSender.SendAsync(payload, payload.Length, globalTarget);
                    }
                    catch (Exception)
                    {
                        // Some Linux networks reject 255.255.255.255 entirely, this is harmless if subnet broadcasts worked.
                    }
                }

                await Task.Delay(2000, ct);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            globalSender?.Dispose();
        }
    }

    private async Task ListenAsync(UdpClient listener, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var result = await listener.ReceiveAsync(ct);
                var json = Encoding.UTF8.GetString(result.Buffer);
                
                try
                {
                    var message = JsonSerializer.Deserialize<DiscoveryMessage>(json);
                    
                    if (message != null && (message.Type == "HELLO" || message.Type == "BYE") && message.Id == AppId)
                    {
                        PeerDiscovered?.Invoke(this, new PeerDiscoveredEventArgs(message, result.RemoteEndPoint.Address));
                    }
                    else
                    {
                        // Ignore silently
                    }
                }
                catch (JsonException)
                {
                    // Ignore silently
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException ex) { Log($"Socket error: {ex.Message}"); }
        catch (ObjectDisposedException) { }
    }

    public void Dispose()
    {
        Stop();
        _globalListener?.Dispose();
        
        // Restore original Ethernet config on Linux
        var restoreLog = EthernetConfigurator.RestoreOriginalConfig();
        foreach (var line in restoreLog)
        {
            Log(line);
        }
    }

    private string GetCurrentOS()
    {
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows)) return "Windows";
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX)) return "macOS";
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux)) return "Linux";
        return "Unknown";
    }
}
