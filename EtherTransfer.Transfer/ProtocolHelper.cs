using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EtherTransfer.Core.Models;

namespace EtherTransfer.Transfer;

/// <summary>
/// Contains shared helper methods for transferring metadata and binary data over a TCP stream.
/// </summary>
public static class ProtocolHelper
{
    // Write a JSON metadata message to the stream
    public static async Task SendMessageAsync<T>(NetworkStream stream, T message, CancellationToken ct) where T : class
    {
        var json = JsonSerializer.Serialize(message);
        var bytes = Encoding.UTF8.GetBytes(json);

        // Protocol: 4 bytes length prefix, followed by UTF8 JSON payload
        var lengthPrefix = BitConverter.GetBytes(bytes.Length);
        await stream.WriteAsync(lengthPrefix, 0, 4, ct);
        await stream.WriteAsync(bytes, 0, bytes.Length, ct);
        await stream.FlushAsync(ct);
    }

    // Read a JSON metadata message from the stream
    public static async Task<T?> ReceiveMessageAsync<T>(NetworkStream stream, CancellationToken ct) where T : class
    {
        // Read 4 bytes length prefix
        var lengthBuffer = new byte[4];
        if (!await ReadExactAsync(stream, lengthBuffer, 4, ct))
            return null;

        var length = BitConverter.ToInt32(lengthBuffer, 0);
        if (length <= 0 || length > 10 * 1024 * 1024) // 10MB sanity limit for metadata
            throw new InvalidDataException($"Invalid metadata length: {length}");

        var payloadBuffer = new byte[length];
        if (!await ReadExactAsync(stream, payloadBuffer, length, ct))
            return null;

        var json = Encoding.UTF8.GetString(payloadBuffer);
        return JsonSerializer.Deserialize<T>(json);
    }

    // Read exactly N bytes from the stream, handling partial reads
    public static async Task<bool> ReadExactAsync(NetworkStream stream, byte[] buffer, int count, CancellationToken ct)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(totalRead, count - totalRead), ct);
            if (read == 0) return false; // Connection closed
            totalRead += read;
        }
        return true;
    }

    /// <summary>
    /// Reads a length-prefixed JSON message from the stream and returns the raw JSON string.
    /// This allows the caller to inspect the "Type" field before deserializing to the correct type.
    /// </summary>
    public static async Task<string?> ReceiveRawJsonAsync(NetworkStream stream, CancellationToken ct)
    {
        var lengthBuffer = new byte[4];
        if (!await ReadExactAsync(stream, lengthBuffer, 4, ct))
            return null;

        var length = BitConverter.ToInt32(lengthBuffer, 0);
        if (length <= 0 || length > 10 * 1024 * 1024)
            throw new InvalidDataException($"Invalid metadata length: {length}");

        var payloadBuffer = new byte[length];
        if (!await ReadExactAsync(stream, payloadBuffer, length, ct))
            return null;

        return Encoding.UTF8.GetString(payloadBuffer);
    }
}
