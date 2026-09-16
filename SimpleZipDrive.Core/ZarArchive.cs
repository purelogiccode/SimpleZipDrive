using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using ZArchiveSharp;

namespace SimpleZipDrive.Core;

/// <summary>
///     SharpCompress adapter over ZArchiveSharp's <see cref="ZArchiveReader" /> so ZArchive
///     (<c>.zar</c>) containers can be mounted by <see cref="ZipFileSystemCore" /> like the
///     other supported archive formats.
///     ZArchive is a read-only, directory-tree archive with per-block zstd compression and
///     no encryption, so password handling never triggers for this format.
/// </summary>
public sealed class ZarArchive : IArchive
{
    private readonly List<ZarArchiveEntry> _entries = [];
    private readonly ZarVolume _volume;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ZarArchive" /> class by parsing the
    ///     given archive stream.
    /// </summary>
    /// <param name="archiveStream">The seekable stream containing the .zar archive.</param>
    /// <exception cref="InvalidOperationException">The stream is not a valid .zar archive.</exception>
    public ZarArchive(Stream archiveStream)
    {
        ZArchiveReader? reader = null;
        try
        {
            // DecodeExtendedNames fixes the upstream 0.1.2 quirk where names of
            // 128+ characters decode to an empty string and become invisible.
            reader = ZArchiveReader.TryOpen(archiveStream, leaveOpen: true,
                         new ZArchiveReaderOptions { DecodeExtendedNames = true })
                     ?? throw new InvalidOperationException("The file is not a valid ZArchive (.zar) archive.");

            Reader = reader;
            _volume = new ZarVolume((archiveStream as FileStream)?.Name ?? string.Empty);
            EnumerateEntries();
        }
        catch
        {
            reader?.Dispose();
            throw;
        }
    }

    internal ZArchiveReader Reader { get; }

    /// <inheritdoc />
    public ArchiveType Type => ArchiveType.Tar;

    /// <inheritdoc />
    public bool IsSolid => false;

    /// <inheritdoc />
    public bool IsComplete => true;

    /// <inheritdoc />
    public bool IsEncrypted => false;

    /// <inheritdoc />
    public long TotalSize => _entries.Sum(static e => e.Size);

    /// <inheritdoc />
    public long TotalUncompressedSize => _entries.Sum(static e => e.Size);

    /// <inheritdoc />
    public IEnumerable<IArchiveEntry> Entries => _entries;

    /// <inheritdoc />
    public IEnumerable<IVolume> Volumes => [_volume];

    /// <inheritdoc />
    public ReaderOptions ReaderOptions { get; } = new();

    /// <summary>
    ///     Opens a seekable read stream for the given entry. Data is decompressed on demand
    ///     per 64 KiB block, so large entries do not need to be fully extracted up front.
    /// </summary>
    internal Stream OpenEntryStream(ZarArchiveEntry entry)
    {
        return new ZarEntryStream(Reader, entry.NodeId, entry.Size);
    }

    /// <inheritdoc />
    public IReader ExtractAllEntries()
    {
        return new ZarReader(this);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Reader.Dispose();
    }

    /// <summary>
    ///     Resolves the archive root node id, or null when the root cannot be found.
    /// </summary>
    private uint? LookUpRoot()
    {
        var node = Reader.LookUp("/", true, true);
        return node == ZArchiveReader.InvalidNode ? null : node;
    }

    /// <summary>
    ///     Walks the archive's directory tree depth-first, materializing one
    ///     <see cref="ZarArchiveEntry" /> per file and directory.
    /// </summary>
    private void EnumerateEntries()
    {
        var rootNode = LookUpRoot()
                       ?? throw new InvalidOperationException("The .zar archive root could not be resolved.");

        var pending = new Stack<(uint Node, string Path)>();
        var visited = new HashSet<uint>();
        pending.Push((rootNode, ""));

        while (pending.Count > 0)
        {
            var (node, path) = pending.Pop();
            if (!visited.Add(node)) continue;

            var count = Reader.GetDirEntryCount(node);
            for (var i = 0ul; i < count; i++)
            {
                if (!Reader.TryGetDirEntry(node, (uint)i, out var childNode, out var item)) continue;
                if (string.IsNullOrEmpty(item.Name)) continue;

                var childPath = path.Length == 0 ? item.Name : path + "/" + item.Name;

                if (item.IsDirectory)
                {
                    _entries.Add(new ZarArchiveEntry(this, childPath + "/", 0, isDirectory: true, childNode));
                    pending.Push((childNode, childPath));
                }
                else
                {
                    _entries.Add(new ZarArchiveEntry(this, childPath, checked((long)item.Size), isDirectory: false,
                        childNode));
                }
            }
        }
    }

    /// <summary>
    ///     Minimal <see cref="IReader" /> implementation so <see cref="ZarArchive.ExtractAllEntries" />
    ///     satisfies the SharpCompress contract; sequential extraction over the entry list.
    /// </summary>
    private sealed class ZarReader(ZarArchive archive) : IReader
    {
        private IEnumerator<ZarArchiveEntry>? _enumerator;
        private readonly ZarArchive _archive = archive;
        private bool _completed;

        public ArchiveType Type => _archive.Type;

        public IEntry Entry => Current ?? throw new InvalidOperationException("No current entry.");

        public bool Cancelled { get; private set; }

        private ZarArchiveEntry? Current
        {
            get
            {
                if (_enumerator == null) return null;

                var current = _enumerator.Current;
                return current;
            }
        }

        public void Cancel() => Cancelled = true;

        public bool MoveToNextEntry()
        {
            if (Cancelled || _completed) return false;

            _enumerator ??= _archive._entries.GetEnumerator();

            if (!_enumerator.MoveNext())
            {
                _enumerator.Dispose();
                _enumerator = null;
                _completed = true;
                return false;
            }

            return true;
        }

        public void WriteEntryTo(Stream writableStream)
        {
            using var entryStream =
                _archive.OpenEntryStream(Current ?? throw new InvalidOperationException("No current entry."));
            entryStream.CopyTo(writableStream);
        }

        public EntryStream OpenEntryStream()
        {
            throw new NotSupportedException("Sequential entry streams are not supported for ZAR archives.");
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _enumerator?.Dispose();
            _enumerator = null;
        }
    }

    /// <summary>Single <see cref="IVolume" /> for a single-file .zar archive.</summary>
    private sealed class ZarVolume(string fileName) : IVolume
    {
        public int Index => 0;

        public string FileName { get; } = fileName;

        /// <inheritdoc />
        public void Dispose()
        {
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}