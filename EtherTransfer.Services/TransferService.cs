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

    public int TcpPort => _server.Port;

    public event EventHandler<StructuredLogMessage>? DebugLog;
    public event EventHandler<TransferProgressEventArgs>? ProgressUpdated;
    public event EventHandler<TransferResult>? TransferFinished;

    // Delegate to ask the UI for permission
    public Func<TransferRequestMessage, CancellationToken, Task<(bool accept, string savePath, CancellationToken cancelToken)>>? OnIncomingTransfer { get; set; }

    private void Log(string msg, LogLevel level = LogLevel.Info, string eventId = "transfer.log") => DebugLog?.Invoke(this, new StructuredLogMessage(eventId, msg, level));

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
        try
        {
            var receiver = new TransferReceiver();
            receiver.DebugLog += (_, msg) => DebugLog?.Invoke(this, msg);
            receiver.ProgressUpdated += (_, e) => ProgressUpdated?.Invoke(this, e);
            receiver.OnIncomingTransfer = OnIncomingTransfer;

            var result = await receiver.HandleClientAsync(client, _cts.Token);
            TransferFinished?.Invoke(this, result);
        }
        catch (Exception ex)
        {
            Log($"Unhandled error in incoming client handler: {ex.Message}", LogLevel.Error);
            try { client.Dispose(); } catch { }
        }
    }

    public Task<PayloadItem> ScanItemAsync(string path, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var sender = new TransferSender();
        sender.DebugLog += (_, msg) => DebugLog?.Invoke(this, msg);

        return sender.ScanItemAsync(path, progress, ct);
    }

    public async Task TransmitSessionAsync(string targetIp, int targetPort, TransferSession session, CancellationToken userCt = default)
    {
        var sender = new TransferSender();
        sender.DebugLog += (_, msg) => DebugLog?.Invoke(this, msg);
        sender.ProgressUpdated += (_, e) => ProgressUpdated?.Invoke(this, e);

        // Run send in background task so UI doesn't block
        await Task.Run(async () =>
        {
            TransferResult result;
            try
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, userCt);
                result = await sender.TransmitSessionAsync(targetIp, targetPort, _computerName, session, linkedCts.Token);
            }
            catch (Exception ex)
            {
                Log($"Send failed: {ex.Message}");
                result = new TransferResult { Success = false, ErrorMessage = ex.Message };
            }
            TransferFinished?.Invoke(this, result);
        }, userCt);
    }

    public void Dispose()
    {
        Stop();
        _server.Dispose();
        _cts.Dispose();
    }
}
