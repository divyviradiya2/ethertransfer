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

    public Task<PayloadItem> ScanItemAsync(string path, IProgress<int>? progress = null)
    {
        return Task.Run(() =>
        {
            var payload = new PayloadItem
            {
                Path = path
            };

            try
            {
                if (File.Exists(path))
                {
                    var fi = new FileInfo(path);
                    payload.Name = fi.Name;
                    payload.Type = PayloadItemType.File;
                    payload.DeepScannedFiles.Add(new FileSelectionItem { AbsolutePath = path, RelativePath = fi.Name, Size = fi.Length });
                }
                else if (Directory.Exists(path))
                {
                    var baseDir = new DirectoryInfo(path);
                    payload.Name = baseDir.Name;
                    payload.Type = PayloadItemType.Folder;

                    var parentDir = baseDir.Parent?.FullName ?? baseDir.FullName;
                    var options = new EnumerationOptions
                    {
                        IgnoreInaccessible = true,
                        RecurseSubdirectories = true,
                        ReturnSpecialDirectories = false
                    };

                    int count = 0;
                    foreach (var fileInfo in baseDir.EnumerateFiles("*", options))
                    {
                        try
                        {
                            string relPath = fileInfo.FullName.Substring(parentDir.Length);
                            if (relPath.StartsWith(Path.DirectorySeparatorChar.ToString()) || relPath.StartsWith(Path.AltDirectorySeparatorChar.ToString()))
                            {
                                relPath = relPath.Substring(1);
                            }
                            relPath = relPath.Replace('\\', '/');

                            payload.DeepScannedFiles.Add(new FileSelectionItem { AbsolutePath = fileInfo.FullName, RelativePath = relPath, Size = fileInfo.Length });

                            count++;
                            if (count % 100 == 0)
                            {
                                progress?.Report(count);
                            }
                        }
                        catch { }
                    }
                    progress?.Report(count); // Final report
                }
            }
            catch (Exception ex)
            {
                Log($"Error scanning {path}: {ex.Message}");
            }

            Log($"Scanned {payload.Name} -> {payload.DeepScannedFiles.Count} files, {payload.TotalSize / 1024 / 1024} MB");
            return payload;
        });
    }

    public async Task TransmitSessionAsync(string targetIp, int targetPort, string senderName, TransferSession session, CancellationToken ct)
    {
        if (session.Files.Count == 0) return;

        Log($"Connecting to {targetIp}:{targetPort}...");
        using var client = new TcpClient();

        var connectTcs = new TaskCompletionSource();
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(5000);

        try
        {
            await using (connectCts.Token.Register(() => connectTcs.TrySetCanceled()))
            {
                var connectTask = client.ConnectAsync(targetIp, targetPort);
                await Task.WhenAny(connectTask, connectTcs.Task);
                if (connectTcs.Task.IsCanceled)
                    throw new TimeoutException("Connection timed out.");
                await connectTask;
            }
        }
        catch (Exception ex)
        {
            Log($"Failed to connect: {ex.Message}");
            throw;
        }

        var stream = client.GetStream();

        var request = new TransferRequestMessage
        {
            SenderName = senderName,
            TotalFiles = session.TotalFiles,
            TotalSize = session.TotalSize,
            ContainsFolders = session.ContainsFolders,
            PayloadFolderCount = session.PayloadFolderCount,
            PayloadFileCount = session.PayloadFileCount
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

        Log($"Transfer accepted! Streaming {session.TotalFiles} files...");

        // 3. Stream Files
        long totalSent = 0;
        int filesSent = 0;
        int filesSkipped = 0;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var buffer = new byte[1024 * 1024]; // 1 MB read buffer

        foreach (var item in session.Files)
        {
            ct.ThrowIfCancellationRequested();

            // Attempt to open the file — handle locks, deletions, permission issues gracefully
            FileStream? fs;
            try
            {
                fs = new FileStream(item.AbsolutePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 65536, useAsync: true);
            }
            catch (FileNotFoundException)
            {
                Log($"SKIP (deleted): {item.RelativePath}");
                await SendFileSkip(stream, item.RelativePath, "File was deleted after scan.", ct);
                filesSkipped++;
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                Log($"SKIP (access denied): {item.RelativePath}");
                await SendFileSkip(stream, item.RelativePath, "Permission denied.", ct);
                filesSkipped++;
                continue;
            }
            catch (IOException ex)
            {
                Log($"SKIP (locked): {item.RelativePath}");
                await SendFileSkip(stream, item.RelativePath, $"File locked: {ex.Message}", ct);
                filesSkipped++;
                continue;
            }

            using (fs)
            {
                var actualSize = fs.Length;

                var fileBegin = new BaseProtocolMessage { Type = "FILE_BEGIN" };
                await ProtocolHelper.SendMessageAsync(stream, fileBegin, ct);

                var meta = new FileItemMetadata
                {
                    RelativePath = item.RelativePath,
                    Size = actualSize
                };
                await ProtocolHelper.SendMessageAsync(stream, meta, ct);

                int read;
                long fileSent = 0;

                var elapsedSecInitial = watch.Elapsed.TotalSeconds;
                var initialSpeed = elapsedSecInitial > 0 ? (totalSent / 1024.0 / 1024.0) / elapsedSecInitial : 0;

                ProgressUpdated?.Invoke(this, new TransferProgressEventArgs
                {
                    CurrentFile = item.RelativePath,
                    BytesSent = totalSent,
                    TotalBytes = session.TotalSize,
                    SpeedMbPerSec = initialSpeed
                });

                var lastUpdate = watch.ElapsedMilliseconds;

                using var watchdogCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

                while ((read = await fs.ReadAsync(buffer, ct)) > 0)
                {
                    watchdogCts.CancelAfter(15000); // 15 seconds to write 1MB before considering connection dead
                    try
                    {
                        await stream.WriteAsync(buffer, 0, read, watchdogCts.Token);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        throw new IOException("Connection timed out (Ethernet cable disconnected or network dropped).");
                    }

                    fileSent += read;
                    totalSent += read;

                    var currentElapsed = watch.ElapsedMilliseconds;
                    if (currentElapsed - lastUpdate >= 50 || totalSent == session.TotalSize)
                    {
                        lastUpdate = currentElapsed;
                        var elapsedSec = watch.Elapsed.TotalSeconds;
                        var speed = elapsedSec > 0 ? (totalSent / 1024.0 / 1024.0) / elapsedSec : 0;

                        ProgressUpdated?.Invoke(this, new TransferProgressEventArgs
                        {
                            CurrentFile = item.RelativePath,
                            BytesSent = totalSent,
                            TotalBytes = session.TotalSize,
                            SpeedMbPerSec = speed
                        });
                    }
                }
                await stream.FlushAsync(ct);
                filesSent++;
            }
        }

        // 4. End of Transfer
        var endMsg = new BaseProtocolMessage { Type = "TRANSFER_END" };
        await ProtocolHelper.SendMessageAsync(stream, endMsg, ct);

        watch.Stop();
        var summary = $"Transfer complete! Sent {totalSent / 1024 / 1024} MB ({filesSent} files) in {watch.Elapsed.TotalSeconds:F1}s.";
        if (filesSkipped > 0)
            summary += $" ({filesSkipped} skipped)";
        Log(summary);
    }

    private static async Task SendFileSkip(NetworkStream stream, string relativePath, string reason, CancellationToken ct)
    {
        var skipMsg = new FileSkipMessage
        {
            RelativePath = relativePath,
            Reason = reason
        };
        await ProtocolHelper.SendMessageAsync(stream, skipMsg, ct);
    }
}
