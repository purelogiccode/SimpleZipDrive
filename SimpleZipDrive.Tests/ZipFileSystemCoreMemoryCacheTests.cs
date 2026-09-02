using System.Collections.Concurrent;
using System.IO.Compression;
using System.Reflection;
using SimpleZipDrive.Core;

namespace SimpleZipDrive.Tests;

/// <summary>
///     Tests for the shared per-entry memory cache: one decompressed buffer per entry,
///     shared across concurrent opens, kept warm after the last handle closes, and
///     evicted (LRU) only when the total memory cache limit would be exceeded.
/// </summary>
public class ZipFileSystemCoreMemoryCacheTests : IDisposable
{
    private const int EntrySize = 100_000;

    private readonly List<IDisposable> _disposables = [];

    public void Dispose()
    {
        foreach (var disposable in _disposables) disposable.Dispose();

        _disposables.Clear();
        GC.SuppressFinalize(this);
    }

    private ZipFileSystemCore CreateCore(Stream? stream = null, long maxMemory = ZipFileSystemCore.DefaultMaxMemorySize)
    {
        var ms = stream ?? CreateZipStream();
        if (stream == null) _disposables.Add(ms);
        var core = new ZipFileSystemCore(ms, "M:\\", static (_, _) => { }, static () => null,
            "zip", maxMemory);
        _disposables.Add(core);
        return core;
    }

    private static MemoryStream CreateZipStream()
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            CreateEntry(zip, "small.bin", EntrySize);
            CreateEntry(zip, "other.bin", EntrySize);
            CreateEntry(zip, "third.bin", EntrySize * 2);
        }

        ms.Position = 0;
        return ms;
    }

    private static void CreateEntry(ZipArchive zip, string name, int size)
    {
        var entry = zip.CreateEntry(name);
        using var writer = new BinaryWriter(entry.Open());
        var data = new byte[size];
        new Random(name.GetHashCode(StringComparison.OrdinalIgnoreCase)).NextBytes(data);
        writer.Write(data);
    }

    [Fact]
    public void OpenEntryStream_TwoConcurrentOpens_ShareSingleBuffer()
    {
        var core = CreateCore();
        var entry = core.ArchiveEntries["/small.bin"];

        var stream1 = core.OpenEntryStream(entry, "/small.bin");
        Assert.NotNull(stream1);
        var afterFirst = core.CurrentMemoryUsage;
        Assert.True(afterFirst > 0);

        // A second concurrent open must reuse the shared buffer: no memory growth.
        var stream2 = core.OpenEntryStream(entry, "/small.bin");
        Assert.NotNull(stream2);
        Assert.Equal(afterFirst, core.CurrentMemoryUsage);

        stream1.Dispose();
        stream2.Dispose();
    }

    [Fact]
    public void OpenEntryStream_ReopenAfterClose_ReusesWarmBuffer()
    {
        var core = CreateCore();
        var entry = core.ArchiveEntries["/small.bin"];

        var stream1 = core.OpenEntryStream(entry, "/small.bin");
        Assert.NotNull(stream1);
        stream1.Dispose();

        var afterClose = core.CurrentMemoryUsage;
        Assert.True(afterClose > 0);

        // Reopening after close must not decompress again or grow memory (warm cache).
        var stream2 = core.OpenEntryStream(entry, "/small.bin");
        Assert.NotNull(stream2);
        Assert.Equal(afterClose, core.CurrentMemoryUsage);

        stream2.Dispose();
    }

    [Fact]
    public void OpenEntryStream_SharedStreams_ReadIndependentPositions()
    {
        var core = CreateCore();
        var entry = core.ArchiveEntries["/small.bin"];

        using var stream1 = core.OpenEntryStream(entry, "/small.bin");
        using var stream2 = core.OpenEntryStream(entry, "/small.bin");
        Assert.NotNull(stream1);
        Assert.NotNull(stream2);

        stream1.Position = 0;
        stream2.Position = 100;

        var buf1 = new byte[10];
        var buf2 = new byte[10];
        var read1 = stream1.Read(buf1, 0, 10);
        var read2 = stream2.Read(buf2, 0, 10);

        Assert.Equal(10, read1);
        Assert.Equal(10, read2);
        Assert.NotEqual(buf1, buf2); // positions are independent despite the shared buffer
    }

    [Fact]
    public void OpenEntryStream_MemoryPressure_EvictsColdEntries()
    {
        var core = CreateCore();
        var entryA = core.ArchiveEntries["/small.bin"];
        var entryB = core.ArchiveEntries["/other.bin"];

        var streamA = core.OpenEntryStream(entryA, "/small.bin");
        Assert.NotNull(streamA);
        streamA.Dispose(); // A is warm (RefCount == 0) but still cached.

        Assert.Equal(EntrySize, core.CurrentMemoryUsage);

        // Simulate memory pressure: a new entry can only fit if the warm entry is evicted.
        core.CurrentMemoryUsage = core.MaxTotalMemoryCache - 5;

        var streamB = core.OpenEntryStream(entryB, "/other.bin");
        Assert.NotNull(streamB);

        // A was evicted to make room for B; the total stays within the limit.
        Assert.Equal(core.MaxTotalMemoryCache - 5, core.CurrentMemoryUsage);

        streamB.Dispose();

        // Reopening A now decompresses it again (it was evicted), evicting the cold B.
        var streamA2 = core.OpenEntryStream(entryA, "/small.bin");
        Assert.NotNull(streamA2);
        Assert.Equal(core.MaxTotalMemoryCache - 5, core.CurrentMemoryUsage);

        streamA2.Dispose();
    }

    [Fact]
    public void OpenEntryStream_EntryLockDisposedDuringShutdown_ReturnsNullWithoutThrowing()
    {
        var core = CreateCore();
        var entryA = core.ArchiveEntries["/small.bin"];
        var entryB = core.ArchiveEntries["/other.bin"];
        var entryC = core.ArchiveEntries["/third.bin"];

        // Open A and B so their per-entry semaphores exist in _entryLocks.
        var streamA = core.OpenEntryStream(entryA, "/small.bin");
        var streamB = core.OpenEntryStream(entryB, "/other.bin");
        Assert.NotNull(streamA);
        Assert.NotNull(streamB);
        streamA.Dispose();
        streamB.Dispose();

        // Simulate the Dispose() window: B's per-entry semaphore is disposed while the
        // _entryLocks dictionary still contains it (race between the dispose loop and a
        // concurrent open).
        var entryLocks = (ConcurrentDictionary<string, SemaphoreSlim>)typeof(ZipFileSystemCore)
            .GetField("_entryLocks", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(core)!;
        entryLocks["/other.bin"].Dispose();

        // Evict A and B from the memory cache under memory pressure so the next open of
        // B misses the cache and must take the (disposed) per-entry semaphore.
        core.CurrentMemoryUsage = core.MaxTotalMemoryCache - 5;
        var streamC = core.OpenEntryStream(entryC, "/third.bin");
        Assert.NotNull(streamC);
        streamC.Dispose();

        // B is no longer cached and its semaphore is disposed: both the memory and disk
        // paths must abort gracefully instead of throwing ObjectDisposedException.
        var ex = Record.Exception(() => core.OpenEntryStream(entryB, "/other.bin"));
        Assert.Null(ex);
    }

    [Fact]
    public void OpenEntryStream_AfterCoreDisposed_ReturnsNullWithoutThrowing()
    {
        var core = CreateCore();
        var entry = core.ArchiveEntries["/small.bin"];

        var stream = core.OpenEntryStream(entry, "/small.bin");
        Assert.NotNull(stream);
        stream.Dispose();

        core.Dispose();

        // Late opens during/after shutdown must fail gracefully, never throw.
        var ex = Record.Exception(() => core.OpenEntryStream(entry, "/small.bin"));
        Assert.Null(ex);
    }
}