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
    
    // Track active UDP clients (used only for sending) by their local bound IP
    private readonly ConcurrentDictionary<string, UdpClient> _senders = new();
    
    public event EventHandler<PeerDiscoveredEventArgs>? PeerDiscovered;

    public void Start(string computerName, int tcpPort)
    {
        _cts = new CancellationTokenSource();
        
        try
        {
            // 1. Global listener to receive broadcast packets
            _globalListener = new UdpClient();
            _globalListener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _globalListener.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
            
            _ = Task.Run(() => ListenAsync(_globalListener, _cts.Token));
        }
        catch { }
        
        // 2. Start the master network loop for broadcasting
        _ = Task.Run(() => BroadcastLoopAsync(computerName, tcpPort, _cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
    }

    private async Task BroadcastLoopAsync(string computerName, int tcpPort, CancellationToken cancellationToken)
    {
        var message = new DiscoveryMessage
        {
            Type = "HELLO",
            ComputerName = computerName,
            TcpPort = tcpPort,
            Id = AppId
        };
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var currentInterfaces = NetworkHelper.GetEthernetInterfaces().ToList();
                var currentIps = currentInterfaces.Select(i => i.LocalAddress.ToString()).ToHashSet();

                // 1. Clean up stale senders
                var toRemove = _senders.Keys.Where(ip => !currentIps.Contains(ip)).ToList();
                foreach (var ip in toRemove)
                {
                    if (_senders.TryRemove(ip, out var oldSender))
                    {
                        oldSender.Dispose();
                    }
                }

                // 2. Start senders for new Ethernet interfaces
                foreach (var netIf in currentInterfaces)
                {
                    var ipStr = netIf.LocalAddress.ToString();
                    if (!_senders.ContainsKey(ipStr))
                    {
                        try
                        {
                            var sender = new UdpClient();
                            // Bind explicitly so packets originate from this exact Ethernet interface
                            sender.Client.Bind(new IPEndPoint(netIf.LocalAddress, 0));
                            sender.EnableBroadcast = true;
                            _senders.TryAdd(ipStr, sender);
                        }
                        catch { }
                    }
                }

                // 3. Broadcast out of all active Ethernet sockets
                foreach (var netIf in currentInterfaces)
                {
                    var ipStr = netIf.LocalAddress.ToString();
                    if (_senders.TryGetValue(ipStr, out var sender))
                    {
                        try
                        {
                            // Send explicitly to this interface's broadcast address (e.g. 169.254.255.255)
                            var broadcastEndpoint = new IPEndPoint(netIf.BroadcastAddress, DiscoveryPort);
                            await sender.SendAsync(bytes, bytes.Length, broadcastEndpoint);
                        }
                        catch { }
                    }
                }

                await Task.Delay(2000, cancellationToken);
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task ListenAsync(UdpClient listener, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await listener.ReceiveAsync(cancellationToken);
                var json = Encoding.UTF8.GetString(result.Buffer);
                
                try
                {
                    var message = JsonSerializer.Deserialize<DiscoveryMessage>(json);
                    
                    // Validate App ID to ignore random noise on Port 50000
                    if (message != null && message.Type == "HELLO" && message.Id == AppId)
                    {
                        if (NetworkHelper.IsOnEthernetSubnet(result.RemoteEndPoint.Address))
                        {
                            PeerDiscovered?.Invoke(this, new PeerDiscoveredEventArgs(message, result.RemoteEndPoint.Address));
                        }
                    }
                }
                catch (JsonException)
                { }
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException) { }
        catch (ObjectDisposedException) { }
    }

    public void Dispose()
    {
        Stop();
        _globalListener?.Dispose();
        foreach (var sender in _senders.Values)
        {
            sender.Dispose();
        }
        _senders.Clear();
    }
}
