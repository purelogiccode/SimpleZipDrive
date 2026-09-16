using System.Diagnostics.CodeAnalysis;
using SharpCompress.Archives;
using SharpCompress.Common;
using XISOSharp;

namespace SimpleZipDrive.Core;

/// <summary>
///     An <see cref="IArchiveEntry" /> backed by an entry in an <see cref="XisoArchive" />.
/// </summary>
public sealed class XisoArchiveEntry : IArchiveEntry
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="XisoArchiveEntry" /> class.
    /// </summary>
    /// <param name="archive">The owning archive.</param>
    /// <param name="key">The entry key, relative to the image root (directories end with '/').</param>
    /// <param name="internalPath">The image-internal path (leading '/', e.g. <c>/data/info.txt</c>).</param>
    /// <param name="startSector">Partition-relative first sector of the entry's data.</param>
    /// <param name="size">The uncompressed size in bytes (0 for directories).</param>
    /// <param name="isDirectory">Whether the entry is a directory.</param>
    /// <param name="node">The explorer node backing path-based mounts, or null for stream-backed mounts.</param>
    internal XisoArchiveEntry(XisoArchive archive, string key, string internalPath, uint startSector, long size,
        bool isDirectory, ExplorerNode? node = null)
    {
        Archive = archive;
        Key = key;
        InternalPath = internalPath;
        StartSector = startSector;
        Size = size;
        IsDirectory = isDirectory;
        Node = node;
    }

    /// <inheritdoc />
    public IArchive Archive { get; }

    /// <inheritdoc />
    public bool IsComplete => true;

    /// <inheritdoc />
    public string Key { get; }

    /// <summary>Gets the image-internal path of the entry (leading '/').</summary>
    internal string InternalPath { get; }

    /// <summary>Gets the partition-relative first sector of the entry's data.</summary>
    internal uint StartSector { get; }

    /// <summary>Gets the explorer node backing this entry, or null for stream-backed mounts.</summary>
    internal ExplorerNode? Node { get; }

    /// <inheritdoc />
    public CompressionType CompressionType => CompressionType.None;

    /// <inheritdoc />
    public DateTime? ArchivedTime => null;

    /// <inheritdoc />
    public long CompressedSize => Size;

    /// <inheritdoc />
    public long Crc => 0;

    /// <inheritdoc />
    public DateTime? CreatedTime => null;

    /// <inheritdoc />
    public string? LinkTarget => null;

    /// <inheritdoc />
    public bool IsDirectory { get; }

    /// <inheritdoc />
    public bool IsEncrypted => false;

    /// <inheritdoc />
    public bool IsSplitAfter => false;

    /// <inheritdoc />
    public bool IsSolid => false;

    /// <inheritdoc />
    public int VolumeIndexFirst => 0;

    /// <inheritdoc />
    public int VolumeIndexLast => 0;

    /// <inheritdoc />
    public DateTime? LastAccessedTime => null;

    /// <inheritdoc />
    public DateTime? LastModifiedTime => null;

    /// <inheritdoc />
    public long Size { get; }

    /// <inheritdoc />
    public int? Attrib => null;

    /// <inheritdoc />
    public SharpCompress.Common.Options.IReaderOptions Options => Archive.ReaderOptions;

    /// <inheritdoc />
    public Stream OpenEntryStream()
    {
        return ((XisoArchive)Archive).OpenEntryStream(this);
    }

    /// <inheritdoc />
    public ValueTask<Stream> OpenEntryStreamAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(OpenEntryStream());
    }

    /// <inheritdoc />
    [ExcludeFromCodeCoverage]
    public override string ToString()
    {
        return Key;
    }
}