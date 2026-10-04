using System.Diagnostics.CodeAnalysis;
using SimpleZipDrive.Core.Services;

namespace SimpleZipDrive.Tests;

/// <summary>
///     Tests for <see cref="ScreenshotService" />, focusing on the save-location fallback:
///     the application's "Screenshot" folder first, then the "Screenshot" folder under
///     <c>%LOCALAPPDATA%\SimpleZipDrive</c>.
/// </summary>
[SuppressMessage("ReSharper", "NullableWarningSuppressionIsUsed")]
public class ScreenshotServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "SimpleZipDriveTests", Guid.NewGuid().ToString("N"));

    private readonly LoggingService _loggingService = new();

    public ScreenshotServiceTests()
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

        _loggingService.Clear();
    }

    private string TempDirectory(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private string TempFile(string name)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, "blocker");
        return path;
    }

    // ─── Constructor ───

    [Fact]
    public void Constructor_NullLoggingService_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(static () => new ScreenshotService(null!));
    }

    [Fact]
    public void Constructor_ValidLoggingService_CreatesInstance()
    {
        var service = new ScreenshotService(_loggingService);

        Assert.NotNull(service);
    }

    // ─── Default directories ───

    [Fact]
    public void DefaultPrimaryDirectory_IsScreenshotFolderUnderApplicationBase()
    {
        var expected = Path.Combine(AppContext.BaseDirectory, "Screenshot");

        Assert.Equal(expected, ScreenshotService.ScreenshotDirectory);
    }

    [Fact]
    public void DefaultFallbackDirectory_IsScreenshotFolderUnderLocalAppDataSimpleZipDrive()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SimpleZipDrive", "Screenshot");

        Assert.Equal(expected, ScreenshotService.FallbackScreenshotDirectory);
    }

    // ─── CaptureActiveWindow without an Avalonia application ───

    [Fact]
    public void CaptureActiveWindow_NoApplication_ReturnsFailureWithReason()
    {
        var service = new ScreenshotService(_loggingService);

        var result = service.CaptureActiveWindow();

        Assert.False(result.Success);
        Assert.Null(result.FilePath);
        Assert.Equal("No active application.", result.ErrorMessage);
    }

    // ─── SaveScreenshotCore: primary location ───

    [Fact]
    public void SaveScreenshotCore_PrimaryWritable_SavesToPrimaryDirectory()
    {
        var primary = TempDirectory("primary");
        var fallback = TempDirectory("fallback");
        var service = new ScreenshotService(_loggingService);

        var result = service.SaveScreenshotCore(
            path => File.WriteAllBytes(path, [1, 2, 3]), primary, fallback);

        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
        Assert.NotNull(result.FilePath);
        Assert.StartsWith(primary, result.FilePath, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(result.FilePath));
    }

    [Fact]
    public void SaveScreenshotCore_PrimaryDirectoryMissing_IsCreated()
    {
        var primary = Path.Combine(_root, "created", "nested");
        var fallback = TempDirectory("fallback");
        var service = new ScreenshotService(_loggingService);

        var result = service.SaveScreenshotCore(
            path => File.WriteAllBytes(path, [1]), primary, fallback);

        Assert.True(result.Success);
        Assert.True(Directory.Exists(primary));
        Assert.True(File.Exists(result.FilePath));
    }

    [Fact]
    public void SaveScreenshotCore_WritesExactlyWhatTheCallbackWrites()
    {
        var primary = TempDirectory("primary");
        var fallback = TempDirectory("fallback");
        var service = new ScreenshotService(_loggingService);
        byte[] content = [10, 20, 30, 40];

        var result = service.SaveScreenshotCore(
            path => File.WriteAllBytes(path, content), primary, fallback);

        Assert.Equal(content, File.ReadAllBytes(result.FilePath!));
    }

    [Fact]
    public void SaveScreenshotCore_FileName_HasScreenshotPrefixAndPngExtension()
    {
        var primary = TempDirectory("primary");
        var fallback = TempDirectory("fallback");
        var service = new ScreenshotService(_loggingService);

        var result = service.SaveScreenshotCore(
            path => File.WriteAllBytes(path, [1]), primary, fallback);

        var fileName = Path.GetFileName(result.FilePath!);
        Assert.StartsWith("Screenshot_", fileName, StringComparison.Ordinal);
        Assert.EndsWith(".png", fileName, StringComparison.Ordinal);
    }

    [Fact]
    public void SaveScreenshotCore_PrimarySuccess_LogsSavedPath()
    {
        var primary = TempDirectory("primary");
        var fallback = TempDirectory("fallback");
        var service = new ScreenshotService(_loggingService);

        var result = service.SaveScreenshotCore(
            path => File.WriteAllBytes(path, [1]), primary, fallback);

        Assert.Contains(_loggingService.LogEntries,
            entry => entry.Message.Contains(result.FilePath!, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(_loggingService.LogEntries, static entry => entry.IsError);
    }

    // ─── SaveScreenshotCore: fallback location ───

    [Fact]
    public void SaveScreenshotCore_PrimarySaveThrows_SavesToFallbackDirectory()
    {
        var primary = TempDirectory("primary");
        var fallback = TempDirectory("fallback");
        var service = new ScreenshotService(_loggingService);

        var result = service.SaveScreenshotCore(
            path =>
            {
                if (path.StartsWith(primary, StringComparison.OrdinalIgnoreCase))
                    throw new UnauthorizedAccessException("Access denied.");

                File.WriteAllBytes(path, [7]);
            }, primary, fallback);

        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
        Assert.NotNull(result.FilePath);
        Assert.StartsWith(fallback, result.FilePath, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(result.FilePath));
    }

    [Fact]
    public void SaveScreenshotCore_PrimaryDirectoryBlockedByFile_FallsBack()
    {
        // A file where the primary directory should be makes Directory.CreateDirectory throw.
        var blocker = TempFile("blocker");
        var primary = Path.Combine(blocker, "Screenshot");
        var fallback = TempDirectory("fallback");
        var service = new ScreenshotService(_loggingService);

        var result = service.SaveScreenshotCore(
            path => File.WriteAllBytes(path, [7]), primary, fallback);

        Assert.True(result.Success);
        Assert.StartsWith(fallback, result.FilePath!, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(result.FilePath));
    }

    [Fact]
    public void SaveScreenshotCore_FallbackDirectoryMissing_IsCreated()
    {
        var primary = Path.Combine(_root, "missing-primary");
        var fallback = Path.Combine(_root, "missing-fallback", "nested");
        var service = new ScreenshotService(_loggingService);

        var result = service.SaveScreenshotCore(
            path =>
            {
                if (path.StartsWith(primary, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Primary write failed.");

                File.WriteAllBytes(path, [9]);
            }, primary, fallback);

        Assert.True(result.Success);
        Assert.True(Directory.Exists(fallback));
        Assert.True(File.Exists(result.FilePath));
    }

    [Fact]
    public void SaveScreenshotCore_PrimaryFailure_LogsErrorAndFallbackSuccess()
    {
        var primary = TempDirectory("primary");
        var fallback = TempDirectory("fallback");
        var service = new ScreenshotService(_loggingService);

        service.SaveScreenshotCore(
            path =>
            {
                if (path.StartsWith(primary, StringComparison.OrdinalIgnoreCase))
                    throw new UnauthorizedAccessException("Access denied.");

                File.WriteAllBytes(path, [7]);
            }, primary, fallback);

        Assert.Contains(_loggingService.LogEntries, static entry =>
            entry.IsError && entry.Message.Contains("Failed to save screenshot", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(_loggingService.LogEntries, static entry =>
            entry.IsError && entry.Message.Contains("Trying fallback location", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(_loggingService.LogEntries, static entry =>
            !entry.IsError && entry.Message.Contains("fallback location", StringComparison.OrdinalIgnoreCase));
    }

    // ─── SaveScreenshotCore: total failure ───

    [Fact]
    public void SaveScreenshotCore_BothLocationsFail_ReturnsFailureWithNullPath()
    {
        var primary = TempDirectory("primary");
        var fallback = TempDirectory("fallback");
        var service = new ScreenshotService(_loggingService);

        var result = service.SaveScreenshotCore(
            path => throw new UnauthorizedAccessException("Denied."), primary, fallback);

        Assert.False(result.Success);
        Assert.Null(result.FilePath);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    [Fact]
    public void SaveScreenshotCore_BothDirectoriesBlockedByFiles_ReturnsFailure()
    {
        var primary = Path.Combine(TempFile("primary-blocker"), "Screenshot");
        var fallback = Path.Combine(TempFile("fallback-blocker"), "Screenshot");
        var service = new ScreenshotService(_loggingService);

        var result = service.SaveScreenshotCore(
            path => File.WriteAllBytes(path, [1]), primary, fallback);

        Assert.False(result.Success);
        Assert.Null(result.FilePath);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void SaveScreenshotCore_BothLocationsFail_LogsBothErrors()
    {
        var primary = TempDirectory("primary");
        var fallback = TempDirectory("fallback");
        var service = new ScreenshotService(_loggingService);

        service.SaveScreenshotCore(path => throw new IOException("Nope."), primary, fallback);

        Assert.Contains(_loggingService.LogEntries, entry =>
            entry.IsError && entry.Message.Contains($"'{primary}'", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(_loggingService.LogEntries, entry =>
            entry.IsError && entry.Message.Contains($"'{fallback}'", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SaveScreenshotCore_Failure_DoesNotLeavePartialFileInFallback()
    {
        var primary = TempDirectory("primary");
        var fallback = TempDirectory("fallback");
        var service = new ScreenshotService(_loggingService);

        var result = service.SaveScreenshotCore(
            path => throw new IOException("Nope."), primary, fallback);

        Assert.False(result.Success);
        Assert.Empty(Directory.GetFiles(fallback));
    }

    [Fact]
    public void SaveScreenshotCore_EmptyDirectories_ReturnsFailureWithoutThrowing()
    {
        var service = new ScreenshotService(_loggingService);

        var result = service.SaveScreenshotCore(static _ => { }, string.Empty, string.Empty);

        Assert.False(result.Success);
        Assert.Null(result.FilePath);
    }
}
