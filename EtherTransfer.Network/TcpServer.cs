using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using EtherTransfer.Core.Models;

namespace EtherTransfer.Network;

public class TcpServer : IDisposable
{
    private readonly int _port;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;

    public int Port { get; private set; }

    // Action to handle incoming client connections.
    // The handler is responsible for taking ownership of the TcpClient and eventually disposing it.
    public Action<TcpClient>? OnClientConnected { get; set; }

    public event EventHandler<StructuredLogMessage>? DebugLog;

    private void Log(string msg, LogLevel level = LogLevel.Info, string eventId = "tcpserver.log")
    {
        DebugLog?.Invoke(this, new StructuredLogMessage(eventId, msg, level));
    }

    public TcpServer(int port)
    {
        _port = port;
        Port = port;
    }

    public void Start()
    {
        Stop(); // Ensure any previous listener is stopped

        _cts = new CancellationTokenSource();

        try
        {
            // Bind to IPAddress.Any (0.0.0.0) to accept connections on all available IPv4 interfaces.
            // This is robust for direct Ethernet connections where the interface has an APIPA address.
            _listener = new TcpListener(IPAddress.Any, _port);

            // Allow address reuse to prevent port exhaustion/locking issues across restarts
            _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

            _listener.Start();
            
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Log($"Started listening on 0.0.0.0:{Port}");

            // Start accepting connections in the background
            _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
        }
        catch (Exception ex)
        {
            Log($"Failed to start TCP Server on port {_port}: {ex.Message}", LogLevel.Error);
            throw;
        }
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (_listener == null) break;

                // Accept incoming connection
                var client = await _listener.AcceptTcpClientAsync(ct);

                var remoteEp = client.Client.RemoteEndPoint?.ToString();
                Log($"Accepted connection from {remoteEp}");

                // Fire the event on a background thread so we don't block the accept loop
                if (OnClientConnected != null)
                {
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            OnClientConnected(client);
                        }
                        catch (Exception ex)
                        {
                            Log($"Error in client handler for {remoteEp}: {ex.Message}");
                            client.Dispose();
                        }
                    }, ct);
                }
                else
                {
                    // No handler registered, drop the connection
                    Log($"No handler registered. Dropping connection from {remoteEp}.");
                    client.Dispose();
                }
            }
        }
        catch (OperationCanceledException)
        {
            Log("Accept loop canceled.");
        }
        catch (SocketException ex)
        {
            Log($"Socket error in accept loop: {ex.Message}");
        }
        catch (ObjectDisposedException)
        {
            Log("Listener disposed.");
        }
        finally
        {
            _listener?.Stop();
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listener?.Stop();
        Log("Stopped.");
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            Stop();
            _cts?.Dispose();
            // _listener does not have a public Dispose method in older targets, 
            // but in some .NET targets it implements IDisposable implicitly.
            (_listener as IDisposable)?.Dispose();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
