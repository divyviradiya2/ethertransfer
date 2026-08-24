using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EtherTransfer.Core.Models;

namespace EtherTransfer.Transfer;

public class TransferReceiver
{
    public Func<TransferRequestMessage, CancellationToken, Task<(bool accept, string savePath, CancellationToken cancelToken)>>? OnIncomingTransfer { get; set; }

    public event EventHandler<TransferProgressEventArgs>? ProgressUpdated;
    public event EventHandler<StructuredLogMessage>? DebugLog;

    private void Log(string msg, LogLevel level = LogLevel.Info, string eventId = "receiver.log") =>
        DebugLog?.Invoke(this, new StructuredLogMessage(eventId, msg, level));

    public async Task<TransferResult> HandleClientAsync(TcpClient client, CancellationToken appCt)
    {
        var result = new TransferResult();
        var remoteEp = client.Client.RemoteEndPoint?.ToString();
        Log($"Handling incoming connection from {remoteEp}");

        try
        {
            using (client)
            {
                var networkStream = client.GetStream();
                client.NoDelay = true;
                client.SendBufferSize = 2 * 1024 * 1024;
                client.ReceiveBufferSize = 2 * 1024 * 1024;
                client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

                var request = await ProtocolHelper.ReceiveMessageAsync<TransferRequestMessage>(networkStream, appCt, 3000).ConfigureAwait(false);
                if (request == null)
                    throw new Exception("Did not receive TransferRequest.");

                Log($"Incoming: {request.SenderName} - {request.TotalFiles} files, {request.TotalSize / 1024 / 1024} MB");

                if (OnIncomingTransfer == null)
                    throw new Exception("No UI handler attached for incoming transfers.");

                var (accepted, savePath, cancelToken) = await OnIncomingTransfer(request, appCt).ConfigureAwait(false);

                using var linkedCt = CancellationTokenSource.CreateLinkedTokenSource(appCt, cancelToken);
                var transferCt = linkedCt.Token;

                var response = new TransferResponseMessage
                {
                    Accepted = accepted,
                    Reason = accepted ? "" : "User declined."
                };
                await ProtocolHelper.SendMessageAsync(networkStream, response, transferCt, 3000).ConfigureAwait(false);

                if (!accepted)
                {
                    Log("Transfer declined by user.");
                    result.ErrorMessage = "User declined.";
                    return result;
                }

                Directory.CreateDirectory(savePath);
                Log($"Transfer accepted. Saving to: {savePath}");

                long totalBytesReceived = 0;
                int filesReceived = 0;
                int filesSkipped = 0;
                var filesByRootElement = new Dictionary<string, List<string>>();
                var createdDirectoriesThisSession = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                int totalElements = request.PayloadFolderCount + request.PayloadFileCount;
                result.TotalElements = totalElements;
                result.AllElementNames = request.RootElementNames ?? new List<string>();
                int currentElementIndex = 0;
                string? currentRootName = null;

                bool receivedTransferEnd = false;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var speedTracker = new SpeedTracker();
                speedTracker.Start();

                var lastProgressReport = watch.ElapsedMilliseconds;

                void ReportProgress(bool force = false)
                {
                    var now = watch.ElapsedMilliseconds;
                    if (force || now - lastProgressReport >= 50 || totalBytesReceived >= request.TotalSize)
                    {
                        lastProgressReport = now;
                        var currentSpeed = speedTracker.CalculateSpeed(totalBytesReceived);

                        ProgressUpdated?.Invoke(this, new TransferProgressEventArgs
                        {
                            CurrentFile = currentRootName ?? string.Empty,
                            BytesSent = totalBytesReceived,
                            TotalBytes = request.TotalSize,
                            SpeedMbPerSec = currentSpeed,
                            CurrentElementIndex = currentElementIndex,
                            TotalElements = totalElements
                        });
                    }
                }

                using var countingStream = new CountingStream(networkStream, onBytesRead: read =>
                {
                    totalBytesReceived = read;
                    ReportProgress(force: false);
                });

                try
                {
                    while (true)
                    {
                        transferCt.ThrowIfCancellationRequested();

                        var markerJson = await ProtocolHelper.ReceiveRawJsonAsync(countingStream, transferCt, 5000).ConfigureAwait(false);
                        if (markerJson == null)
                        {
                            throw new IOException("Connection closed by sender unexpectedly before transfer completed.");
                        }

                        var baseMsg = JsonSerializer.Deserialize<BaseProtocolMessage>(markerJson);
                        if (baseMsg == null)
                        {
                            throw new IOException("Received invalid protocol message header.");
                        }

                        if (baseMsg.Type == ProtocolMessageTypes.TransferEnd)
                        {
                            receivedTransferEnd = true;
                            break;
                        }

                        if (baseMsg.Type == ProtocolMessageTypes.FileSkip)
                        {
                            var skipMsg = JsonSerializer.Deserialize<FileSkipMessage>(markerJson);
                            if (skipMsg != null)
                                Log($"Sender skipped: {skipMsg.RelativePath} - {skipMsg.Reason}");
                            filesSkipped++;
                            continue;
                        }

                        if (baseMsg.Type == ProtocolMessageTypes.FolderBegin)
                        {
                            var folderMeta = await ProtocolHelper.ReceiveMessageAsync<FolderTarMetadata>(countingStream, transferCt, 3000).ConfigureAwait(false);
                            if (folderMeta == null)
                                throw new IOException("Connection lost while reading folder metadata.");

                            if (folderMeta.RootName != currentRootName)
                            {
                                if (currentRootName != null && !result.CompletedElementNames.Contains(currentRootName))
                                {
                                    result.CompletedElementNames.Add(currentRootName);
                                }
                                currentRootName = folderMeta.RootName;
                                currentElementIndex++;
                            }

                            if (!filesByRootElement.ContainsKey(folderMeta.RootName))
                            {
                                filesByRootElement[folderMeta.RootName] = new List<string>();
                            }

                            ReportProgress(force: true);

                            string? inFlightFolderFile = null;
                            try
                            {
                                var lenBuffer = System.Buffers.ArrayPool<byte>.Shared.Rent(4);
                                var sizeBuffer = System.Buffers.ArrayPool<byte>.Shared.Rent(8);
                                var copyBuffer = System.Buffers.ArrayPool<byte>.Shared.Rent(128 * 1024);
                                try
                                {
                                    while (true)
                                    {
                                        transferCt.ThrowIfCancellationRequested();

                                        if (!await ProtocolHelper.ReadExactAsync(countingStream, lenBuffer, 4, transferCt, 5000).ConfigureAwait(false))
                                        {
                                            throw new IOException("Connection lost while reading folder file path length.");
                                        }

                                        int pathLen = BitConverter.ToInt32(lenBuffer, 0);
                                        if (pathLen == 0)
                                        {
                                            break;
                                        }

                                        if (pathLen < 0 || pathLen > 4096)
                                        {
                                            throw new InvalidDataException($"Invalid path length in folder stream: {pathLen}");
                                        }

                                        var pathBuffer = System.Buffers.ArrayPool<byte>.Shared.Rent(pathLen);
                                        string relativePath;
                                        try
                                        {
                                            if (!await ProtocolHelper.ReadExactAsync(countingStream, pathBuffer, pathLen, transferCt, 5000).ConfigureAwait(false))
                                            {
                                                throw new IOException("Connection lost while reading folder file path.");
                                            }
                                            relativePath = System.Text.Encoding.UTF8.GetString(pathBuffer, 0, pathLen);
                                        }
                                        finally
                                        {
                                            System.Buffers.ArrayPool<byte>.Shared.Return(pathBuffer);
                                        }

                                        if (!await ProtocolHelper.ReadExactAsync(countingStream, sizeBuffer, 8, transferCt, 5000).ConfigureAwait(false))
                                        {
                                            throw new IOException("Connection lost while reading folder file size.");
                                        }

                                        long fileSize = BitConverter.ToInt64(sizeBuffer, 0);
                                        if (fileSize < 0)
                                        {
                                            throw new InvalidDataException($"Invalid file size in folder stream: {fileSize}");
                                        }

                                        var safePath = PathSanitizer.SanitizeRelativePath(savePath, relativePath);
                                        if (safePath == null)
                                        {
                                            Log($"SECURITY: Blocked malicious path in folder stream: {relativePath}");
                                            await DrainBytesAsync(countingStream, fileSize, transferCt).ConfigureAwait(false);
                                            filesSkipped++;
                                            continue;
                                        }

                                        var dirPath = Path.GetDirectoryName(safePath);
                                        if (dirPath != null && !createdDirectoriesThisSession.Contains(dirPath))
                                        {
                                            Directory.CreateDirectory(dirPath);
                                            createdDirectoriesThisSession.Add(dirPath);
                                        }

                                        inFlightFolderFile = safePath;
                                        filesByRootElement[folderMeta.RootName].Add(safePath);

                                        if (fileSize > 0)
                                        {
                                            FileStream? fs = new FileStream(
                                                safePath, 
                                                FileMode.Create, 
                                                FileAccess.Write, 
                                                FileShare.None, 
                                                1, 
                                                FileOptions.Asynchronous | FileOptions.SequentialScan);

                                            try
                                            {
                                                if (fileSize >= 1024 * 1024)
                                                {
                                                    try { fs.SetLength(fileSize); } catch { }
                                                }

                                                long remaining = fileSize;
                                                while (remaining > 0)
                                                {
                                                    transferCt.ThrowIfCancellationRequested();
                                                    int toRead = (int)Math.Min(copyBuffer.Length, remaining);
                                                    int read = await countingStream.ReadAsync(copyBuffer.AsMemory(0, toRead), transferCt).ConfigureAwait(false);
                                                    if (read == 0)
                                                    {
                                                        throw new IOException("Connection lost unexpectedly while reading folder file data.");
                                                    }
                                                    await fs.WriteAsync(copyBuffer.AsMemory(0, read), transferCt).ConfigureAwait(false);
                                                    remaining -= read;
                                                }

                                                await fs.DisposeAsync().ConfigureAwait(false);
                                                fs = null;
                                            }
                                            finally
                                            {
                                                if (fs != null)
                                                {
                                                    try { await fs.DisposeAsync().ConfigureAwait(false); } catch { }
                                                }
                                            }
                                        }
                                        else
                                        {
                                            using (new FileStream(safePath, FileMode.Create, FileAccess.Write, FileShare.None)) { }
                                        }

                                        inFlightFolderFile = null;
                                        filesReceived++;
                                    }
                                }
                                finally
                                {
                                    System.Buffers.ArrayPool<byte>.Shared.Return(lenBuffer);
                                    System.Buffers.ArrayPool<byte>.Shared.Return(sizeBuffer);
                                    System.Buffers.ArrayPool<byte>.Shared.Return(copyBuffer);
                                }
                            }
                            catch
                            {
                                if (inFlightFolderFile != null)
                                {
                                    try
                                    {
                                        if (File.Exists(inFlightFolderFile))
                                        {
                                            File.Delete(inFlightFolderFile);
                                        }
                                    }
                                    catch { }
                                }
                                throw;
                            }

                            if (!result.CompletedElementNames.Contains(folderMeta.RootName))
                            {
                                result.CompletedElementNames.Add(folderMeta.RootName);
                            }
                            continue;
                        }

                        if (baseMsg.Type == ProtocolMessageTypes.FileBegin)
                        {
                            var fileMeta = await ProtocolHelper.ReceiveMessageAsync<FileItemMetadata>(countingStream, transferCt, 3000).ConfigureAwait(false);
                            if (fileMeta == null)
                            {
                                throw new IOException("Connection lost while reading file metadata.");
                            }

                            if (fileMeta.RootName != currentRootName)
                            {
                                if (currentRootName != null && !result.CompletedElementNames.Contains(currentRootName))
                                {
                                    result.CompletedElementNames.Add(currentRootName);
                                }
                                currentRootName = fileMeta.RootName;
                                currentElementIndex++;
                            }

                            var rootKey = string.IsNullOrEmpty(fileMeta.RootName) ? fileMeta.RelativePath : fileMeta.RootName;
                            if (!filesByRootElement.ContainsKey(rootKey))
                            {
                                filesByRootElement[rootKey] = new List<string>();
                            }

                            var safePath = PathSanitizer.SanitizeRelativePath(savePath, fileMeta.RelativePath);
                            if (safePath == null)
                            {
                                Log($"SECURITY: Blocked malicious path: {fileMeta.RelativePath}");
                                await DrainBytesAsync(countingStream, fileMeta.Size, transferCt).ConfigureAwait(false);
                                filesSkipped++;
                                continue;
                            }

                            safePath = PathSanitizer.ResolveCollision(safePath);

                            var dirPath = Path.GetDirectoryName(safePath);
                            if (dirPath != null && !createdDirectoriesThisSession.Contains(dirPath))
                            {
                                Directory.CreateDirectory(dirPath);
                                createdDirectoriesThisSession.Add(dirPath);
                            }

                            ReportProgress(force: true);

                            FileStream? fs = null;
                            try
                            {
                                filesByRootElement[rootKey].Add(safePath);

                                fs = new FileStream(
                                    safePath, 
                                    FileMode.Create, 
                                    FileAccess.Write, 
                                    FileShare.None, 
                                    1, 
                                    FileOptions.Asynchronous | FileOptions.SequentialScan);

                                await PipelinedTransferEngine.StreamNetworkToFileAsync(
                                    countingStream,
                                    fs,
                                    fileMeta.Size,
                                    onBytesReceived: _ => ReportProgress(force: false),
                                    transferCt).ConfigureAwait(false);

                                await fs.DisposeAsync().ConfigureAwait(false);
                                fs = null;

                                filesReceived++;

                                if (totalElements > 1 && (fileMeta.RelativePath == fileMeta.RootName || string.IsNullOrEmpty(fileMeta.RootName)))
                                {
                                    if (!result.CompletedElementNames.Contains(rootKey))
                                    {
                                        result.CompletedElementNames.Add(rootKey);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Log($"Error receiving {fileMeta.RelativePath}: {ex.Message}");

                                if (fs != null)
                                {
                                    try { await fs.DisposeAsync().ConfigureAwait(false); } catch { }
                                    fs = null;
                                }

                                try
                                {
                                    if (File.Exists(safePath))
                                    {
                                        File.Delete(safePath);
                                        Log($"Cleaned up partial file: {fileMeta.RelativePath}");
                                    }
                                }
                                catch (Exception delEx)
                                {
                                    Log($"Failed to delete partial file: {delEx.Message}", LogLevel.Warning);
                                }

                                throw;
                            }
                            finally
                            {
                                if (fs != null)
                                {
                                    try { await fs.DisposeAsync().ConfigureAwait(false); } catch { }
                                }
                            }
                        }
                    }

                    if (!receivedTransferEnd)
                    {
                        throw new IOException("Transfer ended prematurely without TRANSFER_END marker.");
                    }

                    if (currentRootName != null && !result.CompletedElementNames.Contains(currentRootName))
                    {
                        result.CompletedElementNames.Add(currentRootName);
                    }

                    result.Success = true;
                    watch.Stop();
                    ReportProgress(force: true);

                    var summary = $"Transfer complete! Received {totalBytesReceived / 1024 / 1024} MB ({filesReceived} files) in {watch.Elapsed.TotalSeconds:F1}s.";
                    Log(summary);
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.ErrorMessage = ex is OperationCanceledException
                        ? "Transfer cancelled."
                        : (ex is System.IO.IOException || ex is System.Net.Sockets.SocketException
                            ? "Connection lost (sender aborted or network disconnected)."
                            : ex.Message);

                    if (totalElements <= 1)
                    {

                        foreach (var kvp in filesByRootElement)
                        {
                            foreach (var file in kvp.Value)
                            {
                                try
                                {
                                    if (File.Exists(file))
                                    {
                                        File.Delete(file);
                                        Log($"Rollback deleted session file: {file}");
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                    else
                    {

                        foreach (var kvp in filesByRootElement)
                        {
                            if (!result.CompletedElementNames.Contains(kvp.Key))
                            {
                                foreach (var file in kvp.Value)
                                {
                                    try
                                    {
                                        if (File.Exists(file))
                                        {
                                            File.Delete(file);
                                            Log($"Rollback deleted incomplete element file: {file}");
                                        }
                                    }
                                    catch { }
                                }
                            }
                        }
                    }

                    foreach (var dir in createdDirectoriesThisSession.OrderByDescending(d => d.Length))
                    {
                        try
                        {
                            if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                            {
                                Directory.Delete(dir);
                                Log($"Rollback deleted empty directory: {dir}");
                            }
                        }
                        catch { }
                    }

                    try { client.LingerState = new LingerOption(true, 0); client.Close(); } catch { }
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

        result.FailedElementNames = result.AllElementNames
            .Where(name => !result.CompletedElementNames.Contains(name))
            .ToList();

        return result;
    }

    private static async Task DrainBytesAsync(Stream stream, long count, CancellationToken ct)
    {
        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(65536);
        try
        {
            long drained = 0;
            while (drained < count)
            {
                int toRead = (int)Math.Min(buffer.Length, count - drained);
                int read = await stream.ReadAsync(buffer.AsMemory(0, toRead), ct).ConfigureAwait(false);
                if (read == 0)
                    throw new IOException("Connection lost while draining skipped file data.");

                drained += read;
            }
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}

