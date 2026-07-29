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
using EtherTransfer.Core;
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
    private readonly string _sessionId = Guid.NewGuid().ToString();
    private readonly NetworkConfig _config = NetworkConfig.Default;

    public event EventHandler<PeerDiscoveredEventArgs>? PeerDiscovered;

    // Debug log for the UI
    public event EventHandler<StructuredLogMessage>? DebugLog;

    private void Log(string msg, LogLevel level = LogLevel.Info, string eventId = "discovery.log")
    {
        DebugLog?.Invoke(this, new StructuredLogMessage(eventId, msg, level));
    }

    public async Task StartAsync(string computerName, int tcpPort, bool isRebind = false)
    {
        Stop();

        _computerName = computerName;
        _cts = new CancellationTokenSource();

        if (!isRebind)
        {
            Log($"Starting discovery as '{computerName}' on port {_config.DiscoveryPort}");
        }

        // Global listener on 0.0.0.0:<DiscoveryPort> to receive ALL broadcast packets
        try
        {
            _globalListener = new UdpClient();
            _globalListener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _globalListener.Client.Bind(new IPEndPoint(IPAddress.Any, _config.DiscoveryPort));
            
            if (!isRebind)
            {
                Log("Listener bound to 0.0.0.0:" + _config.DiscoveryPort);
            }
            
            _ = Task.Run(() => ListenAsync(_globalListener, _cts.Token));
        }
        catch (Exception ex)
        {
            Log($"FAILED to bind listener: {ex.Message}", LogLevel.Error, "discovery.bind.error");
            throw; // Must throw to inform UI
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
                SessionId = _sessionId,
                OS = GetCurrentOS()
            };
            var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));

            List<InterfaceAddressInfo> ethInterfaces = new List<InterfaceAddressInfo>();
            try
            {
                ethInterfaces = NetworkHelper.GetEthernetInterfaces().ToList();
            }
            catch (Exception ex)
            {
                Log($"Failed to get interfaces during SendBye: {ex.Message}", LogLevel.Warning, "discovery.sendbye.error");
            }

            foreach (var netIf in ethInterfaces)
            {
                try
                {
                    using var sender = new UdpClient();
                    sender.Client.Bind(new IPEndPoint(netIf.LocalAddress, 0));
                    sender.EnableBroadcast = true;
                    var target = new IPEndPoint(netIf.BroadcastAddress, _config.DiscoveryPort);
                    sender.Send(payload, payload.Length, target);
                }
                catch { }
            }

        }
        catch { }
    }

    private async Task BroadcastLoopAsync(int tcpPort, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Get all Ethernet interface broadcast addresses
                var ethInterfaces = NetworkHelper.GetEthernetInterfaces().ToList();

                // Re-build message each loop to pick up any custom name changes
                var message = new DiscoveryMessage
                {
                    Type = "HELLO",
                    ComputerName = _computerName,
                    TcpPort = tcpPort,
                    Id = AppId,
                    SessionId = _sessionId,
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
                        var target = new IPEndPoint(netIf.BroadcastAddress, _config.DiscoveryPort);
                        await sender.SendAsync(payload, payload.Length, target);
                    }
                    catch (Exception ex)
                    {
                        Log($"Send failed on {netIf.LocalAddress}: {ex.Message}", LogLevel.Warning, "discovery.send.failed");
                    }
                }

                await Task.Delay(_config.BroadcastIntervalMs, ct);
            }
        }
        catch (OperationCanceledException) { }
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
                        if (message.SessionId != _sessionId)
                        {
                            // Enterprise Rule: Strictly verify the packet originated from a known Ethernet subnet.
                            // This drops any discovery packets that leaked in via Wi-Fi adapters.
                            var sourceIpStr = result.RemoteEndPoint.Address.ToString();
                            if (NetworkHelper.IsIpInActiveSubnets(sourceIpStr))
                            {
                                PeerDiscovered?.Invoke(this, new PeerDiscoveredEventArgs(message, result.RemoteEndPoint.Address));
                            }
                            else
                            {
                                // Log($"Dropped packet from {sourceIpStr}: Not on an active Ethernet subnet", LogLevel.Debug, "discovery.listen.filtered");
                            }
                        }
                    }
                }
                catch (JsonException)
                {
                    // Ignore silently
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException ex) { Log($"Socket error: {ex.Message}", LogLevel.Error, "discovery.listen.error"); }
        catch (ObjectDisposedException) { }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            Stop();
            _cts?.Dispose();
            _globalListener?.Dispose();


        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private static string GetCurrentOS()
    {
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows)) return "Windows";
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX)) return "macOS";
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux)) return "Linux";
        return "Unknown";
    }
}
