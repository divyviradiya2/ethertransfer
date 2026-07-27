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
    private readonly UdpClient _udpListenerV4;
    private readonly UdpClient _udpListenerV6;
    private readonly UdpClient _udpBroadcasterV4;
    private readonly UdpClient _udpBroadcasterV6;
    private CancellationTokenSource? _cts;
    
    public event EventHandler<PeerDiscoveredEventArgs>? PeerDiscovered;

    public DiscoveryService()
    {
        // IPv4 Listener
        _udpListenerV4 = new UdpClient(AddressFamily.InterNetwork);
        _udpListenerV4.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _udpListenerV4.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));

        // IPv6 Listener
        _udpListenerV6 = new UdpClient(AddressFamily.InterNetworkV6);
        _udpListenerV6.Client.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, true);
        _udpListenerV6.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _udpListenerV6.Client.Bind(new IPEndPoint(IPAddress.IPv6Any, DiscoveryPort));

        // Broadcasters
        _udpBroadcasterV4 = new UdpClient(AddressFamily.InterNetwork);
        _udpBroadcasterV4.EnableBroadcast = true;
        
        _udpBroadcasterV6 = new UdpClient(AddressFamily.InterNetworkV6);
    }

    public void Start(string computerName, int tcpPort)
    {
        _cts = new CancellationTokenSource();
        
        // Start listening on both IPv4 and IPv6
        _ = Task.Run(() => ListenAsync(_udpListenerV4, _cts.Token));
        _ = Task.Run(() => ListenAsync(_udpListenerV6, _cts.Token));
        
        // Start dual-stack broadcasting
        _ = Task.Run(() => BroadcastLoopAsync(computerName, tcpPort, _cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
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
        catch (OperationCanceledException) { }
        catch (SocketException) { }
        catch (ObjectDisposedException) { }
    }

    private async Task BroadcastLoopAsync(string computerName, int tcpPort, CancellationToken cancellationToken)
    {
        var message = new DiscoveryMessage
        {
            Type = "HELLO",
            ComputerName = computerName,
            TcpPort = tcpPort
        };
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
        var ipv6Multicast = new IPEndPoint(IPAddress.Parse("FF02::1"), DiscoveryPort);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // 1. IPv4 Broadcasts (targets Wi-Fi and resolved APIPA networks)
                var broadcastAddresses = NetworkHelper.GetBroadcastAddresses().Distinct().ToList();
                foreach (var address in broadcastAddresses)
                {
                    try
                    {
                        var endpoint = new IPEndPoint(address, DiscoveryPort);
                        await _udpBroadcasterV4.SendAsync(bytes, bytes.Length, endpoint);
                    }
                    catch { }
                }

                // 2. IPv6 Multicast (instant link-local connection over raw Ethernet cables)
                try
                {
                    // "FF02::1" is the all-nodes link-local multicast group for IPv6.
                    // This bypasses DHCP and APIPA delays entirely.
                    await _udpBroadcasterV6.SendAsync(bytes, bytes.Length, ipv6Multicast);
                }
                catch { }

                await Task.Delay(2000, cancellationToken);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            await Task.Delay(2000, cancellationToken);
        }
    }

    public void Dispose()
    {
        Stop();
        _udpListenerV4.Dispose();
        _udpListenerV6.Dispose();
        _udpBroadcasterV4.Dispose();
        _udpBroadcasterV6.Dispose();
    }
}
