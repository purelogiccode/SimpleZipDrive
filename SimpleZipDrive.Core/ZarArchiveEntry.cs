using System.Diagnostics.CodeAnalysis;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace SimpleZipDrive.Core;

/// <summary>
///     An <see cref="IArchiveEntry" /> backed by a node in a <see cref="ZarArchive" />.
/// </summary>
public sealed class ZarArchiveEntry : IArchiveEntry
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="ZarArchiveEntry" /> class.
    /// </summary>
    /// <param name="archive">The owning archive.</param>
    /// <param name="key">The entry key, relative to the archive root (directories end with '/').</param>
    /// <param name="size">The uncompressed size in bytes (0 for directories).</param>
    /// <param name="isDirectory">Whether the entry is a directory.</param>
    /// <param name="nodeId">The node id of the entry within the archive's file tree.</param>
    internal ZarArchiveEntry(ZarArchive archive, string key, long size, bool isDirectory, uint nodeId)
    {
        Archive = archive;
        Key = key;
        Size = size;
        IsDirectory = isDirectory;
        NodeId = nodeId;
    }

    /// <inheritdoc />
    public IArchive Archive { get; }

    /// <inheritdoc />
    public bool IsComplete => true;

    /// <inheritdoc />
    public string Key { get; }

    /// <summary>Gets the node id of the entry within the archive's file tree.</summary>
    internal uint NodeId { get; }

    /// <inheritdoc />
    public CompressionType CompressionType => CompressionType.ZStandard;

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
        return ((ZarArchive)Archive).OpenEntryStream(this);
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