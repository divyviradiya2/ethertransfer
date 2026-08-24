using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.Linq;
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

    private void Log(string msg, LogLevel level = LogLevel.Info, string eventId = "sender.log") =>
        DebugLog?.Invoke(this, new StructuredLogMessage(eventId, msg, level));

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
                    if (targetBytes[0] == 169 && targetBytes[1] == 254 &&
                        localBytes[0] == 169 && localBytes[1] == 254)
                    {
                        return new TcpClient(new System.Net.IPEndPoint(iface.LocalAddress, 0));
                    }

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
                    payload.DeepScannedFiles.Add(new FileSelectionItem
                    {
                        AbsolutePath = path,
                        RelativePath = fi.Name,
                        RootName = fi.Name,
                        Size = fi.Length
                    });
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
                        payload.DeepScannedFiles.Add(new FileSelectionItem
                        {
                            AbsolutePath = file,
                            RelativePath = Path.GetRelativePath(parentDir, file).Replace('\\', '/'),
                            RootName = baseDir.Name,
                            Size = fi.Length
                        });
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
        }, ct);
    }

    public async Task<TransferResult> TransmitSessionAsync(
        string targetIp,
        int targetPort,
        string senderName,
        TransferSession session,
        CancellationToken ct)
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
            await client.ConnectAsync(parsedTargetIp, targetPort, connectCts.Token).ConfigureAwait(false);
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

        var networkStream = client.GetStream();
        client.NoDelay = true;
        client.SendBufferSize = 2 * 1024 * 1024;
        client.ReceiveBufferSize = 2 * 1024 * 1024;
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

        await ProtocolHelper.SendMessageAsync(networkStream, request, ct, 3000).ConfigureAwait(false);

        Log("Waiting for receiver to accept...");
        var response = await ProtocolHelper.ReceiveMessageAsync<TransferResponseMessage>(networkStream, ct).ConfigureAwait(false);

        if (response == null)
            throw new Exception("Connection closed by receiver before response.");

        if (!response.Accepted)
        {
            Log($"Transfer declined: {response.Reason}");
            throw new Exception($"Receiver declined the transfer: {response.Reason}");
        }

        Log($"Transfer accepted! Streaming {session.TotalFiles} files ({session.TotalSize / 1024 / 1024} MB)...");

        long totalBytesSent = 0;
        int filesSent = 0;
        int filesSkipped = 0;
        int totalElements = session.PayloadFolderCount + session.PayloadFileCount;
        int currentElementIndex = 0;
        string? currentRootName = null;

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var speedTracker = new SpeedTracker();
        speedTracker.Start();

        var lastProgressReport = watch.ElapsedMilliseconds;

        void ReportProgress(bool force = false)
        {
            var now = watch.ElapsedMilliseconds;
            if (force || now - lastProgressReport >= 50 || totalBytesSent >= session.TotalSize)
            {
                lastProgressReport = now;
                var currentSpeed = speedTracker.CalculateSpeed(totalBytesSent);

                ProgressUpdated?.Invoke(this, new TransferProgressEventArgs
                {
                    CurrentFile = currentRootName ?? string.Empty,
                    BytesSent = totalBytesSent,
                    TotalBytes = session.TotalSize,
                    SpeedMbPerSec = currentSpeed,
                    CurrentElementIndex = currentElementIndex,
                    TotalElements = totalElements
                });
            }
        }

        using var countingStream = new CountingStream(networkStream, onBytesWritten: written =>
        {
            totalBytesSent = written;
            ReportProgress(force: false);
        });

        var itemsByRoot = new List<(string RootName, List<FileSelectionItem> Files)>();
        var seenRoots = new HashSet<string>();
        foreach (var file in session.Files)
        {
            if (seenRoots.Add(file.RootName))
            {
                itemsByRoot.Add((file.RootName, session.Files.Where(f => f.RootName == file.RootName).ToList()));
            }
        }

        try
        {
            foreach (var (rootName, rootFiles) in itemsByRoot)
            {
                ct.ThrowIfCancellationRequested();

                currentRootName = rootName;
                currentElementIndex++;
                ReportProgress(force: true);

                bool isFolder = rootFiles.Count > 1 ||
                                (rootFiles.Count == 1 && rootFiles[0].RelativePath != rootFiles[0].RootName) ||
                                (rootFiles.Count == 1 && Directory.Exists(rootFiles[0].AbsolutePath));

                if (isFolder)
                {

                    var folderBegin = new BaseProtocolMessage { Type = ProtocolMessageTypes.FolderBegin };
                    await ProtocolHelper.SendMessageAsync(countingStream, folderBegin, ct, 3000).ConfigureAwait(false);

                    var folderMeta = new FolderTarMetadata
                    {
                        RootName = rootName,
                        TotalFiles = rootFiles.Count,
                        TotalSize = rootFiles.Sum(f => f.Size)
                    };
                    await ProtocolHelper.SendMessageAsync(countingStream, folderMeta, ct, 3000).ConfigureAwait(false);

                    var tarWriter = new TarWriter(countingStream, TarEntryFormat.Pax, leaveOpen: true);
                    await using (tarWriter.ConfigureAwait(false))
                    {
                        foreach (var item in rootFiles)
                        {
                            ct.ThrowIfCancellationRequested();

                            if (!File.Exists(item.AbsolutePath))
                            {
                                Log($"SKIP (missing): {item.RelativePath}");
                                filesSkipped++;
                                continue;
                            }

                            try
                            {
                                var entry = new PaxTarEntry(TarEntryType.RegularFile, item.RelativePath);
                                using var fs = new FileStream(
                                    item.AbsolutePath,
                                    FileMode.Open,
                                    FileAccess.Read,
                                    FileShare.ReadWrite,
                                    128 * 1024,
                                    FileOptions.Asynchronous | FileOptions.SequentialScan);

                                entry.DataStream = fs;
                                await tarWriter.WriteEntryAsync(entry, ct).ConfigureAwait(false);
                                filesSent++;
                            }
                            catch (Exception ex)
                            {
                                Log($"SKIP (read error): {item.RelativePath} - {ex.Message}");
                                filesSkipped++;
                            }
                        }
                    }

                    await countingStream.FlushAsync(ct).ConfigureAwait(false);

                    if (!result.CompletedElementNames.Contains(rootName))
                    {
                        result.CompletedElementNames.Add(rootName);
                    }
                }
                else
                {

                    var item = rootFiles[0];

                    FileStream? fs = null;
                    try
                    {
                        fs = new FileStream(
                            item.AbsolutePath,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.ReadWrite,
                            1,
                            FileOptions.Asynchronous | FileOptions.SequentialScan);
                    }
                    catch (FileNotFoundException)
                    {
                        Log($"SKIP (deleted): {item.RelativePath}");
                        await SendFileSkip(countingStream, item.RelativePath, "File was deleted after scan.", ct).ConfigureAwait(false);
                        filesSkipped++;
                        continue;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        Log($"SKIP (access denied): {item.RelativePath}");
                        await SendFileSkip(countingStream, item.RelativePath, "Permission denied.", ct).ConfigureAwait(false);
                        filesSkipped++;
                        continue;
                    }
                    catch (IOException ex)
                    {
                        Log($"SKIP (locked): {item.RelativePath}");
                        await SendFileSkip(countingStream, item.RelativePath, $"File locked: {ex.Message}", ct).ConfigureAwait(false);
                        filesSkipped++;
                        continue;
                    }

                    using (fs)
                    {
                        var actualSize = fs.Length;

                        var fileBegin = new BaseProtocolMessage { Type = ProtocolMessageTypes.FileBegin };
                        await ProtocolHelper.SendMessageAsync(countingStream, fileBegin, ct, 3000).ConfigureAwait(false);

                        var meta = new FileItemMetadata
                        {
                            RelativePath = item.RelativePath,
                            RootName = item.RootName,
                            Size = actualSize
                        };
                        await ProtocolHelper.SendMessageAsync(countingStream, meta, ct, 3000).ConfigureAwait(false);

                        await PipelinedTransferEngine.StreamFileToNetworkAsync(
                            fs,
                            countingStream,
                            actualSize,
                            onBytesSent: _ => ReportProgress(force: false),
                            ct).ConfigureAwait(false);

                        await countingStream.FlushAsync(ct).ConfigureAwait(false);

                        filesSent++;

                        if (!result.CompletedElementNames.Contains(item.RootName))
                        {
                            result.CompletedElementNames.Add(item.RootName);
                        }
                    }
                }
            }

            var endMsg = new BaseProtocolMessage { Type = ProtocolMessageTypes.TransferEnd };
            await ProtocolHelper.SendMessageAsync(countingStream, endMsg, ct, 3000).ConfigureAwait(false);
            await countingStream.FlushAsync(ct).ConfigureAwait(false);

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
            }
            catch { }
        }

        watch.Stop();
        ReportProgress(force: true);

        var summary = $"Transfer complete! Sent {totalBytesSent / 1024 / 1024} MB ({filesSent} files) in {watch.Elapsed.TotalSeconds:F1}s.";
        if (filesSkipped > 0)
            summary += $" ({filesSkipped} skipped)";
        Log(summary);

        result.FailedElementNames = result.AllElementNames
            .Where(name => !result.CompletedElementNames.Contains(name))
            .ToList();

        return result;
    }

    private static async Task SendFileSkip(Stream stream, string relativePath, string reason, CancellationToken ct)
    {
        var skipMsg = new FileSkipMessage
        {
            RelativePath = relativePath,
            Reason = reason
        };
        await ProtocolHelper.SendMessageAsync(stream, skipMsg, ct, 3000).ConfigureAwait(false);
    }
}

