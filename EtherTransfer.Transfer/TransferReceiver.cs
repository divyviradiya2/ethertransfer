using System;
using System.IO;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EtherTransfer.Core.Models;

namespace EtherTransfer.Transfer;

public class IncomingTransferEventArgs : EventArgs
{
    public TransferRequestMessage Request { get; }

    public bool Accept { get; set; }
    public string SaveDirectory { get; set; } = string.Empty;

    public IncomingTransferEventArgs(TransferRequestMessage request)
    {
        Request = request;
    }
}

public class TransferReceiver
{
    public Func<TransferRequestMessage, CancellationToken, Task<(bool accept, string savePath, CancellationToken cancelToken)>>? OnIncomingTransfer { get; set; }

    public event EventHandler<TransferProgressEventArgs>? ProgressUpdated;
    public event EventHandler<string>? DebugLog;
    public event EventHandler<(bool success, string? error)>? TransferFinished;

    private void Log(string msg) => DebugLog?.Invoke(this, $"[{DateTime.Now:HH:mm:ss}] [Receiver] {msg}");

    public async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        var remoteEp = client.Client.RemoteEndPoint?.ToString();
        Log($"Handling incoming connection from {remoteEp}");

        try
        {
            using (client)
            {
                var stream = client.GetStream();

                // 1. Wait for Request
                var request = await ProtocolHelper.ReceiveMessageAsync<TransferRequestMessage>(stream, ct);
                if (request == null)
                    throw new Exception("Did not receive TransferRequest.");

                Log($"Incoming: {request.SenderName} — {request.TotalFiles} files, {request.TotalSize / 1024 / 1024} MB");

                // 2. Ask UI
                if (OnIncomingTransfer == null)
                    throw new Exception("No UI handler attached for incoming transfers.");

                using var disconnectCts = new CancellationTokenSource();
                using var uiCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

                var uiTask = OnIncomingTransfer(request, uiCts.Token);

                var disconnectTask = Task.Run(async () =>
                {
                    while (!disconnectCts.Token.IsCancellationRequested)
                    {
                        try
                        {
                            if (client.Client.Poll(1000, SelectMode.SelectRead) && client.Client.Available == 0)
                                return true;
                        }
                        catch { return true; }
                        await Task.Delay(200, disconnectCts.Token);
                    }
                    return false;
                });

                var finishedTask = await Task.WhenAny(uiTask, disconnectTask);

                if (finishedTask == disconnectTask)
                {
                    uiCts.Cancel();
                    Log("Sender disconnected before request was accepted.");
                    return;
                }

                disconnectCts.Cancel();
                var (accepted, savePath, cancelToken) = await uiTask;

                using var linkedCt = CancellationTokenSource.CreateLinkedTokenSource(ct, cancelToken);
                var transferCt = linkedCt.Token;

                // 3. Send Response
                var response = new TransferResponseMessage
                {
                    Accepted = accepted,
                    Reason = accepted ? "" : "User declined."
                };
                await ProtocolHelper.SendMessageAsync(stream, response, transferCt);

                if (!accepted)
                {
                    Log("Transfer declined by user.");
                    return;
                }

                Directory.CreateDirectory(savePath);
                Log($"Transfer accepted. Saving to: {savePath}");

                // 4. Receive Files
                long totalReceived = 0;
                int filesReceived = 0;
                int filesSkipped = 0;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var buffer = new byte[1024 * 1024];

                while (!transferCt.IsCancellationRequested)
                {
                    var markerJson = await ProtocolHelper.ReceiveRawJsonAsync(stream, transferCt);
                    if (markerJson == null) break;

                    var baseMsg = JsonSerializer.Deserialize<BaseProtocolMessage>(markerJson);
                    if (baseMsg == null) break;

                    if (baseMsg.Type == "TRANSFER_END")
                        break;

                    if (baseMsg.Type == "FILE_SKIP")
                    {
                        var skipMsg = JsonSerializer.Deserialize<FileSkipMessage>(markerJson);
                        if (skipMsg != null)
                            Log($"Sender skipped: {skipMsg.RelativePath} — {skipMsg.Reason}");
                        filesSkipped++;
                        continue;
                    }

                    if (baseMsg.Type != "FILE_BEGIN")
                        continue;

                    // FILE_BEGIN — read metadata
                    var fileMeta = await ProtocolHelper.ReceiveMessageAsync<FileItemMetadata>(stream, transferCt);
                    if (fileMeta == null) break;

                    // === PATH SECURITY ===
                    var safePath = PathSanitizer.SanitizeRelativePath(savePath, fileMeta.RelativePath);
                    if (safePath == null)
                    {
                        Log($"SECURITY: Blocked malicious path: {fileMeta.RelativePath}");
                        await DrainBytesAsync(stream, fileMeta.Size, buffer, transferCt);
                        filesSkipped++;
                        continue;
                    }

                    // === COLLISION RESOLUTION ===
                    safePath = PathSanitizer.ResolveCollision(safePath);

                    var dirPath = Path.GetDirectoryName(safePath);
                    if (dirPath != null) Directory.CreateDirectory(dirPath);

                    try
                    {
                        using var fs = new FileStream(safePath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true);
                        
                        long fileReceived = 0;

                        var elapsedSecInitial = watch.Elapsed.TotalSeconds;
                        var initialSpeed = elapsedSecInitial > 0 ? (totalReceived / 1024.0 / 1024.0) / elapsedSecInitial : 0;

                        ProgressUpdated?.Invoke(this, new TransferProgressEventArgs
                        {
                            CurrentFile = fileMeta.RelativePath,
                            BytesSent = totalReceived,
                            TotalBytes = request.TotalSize,
                            SpeedMbPerSec = initialSpeed
                        });

                        var lastUpdate = watch.ElapsedMilliseconds;

                        using var watchdogCts = CancellationTokenSource.CreateLinkedTokenSource(transferCt);

                        while (fileReceived < fileMeta.Size)
                        {
                            watchdogCts.CancelAfter(15000); // 15 seconds to receive 1MB
                            
                            int toRead = (int)Math.Min(buffer.Length, fileMeta.Size - fileReceived);
                            
                            try
                            {
                                if (!await ProtocolHelper.ReadExactAsync(stream, buffer, toRead, watchdogCts.Token))
                                    throw new IOException("Connection lost while reading file data.");
                            }
                            catch (OperationCanceledException) when (!transferCt.IsCancellationRequested)
                            {
                                throw new IOException("Connection timed out (Ethernet cable disconnected or network dropped).");
                            }

                            await fs.WriteAsync(buffer, 0, toRead, transferCt);

                            fileReceived += toRead;
                            totalReceived += toRead;

                            var currentElapsed = watch.ElapsedMilliseconds;
                            if (currentElapsed - lastUpdate >= 50 || totalReceived == request.TotalSize)
                            {
                                lastUpdate = currentElapsed;
                                var elapsedSec = watch.Elapsed.TotalSeconds;
                                var speed = elapsedSec > 0 ? (totalReceived / 1024.0 / 1024.0) / elapsedSec : 0;

                                ProgressUpdated?.Invoke(this, new TransferProgressEventArgs
                                {
                                    CurrentFile = fileMeta.RelativePath,
                                    BytesSent = totalReceived,
                                    TotalBytes = request.TotalSize,
                                    SpeedMbPerSec = speed
                                });
                            }
                        }

                        filesReceived++;
                    }
                    catch (Exception ex)
                    {
                        Log($"Error receiving {fileMeta.RelativePath}: {ex.Message}");
                        
                        // Clean up partially received file
                        try
                        {
                            if (File.Exists(safePath))
                                File.Delete(safePath);
                        }
                        catch { }

                        if (ex is OperationCanceledException)
                            throw;
                    }
                }

                watch.Stop();
                Log($"Transfer complete! {totalReceived / 1024 / 1024} MB in {watch.Elapsed.TotalSeconds:F1}s — {filesReceived} received, {filesSkipped} skipped.");
                TransferFinished?.Invoke(this, (true, null));
            }
        }
        catch (OperationCanceledException)
        {
            Log("Transfer cancelled.");
            TransferFinished?.Invoke(this, (false, "Transfer cancelled by user."));
        }
        catch (Exception ex)
        {
            var msg = ex.Message;
            if (ex is IOException || ex is SocketException)
                msg = "Connection lost (Ethernet cable disconnected or sender aborted).";
                
            Log($"Error handling connection: {msg}");
            TransferFinished?.Invoke(this, (false, msg));
        }
    }

    private static async Task DrainBytesAsync(NetworkStream stream, long count, byte[] buffer, CancellationToken ct)
    {
        long drained = 0;
        while (drained < count)
        {
            int toRead = (int)Math.Min(buffer.Length, count - drained);
            if (!await ProtocolHelper.ReadExactAsync(stream, buffer, toRead, ct))
                throw new IOException("Connection lost while draining skipped file data.");
            drained += toRead;
        }
    }
}
