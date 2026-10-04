using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using SimpleZipDrive.Core;

namespace SimpleZipDrive.Tests;

/// <summary>
///     Tests for <see cref="SevenZipFallback" />. Extraction success is only asserted when the
///     native 7z library is present in the test output; the failure paths are asserted always.
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

    /// <summary>
    ///     Returns true only when the native 7z library exists AND its PE architecture matches
    ///     the test process. The repository currently ships the 32-bit 7z.dll, which cannot be
    ///     loaded by the x64 test host, so extraction success is only asserted when usable.
    /// </summary>
    private static bool CanUseNativeLibrary()
    {
        if (!SevenZipFallback.IsAvailable()) return false;

        var isArm64 = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
        var dllPath = Path.Combine(AppContext.BaseDirectory, isArm64 ? "7z_arm64.dll" : "7z.dll");
        try
        {
            var bytes = File.ReadAllBytes(dllPath);
            if (bytes.Length < 0x40) return false;

            var peOffset = BitConverter.ToInt32(bytes, 0x3C);
            if (peOffset <= 0 || peOffset + 6 > bytes.Length) return false;

            var machine = BitConverter.ToUInt16(bytes, peOffset + 4);
            return machine switch
            {
                0x8664 => RuntimeInformation.ProcessArchitecture == Architecture.X64,
                0xAA64 => RuntimeInformation.ProcessArchitecture == Architecture.Arm64,
                0x014C => RuntimeInformation.ProcessArchitecture == Architecture.X86,
                _ => false
            };
        }
        catch
        {
            return false;
        }
    }

    [Fact]
    public void IsAvailable_DoesNotThrow()
    {
        var ex = Record.Exception(static () => _ = SevenZipFallback.IsAvailable());

        Assert.Null(ex);
    }

    [Fact]
    public void IsAvailable_MatchesPresenceOfNativeLibrary()
    {
        var isArm64 = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
        var expected = File.Exists(Path.Combine(AppContext.BaseDirectory, isArm64 ? "7z_arm64.dll" : "7z.dll"));

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
    public void TryExtractEntry_WhenLibraryUsable_ExtractsEntryContent()
    {
        if (!CanUseNativeLibrary()) return;

        var sourceDir = Path.Combine(_root, "source");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(sourceDir, "readme.txt"), "Hello World", new UTF8Encoding(false));
        var zipPath = Path.Combine(_root, "sample.zip");
        ZipFile.CreateFromDirectory(sourceDir, zipPath);

        using var fallback = new SevenZipFallback(zipPath, static () => null);
        using var output = new MemoryStream();

        Assert.True(fallback.TryExtractEntry("readme.txt", output));
        Assert.Equal("Hello World", Encoding.UTF8.GetString(output.ToArray()));
    }

    [Fact]
    public void TryExtractEntry_WhenLibraryUsable_UnknownEntry_ReturnsFalse()
    {
        if (!CanUseNativeLibrary()) return;

        var sourceDir = Path.Combine(_root, "source");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(sourceDir, "readme.txt"), "Hello World", new UTF8Encoding(false));
        var zipPath = Path.Combine(_root, "sample.zip");
        ZipFile.CreateFromDirectory(sourceDir, zipPath);

        using var fallback = new SevenZipFallback(zipPath, static () => null);
        using var output = new MemoryStream();

        Assert.False(fallback.TryExtractEntry("does-not-exist.txt", output));
    }

    [Fact]
    public void TryExtractEntry_WhenLibraryUsable_BackslashPath_IsNormalized()
    {
        if (!CanUseNativeLibrary()) return;

        var sourceDir = Path.Combine(_root, "source");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(Path.Combine(sourceDir, "data"));
        File.WriteAllText(Path.Combine(sourceDir, "data", "info.txt"), "Nested content", new UTF8Encoding(false));
        var zipPath = Path.Combine(_root, "sample.zip");
        ZipFile.CreateFromDirectory(sourceDir, zipPath);

        using var fallback = new SevenZipFallback(zipPath, static () => null);
        using var output = new MemoryStream();

        Assert.True(fallback.TryExtractEntry(@"data\info.txt", output));
        Assert.Equal("Nested content", Encoding.UTF8.GetString(output.ToArray()));
    }
}
