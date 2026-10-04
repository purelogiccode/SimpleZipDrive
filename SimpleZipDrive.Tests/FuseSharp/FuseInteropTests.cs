using SimpleZipDrive.FuseSharp;

namespace SimpleZipDrive.Tests.FuseSharp;

/// <summary>
/// Tests the FUSE library resolver and availability probe. Probes use explicit candidate
/// lists so the outcome is deterministic regardless of whether the host has FUSE 3.
/// </summary>
public class FuseInteropTests
{
    /// <summary>
    /// Verifies the resolver registration is idempotent and never throws.
    /// </summary>
    [Fact]
    public void RegisterResolver_IsIdempotent()
    {
        FuseInterop.RegisterResolver();
        FuseInterop.RegisterResolver();
    }

    /// <summary>
    /// Verifies probing a list that contains only an unloadable candidate fails on every platform.
    /// </summary>
    [Fact]
    public void TryLoadLibrary_WithOnlyBogusCandidate_ReturnsFalse()
    {
        var bogus = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dll");

        Assert.False(FuseInterop.TryLoadLibrary([bogus], out var libraryPath));
        Assert.Null(libraryPath);
    }

    /// <summary>
    /// Verifies the availability probe reports failure (and prints guidance) for a
    /// candidate list that cannot be loaded, on every platform.
    /// </summary>
    [Fact]
    public void Check_WithOnlyBogusCandidate_ReturnsFalse()
    {
        var bogus = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dll");
        var originalError = Console.Error;
        using var suppressed = new StringWriter();
        Console.SetError(suppressed);

        try
        {
            Assert.False(FuseAvailability.Check(out var libraryPath, [bogus]));
            Assert.Null(libraryPath);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    /// <summary>
    /// Verifies library candidates are ordered by numeric version so double-digit
    /// suffixes outrank single-digit ones and unparsable names sort lowest.
    /// </summary>
    [Fact]
    public void CompareLibraryFileNames_OrdersByNumericVersion()
    {
        Assert.True(FuseInterop.CompareLibraryFileNames("libfuse3.so.9", "libfuse3.so.10") < 0);
        Assert.True(FuseInterop.CompareLibraryFileNames("libfuse3.so.3", "libfuse3.so.3.14.0") < 0);
        Assert.True(FuseInterop.CompareLibraryFileNames("libfuse3.so.10", "libfuse3.so.3.14.0") > 0);
        Assert.True(FuseInterop.CompareLibraryFileNames("libfuse3.dylib", "libfuse3.so.3") < 0);
        Assert.Equal(0, FuseInterop.CompareLibraryFileNames("libfuse3.so.3", "/usr/lib/other/libfuse3.so.3"));
    }

    /// <summary>
    /// Verifies identical library names compare as equal.
    /// </summary>
    [Fact]
    public void CompareLibraryFileNames_IdenticalNames_ReturnZero()
    {
        Assert.Equal(0, FuseInterop.CompareLibraryFileNames("libfuse3.so.3", "libfuse3.so.3"));
    }

    /// <summary>
    /// Verifies an unversioned library sorts below a versioned one.
    /// </summary>
    [Fact]
    public void CompareLibraryFileNames_UnversionedRanksBelowVersioned()
    {
        Assert.True(FuseInterop.CompareLibraryFileNames("libfuse3.so", "libfuse3.so.3") < 0);
    }

    /// <summary>
    /// Verifies patch-level differences are ordered numerically.
    /// </summary>
    [Fact]
    public void CompareLibraryFileNames_OrdersPatchLevelsNumerically()
    {
        Assert.True(FuseInterop.CompareLibraryFileNames("libfuse3.so.3.14.0", "libfuse3.so.3.15.0") < 0);
        Assert.True(FuseInterop.CompareLibraryFileNames("libfuse3.so.3.15.0", "libfuse3.so.3.14.0") > 0);
    }

    /// <summary>
    /// Verifies the same file name in different directories compares as equal.
    /// </summary>
    [Fact]
    public void CompareLibraryFileNames_SameNameDifferentDirectory_ReturnZero()
    {
        Assert.Equal(0,
            FuseInterop.CompareLibraryFileNames("/usr/lib/libfuse3.so.3", "/opt/lib/libfuse3.so.3"));
    }

    /// <summary>
    /// Verifies a whitespace-only candidate is rejected without throwing.
    /// </summary>
    [Fact]
    public void TryLoadLibrary_WithWhitespaceCandidate_ReturnsFalse()
    {
        Assert.False(FuseInterop.TryLoadLibrary(["   "], out var libraryPath));
        Assert.Null(libraryPath);
    }
}