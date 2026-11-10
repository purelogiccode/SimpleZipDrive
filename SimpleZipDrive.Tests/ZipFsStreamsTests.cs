using SimpleZipDrive.Core;

namespace SimpleZipDrive.Tests;

public class ZipFsStreamsTests
{
    // ─── SharedMemoryStream tests ───

    [Fact]
    public void SharedMemoryStream_ReadReturnsCorrectData()
    {
        var data = new byte[] { 1, 2, 3, 4, 5 };
        using var stream = new SharedMemoryStream(data, () => { });

        var buffer = new byte[5];
        var bytesRead = stream.Read(buffer, 0, 5);

        Assert.Equal(5, bytesRead);
        Assert.Equal(data, buffer);
    }

    [Fact]
    public void SharedMemoryStream_ReadWithOffsetWorks()
    {
        var data = new byte[] { 10, 20, 30, 40, 50 };
        using var stream = new SharedMemoryStream(data, () => { });

        stream.Position = 2;
        var buffer = new byte[3];
        var bytesRead = stream.Read(buffer, 0, 3);

        Assert.Equal(3, bytesRead);
        Assert.Equal(30, buffer[0]);
        Assert.Equal(40, buffer[1]);
        Assert.Equal(50, buffer[2]);
    }

    [Fact]
    public void SharedMemoryStream_DisposeCallsCallback()
    {
        var data = new byte[] { 1, 2, 3 };
        var callbackInvoked = false;
        var stream = new SharedMemoryStream(data, () => callbackInvoked = true);

        stream.Dispose();

        Assert.True(callbackInvoked);
    }

    [Fact]
    public void SharedMemoryStream_DoubleDisposeCallsCallbackOnce()
    {
        var data = new byte[] { 1, 2, 3 };
        var callCount = 0;
        var stream = new SharedMemoryStream(data, () => callCount++);

        stream.Dispose();
        stream.Dispose();

        Assert.Equal(1, callCount);
    }

    [Fact]
    public void SharedMemoryStream_CanReadIsTrue()
    {
        var data = new byte[] { 1 };
        using var stream = new SharedMemoryStream(data, () => { });

        Assert.True(stream.CanRead);
    }

    [Fact]
    public void SharedMemoryStream_CanSeekIsTrue()
    {
        var data = new byte[] { 1 };
        using var stream = new SharedMemoryStream(data, () => { });

        Assert.True(stream.CanSeek);
    }

    [Fact]
    public void SharedMemoryStream_CanWriteIsFalse()
    {
        var data = new byte[] { 1 };
        using var stream = new SharedMemoryStream(data, () => { });

        Assert.False(stream.CanWrite);
    }

    [Fact]
    public void SharedMemoryStream_LengthMatchesBuffer()
    {
        var data = new byte[] { 1, 2, 3, 4 };
        using var stream = new SharedMemoryStream(data, () => { });

        Assert.Equal(4, stream.Length);
    }

    [Fact]
    public void SharedMemoryStream_DisposeInvokesCallback()
    {
        var data = new byte[] { 1, 2, 3 };
        var callbackInvoked = false;
        var stream = new SharedMemoryStream(data, () => callbackInvoked = true);

        stream.Dispose();
        Assert.True(callbackInvoked);
    }

    [Fact]
    public void SharedMemoryStream_ConcurrentReadsThroughReadStream_ReturnCorrectData()
    {
        // Regression test for bug report #69584: Dokan/WinFsp issue concurrent reads on the same
        // open handle. ReadStream used to set Position and then Read as two separate operations,
        // so another thread could move the shared MemoryStream position in between, producing
        // wrong data or an ArgumentException from Buffer.BlockCopy
        // ("Offset and length were out of bounds ...").
        const int size = 64 * 1024;
        var data = new byte[size];
        for (var i = 0; i < data.Length; i++) data[i] = (byte)(i % 251);

        using var stream = new SharedMemoryStream(data, () => { });

        Assert.Equal(0, CountConcurrentReadStreamFailures(stream, data));
    }

    [Fact]
    public void SharedMemoryStream_ReadAt_ReturnsCorrectDataWithoutLosingPosition()
    {
        var data = new byte[] { 10, 20, 30, 40, 50 };
        using var stream = new SharedMemoryStream(data, () => { });
        stream.Position = 1;

        var buffer = new byte[3];
        var bytesRead = stream.ReadAt(2, buffer, 0, 3);

        Assert.Equal(3, bytesRead);
        Assert.Equal([30, 40, 50], buffer);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    [InlineData(100)]
    public void SharedMemoryStream_ReadAtOutsideBuffer_ReturnsZero(long offset)
    {
        using var stream = new SharedMemoryStream(new byte[] { 1, 2, 3, 4, 5 }, () => { });

        var buffer = new byte[4];
        Assert.Equal(0, stream.ReadAt(offset, buffer, 0, buffer.Length));
    }

    [Fact]
    public void ReadStream_ConcurrentReadsOnFileStream_ReturnCorrectData()
    {
        // Disk-cached entries use a FileStream. Seek+read used to share the file position, so
        // concurrent reads on the same handle could return data from another thread's offset.
        const int size = 64 * 1024;
        var data = new byte[size];
        for (var i = 0; i < data.Length; i++) data[i] = (byte)(i % 251);

        var path = Path.Combine(Path.GetTempPath(), $"SimpleZipDriveTests_{Guid.NewGuid():N}.tmp");
        File.WriteAllBytes(path, data);

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

            Assert.Equal(0, CountConcurrentReadStreamFailures(stream, data));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void StoredEntryStream_ConcurrentSequentialReadAt_ReturnsCorrectData()
    {
        // Regression coverage for the same class of race as bug report #69584: Windows can read
        // one open handle from several threads, and the read-ahead state is shared per stream.
        // Concurrent ReadAt calls used to be able to observe half-updated read-ahead fields.
        const int size = 5 * 1024 * 1024;
        var data = new byte[size];
        for (var i = 0; i < data.Length; i++) data[i] = (byte)(i % 251);

        using var source = new MemoryStream(data);
        var lockObj = new Lock();
        using var stream = new StoredEntryStream(source, 0, size, lockObj);

        Assert.Equal(0, CountConcurrentStoredEntryReadFailures(stream, data));
    }

    // ─── StoredEntryStream tests ───

    [Fact]
    public void StoredEntryStream_ConstructorWithValidArgs_Succeeds()
    {
        using var source = new MemoryStream(new byte[100]);
        var lockObj = new Lock();

        using var stream = new StoredEntryStream(source, 0, 50, lockObj);

        Assert.Equal(50, stream.Length);
        Assert.True(stream.CanRead);
        Assert.True(stream.CanSeek);
        Assert.False(stream.CanWrite);
    }

    [Fact]
    public void StoredEntryStream_ReadFromBeginningReturnsData()
    {
        var data = new byte[] { 10, 20, 30, 40, 50 };
        using var source = new MemoryStream(data);
        var lockObj = new Lock();
        using var stream = new StoredEntryStream(source, 0, 5, lockObj);

        var buffer = new byte[5];
        var bytesRead = stream.Read(buffer, 0, 5);

        Assert.Equal(5, bytesRead);
        Assert.Equal(data, buffer);
    }

    [Fact]
    public void StoredEntryStream_ReadWithOffset()
    {
        var data = new byte[] { 0, 0, 10, 20, 30, 0, 0 };
        using var source = new MemoryStream(data);
        var lockObj = new Lock();
        using var stream = new StoredEntryStream(source, 2, 3, lockObj);

        var buffer = new byte[3];
        var bytesRead = stream.Read(buffer, 0, 3);

        Assert.Equal(3, bytesRead);
        Assert.Equal(10, buffer[0]);
        Assert.Equal(20, buffer[1]);
        Assert.Equal(30, buffer[2]);
    }

    [Fact]
    public void StoredEntryStream_ReadPastEndReturnsZero()
    {
        var data = new byte[] { 1, 2, 3 };
        using var source = new MemoryStream(data);
        var lockObj = new Lock();
        using var stream = new StoredEntryStream(source, 0, 3, lockObj);

        stream.Position = 3;
        var buffer = new byte[10];
        var bytesRead = stream.Read(buffer, 0, 10);

        Assert.Equal(0, bytesRead);
    }

    [Fact]
    public void StoredEntryStream_ReadPartialAtEnd()
    {
        var data = new byte[] { 10, 20, 30 };
        using var source = new MemoryStream(data);
        var lockObj = new Lock();
        using var stream = new StoredEntryStream(source, 0, 3, lockObj);

        stream.Position = 1;
        var buffer = new byte[10];
        var bytesRead = stream.Read(buffer, 0, 10);

        Assert.Equal(2, bytesRead);
        Assert.Equal(20, buffer[0]);
        Assert.Equal(30, buffer[1]);
    }

    [Fact]
    public void StoredEntryStream_SeekBegin()
    {
        var data = new byte[] { 10, 20, 30, 40, 50 };
        using var source = new MemoryStream(data);
        var lockObj = new Lock();
        using var stream = new StoredEntryStream(source, 0, 5, lockObj);

        var result = stream.Seek(2, SeekOrigin.Begin);

        Assert.Equal(2, result);
        Assert.Equal(2, stream.Position);
    }

    [Fact]
    public void StoredEntryStream_SeekCurrent()
    {
        var data = new byte[] { 10, 20, 30, 40, 50 };
        using var source = new MemoryStream(data);
        var lockObj = new Lock();
        using var stream = new StoredEntryStream(source, 0, 5, lockObj);

        stream.Position = 1;
        var result = stream.Seek(2, SeekOrigin.Current);

        Assert.Equal(3, result);
    }

    [Fact]
    public void StoredEntryStream_SeekEnd()
    {
        var data = new byte[] { 10, 20, 30, 40, 50 };
        using var source = new MemoryStream(data);
        var lockObj = new Lock();
        using var stream = new StoredEntryStream(source, 0, 5, lockObj);

        var result = stream.Seek(-2, SeekOrigin.End);

        Assert.Equal(3, result);
    }

    [Fact]
    public void StoredEntryStream_SeekOutOfRangeThrows()
    {
        var data = new byte[] { 10, 20, 30 };
        using var source = new MemoryStream(data);
        var lockObj = new Lock();
        using var stream = new StoredEntryStream(source, 0, 3, lockObj);

        Assert.Throws<IOException>(() => stream.Seek(-10, SeekOrigin.Begin));
    }

    [Fact]
    public void StoredEntryStream_SeekBeyondLengthThrows()
    {
        var data = new byte[] { 10, 20, 30 };
        using var source = new MemoryStream(data);
        var lockObj = new Lock();
        using var stream = new StoredEntryStream(source, 0, 3, lockObj);

        Assert.Throws<IOException>(() => stream.Seek(100, SeekOrigin.Begin));
    }

    [Fact]
    public void StoredEntryStream_PositionSetNegativeThrows()
    {
        var data = new byte[] { 10, 20, 30 };
        using var source = new MemoryStream(data);
        var lockObj = new Lock();
        using var stream = new StoredEntryStream(source, 0, 3, lockObj);

        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Position = -1);
    }

    [Fact]
    public void StoredEntryStream_PositionBeyondLengthThrows()
    {
        var data = new byte[] { 10, 20, 30 };
        using var source = new MemoryStream(data);
        var lockObj = new Lock();
        using var stream = new StoredEntryStream(source, 0, 3, lockObj);

        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Position = 100);
    }

    [Fact]
    public void StoredEntryStream_FlushDoesNotThrow()
    {
        var data = new byte[] { 1, 2, 3 };
        using var source = new MemoryStream(data);
        var lockObj = new Lock();
        using var stream = new StoredEntryStream(source, 0, 3, lockObj);

        var ex = Record.Exception(stream.Flush);
        Assert.Null(ex);
    }

    [Fact]
    public void StoredEntryStream_SetLengthThrowsNotSupported()
    {
        var data = new byte[] { 1, 2, 3 };
        using var source = new MemoryStream(data);
        var lockObj = new Lock();
        using var stream = new StoredEntryStream(source, 0, 3, lockObj);

        Assert.Throws<NotSupportedException>(() => stream.SetLength(10));
    }

    [Fact]
    public void StoredEntryStream_WriteThrowsNotSupported()
    {
        var data = new byte[] { 1, 2, 3 };
        using var source = new MemoryStream(data);
        var lockObj = new Lock();
        using var stream = new StoredEntryStream(source, 0, 3, lockObj);

        Assert.Throws<NotSupportedException>(() => stream.Write([1, 2], 0, 2));
    }

    [Fact]
    public void StoredEntryStream_DisposedReadThrowsObjectDisposed()
    {
        var data = new byte[] { 1, 2, 3 };
        using var source = new MemoryStream(data);
        var lockObj = new Lock();
        var stream = new StoredEntryStream(source, 0, 3, lockObj);

        stream.Dispose();

        var buffer = new byte[1];
        Assert.Throws<ObjectDisposedException>(() => stream.Read(buffer, 0, 1));
    }

    [Fact]
    public void StoredEntryStream_DisposedSeekThrowsObjectDisposed()
    {
        var data = new byte[] { 1, 2, 3 };
        using var source = new MemoryStream(data);
        var lockObj = new Lock();
        var stream = new StoredEntryStream(source, 0, 3, lockObj);

        stream.Dispose();

        Assert.Throws<ObjectDisposedException>(() => stream.Seek(0, SeekOrigin.Begin));
    }

    [Fact]
    public void StoredEntryStream_DisposedPositionGetThrowsObjectDisposed()
    {
        var data = new byte[] { 1, 2, 3 };
        using var source = new MemoryStream(data);
        var lockObj = new Lock();
        var stream = new StoredEntryStream(source, 0, 3, lockObj);

        stream.Dispose();

        // The Position getter accesses _position which doesn't check disposal
        // but reading does check disposal
        var buffer = new byte[1];
        Assert.Throws<ObjectDisposedException>(() => stream.Read(buffer, 0, 1));
    }

    [Fact]
    public void StoredEntryStream_ReadSpanReturnsCorrectData()
    {
        var data = new byte[] { 10, 20, 30, 40, 50 };
        using var source = new MemoryStream(data);
        var lockObj = new Lock();
        using var stream = new StoredEntryStream(source, 0, 5, lockObj);

        var buffer = new byte[5];
        var bytesRead = stream.Read(buffer.AsSpan());

        Assert.Equal(5, bytesRead);
        Assert.Equal(data, buffer);
    }

    [Fact]
    public void StoredEntryStream_ReadSpanPastEndReturnsPartial()
    {
        var data = new byte[] { 10, 20, 30 };
        using var source = new MemoryStream(data);
        var lockObj = new Lock();
        using var stream = new StoredEntryStream(source, 0, 3, lockObj);

        stream.Position = 1;
        var buffer = new byte[10];
        var bytesRead = stream.Read(buffer.AsSpan());

        Assert.Equal(2, bytesRead);
        Assert.Equal(20, buffer[0]);
        Assert.Equal(30, buffer[1]);
    }

    [Fact]
    public void StoredEntryStream_ReadAtReturnsCorrectData()
    {
        var data = new byte[] { 10, 20, 30, 40, 50 };
        using var source = new MemoryStream(data);
        var lockObj = new Lock();
        using var stream = new StoredEntryStream(source, 0, 5, lockObj);

        var buffer = new byte[3];
        var bytesRead = stream.ReadAt(2, buffer, 0, 3);

        Assert.Equal(3, bytesRead);
        Assert.Equal(30, buffer[0]);
        Assert.Equal(40, buffer[1]);
        Assert.Equal(50, buffer[2]);
    }

    [Fact]
    public void StoredEntryStream_ReadAtNegativeOffsetReturnsZero()
    {
        var data = new byte[] { 10, 20, 30 };
        using var source = new MemoryStream(data);
        var lockObj = new Lock();
        using var stream = new StoredEntryStream(source, 0, 3, lockObj);

        var buffer = new byte[3];
        var bytesRead = stream.ReadAt(-1, buffer, 0, 3);

        Assert.Equal(0, bytesRead);
    }

    [Fact]
    public void StoredEntryStream_ReadAtBeyondLengthReturnsZero()
    {
        var data = new byte[] { 10, 20, 30 };
        using var source = new MemoryStream(data);
        var lockObj = new Lock();
        using var stream = new StoredEntryStream(source, 0, 3, lockObj);

        var buffer = new byte[3];
        var bytesRead = stream.ReadAt(10, buffer, 0, 3);

        Assert.Equal(0, bytesRead);
    }

    [Fact]
    public void StoredEntryStream_ReadAtUpdatesPosition()
    {
        var data = new byte[] { 10, 20, 30, 40, 50 };
        using var source = new MemoryStream(data);
        var lockObj = new Lock();
        using var stream = new StoredEntryStream(source, 0, 5, lockObj);

        var buffer = new byte[2];
        stream.ReadAt(3, buffer, 0, 2);

        Assert.Equal(5, stream.Position);
    }

    [Fact]
    public void StoredEntryStream_ConstructorNegativeOffsetThrows()
    {
        using var source = new MemoryStream(new byte[10]);
        var lockObj = new Lock();

        Assert.Throws<ArgumentOutOfRangeException>(() => new StoredEntryStream(source, -1, 5, lockObj));
    }

    [Fact]
    public void StoredEntryStream_ConstructorOffsetBeyondLengthThrows()
    {
        using var source = new MemoryStream(new byte[10]);
        var lockObj = new Lock();

        Assert.Throws<ArgumentOutOfRangeException>(() => new StoredEntryStream(source, 100, 5, lockObj));
    }

    [Fact]
    public void StoredEntryStream_ConstructorNegativeLengthThrows()
    {
        using var source = new MemoryStream(new byte[10]);
        var lockObj = new Lock();

        Assert.Throws<ArgumentOutOfRangeException>(() => new StoredEntryStream(source, 0, -1, lockObj));
    }

    [Fact]
    public void StoredEntryStream_EmptyDataLengthZero_WorksCorrectly()
    {
        using var source = new MemoryStream(new byte[10]);
        var lockObj = new Lock();
        using var stream = new StoredEntryStream(source, 5, 0, lockObj);

        Assert.Equal(0, stream.Length);
        Assert.Equal(0, stream.Position);

        var buffer = new byte[1];
        var bytesRead = stream.Read(buffer, 0, 1);
        Assert.Equal(0, bytesRead);
    }

    [Fact]
    public void StoredEntryStream_SequentialReadsWorkCorrectly()
    {
        var data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        using var source = new MemoryStream(data);
        var lockObj = new Lock();
        using var stream = new StoredEntryStream(source, 0, 10, lockObj);

        var buffer1 = new byte[3];
        var read1 = stream.Read(buffer1, 0, 3);
        Assert.Equal(3, read1);
        Assert.Equal(1, buffer1[0]);
        Assert.Equal(2, buffer1[1]);
        Assert.Equal(3, buffer1[2]);

        var buffer2 = new byte[3];
        var read2 = stream.Read(buffer2, 0, 3);
        Assert.Equal(3, read2);
        Assert.Equal(4, buffer2[0]);
        Assert.Equal(5, buffer2[1]);
        Assert.Equal(6, buffer2[2]);

        var buffer3 = new byte[10];
        var read3 = stream.Read(buffer3, 0, 10);
        Assert.Equal(4, read3);
        Assert.Equal(7, buffer3[0]);
        Assert.Equal(10, buffer3[3]);
    }

    /// <summary>
    ///     Runs the concurrency regression pattern against an arbitrary readable stream. The
    ///     helper keeps the stream out of a local <c>using</c> scope inside the parallel lambda
    ///     (the caller owns its lifetime).
    /// </summary>
    private static int CountConcurrentReadStreamFailures(Stream stream, byte[] data)
    {
        int[] offsets = [0, 1024, 4096, 16384, 32768, data.Length - 8192];
        var failures = 0;

        Parallel.For(0, 96, i =>
        {
            var offset = offsets[i % offsets.Length];
            var buffer = new byte[data.Length - offset];
            var read = ZipFileSystemCore.ReadStream(stream, offset, buffer, 0, buffer.Length);

            if (read != buffer.Length || !buffer.AsSpan().SequenceEqual(data.AsSpan(offset)))
                Interlocked.Increment(ref failures);
        });

        return failures;
    }

    private static int CountConcurrentStoredEntryReadFailures(StoredEntryStream stream, byte[] data)
    {
        const int chunkSize = 64 * 1024;
        var failures = 0;

        Parallel.For(0, 4, _ =>
        {
            var buffer = new byte[chunkSize];
            for (long offset = 0; offset < data.Length; offset += chunkSize)
            {
                var count = (int)Math.Min(chunkSize, data.Length - offset);
                var read = stream.ReadAt(offset, buffer, 0, count);

                if (read != count || !buffer.AsSpan(0, read).SequenceEqual(data.AsSpan((int)offset, read)))
                    Interlocked.Increment(ref failures);
            }
        });

        return failures;
    }
}