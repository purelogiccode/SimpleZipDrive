using System.Buffers.Binary;
using System.Text;
using SimpleZipDrive.Core;
using XISOSharp;

namespace SimpleZipDrive.Tests;

/// <summary>
///     Tests for the <see cref="XisoArchive" /> adapter and Xbox XISO mounting through
///     <see cref="ZipFileSystemCore" />.
/// </summary>
public class XisoArchiveTests : IDisposable
{
    private readonly List<IDisposable> _disposables = [];

    public void Dispose()
    {
        foreach (var d in _disposables)
        {
            try
            {
                d.Dispose();
            }
            catch
            {
                /* best effort */
            }
        }

        GC.SuppressFinalize(this);
    }

    private string CreateSampleXiso()
    {
        var sourceDir = Path.Combine(Path.GetTempPath(), "SimpleZipDriveTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sourceDir);
        _disposables.Add(new TempDirectory(sourceDir));

        File.WriteAllText(Path.Combine(sourceDir, "readme.txt"), "Hello World", new UTF8Encoding(false));
        Directory.CreateDirectory(Path.Combine(sourceDir, "data"));
        File.WriteAllText(Path.Combine(sourceDir, "data", "info.txt"), "Nested content", new UTF8Encoding(false));
        Directory.CreateDirectory(Path.Combine(sourceDir, "empty"));

        // File larger than one 2 KiB sector to exercise multi-sector range reads.
        var largeBytes = new byte[150 * 1024];
        for (var i = 0; i < largeBytes.Length; i++) largeBytes[i] = (byte)(i % 251);
        File.WriteAllBytes(Path.Combine(sourceDir, "large.bin"), largeBytes);

        var outputDir = Path.Combine(Path.GetTempPath(), "SimpleZipDriveTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDir);
        _disposables.Add(new TempDirectory(outputDir));

        var result = XisoWriter.CreateXiso(sourceDir, outputDir, null, null, out var isoPath, "game.iso", null);
        Assert.Equal(0, result);
        Assert.NotNull(isoPath);
        _disposables.Add(new TempFile(isoPath));

        return isoPath;
    }

    private XisoArchive OpenXiso(string isoPath)
    {
        var stream = new FileStream(isoPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        _disposables.Add(stream);
        var archive = new XisoArchive(stream);
        _disposables.Add(archive);
        return archive;
    }

    // ─── XisoArchive adapter tests ───

    [Fact]
    public void Constructor_ValidXiso_EnumeratesEntries()
    {
        using var archive = OpenXiso(CreateSampleXiso());

        var keys = archive.Entries.Select(static e => e.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("readme.txt", keys);
        Assert.Contains("data/info.txt", keys);
        Assert.Contains("large.bin", keys);
        Assert.Contains("data/", keys);
    }

    [Fact]
    public void Constructor_InvalidStream_Throws()
    {
        using var ms = new MemoryStream([1, 2, 3, 4]);

        Assert.Throws<InvalidOperationException>(() => new XisoArchive(ms));
    }

    [Fact]
    public void Constructor_StreamImage_EnumeratesEntries()
    {
        var isoPath = CreateSampleXiso();
        using var ms = new MemoryStream(File.ReadAllBytes(isoPath));
        using var archive = new XisoArchive(ms);

        var keys = archive.Entries.Select(static e => e.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("readme.txt", keys);
        Assert.Contains("data/info.txt", keys);
        Assert.Contains("data/", keys);
    }

    [Fact]
    public void StreamBacked_OpenEntryStream_ReadsFileContent()
    {
        var isoPath = CreateSampleXiso();
        using var ms = new MemoryStream(File.ReadAllBytes(isoPath));
        using var archive = new XisoArchive(ms);
        var entry = archive.Entries.Single(static e => string.Equals(e.Key, "data/info.txt", StringComparison.Ordinal));

        using var stream = entry.OpenEntryStream();
        using var reader = new StreamReader(stream, new UTF8Encoding(false));

        Assert.Equal("Nested content", reader.ReadToEnd());
    }

    [Fact]
    public void Constructor_CsoImage_EnumeratesAndReadsEntries()
    {
        var isoPath = CreateSampleXiso();
        var csoPath = Path.ChangeExtension(isoPath, ".cso");
        Assert.Equal(0, CisoWriter.CompressToCso(isoPath, csoPath));
        _disposables.Add(new TempFile(csoPath));

        using var archive = OpenXiso(csoPath);

        var entry = archive.Entries.Single(static e =>
            string.Equals(e.Key, "data/info.txt", StringComparison.Ordinal));
        using var stream = entry.OpenEntryStream();
        using var reader = new StreamReader(stream, new UTF8Encoding(false));

        Assert.Equal("Nested content", reader.ReadToEnd());
    }

    [Fact]
    public void Entries_ReportSizesAndDirectories()
    {
        using var archive = OpenXiso(CreateSampleXiso());

        var readme = archive.Entries.Single(static e => string.Equals(e.Key, "readme.txt", StringComparison.Ordinal));
        Assert.False(readme.IsDirectory);
        Assert.Equal("Hello World".Length, readme.Size);
        Assert.False(readme.IsEncrypted);
        Assert.Equal(SharpCompress.Common.CompressionType.None, readme.CompressionType);

        var dataDir = archive.Entries.Single(static e => string.Equals(e.Key, "data/", StringComparison.Ordinal));
        Assert.True(dataDir.IsDirectory);
        Assert.Equal(0, dataDir.Size);
    }

    [Fact]
    public void OpenEntryStream_ReadsFileContent()
    {
        using var archive = OpenXiso(CreateSampleXiso());
        var entry = archive.Entries.Single(static e => string.Equals(e.Key, "readme.txt", StringComparison.Ordinal));

        using var stream = entry.OpenEntryStream();
        using var reader = new StreamReader(stream, new UTF8Encoding(false));

        Assert.Equal("Hello World", reader.ReadToEnd());
    }

    [Fact]
    public void OpenEntryStream_SeekAndRead_Works()
    {
        using var archive = OpenXiso(CreateSampleXiso());
        var entry = archive.Entries.Single(static e => string.Equals(e.Key, "readme.txt", StringComparison.Ordinal));

        using var stream = entry.OpenEntryStream();
        Assert.Equal(11, stream.Seek(0, SeekOrigin.End));
        stream.Position = 6;
        var buffer = new byte[5];
        Assert.Equal(5, stream.Read(buffer, 0, 5));

        Assert.Equal("World", Encoding.UTF8.GetString(buffer));
    }

    [Fact]
    public void OpenEntryStream_MultiSectorFile_RoundTrips()
    {
        using var archive = OpenXiso(CreateSampleXiso());
        var entry = archive.Entries.Single(static e => string.Equals(e.Key, "large.bin", StringComparison.Ordinal));
        Assert.Equal(150 * 1024, entry.Size);

        using var stream = entry.OpenEntryStream();
        var result = new MemoryStream();
        stream.CopyTo(result);

        var expected = new byte[150 * 1024];
        for (var i = 0; i < expected.Length; i++) expected[i] = (byte)(i % 251);

        Assert.Equal(expected, result.ToArray());
    }

    [Fact]
    public void OpenEntryStream_ReadPastEnd_ReturnsZero()
    {
        using var archive = OpenXiso(CreateSampleXiso());
        var entry = archive.Entries.Single(static e => string.Equals(e.Key, "readme.txt", StringComparison.Ordinal));

        using var stream = entry.OpenEntryStream();
        stream.Position = stream.Length;

        var buffer = new byte[10];
        Assert.Equal(0, stream.Read(buffer, 0, 10));
    }

    [Fact]
    public void Archive_Properties_AreReadOnlyAndComplete()
    {
        using var archive = OpenXiso(CreateSampleXiso());

        Assert.False(archive.IsSolid);
        Assert.True(archive.IsComplete);
        Assert.False(archive.IsEncrypted);
        Assert.Single(archive.Volumes);
    }

    // ─── ZipFileSystemCore integration tests ───

    private ZipFileSystemCore CreateXisoCore(string isoPath,
        long maxMemorySize = ZipFileSystemCore.DefaultMaxMemorySize)
    {
        var stream = new FileStream(isoPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        _disposables.Add(stream);
        var core = new ZipFileSystemCore(stream, "M:\\", static (_, _) => { }, static () => null, "xiso",
            maxMemorySize);
        _disposables.Add(core);
        return core;
    }

    [Fact]
    public void ZipFileSystemCore_XisoType_PopulatesEntries()
    {
        var core = CreateXisoCore(CreateSampleXiso());

        Assert.Equal("xiso", core.ArchiveType);
        Assert.True(core.ArchiveEntries.ContainsKey("/readme.txt"));
        Assert.True(core.ArchiveEntries.ContainsKey("/data/info.txt"));
    }

    [Fact]
    public void ZipFileSystemCore_Xiso_ListDirectory_ReturnsChildren()
    {
        var core = CreateXisoCore(CreateSampleXiso());

        var root = core.ListDirectory("/");
        var rootNames = root.Select(static n => n.NormalizedPath).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("/readme.txt", rootNames);
        Assert.Contains("/data", rootNames);
        Assert.Contains("/large.bin", rootNames);

        var data = core.ListDirectory("/data");
        Assert.Contains(data, static n => string.Equals(n.NormalizedPath, "/data/info.txt", StringComparison.Ordinal));
    }

    [Fact]
    public void ZipFileSystemCore_Xiso_GetEntryNode_ReturnsFileWithSize()
    {
        var core = CreateXisoCore(CreateSampleXiso());

        var node = core.GetEntryNode("/readme.txt");

        Assert.NotNull(node);
        Assert.False(node.IsDir);
        Assert.Equal("Hello World".Length, node.FileSize);
    }

    [Fact]
    public void ZipFileSystemCore_Xiso_OpenEntryStream_ReadsContent()
    {
        var core = CreateXisoCore(CreateSampleXiso());
        var entry = core.ArchiveEntries["/data/info.txt"];

        using var stream = core.OpenEntryStream(entry, "/data/info.txt");
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream, new UTF8Encoding(false));

        Assert.Equal("Nested content", reader.ReadToEnd());
    }

    [Fact]
    public void ZipFileSystemCore_Xiso_LargeFile_ExtractsToDiskCache()
    {
        // A 64 KiB per-file memory limit forces the 150 KiB entry through the disk-cache path.
        var core = CreateXisoCore(CreateSampleXiso(), 64 * 1024);
        var entry = core.ArchiveEntries["/large.bin"];

        using var stream = core.OpenEntryStream(entry, "/large.bin");
        Assert.NotNull(stream);
        Assert.True(stream.CanSeek);

        var buffer = new byte[16];
        stream.Position = 150 * 1024 - 16;
        Assert.Equal(16, stream.Read(buffer, 0, 16));
        Assert.Equal((byte)((150 * 1024 - 16) % 251), buffer[0]);
    }

    // ─── Corrupt image hardening ───

    /// <summary>
    ///     Builds a minimal XISO whose root table contains a single directory ("D")
    ///     that points back at the root table, producing a cross-table cycle.
    /// </summary>
    private string CreateCyclicXiso()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), "SimpleZipDriveTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDir);
        _disposables.Add(new TempDirectory(outputDir));

        var isoPath = Path.Combine(outputDir, Guid.NewGuid().ToString("N") + ".iso");
        _disposables.Add(new TempFile(isoPath));

        const int rootSector = 1;
        const int tableBytes = 32;

        using var fs = new FileStream(isoPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        fs.SetLength(0x10800 + 2048);
        fs.Seek(Constants.HeaderOffset, SeekOrigin.Begin);
        fs.Write(Encoding.ASCII.GetBytes(Constants.HeaderData));
        fs.Write(BitConverter.GetBytes((uint)rootSector));
        fs.Write(BitConverter.GetBytes((uint)tableBytes));
        fs.Write(new byte[8]);

        var entry = new byte[tableBytes];
        BinaryPrimitives.WriteUInt16LittleEndian(entry.AsSpan(0), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(entry.AsSpan(2), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(4), rootSector);
        BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(8), tableBytes);
        entry[12] = Constants.AttributeDir;
        entry[13] = 1;
        entry[14] = (byte)'D';

        fs.Seek(rootSector * (long)Constants.SectorSize, SeekOrigin.Begin);
        fs.Write(entry);

        return isoPath;
    }

    [Fact]
    public void Constructor_CyclicDirectoryTable_TerminatesAndListsDirectoryOnce()
    {
        using var archive = OpenXiso(CreateCyclicXiso());

        var directories = archive.Entries.Where(static e => e.IsDirectory).Select(static e => e.Key).ToList();

        Assert.Equal(["D/"], directories);
    }

    private sealed class TempDirectory(string path) : IDisposable
    {
        private readonly string _path = path;

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_path)) Directory.Delete(_path, true);
            }
            catch
            {
                /* best effort */
            }
        }
    }

    private sealed class TempFile(string path) : IDisposable
    {
        private readonly string _path = path;

        public void Dispose()
        {
            try
            {
                if (File.Exists(_path)) File.Delete(_path);
            }
            catch
            {
                /* best effort */
            }
        }
    }
}
