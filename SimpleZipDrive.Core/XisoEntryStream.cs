namespace SimpleZipDrive.Core;

/// <summary>
///     Seekable, read-only stream over a file stored in an Xbox XISO disc image. Reads
///     are served directly from the image stream through the file's sector extent, so
///     seeking never decompresses or extracts the entry.
/// </summary>
public sealed class XisoEntryStream : Stream
{
    private readonly Lock _readLock;
    private readonly Stream _source;
    private readonly long _start;
    private bool _disposed;
    private long _position;

    /// <summary>
    ///     Initializes a new instance of the <see cref="XisoEntryStream" /> class.
    /// </summary>
    /// <param name="source">The shared image stream (not owned by this stream).</param>
    /// <param name="start">The absolute byte offset where the entry's data begins.</param>
    /// <param name="length">The entry's size in bytes.</param>
    /// <param name="readLock">Lock serializing reads on the shared image stream.</param>
    internal XisoEntryStream(Stream source, long start, long length, Lock readLock)
    {
        _source = source;
        _start = start;
        _readLock = readLock;
        Length = length;
    }

    /// <inheritdoc />
    public override bool CanRead => !_disposed;

    /// <inheritdoc />
    public override bool CanSeek => !_disposed;

    /// <inheritdoc />
    public override bool CanWrite => false;

    /// <inheritdoc />
    public override long Length { get; }

    /// <inheritdoc />
    public override long Position
    {
        get => _position;
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            _position = value >= 0
                ? value
                : throw new ArgumentOutOfRangeException(nameof(value), "Position cannot be negative.");
        }
    }

    /// <inheritdoc />
    public override void Flush()
    {
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateBuffer(buffer, offset, count);

        var remaining = Length - _position;
        if (remaining <= 0 || count <= 0) return 0;

        var toRead = (int)Math.Min(count, remaining);

        lock (_readLock)
        {
            _source.Seek(_start + _position, SeekOrigin.Begin);
            var bytesRead = _source.Read(buffer, offset, toRead);

            _position += bytesRead;
            return bytesRead;
        }
    }

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => Length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin), origin, "Invalid seek origin.")
        };

        if (target < 0)
            throw new IOException("An attempt was made to move the position before the beginning of the stream.");

        _position = target;
        return _position;
    }

    /// <inheritdoc />
    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        _disposed = true;
        base.Dispose(disposing);
    }

    private static void ValidateBuffer(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset), "Offset cannot be negative.");

        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), "Count cannot be negative.");

        if (buffer.Length - offset < count)
            throw new ArgumentException("Offset and count exceed the buffer length.");
    }
}
