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

                            string? inFlightTarFile = null;
                            try
                            {
                                var tarReader = new TarReader(countingStream, leaveOpen: true);
                                await using (tarReader.ConfigureAwait(false))
                                {
                                    while (await tarReader.GetNextEntryAsync(cancellationToken: transferCt).ConfigureAwait(false) is { } entry)
                                    {
                                        transferCt.ThrowIfCancellationRequested();

                                        if (entry.EntryType == TarEntryType.RegularFile || entry.EntryType == TarEntryType.V7RegularFile)
                                        {
                                            var safePath = PathSanitizer.SanitizeRelativePath(savePath, entry.Name);
                                            if (safePath == null)
                                            {
                                                Log($"SECURITY: Blocked malicious path in TAR: {entry.Name}");
                                                filesSkipped++;
                                                continue;
                                            }

                                            var dirPath = Path.GetDirectoryName(safePath);
                                            if (dirPath != null && !createdDirectoriesThisSession.Contains(dirPath))
                                            {
                                                Directory.CreateDirectory(dirPath);
                                                createdDirectoriesThisSession.Add(dirPath);
                                            }

                                            if (entry.DataStream != null)
                                            {
                                                inFlightTarFile = safePath;
                                                filesByRootElement[folderMeta.RootName].Add(safePath);

                                                FileStream? fs = new FileStream(
                                                    safePath, 
                                                    FileMode.Create, 
                                                    FileAccess.Write, 
                                                    FileShare.None, 
                                                    128 * 1024, 
                                                    FileOptions.Asynchronous | FileOptions.SequentialScan);

                                                try
                                                {
                                                    if (entry.Length >= 1024 * 1024)
                                                    {
                                                        try { fs.SetLength(entry.Length); } catch { }
                                                    }
                                                    await entry.DataStream.CopyToAsync(fs, 128 * 1024, transferCt).ConfigureAwait(false);
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

                                                inFlightTarFile = null;
                                            }
                                            else
                                            {
                                                filesByRootElement[folderMeta.RootName].Add(safePath);
                                                using (new FileStream(safePath, FileMode.Create, FileAccess.Write, FileShare.None)) { }
                                            }

                                            filesReceived++;
                                            ReportProgress(force: false);
                                        }
                                        else if (entry.EntryType == TarEntryType.Directory)
                                        {
                                            var safeDir = PathSanitizer.SanitizeRelativePath(savePath, entry.Name);
                                            if (safeDir != null && !createdDirectoriesThisSession.Contains(safeDir))
                                            {
                                                Directory.CreateDirectory(safeDir);
                                                createdDirectoriesThisSession.Add(safeDir);
                                            }
                                        }
                                    }
                                }

                                var trailingTarEofBlock = System.Buffers.ArrayPool<byte>.Shared.Rent(512);
                                try
                                {
                                    await ProtocolHelper.ReadExactAsync(countingStream, trailingTarEofBlock, 512, transferCt, 3000).ConfigureAwait(false);
                                }
                                finally
                                {
                                    System.Buffers.ArrayPool<byte>.Shared.Return(trailingTarEofBlock);
                                }
                            }
                            catch
                            {
                                if (inFlightTarFile != null)
                                {
                                    try
                                    {
                                        if (File.Exists(inFlightTarFile))
                                        {
                                            File.Delete(inFlightTarFile);
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

