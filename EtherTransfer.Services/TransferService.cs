using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EtherTransfer.Core.Models;
using EtherTransfer.Network;
using EtherTransfer.Transfer;

namespace EtherTransfer.Services;

public class TransferService : IDisposable
{
    private readonly TcpServer _server;
    private readonly CancellationTokenSource _cts;
    private readonly string _computerName;
    
    public event EventHandler<string>? DebugLog;
    public event EventHandler<TransferProgressEventArgs>? ProgressUpdated;

    // Delegate to ask the UI for permission
    public Func<TransferRequestMessage, Task<(bool accept, string savePath)>>? OnIncomingTransfer { get; set; }

    private void Log(string msg) => DebugLog?.Invoke(this, $"[TransferService] {msg}");

    public TransferService(string computerName, int tcpPort)
    {
        _computerName = computerName;
        _cts = new CancellationTokenSource();
        _server = new TcpServer(tcpPort);
        
        _server.DebugLog += (_, msg) => DebugLog?.Invoke(this, msg);
        _server.OnClientConnected = HandleIncomingClient;
    }

    public void Start()
    {
        _server.Start();
    }

    public void Stop()
    {
        _cts.Cancel();
        _server.Stop();
    }

    private async void HandleIncomingClient(System.Net.Sockets.TcpClient client)
    {
        var receiver = new TransferReceiver();
        receiver.DebugLog += (_, msg) => DebugLog?.Invoke(this, msg);
        receiver.ProgressUpdated += (_, e) => ProgressUpdated?.Invoke(this, e);
        receiver.OnIncomingTransfer = OnIncomingTransfer;

        await receiver.HandleClientAsync(client, _cts.Token);
    }

    public Task<PayloadItem> ScanItemAsync(string path)
    {
        var sender = new TransferSender();
        sender.DebugLog += (_, msg) => DebugLog?.Invoke(this, msg);
        
        return sender.ScanItemAsync(path);
    }

    public async Task TransmitSessionAsync(string targetIp, int targetPort, TransferSession session, CancellationToken userCt = default)
    {
        var sender = new TransferSender();
        sender.DebugLog += (_, msg) => DebugLog?.Invoke(this, msg);
        sender.ProgressUpdated += (_, e) => ProgressUpdated?.Invoke(this, e);

        // Run send in background task so UI doesn't block
        await Task.Run(async () =>
        {
            try
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, userCt);
                await sender.TransmitSessionAsync(targetIp, targetPort, _computerName, session, linkedCts.Token);
            }
            catch (Exception ex)
            {
                Log($"Send failed: {ex.Message}");
                throw;
            }
        });
    }

    public void Dispose()
    {
        Stop();
        _server.Dispose();
        _cts.Dispose();
    }
}
