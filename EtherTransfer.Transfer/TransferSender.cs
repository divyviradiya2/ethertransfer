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
    public int CurrentElementIndex { get; set; }
    public int TotalElements { get; set; }
}

public class TransferSender
{
    public event EventHandler<TransferProgressEventArgs>? ProgressUpdated;
    public event EventHandler<StructuredLogMessage>? DebugLog;

    private void Log(string msg, LogLevel level = LogLevel.Info, string eventId = "sender.log") => DebugLog?.Invoke(this, new StructuredLogMessage(eventId, msg, level));

    private static TcpClient CreateBoundClient(System.Net.IPAddress targetAddress)
    {
        try
        {
            var targetBytes = targetAddress.GetAddressBytes();
            var interfaces = EtherTransfer.Network.NetworkInterfaces.NetworkHelper.GetEthernetInterfaces();

            foreach (var iface in interfaces)
            {
                var localBytes = iface.LocalAddress.GetAddressBytes();
                if (targetBytes.Length == 4 && localBytes.Length == 4)
                {
                    // Match link-local 169.254.x.x
                    if (targetBytes[0] == 169 && targetBytes[1] == 254 &&
                        localBytes[0] == 169 && localBytes[1] == 254)
                    {
                        return new TcpClient(new System.Net.IPEndPoint(iface.LocalAddress, 0));
                    }

                    // Match same /24 subnet
                    if (targetBytes[0] == localBytes[0] && targetBytes[1] == localBytes[1] && targetBytes[2] == localBytes[2])
                    {
                        return new TcpClient(new System.Net.IPEndPoint(iface.LocalAddress, 0));
                    }
                }
            }
        }
        catch { }

        return new TcpClient();
    }

    public Task<PayloadItem> ScanItemAsync(string path, IProgress<int>? progress = null, CancellationToken ct = default)
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
                    payload.DeepScannedFiles.Add(new FileSelectionItem { AbsolutePath = path, RelativePath = fi.Name, RootName = fi.Name, Size = fi.Length });
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
                    foreach (var file in Directory.EnumerateFiles(path, "*", options))
                    {
                        ct.ThrowIfCancellationRequested();
                        var fi = new FileInfo(file);
                        payload.DeepScannedFiles.Add(new FileSelectionItem { AbsolutePath = file, RelativePath = Path.GetRelativePath(parentDir, file).Replace('\\', '/'), RootName = baseDir.Name, Size = fi.Length });
                        count++;
                        if (count % 100 == 0) progress?.Report(count);
                    }
                    progress?.Report(count);
                }
            }
            catch (OperationCanceledException)
            {
                payload.DeepScannedFiles.Clear();
                throw;
            }
            catch (Exception ex)
            {
                Log($"Error scanning {path}: {ex.Message}");
            }

            Log($"Scanned {payload.Name} -> {payload.DeepScannedFiles.Count} files, {payload.TotalSize / 1024 / 1024} MB");
            return payload;
        });
    }

    public async Task<TransferResult> TransmitSessionAsync(string targetIp, int targetPort, string senderName, TransferSession session, CancellationToken ct)
    {
        var rootElements = session.Files.Select(f => f.RootName).Distinct().ToList();
        var result = new TransferResult 
        { 
            TotalElements = session.PayloadFolderCount + session.PayloadFileCount,
            AllElementNames = rootElements
        };
        if (session.Files.Count == 0)
        {
            result.Success = true;
            return result;
        }

        Log($"Connecting to {targetIp}:{targetPort}...");
        var parsedTargetIp = System.Net.IPAddress.Parse(targetIp);
        using var client = CreateBoundClient(parsedTargetIp);

        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(5000);

        try
        {
            await client.ConnectAsync(parsedTargetIp, targetPort, connectCts.Token);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("Connection timed out.");
        }
        catch (Exception ex)
        {
            Log($"Failed to connect: {ex.Message}");
            throw;
        }

        var stream = client.GetStream();
        client.NoDelay = true;
        client.SendBufferSize = 1024 * 1024;
        client.ReceiveBufferSize = 1024 * 1024;
        client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

        var request = new TransferRequestMessage
        {
            SenderName = senderName,
            TotalFiles = session.TotalFiles,
            TotalSize = session.TotalSize,
            ContainsFolders = session.ContainsFolders,
            PayloadFolderCount = session.PayloadFolderCount,
            PayloadFileCount = session.PayloadFileCount,
            RootElementNames = rootElements
        };

        // 1. Send Request
        await ProtocolHelper.SendMessageAsync(stream, request, ct, 2000);

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
        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(1024 * 1024); // 1 MB read buffer

        int totalElements = session.PayloadFolderCount + session.PayloadFileCount;
        int currentElementIndex = 0;
        string? currentRootName = null;

        var totalFilesInRoot = new Dictionary<string, int>();
        foreach (var file in session.Files)
        {
            var r = file.RootName;
            totalFilesInRoot[r] = totalFilesInRoot.GetValueOrDefault(r, 0) + 1;
        }
        var sentFilesInRoot = new Dictionary<string, int>();

        try
        {
            foreach (var item in session.Files)
            {
                if (item.RootName != currentRootName)
                {
                    currentRootName = item.RootName;
                    currentElementIndex++;
                }

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
                await ProtocolHelper.SendMessageAsync(stream, fileBegin, ct, 2000);

                var meta = new FileItemMetadata
                {
                    RelativePath = item.RelativePath,
                    RootName = item.RootName,
                    Size = actualSize
                };
                await ProtocolHelper.SendMessageAsync(stream, meta, ct, 2000);

                int read;
                long fileSent = 0;

                var elapsedSecInitial = watch.Elapsed.TotalSeconds;
                var initialSpeed = elapsedSecInitial > 0 ? (totalSent / 1024.0 / 1024.0) / elapsedSecInitial : 0;

                ProgressUpdated?.Invoke(this, new TransferProgressEventArgs
                {
                    CurrentFile = item.RootName,
                    BytesSent = totalSent,
                    TotalBytes = session.TotalSize,
                    SpeedMbPerSec = initialSpeed,
                    CurrentElementIndex = currentElementIndex,
                    TotalElements = totalElements
                });

                var lastUpdate = watch.ElapsedMilliseconds;

                using var watchdogCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

                while ((read = await fs.ReadAsync(buffer, ct)) > 0)
                {
                    watchdogCts.CancelAfter(5000); // 5 seconds to write 1MB before considering connection dead
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
                            CurrentFile = item.RootName,
                            BytesSent = totalSent,
                            TotalBytes = session.TotalSize,
                            SpeedMbPerSec = speed,
                            CurrentElementIndex = currentElementIndex,
                            TotalElements = totalElements
                        });
                    }
                }
                await stream.FlushAsync(ct);
                filesSent++;

                sentFilesInRoot[item.RootName] = sentFilesInRoot.GetValueOrDefault(item.RootName, 0) + 1;
                if (sentFilesInRoot[item.RootName] == totalFilesInRoot[item.RootName])
                {
                    if (!result.CompletedElementNames.Contains(item.RootName))
                    {
                        result.CompletedElementNames.Add(item.RootName);
                    }
                }
            }
        }

            // 4. End of Transfer
            var endMsg = new BaseProtocolMessage { Type = "TRANSFER_END" };
            await ProtocolHelper.SendMessageAsync(stream, endMsg, ct, 2000);

            result.Success = true;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex is OperationCanceledException ? "Transfer cancelled." : ex.Message;
            try 
            { 
                client.LingerState = new LingerOption(true, 0);
                client.Close(); 
            } catch { }
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
        }

        watch.Stop();
        var summary = $"Transfer complete! Sent {totalSent / 1024 / 1024} MB ({filesSent} files) in {watch.Elapsed.TotalSeconds:F1}s.";
        if (filesSkipped > 0)
            summary += $" ({filesSkipped} skipped)";
        Log(summary);
        
        result.FailedElementNames = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Where(result.AllElementNames, name => !result.CompletedElementNames.Contains(name)));
        return result;
    }

    private static async Task SendFileSkip(NetworkStream stream, string relativePath, string reason, CancellationToken ct)
    {
        var skipMsg = new FileSkipMessage
        {
            RelativePath = relativePath,
            Reason = reason
        };
        await ProtocolHelper.SendMessageAsync(stream, skipMsg, ct, 2000);
    }
}
