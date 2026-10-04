using System.IO.Compression;
using System.Text;
using SimpleZipDrive.Core;

namespace SimpleZipDrive.Tests;

/// <summary>
///     Tests for <see cref="SevenZipFallback" />, which extracts entries by running the bundled
///     7-Zip command-line executable (7za.exe on Windows, 7zz/7zzs on Linux and macOS).
///     Extraction success is asserted when a matching executable is present in the test output;
///     the failure paths are asserted always.
/// </summary>
public class SevenZipFallbackTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "SimpleZipDriveTests", Guid.NewGuid().ToString("N"));

    public SevenZipFallbackTests()
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

    private static string[] ExpectedExecutableNames()
    {
        return OperatingSystem.IsWindows()
            ? ["7za.exe", "7z.exe"]
            : ["7zz", "7zzs", "7za", "7z"];
    }

    [Fact]
    public void IsAvailable_DoesNotThrow()
    {
        var ex = Record.Exception(static () => _ = SevenZipFallback.IsAvailable());

        Assert.Null(ex);
    }

    [Fact]
    public void IsAvailable_MatchesPresenceOfBundledExecutable()
    {
        var expected = ExpectedExecutableNames()
            .Any(static name => File.Exists(Path.Combine(AppContext.BaseDirectory, name)));

        Assert.Equal(expected, SevenZipFallback.IsAvailable());
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var fallback = new SevenZipFallback(Path.Combine(_root, "missing.7z"), static () => null);

        var ex = Record.Exception(() =>
        {
            fallback.Dispose();
            fallback.Dispose();
        });

        Assert.Null(ex);
    }

    [Fact]
    public void TryExtractEntry_AfterDispose_ReturnsFalse()
    {
        var fallback = new SevenZipFallback(Path.Combine(_root, "missing.7z"), static () => null);
        fallback.Dispose();

        using var output = new MemoryStream();
        Assert.False(fallback.TryExtractEntry("file.txt", output));
    }

    [Fact]
    public void TryExtractEntry_NonExistentArchive_ReturnsFalse()
    {
        using var fallback = new SevenZipFallback(Path.Combine(_root, "missing.7z"), static () => null);

        using var output = new MemoryStream();
        Assert.False(fallback.TryExtractEntry("file.txt", output));
    }

    [Fact]
    public void TryExtractEntry_NullPath_ReturnsFalse()
    {
        using var fallback = new SevenZipFallback(Path.Combine(_root, "missing.7z"), static () => null);

        using var output = new MemoryStream();
        Assert.False(fallback.TryExtractEntry(null!, output));
    }

    [Fact]
    public void TryExtractEntry_WhenExecutableAvailable_ExtractsEntryContent()
    {
        if (!SevenZipFallback.IsAvailable()) return;

        var zipPath = CreateSampleZip();

        using var fallback = new SevenZipFallback(zipPath, static () => null);
        using var output = new MemoryStream();

        Assert.True(fallback.TryExtractEntry("readme.txt", output));
        Assert.Equal("Hello World", Encoding.UTF8.GetString(output.ToArray()));
    }

    [Fact]
    public void TryExtractEntry_WhenExecutableAvailable_UnknownEntry_ReturnsFalse()
    {
        if (!SevenZipFallback.IsAvailable()) return;

        var zipPath = CreateSampleZip();

        using var fallback = new SevenZipFallback(zipPath, static () => null);
        using var output = new MemoryStream();

        Assert.False(fallback.TryExtractEntry("does-not-exist.txt", output));
    }

    [Fact]
    public void TryExtractEntry_WhenExecutableAvailable_BackslashPath_IsNormalized()
    {
        if (!SevenZipFallback.IsAvailable()) return;

        var zipPath = CreateSampleZip();

        using var fallback = new SevenZipFallback(zipPath, static () => null);
        using var output = new MemoryStream();

        Assert.True(fallback.TryExtractEntry(@"data\info.txt", output));
        Assert.Equal("Nested content", Encoding.UTF8.GetString(output.ToArray()));
    }

    [Fact]
    public void TryExtractEntry_WhenExecutableAvailable_WildcardCharactersInName_AreLiteral()
    {
        if (!SevenZipFallback.IsAvailable()) return;

        var sourceDir = Path.Combine(_root, "source");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(sourceDir, "ha[ha].txt"), "Bracket File", new UTF8Encoding(false));
        var zipPath = Path.Combine(_root, "sample.zip");
        ZipFile.CreateFromDirectory(sourceDir, zipPath);

        using var fallback = new SevenZipFallback(zipPath, static () => null);
        using var output = new MemoryStream();

        Assert.True(fallback.TryExtractEntry("ha[ha].txt", output));
        Assert.Equal("Bracket File", Encoding.UTF8.GetString(output.ToArray()));
    }

    private string CreateSampleZip()
    {
        var sourceDir = Path.Combine(_root, "source");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(Path.Combine(sourceDir, "data"));
        File.WriteAllText(Path.Combine(sourceDir, "readme.txt"), "Hello World", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(sourceDir, "data", "info.txt"), "Nested content", new UTF8Encoding(false));

        var zipPath = Path.Combine(_root, "sample.zip");
        ZipFile.CreateFromDirectory(sourceDir, zipPath);
        return zipPath;
    }
}
