using System.Buffers;
using SimpleZipDrive.FuseSharp;

namespace SimpleZipDrive.Mounting.Fuse;

/// <summary>
///     Adapts a <see cref="ZipFileSystemCore" /> archive to the FuseSharp
///     <see cref="IFuseVolume" /> contract so it can be served through FUSE on Linux/macOS.
/// </summary>
internal sealed class FuseVolumeAdapter : IFuseVolume
{
    private readonly ZipFileSystemCore _core;

    public FuseVolumeAdapter(ZipFileSystemCore core)
    {
        _core = core ?? throw new ArgumentNullException(nameof(core));
    }

    /// <inheritdoc />
    public string VolumeLabel => _core.VolumeLabel;

    /// <inheritdoc />
    public DateTime VolumeCreationTime { get; } = DateTime.UtcNow;

    /// <inheritdoc />
    public ulong VolumeSize => (ulong)Math.Max(0, _core.TotalSize);

    /// <inheritdoc />
    public IFuseEntry? GetEntry(string path)
    {
        var node = _core.GetEntryNode(ToCorePath(path));
        return node is null ? null : new EntryAdapter(node);
    }

    /// <inheritdoc />
    public IEnumerable<IFuseEntry> GetFolderList(string path)
    {
        return _core.ListDirectory(ToCorePath(path)).Select(static IFuseEntry (node) => new EntryAdapter(node));
    }

    /// <inheritdoc />
    public int ReadFile(IFuseEntry entry, Span<byte> buffer, long offset)
    {
        if (entry is not EntryAdapter adapter)
            throw new ArgumentException("The entry does not belong to this volume.", nameof(entry));

        var node = adapter.Node;
        if (node.IsDir) throw new IOException("Cannot read a directory.");
        if (node.Entry is null) return 0;
        if (_core.IsFailedEntry(node.NormalizedPath))
            throw new IOException("The archive entry failed to decompress.");

        using var stream = _core.OpenEntryStream(node.Entry, node.NormalizedPath)
                           ?? throw new IOException("The archive entry could not be opened.");

        // ZipFileSystemCore.ReadStream works on byte[]; rent a pooled buffer and copy into the span.
        var rented = ArrayPool<byte>.Shared.Rent(buffer.Length);
        try
        {
            var read = ZipFileSystemCore.ReadStream(stream, offset, rented, 0, buffer.Length);
            rented.AsSpan(0, read).CopyTo(buffer);
            return read;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static string ToCorePath(string path)
    {
        if (string.IsNullOrEmpty(path) || string.Equals(path, "/", StringComparison.Ordinal)) return "/";

        return ZipFsHelpers.NormalizePath(path);
    }

    private sealed class EntryAdapter(EntryNode node) : IFuseEntry
    {
        public EntryNode Node { get; } = node;

        public string FileName => Path.GetFileName(Node.NormalizedPath);

        public bool IsDirectory => Node.IsDir;

        public long Size => Node.IsDir ? 0 : Node.FileSize;
    }
}
