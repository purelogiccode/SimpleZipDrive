namespace SimpleZipDrive.Core;

/// <summary>
///     A read-only stream over a shared, cached decompressed entry buffer.
///     Every open of the same archive entry gets its own stream instance with an independent
///     position, but all instances share the single underlying <see cref="byte" />[] so the entry
///     is decompressed only once regardless of how many handles (or on-demand reads) are active.
///     Disposing the stream releases the caller's reference; the buffer stays warm in the cache
///     until it is evicted under memory pressure or the owning core is disposed.
/// </summary>
/// <remarks>
///     <see cref="MemoryStream" /> is not thread-safe: a read computes the number of available
///     bytes from its position and then copies from that position, so a concurrent position change
///     can make the copy read past the end of the buffer (bug report #69584). Windows can issue
///     concurrent reads on the same open handle (paging I/O, multiple readers of one file), so all
///     access to the inner stream is serialized here and <see cref="ReadAt" /> performs the
///     seek-and-read atomically.
/// </remarks>
internal sealed class SharedMemoryStream : Stream
{
    private readonly MemoryStream _inner;
    private readonly Action _onDispose;
    private readonly Lock _sync = new();
    private bool _disposed;

    /// <summary>
    ///     Initializes a new read-only stream over the supplied shared buffer.
    /// </summary>
    /// <param name="buffer">The shared buffer to expose.</param>
    /// <param name="onDispose">Callback invoked once when the stream is disposed.</param>
    public SharedMemoryStream(byte[] buffer, Action onDispose)
    {
        _inner = new MemoryStream(buffer, false);
        _onDispose = onDispose;
    }

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length
    {
        get
        {
            lock (_sync)
            {
                return _inner.Length;
            }
        }
    }

    public override long Position
    {
        get
        {
            lock (_sync)
            {
                return _inner.Position;
            }
        }
        set
        {
            lock (_sync)
            {
                _inner.Position = value;
            }
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        lock (_sync)
        {
            return _inner.Read(buffer, offset, count);
        }
    }

    public override int Read(Span<byte> buffer)
    {
        lock (_sync)
        {
            return _inner.Read(buffer);
        }
    }

    /// <summary>
    ///     Reads up to <paramref name="count" /> bytes starting at <paramref name="offset" />.
    ///     Seeking and reading happen as a single atomic operation, so concurrent reads on the
    ///     same handle cannot move the position between the two steps.
    /// </summary>
    /// <param name="offset">Absolute offset within the entry to start reading from.</param>
    /// <param name="buffer">Destination buffer.</param>
    /// <param name="bufferOffset">Offset within <paramref name="buffer" /> to write to.</param>
    /// <param name="count">Maximum number of bytes to read.</param>
    /// <returns>The number of bytes actually read.</returns>
    public int ReadAt(long offset, byte[] buffer, int bufferOffset, int count)
    {
        lock (_sync)
        {
            if (offset < 0 || offset >= _inner.Length) return 0;

            _inner.Position = offset;
            return _inner.Read(buffer, bufferOffset, count);
        }
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        lock (_sync)
        {
            return _inner.Seek(offset, origin);
        }
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    protected override void Dispose(bool disposing)
    {
        lock (_sync)
        {
            if (!_disposed)
            {
                _disposed = true;
                if (disposing) _onDispose();
            }

            _inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
