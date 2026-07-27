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
    
    // Track active UDP clients by their local bound IP
    private readonly ConcurrentDictionary<string, UdpClient> _listeners = new();
    
    public event EventHandler<PeerDiscoveredEventArgs>? PeerDiscovered;

    public void Start(string computerName, int tcpPort)
    {
        _cts = new CancellationTokenSource();
        
        // Start the master network loop
        _ = Task.Run(() => NetworkLoopAsync(computerName, tcpPort, _cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
    }

    private async Task NetworkLoopAsync(string computerName, int tcpPort, CancellationToken cancellationToken)
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

                // 1. Clean up stale listeners (if cable was unplugged or IP changed)
                var toRemove = _listeners.Keys.Where(ip => !currentIps.Contains(ip)).ToList();
                foreach (var ip in toRemove)
                {
                    if (_listeners.TryRemove(ip, out var oldClient))
                    {
                        oldClient.Dispose();
                    }
                }

                // 2. Start listeners for new Ethernet interfaces
                foreach (var netIf in currentInterfaces)
                {
                    var ipStr = netIf.LocalAddress.ToString();
                    if (!_listeners.ContainsKey(ipStr))
                    {
                        try
                        {
                            var client = new UdpClient();
                            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                            
                            // Bind strictly to this specific Ethernet adapter's IP. 
                            // This guarantees traffic only comes from and goes to the Ethernet cable!
                            client.Client.Bind(new IPEndPoint(netIf.LocalAddress, DiscoveryPort));
                            
                            client.EnableBroadcast = true;
                            _listeners.TryAdd(ipStr, client);
                            
                            // Start listening on this specific socket
                            _ = Task.Run(() => ListenAsync(client, cancellationToken));
                        }
                        catch
                        {
                            // Port might be exclusively locked or IP invalid, ignore
                        }
                    }
                }

                // 3. Broadcast out of all active Ethernet sockets
                foreach (var netIf in currentInterfaces)
                {
                    var ipStr = netIf.LocalAddress.ToString();
                    if (_listeners.TryGetValue(ipStr, out var client))
                    {
                        try
                        {
                            // Send explicitly to this interface's broadcast address (e.g. 169.254.255.255)
                            var broadcastEndpoint = new IPEndPoint(netIf.BroadcastAddress, DiscoveryPort);
                            await client.SendAsync(bytes, bytes.Length, broadcastEndpoint);
                        }
                        catch
                        { }
                    }
                }

                // Broadcast every 2 seconds as recommended
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
                        PeerDiscovered?.Invoke(this, new PeerDiscoveredEventArgs(message, result.RemoteEndPoint.Address));
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
        foreach (var client in _listeners.Values)
        {
            client.Dispose();
        }
        _listeners.Clear();
    }
}
