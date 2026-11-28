using SimpleZipDrive.Mounting.Fuse;

namespace SimpleZipDrive.Tests;

/// <summary>
///     Tests for the explicit FUSE mount-point validation that replaced the raw
///     "The file '...' already exists." error from <c>Directory.CreateDirectory</c>
///     (bug reports #70391-#70394).
/// </summary>
public sealed class FuseMountPointTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "SimpleZipDriveTests", Guid.NewGuid().ToString("N"));

    public FuseMountPointTests()
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
    public void EnsureMountPointDirectory_CreatesMissingDirectory()
    {
        var mountPoint = Path.Combine(_root, "mount");

        FuseMountService.EnsureMountPointDirectory(mountPoint);

        Assert.True(Directory.Exists(mountPoint));
    }

    [Fact]
    public void EnsureMountPointDirectory_CreatesNestedDirectories()
    {
        var mountPoint = Path.Combine(_root, "nested", "mount");

        FuseMountService.EnsureMountPointDirectory(mountPoint);

        Assert.True(Directory.Exists(mountPoint));
    }

    [Fact]
    public void EnsureMountPointDirectory_ExistingDirectory_IsKept()
    {
        var mountPoint = Path.Combine(_root, "mount");
        Directory.CreateDirectory(mountPoint);
        var entry = Path.Combine(mountPoint, "keep.txt");
        File.WriteAllText(entry, "keep");

        FuseMountService.EnsureMountPointDirectory(mountPoint);

        Assert.True(File.Exists(entry));
    }

    [Fact]
    public void EnsureMountPointDirectory_ExistingFile_ThrowsActionableIOException()
    {
        var mountPoint = Path.Combine(_root, "mnt-f");
        File.WriteAllText(mountPoint, "not a mount point");

        var ex = Assert.Throws<IOException>(() => FuseMountService.EnsureMountPointDirectory(mountPoint));

        Assert.Contains("mount point", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("existing file", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not a folder", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("not a mount point", File.ReadAllText(mountPoint));
    }

    [Fact]
    public void EnsureMountPointDirectory_ParentIsFile_ThrowsActionableIOException()
    {
        var parent = Path.Combine(_root, "parent");
        File.WriteAllText(parent, "not a directory");
        var mountPoint = Path.Combine(parent, "mount");

        var ex = Assert.Throws<IOException>(() => FuseMountService.EnsureMountPointDirectory(mountPoint));

        Assert.Contains("mount point", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cannot be used", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(ex.InnerException);
    }
}
