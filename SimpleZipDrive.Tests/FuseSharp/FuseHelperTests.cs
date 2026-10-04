using System.Runtime.InteropServices;
using SimpleZipDrive.FuseSharp;

namespace SimpleZipDrive.Tests.FuseSharp;

/// <summary>
/// Tests the FUSE path, timestamp and directory-fill helpers directly.
/// </summary>
public class FuseHelperTests
{
    /// <summary>
    /// Verifies native FUSE paths stay POSIX and always start with the root separator.
    /// </summary>
    /// <param name="native">The native path to convert.</param>
    /// <param name="expectedPath">The expected FUSE path.</param>
    [Theory]
    [InlineData("/sub/file.bin", "/sub/file.bin")]
    [InlineData("/", "/")]
    [InlineData("", "/")]
    [InlineData("relative", "/relative")]
    public void ToFusePath_NormalizesNativePaths(string native, string expectedPath)
    {
        var pointer = Marshal.StringToCoTaskMemUTF8(native);
        try
        {
            Assert.Equal(expectedPath, FuseFileSystem.ToFusePath(pointer));
        }
        finally
        {
            Marshal.FreeCoTaskMem(pointer);
        }
    }

    /// <summary>
    /// Verifies a null pointer maps to the root.
    /// </summary>
    [Fact]
    public void ToFusePath_WithNullPointer_ReturnsRoot()
    {
        Assert.Equal("/", FuseFileSystem.ToFusePath(IntPtr.Zero));
    }

    /// <summary>
    /// Verifies known UTC and unspecified timestamps convert to the expected epoch seconds.
    /// </summary>
    [Fact]
    public void ToUnixTime_ConvertsKnownValues()
    {
        Assert.Equal(0, FuseFileSystem.ToUnixTime(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(1577934245, FuseFileSystem.ToUnixTime(new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc)));
        Assert.Equal(1577934245,
            FuseFileSystem.ToUnixTime(new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Unspecified)));
    }

    /// <summary>
    /// Verifies local timestamps are converted through their offset.
    /// </summary>
    [Fact]
    public void ToUnixTime_ForLocalTime_MatchesDateTimeOffset()
    {
        var local = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Local);

        Assert.Equal(new DateTimeOffset(local).ToUnixTimeSeconds(), FuseFileSystem.ToUnixTime(local));
    }

    /// <summary>
    /// Verifies the minimum date converts without throwing.
    /// </summary>
    [Fact]
    public void ToUnixTime_ForMinValue_DoesNotThrow()
    {
        var expectedSeconds = new DateTimeOffset(DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc))
            .ToUnixTimeSeconds();

        Assert.Equal(expectedSeconds, FuseFileSystem.ToUnixTime(DateTime.MinValue));
    }

    /// <summary>
    /// Verifies filling starts at the requested offset and reports the next offset.
    /// </summary>
    [Fact]
    public void FillDirectory_StartsAtOffsetAndReportsNextOffsets()
    {
        var names = new List<string> { ".", "..", "file.bin" };
        var seen = new List<(string Name, long NextOffset)>();

        var result = FuseFileSystem.FillDirectory(names, 2, (name, nextOffset) =>
        {
            seen.Add((Marshal.PtrToStringUTF8(name) ?? string.Empty, nextOffset));
            return 0;
        });

        Assert.Equal(0, result);
        var entry = Assert.Single(seen);
        Assert.Equal("file.bin", entry.Name);
        Assert.Equal(3, entry.NextOffset);
    }

    /// <summary>
    /// Verifies a non-positive offset starts at the first entry.
    /// </summary>
    [Fact]
    public void FillDirectory_NegativeOffset_StartsAtFirstEntry()
    {
        var names = new List<string> { ".", "file.bin" };
        var count = 0;

        FuseFileSystem.FillDirectory(names, -1, (_, _) =>
        {
            count++;
            return 0;
        });

        Assert.Equal(2, count);
    }

    /// <summary>
    /// Verifies a non-zero filler result stops the enumeration.
    /// </summary>
    [Fact]
    public void FillDirectory_StopsWhenCallbackReturnsNonZero()
    {
        var names = new List<string> { ".", "..", "file.bin" };
        var count = 0;

        FuseFileSystem.FillDirectory(names, 0, (_, _) => ++count == 1 ? 1 : 0);

        Assert.Equal(1, count);
    }

    /// <summary>
    /// Verifies an empty directory fills nothing and reports success.
    /// </summary>
    [Fact]
    public void FillDirectory_EmptyList_FillsNothing()
    {
        var count = 0;

        var result = FuseFileSystem.FillDirectory([], 0, (_, _) =>
        {
            count++;
            return 0;
        });

        Assert.Equal(0, result);
        Assert.Equal(0, count);
    }

    /// <summary>
    /// Verifies an offset at the end of the list fills nothing.
    /// </summary>
    [Fact]
    public void FillDirectory_OffsetAtEnd_FillsNothing()
    {
        var names = new List<string> { ".", ".." };
        var count = 0;

        FuseFileSystem.FillDirectory(names, names.Count, (_, _) =>
        {
            count++;
            return 0;
        });

        Assert.Equal(0, count);
    }

    private static readonly long[] ExpectedOffsets = [1L, 2L, 3L];

    /// <summary>
    /// Verifies the reported next offsets are one-based.
    /// </summary>
    [Fact]
    public void FillDirectory_ReportsOneBasedNextOffsets()
    {
        var names = new List<string> { "a", "b", "c" };
        var offsets = new List<long>();

        FuseFileSystem.FillDirectory(names, 0, (_, nextOffset) =>
        {
            offsets.Add(nextOffset);
            return 0;
        });

        Assert.Equal(ExpectedOffsets, offsets);
    }

    /// <summary>
    /// Verifies trailing separators are preserved when converting the native path.
    /// </summary>
    [Fact]
    public void ToFusePath_PreservesTrailingSeparator()
    {
        var pointer = Marshal.StringToCoTaskMemUTF8("/sub/");
        try
        {
            Assert.Equal("/sub/", FuseFileSystem.ToFusePath(pointer));
        }
        finally
        {
            Marshal.FreeCoTaskMem(pointer);
        }
    }

    /// <summary>
    /// Verifies post-epoch UTC timestamps convert to the matching epoch seconds.
    /// </summary>
    [Fact]
    public void ToUnixTime_ForFutureDate_MatchesOffset()
    {
        var value = new DateTime(2030, 6, 15, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal(new DateTimeOffset(value).ToUnixTimeSeconds(), FuseFileSystem.ToUnixTime(value));
        Assert.True(FuseFileSystem.ToUnixTime(value) > 0);
    }
}