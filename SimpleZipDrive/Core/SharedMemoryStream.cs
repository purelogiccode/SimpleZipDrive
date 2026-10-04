namespace SimpleZipDrive.Core;

/// <summary>
///     A read-only stream over a shared, cached decompressed entry buffer.
///     Every open of the same archive entry gets its own stream instance with an independent
///     position, but all instances share the single underlying <see cref="byte" />[] so the entry
///     is decompressed only once regardless of how many handles (or on-demand reads) are active.
///     Disposing the stream releases the caller's reference; the buffer stays warm in the cache
///     until it is evicted under memory pressure or the owning core is disposed.
/// </summary>
internal sealed class SharedMemoryStream : Stream
{
    private readonly MemoryStream _inner;
    private readonly Action _onDispose;
    private bool _disposed;

    public SharedMemoryStream(byte[] buffer, Action onDispose)
    {
        _inner = new MemoryStream(buffer, false);
        _onDispose = onDispose;
    }

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length => _inner.Length;

    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return _inner.Read(buffer, offset, count);
    }

    public override int Read(Span<byte> buffer)
    {
        return _inner.Read(buffer);
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        return _inner.Seek(offset, origin);
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
        if (!_disposed)
        {
            _disposed = true;
            if (disposing) _onDispose();
        }

        _inner.Dispose();
        base.Dispose(disposing);
    }
}