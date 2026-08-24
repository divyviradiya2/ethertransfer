using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EtherTransfer.Core.Models;

namespace EtherTransfer.Transfer;

public static class ProtocolHelper
{
    public static async Task SendMessageAsync<T>(Stream stream, T message, CancellationToken ct, int timeoutMs = -1) where T : class
    {
        var json = JsonSerializer.Serialize(message);
        var bytes = Encoding.UTF8.GetBytes(json);

        var lengthPrefix = BitConverter.GetBytes(bytes.Length);

        if (timeoutMs > 0)
        {
            using var watchdogCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            watchdogCts.CancelAfter(timeoutMs);
            try
            {
                await stream.WriteAsync(lengthPrefix.AsMemory(0, 4), watchdogCts.Token).ConfigureAwait(false);
                await stream.WriteAsync(bytes.AsMemory(0, bytes.Length), watchdogCts.Token).ConfigureAwait(false);
                await stream.FlushAsync(watchdogCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new IOException("Connection timed out (Ethernet cable disconnected or network dropped).");
            }
        }
        else
        {
            await stream.WriteAsync(lengthPrefix.AsMemory(0, 4), ct).ConfigureAwait(false);
            await stream.WriteAsync(bytes.AsMemory(0, bytes.Length), ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }
    }

    public static async Task<T?> ReceiveMessageAsync<T>(Stream stream, CancellationToken ct, int timeoutMs = -1) where T : class
    {
        var lengthBuffer = System.Buffers.ArrayPool<byte>.Shared.Rent(4);
        try
        {
            if (!await ReadExactAsync(stream, lengthBuffer, 4, ct, timeoutMs).ConfigureAwait(false))
                return null;

            var length = BitConverter.ToInt32(lengthBuffer, 0);
            if (length <= 0 || length > 10 * 1024 * 1024)
                throw new InvalidDataException($"Invalid metadata length: {length}");

            var payloadBuffer = System.Buffers.ArrayPool<byte>.Shared.Rent(length);
            try
            {
                if (!await ReadExactAsync(stream, payloadBuffer, length, ct, timeoutMs).ConfigureAwait(false))
                    return null;

                var json = Encoding.UTF8.GetString(payloadBuffer, 0, length);
                return JsonSerializer.Deserialize<T>(json);
            }
            finally
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(payloadBuffer);
            }
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(lengthBuffer);
        }
    }

    public static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, int count, CancellationToken ct, int timeoutMs = -1)
    {
        int totalRead = 0;
        if (timeoutMs > 0)
        {
            using var watchdogCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            watchdogCts.CancelAfter(timeoutMs);

            while (totalRead < count)
            {
                int read;
                try
                {
                    read = await stream.ReadAsync(buffer.AsMemory(totalRead, count - totalRead), watchdogCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new IOException("Connection timed out (Ethernet cable disconnected or network dropped).");
                }

                if (read == 0) return false;
                totalRead += read;
            }
        }
        else
        {
            while (totalRead < count)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(totalRead, count - totalRead), ct).ConfigureAwait(false);
                if (read == 0) return false;
                totalRead += read;
            }
        }
        return true;
    }

    public static async Task<string?> ReceiveRawJsonAsync(Stream stream, CancellationToken ct, int timeoutMs = -1)
    {
        var lengthBuffer = System.Buffers.ArrayPool<byte>.Shared.Rent(4);
        try
        {
            if (!await ReadExactAsync(stream, lengthBuffer, 4, ct, timeoutMs).ConfigureAwait(false))
                return null;

            var length = BitConverter.ToInt32(lengthBuffer, 0);
            if (length <= 0 || length > 10 * 1024 * 1024)
                throw new InvalidDataException($"Invalid metadata length: {length}");

            var payloadBuffer = System.Buffers.ArrayPool<byte>.Shared.Rent(length);
            try
            {
                if (!await ReadExactAsync(stream, payloadBuffer, length, ct, timeoutMs).ConfigureAwait(false))
                    return null;

                return Encoding.UTF8.GetString(payloadBuffer, 0, length);
            }
            finally
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(payloadBuffer);
            }
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(lengthBuffer);
        }
    }
}

