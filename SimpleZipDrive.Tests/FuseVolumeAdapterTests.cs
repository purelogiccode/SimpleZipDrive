using System.IO.Compression;
using System.Text;
using SimpleZipDrive.Core;
using SimpleZipDrive.FuseSharp;
using SimpleZipDrive.Mounting.Fuse;

namespace SimpleZipDrive.Tests;

/// <summary>
///     Tests the FUSE volume adapter that bridges <see cref="ZipFileSystemCore" /> to FuseSharp.
/// </summary>
public class FuseVolumeAdapterTests : IDisposable
{
    private readonly FuseVolumeAdapter _adapter;
    private readonly ZipFileSystemCore _core;
    private readonly MemoryStream _stream;

    public FuseVolumeAdapterTests()
    {
        _stream = CreateZipStream();
        _core = new ZipFileSystemCore(_stream, "/mnt/test", static (_, _) => { }, static () => null, "zip");
        _adapter = new FuseVolumeAdapter(_core);
    }

    public void Dispose()
    {
        _core.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void VolumeLabel_MatchesCore()
    {
        Assert.Equal(_core.VolumeLabel, _adapter.VolumeLabel);
    }

    [Fact]
    public void VolumeSize_MatchesArchiveSize()
    {
        Assert.Equal((ulong)_core.TotalSize, _adapter.VolumeSize);
    }

    [Fact]
    public void GetEntry_Root_ReturnsDirectory()
    {
        var entry = _adapter.GetEntry("/");

        Assert.NotNull(entry);
        Assert.True(entry.IsDirectory);
    }

    [Fact]
    public void GetEntry_File_ReturnsFileWithSize()
    {
        var entry = _adapter.GetEntry("/readme.txt");

        Assert.NotNull(entry);
        Assert.False(entry.IsDirectory);
        Assert.Equal(11, entry.Size);
        Assert.Equal("readme.txt", entry.FileName);
    }

    [Fact]
    public void GetEntry_PathWithoutLeadingSlash_IsNormalized()
    {
        var entry = _adapter.GetEntry("readme.txt");

        Assert.NotNull(entry);
        Assert.Equal("readme.txt", entry.FileName);
    }

    [Fact]
    public void GetEntry_Missing_ReturnsNull()
    {
        Assert.Null(_adapter.GetEntry("/does-not-exist.txt"));
    }

    [Fact]
    public void GetFolderList_Root_ContainsFilesAndDirectories()
    {
        var entries = _adapter.GetFolderList("/").ToList();

        Assert.Contains(entries, static e => string.Equals(e.FileName, "readme.txt", StringComparison.Ordinal));
        Assert.Contains(entries,
            static e => string.Equals(e.FileName, "data", StringComparison.Ordinal) && e.IsDirectory);
    }

    [Fact]
    public void GetFolderList_Subdirectory_ContainsChildren()
    {
        var entries = _adapter.GetFolderList("/data").ToList();

        Assert.Contains(entries, static e => string.Equals(e.FileName, "info.txt", StringComparison.Ordinal));
    }

    [Fact]
    public void ReadFile_ReturnsContentAtOffset()
    {
        var entry = _adapter.GetEntry("/readme.txt");
        Assert.NotNull(entry);

        var buffer = new byte[5];
        var read = _adapter.ReadFile(entry, buffer, 6);

        Assert.Equal(5, read);
        Assert.Equal("World", Encoding.UTF8.GetString(buffer));
    }

    [Fact]
    public void ReadFile_AtEndOfFile_ReturnsZero()
    {
        var entry = _adapter.GetEntry("/readme.txt");
        Assert.NotNull(entry);

        var buffer = new byte[5];
        var read = _adapter.ReadFile(entry, buffer, entry.Size);

        Assert.Equal(0, read);
    }

    [Fact]
    public void ReadFile_ForeignEntry_Throws()
    {
        var foreign = new FakeFuseEntry();

        Assert.Throws<ArgumentException>(() =>
        {
            Span<byte> buffer = stackalloc byte[1];
            _adapter.ReadFile(foreign, buffer, 0);
        });
    }

    private static MemoryStream CreateZipStream()
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            var entry = zip.CreateEntry("readme.txt");
            using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
            {
                writer.Write("Hello World");
            }

            var nested = zip.CreateEntry("data/info.txt");
            using (var writer = new StreamWriter(nested.Open(), new UTF8Encoding(false)))
            {
                writer.Write("Nested content");
            }
        }

        ms.Position = 0;
        return ms;
    }

    private sealed class FakeFuseEntry : IFuseEntry
    {
        public string FileName => "fake.txt";

        public bool IsDirectory => false;

        public long Size => 0;
    }
}
