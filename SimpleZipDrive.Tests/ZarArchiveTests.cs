using System.Text;
using SimpleZipDrive.Core;
using ZArchiveSharp;

namespace SimpleZipDrive.Tests;

/// <summary>
///     Tests for the <see cref="ZarArchive" /> adapter and .zar mounting through
///     <see cref="ZipFileSystemCore" />.
/// </summary>
public class ZarArchiveTests : IDisposable
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

    private string CreateSampleZar()
    {
        var sourceDir = Path.Combine(Path.GetTempPath(), "SimpleZipDriveTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sourceDir);
        _disposables.Add(new TempDirectory(sourceDir));

        File.WriteAllText(Path.Combine(sourceDir, "readme.txt"), "Hello World", new UTF8Encoding(false));
        Directory.CreateDirectory(Path.Combine(sourceDir, "data"));
        File.WriteAllText(Path.Combine(sourceDir, "data", "info.txt"), "Nested content", new UTF8Encoding(false));
        Directory.CreateDirectory(Path.Combine(sourceDir, "empty"));

        // File larger than the 64 KiB block size to exercise multi-block range reads.
        var largeBytes = new byte[150 * 1024];
        for (var i = 0; i < largeBytes.Length; i++) largeBytes[i] = (byte)(i % 251);
        File.WriteAllBytes(Path.Combine(sourceDir, "large.bin"), largeBytes);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(Path.GetTempPath(), "SimpleZipDriveTests"))!);

        var zarPath = Path.Combine(Path.GetTempPath(), "SimpleZipDriveTests", Guid.NewGuid().ToString("N") + ".zar");
        _disposables.Add(new TempFile(zarPath));
        ZArchiveTool.Pack(sourceDir, zarPath);

        return zarPath;
    }

    private string CreateZar(params (string Name, string Content)[] files)
    {
        var sourceDir = Path.Combine(Path.GetTempPath(), "SimpleZipDriveTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sourceDir);
        _disposables.Add(new TempDirectory(sourceDir));

        foreach (var (name, content) in files)
            File.WriteAllText(Path.Combine(sourceDir, name), content, new UTF8Encoding(false));

        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(Path.GetTempPath(), "SimpleZipDriveTests"))!);

        var zarPath = Path.Combine(Path.GetTempPath(), "SimpleZipDriveTests", Guid.NewGuid().ToString("N") + ".zar");
        _disposables.Add(new TempFile(zarPath));
        ZArchiveTool.Pack(sourceDir, zarPath);

        return zarPath;
    }

    private ZarArchive OpenZar(string zarPath)
    {
        var stream = new FileStream(zarPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        _disposables.Add(stream);
        var archive = new ZarArchive(stream);
        _disposables.Add(archive);
        return archive;
    }

    // ─── ZarArchive adapter tests ───

    [Fact]
    public void Constructor_ValidZar_EnumeratesEntries()
    {
        using var archive = OpenZar(CreateSampleZar());

        var keys = archive.Entries.Select(static e => e.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("readme.txt", keys);
        Assert.Contains("data/info.txt", keys);
        Assert.Contains("large.bin", keys);
        Assert.Contains("data/", keys);
    }

    [Fact]
    public void Constructor_LongEntryName_DecodesExtendedNames()
    {
        // ZArchive stores names of 128+ characters in the extended length branch;
        // without DecodeExtendedNames those entries decode to an empty name.
        var longName = new string('x', 140) + ".txt";
        using var archive = OpenZar(CreateZar((longName, "Long name content")));

        var entry = archive.Entries.Single(static e =>
            string.Equals(e.Key, new string('x', 140) + ".txt", StringComparison.Ordinal));
        using var stream = entry.OpenEntryStream();
        using var reader = new StreamReader(stream, new UTF8Encoding(false));

        Assert.Equal("Long name content", reader.ReadToEnd());
    }

    [Fact]
    public void Constructor_InvalidStream_Throws()
    {
        using var ms = new MemoryStream([1, 2, 3, 4]);

        Assert.Throws<InvalidOperationException>(() => new ZarArchive(ms));
    }

    [Fact]
    public void Entries_ReportSizesAndDirectories()
    {
        using var archive = OpenZar(CreateSampleZar());

        var readme = archive.Entries.Single(static e => string.Equals(e.Key, "readme.txt", StringComparison.Ordinal));
        Assert.False(readme.IsDirectory);
        Assert.Equal("Hello World".Length, readme.Size);
        Assert.False(readme.IsEncrypted);

        var dataDir = archive.Entries.Single(static e => string.Equals(e.Key, "data/", StringComparison.Ordinal));
        Assert.True(dataDir.IsDirectory);
        Assert.Equal(0, dataDir.Size);
    }

    [Fact]
    public void OpenEntryStream_ReadsFileContent()
    {
        using var archive = OpenZar(CreateSampleZar());
        var entry = archive.Entries.Single(static e => string.Equals(e.Key, "readme.txt", StringComparison.Ordinal));

        using var stream = entry.OpenEntryStream();
        using var reader = new StreamReader(stream, new UTF8Encoding(false));

        Assert.Equal("Hello World", reader.ReadToEnd());
    }

    [Fact]
    public void OpenEntryStream_SeekAndRead_Works()
    {
        using var archive = OpenZar(CreateSampleZar());
        var entry = archive.Entries.Single(static e => string.Equals(e.Key, "readme.txt", StringComparison.Ordinal));

        using var stream = entry.OpenEntryStream();
        Assert.Equal(11, stream.Seek(0, SeekOrigin.End));
        stream.Position = 6;
        var buffer = new byte[5];
        Assert.Equal(5, stream.Read(buffer, 0, 5));

        Assert.Equal("World", Encoding.UTF8.GetString(buffer));
    }

    [Fact]
    public void OpenEntryStream_MultiBlockFile_RoundTrips()
    {
        using var archive = OpenZar(CreateSampleZar());
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
        using var archive = OpenZar(CreateSampleZar());
        var entry = archive.Entries.Single(static e => string.Equals(e.Key, "readme.txt", StringComparison.Ordinal));

        using var stream = entry.OpenEntryStream();
        stream.Position = stream.Length;

        var buffer = new byte[10];
        Assert.Equal(0, stream.Read(buffer, 0, 10));
    }

    [Fact]
    public void Archive_Properties_AreReadOnlyAndComplete()
    {
        using var archive = OpenZar(CreateSampleZar());

        Assert.False(archive.IsSolid);
        Assert.True(archive.IsComplete);
        Assert.False(archive.IsEncrypted);
        Assert.Single(archive.Volumes);
    }

    // ─── ZipFileSystemCore integration tests ───

    private ZipFileSystemCore CreateZarCore(string zarPath, long maxMemorySize = ZipFileSystemCore.DefaultMaxMemorySize)
    {
        var stream = new FileStream(zarPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        _disposables.Add(stream);
        var core = new ZipFileSystemCore(stream, "M:\\", static (_, _) => { }, static () => null, "zar", maxMemorySize);
        _disposables.Add(core);
        return core;
    }

    [Fact]
    public void ZipFileSystemCore_ZarType_PopulatesEntries()
    {
        var core = CreateZarCore(CreateSampleZar());

        Assert.Equal("zar", core.ArchiveType);
        Assert.True(core.ArchiveEntries.ContainsKey("/readme.txt"));
        Assert.True(core.ArchiveEntries.ContainsKey("/data/info.txt"));
        Assert.True(core.ArchiveEntries.ContainsKey("/empty"));
    }

    [Fact]
    public void ZipFileSystemCore_Zar_ListDirectory_ReturnsChildren()
    {
        var core = CreateZarCore(CreateSampleZar());

        var root = core.ListDirectory("/");
        var rootNames = root.Select(static n => n.NormalizedPath).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("/readme.txt", rootNames);
        Assert.Contains("/data", rootNames);
        Assert.Contains("/large.bin", rootNames);

        var data = core.ListDirectory("/data");
        Assert.Contains(data, static n => string.Equals(n.NormalizedPath, "/data/info.txt", StringComparison.Ordinal));
    }

    [Fact]
    public void ZipFileSystemCore_Zar_GetEntryNode_ReturnsFileWithSize()
    {
        var core = CreateZarCore(CreateSampleZar());

        var node = core.GetEntryNode("/readme.txt");

        Assert.NotNull(node);
        Assert.False(node.IsDir);
        Assert.Equal("Hello World".Length, node.FileSize);
    }

    [Fact]
    public void ZipFileSystemCore_Zar_OpenEntryStream_ReadsContent()
    {
        var core = CreateZarCore(CreateSampleZar());
        var entry = core.ArchiveEntries["/data/info.txt"];

        using var stream = core.OpenEntryStream(entry, "/data/info.txt");
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream, new UTF8Encoding(false));

        Assert.Equal("Nested content", reader.ReadToEnd());
    }

    [Fact]
    public void ZipFileSystemCore_Zar_LargeFile_ExtractsToDiskCache()
    {
        // A 64 KiB per-file memory limit forces the 150 KiB entry through the disk-cache path.
        var core = CreateZarCore(CreateSampleZar(), 64 * 1024);
        var entry = core.ArchiveEntries["/large.bin"];

        using var stream = core.OpenEntryStream(entry, "/large.bin");
        Assert.NotNull(stream);
        Assert.True(stream.CanSeek);

        var buffer = new byte[16];
        stream.Position = (150 * 1024) - 16;
        Assert.Equal(16, stream.Read(buffer, 0, 16));
        Assert.Equal((byte)(((150 * 1024) - 16) % 251), buffer[0]);
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