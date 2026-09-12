using ZArchiveSharp;

namespace SimpleZipDrive.Core;

/// <summary>
///     Seekable, read-only stream over a file stored in a .zar archive. Reads are served
///     through <see cref="ZArchiveReader.ReadFromFile" />, which decompresses only the
///     64 KiB blocks touched by the requested range.
/// </summary>
public sealed class ZarEntryStream : Stream
{
    private readonly uint _node;
    private readonly ZArchiveReader _reader;
    private long _position;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ZarEntryStream" /> class.
    /// </summary>
    /// <param name="reader">The shared archive reader (not owned by this stream).</param>
    /// <param name="node">The node id of the file within the archive.</param>
    /// <param name="size">The uncompressed file size in bytes.</param>
    internal ZarEntryStream(ZArchiveReader reader, uint node, long size)
    {
        _reader = reader;
        _node = node;
        Length = size;
    }

    /// <inheritdoc />
    public override bool CanRead => true;

    /// <inheritdoc />
    public override bool CanSeek => true;

    /// <inheritdoc />
    public override bool CanWrite => false;

    /// <inheritdoc />
    public override long Length { get; }

    /// <inheritdoc />
    public override long Position
    {
        get => _position;
        set => _position = value >= 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), "Position cannot be negative.");
    }

    /// <inheritdoc />
    public override void Flush()
    {
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
    {
        ValidateBuffer(buffer, offset, count);

        var remaining = Length - _position;
        if (remaining <= 0 || count <= 0) return 0;

        var toRead = (int)Math.Min(count, remaining);
        var bytesRead = _reader.ReadFromFile(_node, (ulong)_position, buffer.AsSpan(offset, toRead));

        _position += (long)bytesRead;
        return (int)bytesRead;
    }

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin)
    {
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
