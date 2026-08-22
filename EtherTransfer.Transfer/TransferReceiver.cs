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
    public event EventHandler<StructuredLogMessage>? DebugLog;

    private void Log(string msg, LogLevel level = LogLevel.Info, string eventId = "receiver.log") => DebugLog?.Invoke(this, new StructuredLogMessage(eventId, msg, level));

    public async Task<TransferResult> HandleClientAsync(TcpClient client, CancellationToken appCt)
    {
        var result = new TransferResult();
        var remoteEp = client.Client.RemoteEndPoint?.ToString();
        Log($"Handling incoming connection from {remoteEp}");

        try
        {
            using (client)
            {
                var stream = client.GetStream();
                client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

                // 1. Wait for Request
                var request = await ProtocolHelper.ReceiveMessageAsync<TransferRequestMessage>(stream, appCt, 2000);
                if (request == null)
                    throw new Exception("Did not receive TransferRequest.");

                Log($"Incoming: {request.SenderName} — {request.TotalFiles} files, {request.TotalSize / 1024 / 1024} MB");

                // 2. Ask UI
                if (OnIncomingTransfer == null)
                    throw new Exception("No UI handler attached for incoming transfers.");

                var (accepted, savePath, cancelToken) = await OnIncomingTransfer(request, appCt);

                using var linkedCt = CancellationTokenSource.CreateLinkedTokenSource(appCt, cancelToken);
                var transferCt = linkedCt.Token;

                // 3. Send Response
                var response = new TransferResponseMessage
                {
                    Accepted = accepted,
                    Reason = accepted ? "" : "User declined."
                };
                await ProtocolHelper.SendMessageAsync(stream, response, transferCt, 2000);

                if (!accepted)
                {
                    Log("Transfer declined by user.");
                    result.ErrorMessage = "User declined.";
                    return result;
                }

                Directory.CreateDirectory(savePath);
                Log($"Transfer accepted. Saving to: {savePath}");

                // 4. Receive Files
                long totalReceived = 0;
                int filesReceived = 0;
                int filesSkipped = 0;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(1024 * 1024);

                int totalElements = request.PayloadFolderCount + request.PayloadFileCount;
                result.TotalElements = totalElements;
                int currentElementIndex = 0;
                string? currentRootName = null;

                try
                {
                    while (true)
                    {
                        transferCt.ThrowIfCancellationRequested();

                        var markerJson = await ProtocolHelper.ReceiveRawJsonAsync(stream, transferCt, 5000);
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
                        var fileMeta = await ProtocolHelper.ReceiveMessageAsync<FileItemMetadata>(stream, transferCt, 2000);
                        if (fileMeta == null) break;
                    
                        if (fileMeta.RootName != currentRootName)
                        {
                            if (currentRootName != null && !result.CompletedElementNames.Contains(currentRootName))
                            {
                                result.CompletedElementNames.Add(currentRootName);
                            }
                            currentRootName = fileMeta.RootName;
                            currentElementIndex++;
                        }

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
                                CurrentFile = string.IsNullOrEmpty(fileMeta.RootName) ? fileMeta.RelativePath : fileMeta.RootName,
                                BytesSent = totalReceived,
                                TotalBytes = request.TotalSize,
                                SpeedMbPerSec = initialSpeed,
                                CurrentElementIndex = currentElementIndex,
                                TotalElements = totalElements
                            });

                            var lastUpdate = watch.ElapsedMilliseconds;

                            while (fileReceived < fileMeta.Size)
                            {
                                int toRead = (int)Math.Min(buffer.Length, fileMeta.Size - fileReceived);

                                if (!await ProtocolHelper.ReadExactAsync(stream, buffer, toRead, transferCt, 5000))
                                    throw new IOException("Connection lost while reading file data.");

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
                                        CurrentFile = string.IsNullOrEmpty(fileMeta.RootName) ? fileMeta.RelativePath : fileMeta.RootName,
                                        BytesSent = totalReceived,
                                        TotalBytes = request.TotalSize,
                                        SpeedMbPerSec = speed,
                                        CurrentElementIndex = currentElementIndex,
                                        TotalElements = totalElements
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

                    if (currentRootName != null && !result.CompletedElementNames.Contains(currentRootName))
                    {
                        result.CompletedElementNames.Add(currentRootName);
                    }
                
                    result.Success = true;
                    watch.Stop();
                    var summary = $"Transfer complete! Received {totalReceived / 1024 / 1024} MB ({filesReceived} files) in {watch.Elapsed.TotalSeconds:F1}s.";
                    Log(summary);
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.ErrorMessage = ex is OperationCanceledException ? "Transfer cancelled." : ex.Message;
                    try { client.LingerState = new LingerOption(true, 0); client.Close(); } catch { }
                }
                finally
                {
                    System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
                }
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex is System.IO.IOException || ex is System.Net.Sockets.SocketException 
                ? "Connection lost (Ethernet cable disconnected or sender aborted)." 
                : ex.Message;
            try { client.LingerState = new LingerOption(true, 0); client.Close(); } catch { }
        }
        return result;
    }

    private static async Task DrainBytesAsync(NetworkStream stream, long count, byte[] buffer, CancellationToken ct)
    {
        long drained = 0;
        
        while (drained < count)
        {
            int toRead = (int)Math.Min(buffer.Length, count - drained);
            
            if (!await ProtocolHelper.ReadExactAsync(stream, buffer, toRead, ct, 5000))
                throw new IOException("Connection lost while draining skipped file data.");
            
            drained += toRead;
        }
    }
}
