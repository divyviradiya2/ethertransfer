using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using EtherTransfer.Core.Models;

namespace EtherTransfer.Transfer;

public class TransferProgressEventArgs : EventArgs
{
    public string CurrentFile { get; set; } = string.Empty;
    public long BytesSent { get; set; }
    public long TotalBytes { get; set; }
    public double SpeedMbPerSec { get; set; }
}

public class TransferSender
{
    public event EventHandler<TransferProgressEventArgs>? ProgressUpdated;
    public event EventHandler<string>? DebugLog;

    private void Log(string msg) => DebugLog?.Invoke(this, $"[{DateTime.Now:HH:mm:ss}] [Sender] {msg}");

    /// <summary>
    /// Connects to a receiver, performs the handshake, and sends the items (files/folders) if accepted.
    /// </summary>
    public async Task SendItemsAsync(string targetIp, int targetPort, string senderName, List<string> itemPaths, CancellationToken ct)
    {
        Log($"Connecting to {targetIp}:{targetPort}...");
        using var client = new TcpClient();
        
        // Timeout for connection
        var connectTcs = new TaskCompletionSource();
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(5000); // 5 sec timeout
        
        try
        {
            await using (connectCts.Token.Register(() => connectTcs.TrySetCanceled()))
            {
                var connectTask = client.ConnectAsync(targetIp, targetPort);
                await Task.WhenAny(connectTask, connectTcs.Task);
                if (connectTcs.Task.IsCanceled)
                    throw new TimeoutException("Connection timed out.");
                await connectTask; // propagate any connection exceptions
            }
        }
        catch (Exception ex)
        {
            Log($"Failed to connect: {ex.Message}");
            throw;
        }

        // Build flat list of files to send
        var filesToSend = new List<(string AbsolutePath, string RelativePath, long Size)>();
        long totalSize = 0;
        bool containsFolders = false;

        foreach (var path in itemPaths)
        {
            if (File.Exists(path))
            {
                var fi = new FileInfo(path);
                filesToSend.Add((path, fi.Name, fi.Length));
                totalSize += fi.Length;
            }
            else if (Directory.Exists(path))
            {
                containsFolders = true;
                var baseDir = new DirectoryInfo(path);
                var parentDir = baseDir.Parent?.FullName ?? baseDir.FullName;

                foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                {
                    var fi = new FileInfo(file);
                    // Create relative path like "FolderName/SubFolder/file.txt"
                    // Handle trailing slashes cleanly
                    string relPath = file.Substring(parentDir.Length);
                    if (relPath.StartsWith(Path.DirectorySeparatorChar) || relPath.StartsWith(Path.AltDirectorySeparatorChar))
                    {
                        relPath = relPath.Substring(1);
                    }
                    
                    // Normalize to forward slashes for cross-platform compatibility
                    relPath = relPath.Replace('\\', '/');

                    filesToSend.Add((file, relPath, fi.Length));
                    totalSize += fi.Length;
                }
            }
        }

        if (filesToSend.Count == 0)
        {
            Log("No valid files found to send.");
            return;
        }

        Log("Connected. Sending TransferRequest...");
        var stream = client.GetStream();
        
        var request = new TransferRequestMessage
        {
            SenderName = senderName,
            TotalFiles = filesToSend.Count,
            TotalSize = totalSize,
            ContainsFolders = containsFolders
        };

        // 1. Send Request
        await ProtocolHelper.SendMessageAsync(stream, request, ct);

        // 2. Wait for Response
        Log("Waiting for receiver to accept...");
        var response = await ProtocolHelper.ReceiveMessageAsync<TransferResponseMessage>(stream, ct);
        
        if (response == null)
            throw new Exception("Connection closed by receiver before response.");
            
        if (!response.Accepted)
        {
            Log($"Transfer declined: {response.Reason}");
            throw new Exception($"Receiver declined the transfer: {response.Reason}");
        }

        Log("Transfer accepted! Starting streaming...");

        // 3. Stream Files
        long totalSent = 0;
        var watch = System.Diagnostics.Stopwatch.StartNew();

        // 1MB buffer for fast chunked streaming
        var buffer = new byte[1024 * 1024];

        foreach (var item in filesToSend)
        {
            // A. Send FileBegin metadata
            var fileBegin = new BaseProtocolMessage { Type = "FILE_BEGIN" };
            await ProtocolHelper.SendMessageAsync(stream, fileBegin, ct); // Marker
            
            var meta = new FileItemMetadata
            {
                RelativePath = item.RelativePath,
                Size = item.Size
            };
            await ProtocolHelper.SendMessageAsync(stream, meta, ct);

            Log($"Streaming file: {item.RelativePath} ({item.Size / 1024 / 1024} MB)");

            // B. Send Raw Binary Data
            using var fs = new FileStream(item.AbsolutePath, FileMode.Open, FileAccess.Read, FileShare.Read, buffer.Length, useAsync: true);
            
            int read;
            long fileSent = 0;
            
            // Fire progress immediately so UI shows 0% for this file
            ProgressUpdated?.Invoke(this, new TransferProgressEventArgs
            {
                CurrentFile = item.RelativePath,
                BytesSent = totalSent,
                TotalBytes = totalSize,
                SpeedMbPerSec = 0
            });
            
            while ((read = await fs.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
            {
                await stream.WriteAsync(buffer, 0, read, ct);
                
                fileSent += read;
                totalSent += read;
                
                // Update progress every chunk
                var elapsedSec = watch.Elapsed.TotalSeconds;
                var speed = elapsedSec > 0 ? (totalSent / 1024.0 / 1024.0) / elapsedSec : 0;
                
                ProgressUpdated?.Invoke(this, new TransferProgressEventArgs
                {
                    CurrentFile = item.RelativePath,
                    BytesSent = totalSent,
                    TotalBytes = totalSize,
                    SpeedMbPerSec = speed
                });
            }
            await stream.FlushAsync(ct);
        }

        // 4. End of Transfer
        var endMsg = new BaseProtocolMessage { Type = "TRANSFER_END" };
        await ProtocolHelper.SendMessageAsync(stream, endMsg, ct);
        
        watch.Stop();
        Log($"Transfer complete! Sent {totalSize / 1024 / 1024} MB in {watch.Elapsed.TotalSeconds:F1}s.");
    }
}
