using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
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
    private const int DiscoveryPort = 55001;
    private readonly UdpClient _udpListener;
    private readonly UdpClient _udpBroadcaster;
    private CancellationTokenSource? _cts;
    
    public event EventHandler<PeerDiscoveredEventArgs>? PeerDiscovered;

    public DiscoveryService()
    {
        _udpListener = new UdpClient();
        _udpListener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _udpListener.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));

        _udpBroadcaster = new UdpClient();
        _udpBroadcaster.EnableBroadcast = true;
    }

    public void Start(string computerName, int tcpPort)
    {
        _cts = new CancellationTokenSource();
        
        // Start listening
        _ = Task.Run(() => ListenAsync(_cts.Token));
        
        // Start broadcasting
        _ = Task.Run(() => BroadcastLoopAsync(computerName, tcpPort, _cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await _udpListener.ReceiveAsync(cancellationToken);
                var json = Encoding.UTF8.GetString(result.Buffer);
                
                try
                {
                    var message = JsonSerializer.Deserialize<DiscoveryMessage>(json);
                    if (message != null && message.Type == "HELLO")
                    {
                        PeerDiscovered?.Invoke(this, new PeerDiscoveredEventArgs(message, result.RemoteEndPoint.Address));
                    }
                }
                catch (JsonException)
                {
                    // Ignore malformed packets
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (SocketException)
        {
            // Socket closed
        }
    }

    private async Task BroadcastLoopAsync(string computerName, int tcpPort, CancellationToken cancellationToken)
    {
        var message = new DiscoveryMessage
        {
            Type = "HELLO",
            ComputerName = computerName,
            TcpPort = tcpPort
        };
        var json = JsonSerializer.Serialize(message);
        var bytes = Encoding.UTF8.GetBytes(json);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var broadcastAddresses = NetworkHelper.GetBroadcastAddresses().Distinct().ToList();

                foreach (var address in broadcastAddresses)
                {
                    try
                    {
                        var endpoint = new IPEndPoint(address, DiscoveryPort);
                        await _udpBroadcaster.SendAsync(bytes, bytes.Length, endpoint);
                    }
                    catch
                    {
                        // Ignore individual endpoint broadcast failures
                    }
                }

                await Task.Delay(2000, cancellationToken); // Broadcast every 2 seconds
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception)
        {
            // Network interface might be down, ignore and retry later
            await Task.Delay(2000, cancellationToken);
        }
    }

    public void Dispose()
    {
        Stop();
        _udpListener.Dispose();
        _udpBroadcaster.Dispose();
    }
}
