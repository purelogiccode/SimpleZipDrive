namespace SimpleZipDrive.Core.Models;

/// <summary>
///     A decompressed entry buffer shared by all open streams of the same archive entry.
///     <see cref="RefCount" /> tracks active opens; buffers with <see cref="RefCount" /> == 0 stay
///     warm in the memory cache and are evicted (LRU by <see cref="LastUsed" />) only when a new
///     allocation would exceed the total memory cache limit.
/// </summary>
internal sealed class MemoryEntryCacheEntry
{
    public long LastUsed;

    public int RefCount;
    public required byte[] Buffer { get; init; }

    public required int Size { get; init; }
}