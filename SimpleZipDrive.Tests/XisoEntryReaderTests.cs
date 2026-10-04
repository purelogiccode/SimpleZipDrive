using System.Text;
using SimpleZipDrive.Core;
using XISOSharp;

namespace SimpleZipDrive.Tests;

/// <summary>
///     Tests for the sequential <see cref="SharpCompress.Readers.IReader" /> exposed by
///     <see cref="XisoArchive.ExtractAllEntries" />.
/// </summary>
public class XisoEntryReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "SimpleZipDriveTests", Guid.NewGuid().ToString("N"));

    public XisoEntryReaderTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
        catch
        {
            /* best effort */
        }
    }

    private string CreateSampleXiso()
    {
        var sourceDir = Path.Combine(_root, "source");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(sourceDir, "readme.txt"), "Hello World", new UTF8Encoding(false));
        Directory.CreateDirectory(Path.Combine(sourceDir, "data"));
        File.WriteAllText(Path.Combine(sourceDir, "data", "info.txt"), "Nested content", new UTF8Encoding(false));

        var outputDir = Path.Combine(_root, "output");
        Directory.CreateDirectory(outputDir);

        var result = XisoWriter.CreateXiso(sourceDir, outputDir, null, null, out var isoPath, "game.iso", null);
        Assert.Equal(0, result);
        Assert.NotNull(isoPath);
        return isoPath;
    }

    private static XisoArchive OpenXiso(string isoPath)
    {
        var stream = new FileStream(isoPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var archive = new XisoArchive(stream);
        return archive;
    }

    [Fact]
    public void ExtractAllEntries_ReturnsReaderWithArchiveType()
    {
        using var archive = OpenXiso(CreateSampleXiso());

        using var reader = archive.ExtractAllEntries();

        Assert.Equal(archive.Type, reader.Type);
    }

    [Fact]
    public void MoveToNextEntry_EnumeratesEveryEntry()
    {
        using var archive = OpenXiso(CreateSampleXiso());
        using var reader = archive.ExtractAllEntries();
        var keys = new List<string>();

        while (reader.MoveToNextEntry()) keys.Add(reader.Entry.Key!);

        Assert.Contains(keys, static key => string.Equals(key, "readme.txt", StringComparison.Ordinal));
        Assert.Contains(keys, static key => string.Equals(key, "data/info.txt", StringComparison.Ordinal));
        Assert.Contains(keys, static key => string.Equals(key, "data/", StringComparison.Ordinal));
        Assert.Equal(archive.Entries.Count(), keys.Count);
    }

    [Fact]
    public void MoveToNextEntry_AfterEnd_KeepsReturningFalse()
    {
        using var archive = OpenXiso(CreateSampleXiso());
        using var reader = archive.ExtractAllEntries();

        while (reader.MoveToNextEntry())
        {
        }

        Assert.False(reader.MoveToNextEntry());
        Assert.False(reader.MoveToNextEntry());
    }

    [Fact]
    public void Entry_BeforeFirstMoveToNext_ThrowsInvalidOperationException()
    {
        using var archive = OpenXiso(CreateSampleXiso());
        using var reader = archive.ExtractAllEntries();

        Assert.Throws<InvalidOperationException>(() => _ = reader.Entry);
    }

    [Fact]
    public void Cancel_SetsCancelledAndStopsEnumeration()
    {
        using var archive = OpenXiso(CreateSampleXiso());
        using var reader = archive.ExtractAllEntries();
        Assert.True(reader.MoveToNextEntry());

        reader.Cancel();

        Assert.True(reader.Cancelled);
        Assert.False(reader.MoveToNextEntry());
    }

    [Fact]
    public void OpenEntryStream_ThrowsNotSupportedException()
    {
        using var archive = OpenXiso(CreateSampleXiso());
        using var reader = archive.ExtractAllEntries();
        Assert.True(reader.MoveToNextEntry());

        Assert.Throws<NotSupportedException>(() => reader.OpenEntryStream());
    }

    [Fact]
    public void WriteEntryTo_BeforeFirstMoveToNext_ThrowsInvalidOperationException()
    {
        using var archive = OpenXiso(CreateSampleXiso());
        using var reader = archive.ExtractAllEntries();
        using var destination = new MemoryStream();

        Assert.Throws<InvalidOperationException>(() => reader.WriteEntryTo(destination));
    }

    [Fact]
    public void WriteEntryTo_FileEntry_CopiesContent()
    {
        using var archive = OpenXiso(CreateSampleXiso());
        using var reader = archive.ExtractAllEntries();

        var found = false;
        while (reader.MoveToNextEntry())
        {
            if (!string.Equals(reader.Entry.Key, "readme.txt", StringComparison.Ordinal)) continue;

            found = true;
            using var destination = new MemoryStream();
            reader.WriteEntryTo(destination);
            Assert.Equal("Hello World", Encoding.UTF8.GetString(destination.ToArray()));
            break;
        }

        Assert.True(found, "The readme.txt entry was not enumerated.");
    }

    [Fact]
    public void Dispose_AfterFullEnumeration_DoesNotThrow()
    {
        var archive = OpenXiso(CreateSampleXiso());
        var reader = archive.ExtractAllEntries();
        while (reader.MoveToNextEntry())
        {
        }

        var ex = Record.Exception(reader.Dispose);

        Assert.Null(ex);
        archive.Dispose();
    }
}
