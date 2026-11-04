using SimpleZipDrive.Core;

namespace SimpleZipDrive.Tests;

/// <summary>
///     Tests for the per-session temp directory creation and its fallback to the system temp root
///     when the application data folder is unusable (bug reports #69557-#69563).
/// </summary>
public sealed class ZipFsTempDirectoryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "SimpleZipDriveTests", Guid.NewGuid().ToString("N"));

    public ZipFsTempDirectoryTests()
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
            // Best-effort test cleanup.
        }
    }

    [Fact]
    public void CreateSessionTempDirectory_UsesFirstUsableRoot()
    {
        var path = ZipFsHelpers.CreateSessionTempDirectory("session", [_root]);

        Assert.Equal(Path.Combine(_root, "session"), path);
        Assert.True(Directory.Exists(path));
    }

    [Fact]
    public void CreateSessionTempDirectory_PreferredRootUnusable_FallsBackToNextRoot()
    {
        // A file where a directory is expected makes Directory.CreateDirectory fail with an
        // IOException, mirroring an unusable application data folder.
        var blockedRoot = Path.Combine(_root, "blocked");
        File.WriteAllText(blockedRoot, "not a directory");
        var usableRoot = Path.Combine(_root, "usable");

        var path = ZipFsHelpers.CreateSessionTempDirectory("session", [blockedRoot, usableRoot]);

        Assert.Equal(Path.Combine(usableRoot, "session"), path);
        Assert.True(Directory.Exists(path));
    }

    [Fact]
    public void CreateSessionTempDirectory_AllRootsUnusable_ThrowsIOException()
    {
        var blockedRoot = Path.Combine(_root, "blocked");
        File.WriteAllText(blockedRoot, "not a directory");
        var alsoBlockedRoot = Path.Combine(_root, "also-blocked");
        File.WriteAllText(alsoBlockedRoot, "not a directory");

        var ex = Assert.Throws<IOException>(() =>
            ZipFsHelpers.CreateSessionTempDirectory("session", [blockedRoot, alsoBlockedRoot]));

        Assert.Contains("session", ex.Message, StringComparison.Ordinal);
        Assert.Contains("blocked", ex.Message, StringComparison.Ordinal);
        Assert.IsType<AggregateException>(ex.InnerException);
    }

    [Fact]
    public void CreateSessionTempDirectory_EmptyName_Throws()
    {
        Assert.Throws<ArgumentException>(() => ZipFsHelpers.CreateSessionTempDirectory("", [_root]));
    }
}
