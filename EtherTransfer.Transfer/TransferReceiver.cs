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

                // 4. Receive Files
                long totalReceived = 0;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var buffer = new byte[1024 * 1024];

                while (!ct.IsCancellationRequested)
                {
                    // Read the next marker
                    var marker = await ProtocolHelper.ReceiveMessageAsync<BaseProtocolMessage>(stream, ct);
                    if (marker == null) break;

                    if (marker.Type == "TRANSFER_END")
                    {
                        Log("Received TRANSFER_END.");
                        break;
                    }

                    if (marker.Type == "FILE_BEGIN")
                    {
                        var fileMeta = await ProtocolHelper.ReceiveMessageAsync<FileItemMetadata>(stream, ct);
                        if (fileMeta == null) break;

                        // Securely combine paths to prevent directory traversal
                        var normalizedRelPath = fileMeta.RelativePath.Replace('\\', '/').TrimStart('/');
                        if (normalizedRelPath.Contains("../") || normalizedRelPath.Contains("..\\"))
                            throw new Exception($"Security violation: Directory traversal attempt blocked ({normalizedRelPath})");
                            
                        // Use Path.GetFullPath to ensure it stays within the saveDirectory
                        var safePath = Path.GetFullPath(Path.Combine(savePath, normalizedRelPath));
                        if (!safePath.StartsWith(Path.GetFullPath(savePath)))
                            throw new Exception($"Security violation: Path escaped save directory ({safePath})");

                        var dirPath = Path.GetDirectoryName(safePath);
                        if (dirPath != null) Directory.CreateDirectory(dirPath);

                        using var fs = new FileStream(safePath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true);
                        
                        long fileReceived = 0;
                        
                        var elapsedSecInitial = watch.Elapsed.TotalSeconds;
                        var initialSpeed = elapsedSecInitial > 0 ? (totalReceived / 1024.0 / 1024.0) / elapsedSecInitial : 0;
                        
                        // Fire progress immediately so UI shows the file name change without resetting speed to 0
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
                                throw new Exception("Connection lost while reading file data.");
                                
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
                    }
                }
                
                watch.Stop();
                Log($"Transfer complete! Received {totalReceived / 1024 / 1024} MB in {watch.Elapsed.TotalSeconds:F1}s.");
            }
        }
        catch (Exception ex)
        {
            Log($"Error handling connection: {ex.Message}");
        }
    }
}
