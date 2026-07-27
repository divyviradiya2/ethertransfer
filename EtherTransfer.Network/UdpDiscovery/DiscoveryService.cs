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
    
    public event EventHandler<PeerDiscoveredEventArgs>? PeerDiscovered;
    
    // Debug log for the UI
    public event EventHandler<string>? DebugLog;

    private void Log(string msg)
    {
        DebugLog?.Invoke(this, $"[{DateTime.Now:HH:mm:ss}] {msg}");
    }

    public void Start(string computerName, int tcpPort)
    {
        _cts = new CancellationTokenSource();
        
        Log($"Starting discovery as '{computerName}' on port {DiscoveryPort}");
        
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
        _ = Task.Run(() => BroadcastLoopAsync(computerName, tcpPort, _cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
    }

    private async Task BroadcastLoopAsync(string computerName, int tcpPort, CancellationToken ct)
    {
        var message = new DiscoveryMessage
        {
            Type = "HELLO",
            ComputerName = computerName,
            TcpPort = tcpPort,
            Id = AppId
        };
        var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));

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

        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Get all Ethernet interface broadcast addresses
                var ethInterfaces = NetworkHelper.GetEthernetInterfaces().ToList();
                
                if (ethInterfaces.Count == 0)
                {
                    Log("No Ethernet interfaces found. Waiting...");
                }

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
                        Log($"Sent to {netIf.BroadcastAddress}:{DiscoveryPort} via {netIf.LocalAddress}");
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
                        Log($"Sent global broadcast to 255.255.255.255:{DiscoveryPort}");
                    }
                    catch (Exception ex)
                    {
                        Log($"Global broadcast failed: {ex.Message}");
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
                    
                    if (message != null && message.Type == "HELLO" && message.Id == AppId)
                    {
                        Log($"Received HELLO from '{message.ComputerName}' at {result.RemoteEndPoint.Address}");
                        PeerDiscovered?.Invoke(this, new PeerDiscoveredEventArgs(message, result.RemoteEndPoint.Address));
                    }
                    else
                    {
                        Log($"Ignored packet from {result.RemoteEndPoint}: wrong type/id");
                    }
                }
                catch (JsonException)
                {
                    Log($"Ignored non-JSON packet from {result.RemoteEndPoint}");
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
    }
}
