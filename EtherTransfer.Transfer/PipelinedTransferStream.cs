using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace EtherTransfer.Transfer;

public class SpeedTracker
{
    private readonly System.Diagnostics.Stopwatch _stopwatch = new();
    private long _lastBytes = 0;
    private double _lastTimestamp = 0;
    private double _smoothedSpeedMbPerSec = 0;
    private const double Alpha = 0.35;

    public void Start()
    {
        _stopwatch.Restart();
        _lastBytes = 0;
        _lastTimestamp = 0;
        _smoothedSpeedMbPerSec = 0;
    }

    public double CalculateSpeed(long currentTotalBytes)
    {
        if (!_stopwatch.IsRunning)
        {
            _stopwatch.Start();
        }

        var currentSeconds = _stopwatch.Elapsed.TotalSeconds;
        var deltaSeconds = currentSeconds - _lastTimestamp;

        if (deltaSeconds >= 0.2)
        {
            var deltaBytes = currentTotalBytes - _lastBytes;
            if (deltaBytes < 0) deltaBytes = 0;

            var instantSpeedMbPerSec = deltaSeconds > 0 ? (deltaBytes / (1024.0 * 1024.0)) / deltaSeconds : 0;

            if (_smoothedSpeedMbPerSec <= 0.01)
            {
                _smoothedSpeedMbPerSec = instantSpeedMbPerSec;
            }
            else
            {
                _smoothedSpeedMbPerSec = (Alpha * instantSpeedMbPerSec) + ((1.0 - Alpha) * _smoothedSpeedMbPerSec);
            }

            _lastBytes = currentTotalBytes;
            _lastTimestamp = currentSeconds;
        }

        return _smoothedSpeedMbPerSec;
    }
}

public class CountingStream : Stream
{
    private readonly Stream _innerStream;
    private long _bytesRead;
    private long _bytesWritten;
    private readonly Action<long>? _onBytesRead;
    private readonly Action<long>? _onBytesWritten;

    public long TotalBytesRead => _bytesRead;
    public long TotalBytesWritten => _bytesWritten;

    public CountingStream(Stream innerStream, Action<long>? onBytesRead = null, Action<long>? onBytesWritten = null)
    {
        _innerStream = innerStream ?? throw new ArgumentNullException(nameof(innerStream));
        _onBytesRead = onBytesRead;
        _onBytesWritten = onBytesWritten;
    }

    public override bool CanRead => _innerStream.CanRead;
    public override bool CanSeek => _innerStream.CanSeek;
    public override bool CanWrite => _innerStream.CanWrite;
    public override long Length => _innerStream.Length;
    public override long Position { get => _innerStream.Position; set => _innerStream.Position = value; }

    public override void Flush() => _innerStream.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => _innerStream.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count)
    {
        int read = _innerStream.Read(buffer, offset, count);
        if (read > 0)
        {
            Interlocked.Add(ref _bytesRead, read);
            _onBytesRead?.Invoke(_bytesRead);
        }
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int read = await _innerStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        if (read > 0)
        {
            Interlocked.Add(ref _bytesRead, read);
            _onBytesRead?.Invoke(_bytesRead);
        }
        return read;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        int read = await _innerStream.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
        if (read > 0)
        {
            Interlocked.Add(ref _bytesRead, read);
            _onBytesRead?.Invoke(_bytesRead);
        }
        return read;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        _innerStream.Write(buffer, offset, count);
        Interlocked.Add(ref _bytesWritten, count);
        _onBytesWritten?.Invoke(_bytesWritten);
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await _innerStream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        Interlocked.Add(ref _bytesWritten, buffer.Length);
        _onBytesWritten?.Invoke(_bytesWritten);
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        await _innerStream.WriteAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
        Interlocked.Add(ref _bytesWritten, count);
        _onBytesWritten?.Invoke(_bytesWritten);
    }

    public override long Seek(long offset, SeekOrigin origin) => _innerStream.Seek(offset, origin);
    public override void SetLength(long value) => _innerStream.SetLength(value);
}

internal sealed class BufferChunk : IDisposable
{
    public byte[] Buffer { get; }
    public int Length { get; set; }

    public BufferChunk(int size)
    {
        Buffer = ArrayPool<byte>.Shared.Rent(size);
        Length = 0;
    }

    public void Dispose()
    {
        ArrayPool<byte>.Shared.Return(Buffer);
    }
}

public static class PipelinedTransferEngine
{
    private const int ChunkSize = 1024 * 1024;
    private const int ChannelCapacity = 4;

    public static async Task StreamFileToNetworkAsync(
        FileStream fs,
        Stream networkStream,
        long fileSize,
        Action<int> onBytesSent,
        CancellationToken ct)
    {
        using var internalCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = internalCts.Token;

        var channel = Channel.CreateBounded<BufferChunk>(new BoundedChannelOptions(ChannelCapacity)
        {
            SingleWriter = true,
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait
        });

        var readerTask = Task.Run(async () =>
        {
            try
            {
                long totalRead = 0;
                while (totalRead < fileSize)
                {
                    token.ThrowIfCancellationRequested();

                    int toRead = (int)Math.Min(ChunkSize, fileSize - totalRead);
                    var chunk = new BufferChunk(toRead);

                    int read = await fs.ReadAsync(chunk.Buffer.AsMemory(0, toRead), token).ConfigureAwait(false);
                    if (read == 0)
                    {
                        chunk.Dispose();
                        break;
                    }

                    chunk.Length = read;
                    totalRead += read;

                    await channel.Writer.WriteAsync(chunk, token).ConfigureAwait(false);
                }
                channel.Writer.Complete();
            }
            catch (Exception ex)
            {
                channel.Writer.Complete(ex);
                try { internalCts.Cancel(); } catch { }
                throw;
            }
        }, token);

        var writerTask = Task.Run(async () =>
        {
            try
            {
                while (await channel.Reader.WaitToReadAsync(token).ConfigureAwait(false))
                {
                    while (channel.Reader.TryRead(out var chunk))
                    {
                        using (chunk)
                        {
                            await networkStream.WriteAsync(chunk.Buffer.AsMemory(0, chunk.Length), token).ConfigureAwait(false);
                            onBytesSent(chunk.Length);
                        }
                    }
                }
            }
            catch (Exception)
            {
                try { internalCts.Cancel(); } catch { }
                while (channel.Reader.TryRead(out var chunk))
                {
                    chunk.Dispose();
                }
                throw;
            }
        }, token);

        try
        {
            await Task.WhenAll(readerTask, writerTask).ConfigureAwait(false);
        }
        catch
        {
            while (channel.Reader.TryRead(out var chunk))
            {
                chunk.Dispose();
            }
            throw;
        }
    }

    public static async Task StreamNetworkToFileAsync(
        Stream networkStream,
        FileStream fs,
        long fileSize,
        Action<int> onBytesReceived,
        CancellationToken ct)
    {

        if (fileSize > 0)
        {
            try
            {
                fs.SetLength(fileSize);
            }
            catch
            {

            }
        }

        using var internalCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = internalCts.Token;

        var channel = Channel.CreateBounded<BufferChunk>(new BoundedChannelOptions(ChannelCapacity)
        {
            SingleWriter = true,
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait
        });

        var readerTask = Task.Run(async () =>
        {
            try
            {
                long totalRead = 0;
                while (totalRead < fileSize)
                {
                    token.ThrowIfCancellationRequested();

                    int toRead = (int)Math.Min(ChunkSize, fileSize - totalRead);
                    var chunk = new BufferChunk(toRead);

                    int readExact = 0;
                    while (readExact < toRead)
                    {
                        int read = await networkStream.ReadAsync(chunk.Buffer.AsMemory(readExact, toRead - readExact), token).ConfigureAwait(false);
                        if (read == 0)
                        {
                            chunk.Dispose();
                            throw new IOException("Connection lost unexpectedly while receiving file data.");
                        }
                        readExact += read;
                    }

                    chunk.Length = readExact;
                    totalRead += readExact;

                    await channel.Writer.WriteAsync(chunk, token).ConfigureAwait(false);
                }
                channel.Writer.Complete();
            }
            catch (Exception ex)
            {
                channel.Writer.Complete(ex);
                try { internalCts.Cancel(); } catch { }
                throw;
            }
        }, token);

        var writerTask = Task.Run(async () =>
        {
            try
            {
                while (await channel.Reader.WaitToReadAsync(token).ConfigureAwait(false))
                {
                    while (channel.Reader.TryRead(out var chunk))
                    {
                        using (chunk)
                        {
                            await fs.WriteAsync(chunk.Buffer.AsMemory(0, chunk.Length), token).ConfigureAwait(false);
                            onBytesReceived(chunk.Length);
                        }
                    }
                }

                await fs.FlushAsync(token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                try { internalCts.Cancel(); } catch { }
                while (channel.Reader.TryRead(out var chunk))
                {
                    chunk.Dispose();
                }
                throw;
            }
        }, token);

        try
        {
            await Task.WhenAll(readerTask, writerTask).ConfigureAwait(false);
        }
        catch
        {
            while (channel.Reader.TryRead(out var chunk))
            {
                chunk.Dispose();
            }
            throw;
        }
    }
}

