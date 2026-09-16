using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using XISOSharp;

namespace SimpleZipDrive.Core;

/// <summary>
///     SharpCompress adapter over XISOSharp so Xbox XISO disc images (<c>.iso</c>,
///     <c>.xiso</c> and CISO <c>.cso</c> containers) can be mounted by
///     <see cref="ZipFileSystemCore" /> like the other supported archive formats.
///     Disc images are read-only and unencrypted, so password handling never triggers
///     for this format. File-backed mounts use XISOSharp's keep-open
///     <see cref="XisoExplorer" /> (single image handle, node-based reads); any other
///     seekable stream falls back to the plain-ISO stream APIs.
/// </summary>
public sealed class XisoArchive : IArchive
{
    private readonly long _dataOffset;
    private readonly List<XisoArchiveEntry> _entries = [];
    private readonly XisoExplorer? _explorer;
    private readonly string _imageName;
    private readonly Stream? _imageStream;
    private readonly Lock _readLock = new();
    private readonly XisoVolume _volume;

    /// <summary>
    ///     Initializes a new instance of the <see cref="XisoArchive" /> class by parsing the
    ///     given disc image stream or file.
    /// </summary>
    /// <param name="archiveStream">The seekable stream containing the Xbox disc image.</param>
    /// <exception cref="InvalidOperationException">The stream is not a valid Xbox XISO disc image.</exception>
    public XisoArchive(Stream archiveStream)
    {
        ArgumentNullException.ThrowIfNull(archiveStream);

        try
        {
            var filePath = (archiveStream as FileStream)?.Name;

            if (!string.IsNullOrEmpty(filePath))
            {
                // Path-backed mount: XISOSharp's keep-open explorer holds one image
                // handle and supports .iso, .xiso and .cso containers transparently.
                var explorer = new XisoExplorer(filePath, new XisoExplorerOptions
                {
                    KeepOpen = true,
                    Share = FileShare.ReadWrite
                });

                _explorer = explorer;
                _imageName = filePath;
                _dataOffset = explorer.Volume.DiscLseek;
                _volume = new XisoVolume(filePath);
                EnumerateExplorerEntries(explorer);
            }
            else
            {
                // Stream fallback (plain XISO only): probe the volume once and walk
                // the directory tables through the stream APIs.
                _imageName = "image.iso";
                _imageStream = archiveStream;

                var volume = XisoReader.GetVolumeInfo(archiveStream, _imageName);
                if (!volume.IsValid) throw new XisoFormatException($"Not a valid XISO: {_imageName}");

                _dataOffset = volume.DiscLseek;
                _volume = new XisoVolume(string.Empty);
                EnumerateStreamEntries(archiveStream, volume.RootDirSector);
            }
        }
        catch (XisoFormatException ex)
        {
            _explorer?.Dispose();
            throw new InvalidOperationException(
                "The file is not a valid Xbox XISO disc image (.iso, .xiso, or .cso).", ex);
        }
        catch
        {
            // Damaged images can fail mid-enumeration with other IO exceptions
            // (InvalidDataException, truncated-table IOException). Do not leak the
            // keep-open image handle; let ZipFileSystemCore wrap the failure.
            _explorer?.Dispose();
            throw;
        }
    }

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

    /// <inheritdoc />
    public IReader ExtractAllEntries()
    {
        return new XisoEntryReader(this);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _explorer?.Dispose();
    }

    /// <summary>
    ///     Opens a seekable read stream for the given entry. File-backed mounts route
    ///     through the explorer's node-based bounded stream; stream-backed mounts read
    ///     the entry's sector extent directly from the image stream.
    /// </summary>
    internal Stream OpenEntryStream(XisoArchiveEntry entry)
    {
        if (_explorer is not null && entry.Node is not null)
            return _explorer.OpenReadStream(entry.Node);

        var stream = _imageStream
                     ?? throw new InvalidOperationException("The XISO image stream is not available.");

        return new XisoEntryStream(stream, _dataOffset + ((long)entry.StartSector * XISOSharp.Constants.SectorSize),
            entry.Size, _readLock);
    }

    /// <summary>
    ///     Walks the disc's directory tree through the keep-open explorer, materializing
    ///     one <see cref="XisoArchiveEntry" /> per file and directory.
    /// </summary>
    private void EnumerateExplorerEntries(XisoExplorer explorer)
    {
        var root = explorer.GetNode("/")
                   ?? throw new InvalidOperationException("The XISO root directory could not be resolved.");

        var pending = new Stack<ExplorerNode>();
        var visitedSectors = new HashSet<uint> { root.StartSector };
        pending.Push(root);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();

            foreach (var child in explorer.ListChildren(directory.FullPath))
            {
                var key = child.FullPath.TrimStart('/');

                if (child.IsDirectory)
                {
                    _entries.Add(new XisoArchiveEntry(this, key + "/", child.FullPath, child.StartSector, 0, true,
                        child));

                    // Track directory tables by sector: a corrupt entry pointing back at
                    // an ancestor would otherwise grow the path (and the stack) forever.
                    if (visitedSectors.Add(child.StartSector)) pending.Push(child);
                }
                else
                {
                    _entries.Add(new XisoArchiveEntry(this, key, child.FullPath, child.StartSector, child.Size,
                        false, child));
                }
            }
        }
    }

    /// <summary>
    ///     Walks the disc's directory tree through the stream APIs, materializing one
    ///     <see cref="XisoArchiveEntry" /> per file and directory.
    /// </summary>
    private void EnumerateStreamEntries(Stream stream, uint rootDirSector)
    {
        var pending = new Stack<string>();
        var visitedSectors = new HashSet<uint> { rootDirSector };
        pending.Push("/");

        while (pending.Count > 0)
        {
            var directoryPath = pending.Pop();

            foreach (var item in XisoReader.ListDirectory(stream, _imageName, directoryPath))
            {
                var childPath = XisoExplorer.Combine(directoryPath, item.Name);
                var key = childPath.TrimStart('/');

                if (item.IsDirectory)
                {
                    _entries.Add(new XisoArchiveEntry(this, key + "/", childPath, item.StartSector, 0, true));

                    // Track directory tables by sector: a corrupt entry pointing back at
                    // an ancestor would otherwise grow the path (and the stack) forever.
                    if (visitedSectors.Add(item.StartSector)) pending.Push(childPath);
                }
                else
                {
                    _entries.Add(new XisoArchiveEntry(this, key, childPath, item.StartSector, item.FileSize, false));
                }
            }
        }
    }

    /// <summary>
    ///     Minimal <see cref="IReader" /> implementation so <see cref="ExtractAllEntries" />
    ///     satisfies the SharpCompress contract; sequential extraction over the entry list.
    /// </summary>
    private sealed class XisoEntryReader(XisoArchive archive) : IReader
    {
        private readonly XisoArchive _archive = archive;
        private IEnumerator<XisoArchiveEntry>? _enumerator;
        private bool _completed;

        public ArchiveType Type => _archive.Type;

        public IEntry Entry => Current ?? throw new InvalidOperationException("No current entry.");

        public bool Cancelled { get; private set; }

        private XisoArchiveEntry? Current
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
            throw new NotSupportedException("Sequential entry streams are not supported for XISO images.");
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _enumerator?.Dispose();
            _enumerator = null;
        }
    }

    /// <summary>Single <see cref="IVolume" /> for a single-file Xbox disc image.</summary>
    private sealed class XisoVolume(string fileName) : IVolume
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
