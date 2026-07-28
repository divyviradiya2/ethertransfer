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
    // Event fired when a new request comes in. The subscriber MUST set Accept and SaveDirectory synchronously or block.
    // For async UI, we can use an async delegate or a TCS.
    // Let's use a Func to make it cleanly awaitable from the UI thread.
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

                Log($"Incoming transfer request from {request.SenderName}: {request.TotalFiles} files, {request.TotalSize / 1024 / 1024} MB");

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
                            // If SelectRead is true and 0 bytes available, connection closed.
                            if (client.Client.Poll(1000, SelectMode.SelectRead) && client.Client.Available == 0)
                            {
                                return true;
                            }
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

                disconnectCts.Cancel(); // stop polling, DO NOT cancel uiCts
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

                // Ensure save path exists
                Directory.CreateDirectory(savePath);

                Log($"Transfer accepted. Saving to: {savePath}");

                // 4. Receive Files with integrity verification
                long totalReceived = 0;
                int filesReceived = 0;
                int filesSkipped = 0;
                int filesFailedIntegrity = 0;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var buffer = new byte[1024 * 1024]; // 1 MB write buffer

                while (!ct.IsCancellationRequested)
                {
                    // Read the next marker (generic JSON, we parse the Type field)
                    var markerJson = await ProtocolHelper.ReceiveRawJsonAsync(stream, ct);
                    if (markerJson == null) break;

                    var baseMsg = JsonSerializer.Deserialize<BaseProtocolMessage>(markerJson);
                    if (baseMsg == null) break;

                    if (baseMsg.Type == "TRANSFER_END")
                    {
                        Log("Received TRANSFER_END.");
                        break;
                    }

                    if (baseMsg.Type == "FILE_SKIP")
                    {
                        var skipMsg = JsonSerializer.Deserialize<FileSkipMessage>(markerJson);
                        if (skipMsg != null)
                        {
                            Log($"Sender skipped: {skipMsg.RelativePath} — {skipMsg.Reason}");
                            filesSkipped++;
                        }
                        continue;
                    }

                    if (baseMsg.Type != "FILE_BEGIN")
                    {
                        Log($"Unexpected message type: {baseMsg.Type}. Ignoring.");
                        continue;
                    }

                    // FILE_BEGIN — read metadata
                    var fileMeta = await ProtocolHelper.ReceiveMessageAsync<FileItemMetadata>(stream, ct);
                    if (fileMeta == null) break;

                    // === PATH SECURITY ===
                    // Use PathSanitizer to guarantee the file stays inside the sandbox
                    var safePath = PathSanitizer.SanitizeRelativePath(savePath, fileMeta.RelativePath);
                    if (safePath == null)
                    {
                        Log($"SECURITY: Blocked malicious path: {fileMeta.RelativePath}");
                        // Still must consume the file bytes from the stream to keep protocol in sync
                        await DrainBytesAsync(stream, fileMeta.Size, buffer, ct);
                        // Also consume the checksum message
                        await ProtocolHelper.ReceiveMessageAsync<FileChecksumMessage>(stream, ct);
                        filesSkipped++;
                        continue;
                    }

                    // === COLLISION RESOLUTION ===
                    // Never silently overwrite existing files
                    safePath = PathSanitizer.ResolveCollision(safePath);

                    // Ensure parent directory exists
                    var dirPath = Path.GetDirectoryName(safePath);
                    if (dirPath != null) Directory.CreateDirectory(dirPath);

                    // === .part FILE STRATEGY ===
                    // Write to a temporary .part file. Only rename to final name after
                    // SHA-256 verification passes. If anything fails, the .part file is cleaned up.
                    var partPath = safePath + ".part";
                    string? computedHash = null;
                    bool fileWriteSuccess = false;

                    try
                    {
                        using (var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                        using (var fs = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true))
                        {
                            long fileReceived = 0;

                            var elapsedSecInitial = watch.Elapsed.TotalSeconds;
                            var initialSpeed = elapsedSecInitial > 0 ? (totalReceived / 1024.0 / 1024.0) / elapsedSecInitial : 0;

                            // Fire progress immediately so UI shows the file name change
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

                                // Feed into SHA-256 hasher
                                sha256.AppendData(buffer, 0, toRead);

                                // Write to .part file
                                await fs.WriteAsync(buffer, 0, toRead, ct);

                                fileReceived += toRead;
                                totalReceived += toRead;

                                // Throttle UI updates to max ~20 FPS (every 50ms)
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

                            // Flush to ensure all data is on disk before verification
                            await fs.FlushAsync(ct);

                            computedHash = Convert.ToHexString(sha256.GetHashAndReset());
                        }

                        // === SHA-256 INTEGRITY VERIFICATION ===
                        var checksumMsg = await ProtocolHelper.ReceiveMessageAsync<FileChecksumMessage>(stream, ct);

                        if (checksumMsg == null)
                        {
                            Log($"INTEGRITY FAIL: No checksum received for {fileMeta.RelativePath}");
                            filesFailedIntegrity++;
                        }
                        else if (!string.Equals(checksumMsg.Sha256, computedHash, StringComparison.OrdinalIgnoreCase))
                        {
                            Log($"INTEGRITY FAIL: {fileMeta.RelativePath} — Expected={checksumMsg.Sha256[..16]}... Got={computedHash[..16]}...");
                            filesFailedIntegrity++;
                        }
                        else
                        {
                            // Checksum matches — promote .part to final file
                            fileWriteSuccess = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"Error receiving {fileMeta.RelativePath}: {ex.Message}");
                    }

                    if (fileWriteSuccess)
                    {
                        // Atomic rename: .part -> final name
                        try
                        {
                            // If final file appeared between our collision check and now, resolve again
                            if (File.Exists(safePath))
                                safePath = PathSanitizer.ResolveCollision(safePath);

                            File.Move(partPath, safePath);
                            filesReceived++;
                            Log($"Verified: {fileMeta.RelativePath} SHA256={computedHash![..16]}...");
                        }
                        catch (Exception ex)
                        {
                            Log($"Failed to finalize {fileMeta.RelativePath}: {ex.Message}");
                            CleanupPartFile(partPath);
                        }
                    }
                    else
                    {
                        // Integrity failed or error — clean up the .part file
                        CleanupPartFile(partPath);
                    }
                }

                watch.Stop();
                var summary = $"Transfer complete! Received {totalReceived / 1024 / 1024} MB in {watch.Elapsed.TotalSeconds:F1}s. " +
                              $"({filesReceived} verified, {filesSkipped} skipped, {filesFailedIntegrity} failed integrity)";
                Log(summary);

                if (filesFailedIntegrity > 0)
                {
                    Log($"WARNING: {filesFailedIntegrity} file(s) failed SHA-256 verification and were NOT saved.");
                }
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

    /// <summary>
    /// Drains (discards) a specified number of bytes from the stream.
    /// Used when a file must be skipped but the sender has already started streaming its data.
    /// </summary>
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

    /// <summary>
    /// Safely deletes a .part file. Never throws.
    /// </summary>
    private void CleanupPartFile(string partPath)
    {
        try
        {
            if (File.Exists(partPath))
            {
                File.Delete(partPath);
                Log($"Cleaned up: {Path.GetFileName(partPath)}");
            }
        }
        catch (Exception ex)
        {
            Log($"Warning: Could not clean up {partPath}: {ex.Message}");
        }
    }
}
