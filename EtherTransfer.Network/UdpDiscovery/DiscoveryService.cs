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
    private int _tcpPort;
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
        Stop(sendBye: false);

        _computerName = computerName;
        _tcpPort = tcpPort;
        _cts = new CancellationTokenSource();

        // Global listener on 0.0.0.0:<DiscoveryPort> to receive ALL broadcast packets
        try
        {
            _globalListener = new UdpClient();
            _globalListener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _globalListener.Client.Bind(new IPEndPoint(IPAddress.Any, _config.DiscoveryPort));
            
            if (!isRebind)
            {
                Log($"Starting discovery as '{computerName}' on port {_config.DiscoveryPort}");
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

    public void Stop(bool sendBye = false)
    {
        if (sendBye && _cts != null && !_cts.IsCancellationRequested)
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
                    using var sender = new UdpClient(netIf.LocalAddress.AddressFamily);
                    sender.Client.Bind(new IPEndPoint(netIf.LocalAddress, 0));
                    if (netIf.LocalAddress.AddressFamily == AddressFamily.InterNetwork)
                    {
                        sender.EnableBroadcast = true;
                    }
                    var target = new IPEndPoint(netIf.BroadcastAddress, _config.DiscoveryPort);
                    sender.Send(payload, payload.Length, target);
                }
                catch
                {
                    try
                    {
                        using var fallbackSender = new UdpClient();
                        fallbackSender.EnableBroadcast = true;
                        fallbackSender.Send(payload, payload.Length, new IPEndPoint(netIf.BroadcastAddress, _config.DiscoveryPort));
                    }
                    catch { }
                }
            }
        }
        catch { }
    }

    private async Task BroadcastLoopAsync(int tcpPort, CancellationToken ct)
    {
        try
        {
            int loopCount = 0;
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

                // Send to each Ethernet interface's subnet broadcast
                foreach (var netIf in ethInterfaces)
                {
                    try
                    {
                        using var sender = new UdpClient(netIf.LocalAddress.AddressFamily);
                        sender.Client.Bind(new IPEndPoint(netIf.LocalAddress, 0));
                        if (netIf.LocalAddress.AddressFamily == AddressFamily.InterNetwork)
                        {
                            sender.EnableBroadcast = true;
                        }
                        var target = new IPEndPoint(netIf.BroadcastAddress, _config.DiscoveryPort);
                        await sender.SendAsync(payload, payload.Length, target);
                    }
                    catch
                    {
                        // Fallback if local address is in tentative/DAD transition on Windows
                        try
                        {
                            using var fallbackSender = new UdpClient();
                            fallbackSender.EnableBroadcast = true;
                            await fallbackSender.SendAsync(payload, payload.Length, new IPEndPoint(netIf.BroadcastAddress, _config.DiscoveryPort));
                            await fallbackSender.SendAsync(payload, payload.Length, new IPEndPoint(IPAddress.Broadcast, _config.DiscoveryPort));
                        }
                        catch (Exception fallbackEx)
                        {
                            Log($"Send failed on {netIf.LocalAddress}: {fallbackEx.Message}", LogLevel.Warning, "discovery.send.failed");
                        }
                    }
                }

                // If no specific Ethernet interfaces resolved yet, attempt global broadcast
                if (ethInterfaces.Count == 0)
                {
                    try
                    {
                        using var globalSender = new UdpClient();
                        globalSender.EnableBroadcast = true;
                        await globalSender.SendAsync(payload, payload.Length, new IPEndPoint(IPAddress.Broadcast, _config.DiscoveryPort));
                    }
                    catch { }
                }

                loopCount++;

                // Fast discovery burst on startup/rebind: 250ms, 500ms, 1000ms, then normal interval
                int delayMs;
                if (loopCount <= 2)
                    delayMs = 250;
                else if (loopCount <= 4)
                    delayMs = 500;
                else if (loopCount <= 6)
                    delayMs = 1000;
                else
                    delayMs = _config.BroadcastIntervalMs;

                await Task.Delay(delayMs, ct);
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
                            var sourceIpStr = result.RemoteEndPoint.Address.ToString();
                            if (NetworkHelper.IsIpInActiveSubnets(sourceIpStr))
                            {
                                PeerDiscovered?.Invoke(this, new PeerDiscoveredEventArgs(message, result.RemoteEndPoint.Address));

                                // Immediate directed HELLO reply for instant discovery handshake
                                if (message.Type == "HELLO")
                                {
                                    _ = SendDirectHelloAsync(result.RemoteEndPoint.Address);
                                }
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

    private async Task SendDirectHelloAsync(IPAddress targetAddress)
    {
        try
        {
            var reply = new DiscoveryMessage
            {
                Type = "HELLO",
                ComputerName = _computerName,
                TcpPort = _tcpPort,
                Id = AppId,
                SessionId = _sessionId,
                OS = GetCurrentOS()
            };
            var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(reply));

            using var responder = new UdpClient();
            responder.EnableBroadcast = true;
            await responder.SendAsync(payload, payload.Length, new IPEndPoint(targetAddress, _config.DiscoveryPort));
        }
        catch { }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            Stop(sendBye: true);
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
