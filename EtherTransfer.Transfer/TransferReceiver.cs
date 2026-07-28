using System;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EtherTransfer.Core.Models;

namespace EtherTransfer.Transfer;

public class IncomingTransferEventArgs : EventArgs
{
    public TransferRequestMessage Request { get; }

    // The UI should set this to true if accepted, and provide a SaveDirectory
    public bool Accept { get; set; }
    public string SaveDirectory { get; set; } = string.Empty;

    public IncomingTransferEventArgs(TransferRequestMessage request)
    {
        Request = request;
    }
}

public class TransferReceiver
{
    // SHA-256 hash is always exactly 32 bytes
    private const int Sha256ByteLength = 32;

    public Func<TransferRequestMessage, CancellationToken, Task<(bool accept, string savePath)>>? OnIncomingTransfer { get; set; }

    public event EventHandler<TransferProgressEventArgs>? ProgressUpdated;
    public event EventHandler<string>? DebugLog;

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
                var (accepted, savePath) = await uiTask;

                // 3. Send Response
                var response = new TransferResponseMessage
                {
                    Accepted = accepted,
                    Reason = accepted ? "" : "User declined."
                };
                await ProtocolHelper.SendMessageAsync(stream, response, ct);

                if (!accepted)
                {
                    Log("Transfer declined by user.");
                    return;
                }

                Directory.CreateDirectory(savePath);
                Log($"Transfer accepted. Saving to: {savePath}");

                // 4. Receive Files with integrity verification
                long totalReceived = 0;
                int filesReceived = 0;
                int filesSkipped = 0;
                int filesFailedIntegrity = 0;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var buffer = new byte[1024 * 1024];
                var hashBuffer = new byte[Sha256ByteLength];

                while (!ct.IsCancellationRequested)
                {
                    var markerJson = await ProtocolHelper.ReceiveRawJsonAsync(stream, ct);
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
                    var fileMeta = await ProtocolHelper.ReceiveMessageAsync<FileItemMetadata>(stream, ct);
                    if (fileMeta == null) break;

                    // === PATH SECURITY ===
                    var safePath = PathSanitizer.SanitizeRelativePath(savePath, fileMeta.RelativePath);
                    if (safePath == null)
                    {
                        Log($"SECURITY: Blocked malicious path: {fileMeta.RelativePath}");
                        // Drain file bytes + 32-byte hash to keep protocol in sync
                        await DrainBytesAsync(stream, fileMeta.Size + Sha256ByteLength, buffer, ct);
                        filesSkipped++;
                        continue;
                    }

                    // === COLLISION RESOLUTION ===
                    safePath = PathSanitizer.ResolveCollision(safePath);

                    var dirPath = Path.GetDirectoryName(safePath);
                    if (dirPath != null) Directory.CreateDirectory(dirPath);

                    // === .part FILE STRATEGY ===
                    var partPath = safePath + ".part";
                    string? computedHashHex = null;
                    bool fileWriteSuccess = false;

                    try
                    {
                        using (var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                        using (var fs = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true))
                        {
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

                            while (fileReceived < fileMeta.Size)
                            {
                                int toRead = (int)Math.Min(buffer.Length, fileMeta.Size - fileReceived);
                                if (!await ProtocolHelper.ReadExactAsync(stream, buffer, toRead, ct))
                                    throw new IOException("Connection lost while reading file data.");

                                sha256.AppendData(buffer, 0, toRead);
                                await fs.WriteAsync(buffer, 0, toRead, ct);

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

                            await fs.FlushAsync(ct);
                            computedHashHex = Convert.ToHexString(sha256.GetHashAndReset());
                        }

                        // Read the raw 32-byte SHA-256 hash from the sender
                        if (!await ProtocolHelper.ReadExactAsync(stream, hashBuffer, Sha256ByteLength, ct))
                            throw new IOException("Connection lost while reading checksum.");

                        var senderHashHex = Convert.ToHexString(hashBuffer);

                        if (!string.Equals(senderHashHex, computedHashHex, StringComparison.OrdinalIgnoreCase))
                        {
                            Log($"INTEGRITY FAIL: {fileMeta.RelativePath}");
                            filesFailedIntegrity++;
                        }
                        else
                        {
                            fileWriteSuccess = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"Error receiving {fileMeta.RelativePath}: {ex.Message}");
                    }

                    if (fileWriteSuccess)
                    {
                        try
                        {
                            if (File.Exists(safePath))
                                safePath = PathSanitizer.ResolveCollision(safePath);

                            File.Move(partPath, safePath);
                            filesReceived++;
                        }
                        catch (Exception ex)
                        {
                            Log($"Failed to finalize {fileMeta.RelativePath}: {ex.Message}");
                            CleanupPartFile(partPath);
                        }
                    }
                    else
                    {
                        CleanupPartFile(partPath);
                    }
                }

                watch.Stop();
                Log($"Transfer complete! {totalReceived / 1024 / 1024} MB in {watch.Elapsed.TotalSeconds:F1}s — {filesReceived} verified, {filesSkipped} skipped, {filesFailedIntegrity} failed.");

                if (filesFailedIntegrity > 0)
                    Log($"WARNING: {filesFailedIntegrity} file(s) failed SHA-256 verification.");
            }
        }
        catch (OperationCanceledException)
        {
            Log("Transfer cancelled.");
        }
        catch (Exception ex)
        {
            Log($"Error handling connection: {ex.Message}");
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

    private void CleanupPartFile(string partPath)
    {
        try
        {
            if (File.Exists(partPath))
                File.Delete(partPath);
        }
        catch { }
    }
}
