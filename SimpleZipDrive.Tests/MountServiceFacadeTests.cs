using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using SimpleZipDrive.Core.Interfaces;
using SimpleZipDrive.Core.Models;
using SimpleZipDrive.Mounting;

namespace SimpleZipDrive.Tests;

/// <summary>
///     Tests for the <see cref="MountService" /> facade that do not require a mounted backend
///     or a UI thread (construction, initial state, no-op unmount/dispose, format delegation).
/// </summary>
[SuppressMessage("ReSharper", "NullableWarningSuppressionIsUsed")]
public class MountServiceFacadeTests
{
    private readonly FakeLoggingService _loggingService = new();
    private readonly FakeSettingsService _settingsService = new();

    [Fact]
    public void Constructor_NullLoggingService_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new MountService(null!, _settingsService));
    }

    [Fact]
    public void Constructor_NullSettingsService_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new MountService(_loggingService, null!));
    }

    [Fact]
    public void InitialState_IsNotMountedAndHasNoPaths()
    {
        using var service = new MountService(_loggingService, _settingsService);

        Assert.False(service.IsMounted);
        Assert.Null(service.CurrentMountPoint);
        Assert.Null(service.CurrentArchivePath);
        Assert.Null(service.ActiveBackendName);
    }

    [Fact]
    public async Task UnmountAsync_NoActiveBackend_CompletesWithoutThrowing()
    {
        using var service = new MountService(_loggingService, _settingsService);

        var ex = await Record.ExceptionAsync(service.UnmountAsync);

        Assert.Null(ex);
        Assert.False(service.IsMounted);
    }

    [Fact]
    public void Dispose_NoActiveBackend_DoesNotThrow()
    {
        var service = new MountService(_loggingService, _settingsService);

        var ex = Record.Exception(service.Dispose);

        Assert.Null(ex);
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var service = new MountService(_loggingService, _settingsService);

        var ex = Record.Exception(() =>
        {
            service.Dispose();
            service.Dispose();
        });

        Assert.Null(ex);
    }

    [Theory]
    [InlineData("archive.zip", "zip")]
    [InlineData("archive.7z", "7z")]
    [InlineData("archive.rar", "rar")]
    [InlineData("archive.tar.gz", "tar")]
    [InlineData("archive.cbz", "zip")]
    public void GetArchiveType_DelegatesToArchiveFormats(string filePath, string expected)
    {
        using var service = new MountService(_loggingService, _settingsService);

        Assert.Equal(expected, service.GetArchiveType(filePath));
    }

    private sealed class FakeLoggingService : ILoggingService
    {
        public ObservableCollection<LogEntry> LogEntries { get; } = [];

        public void Log(string message)
        {
        }

        public void LogError(string message)
        {
        }

        public void Clear()
        {
        }

        public string GetAllLogsAsText()
        {
            return string.Empty;
        }
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        public AppSettings Settings { get; } = new();

        public void SaveSettings()
        {
        }

        public void ReloadSettings()
        {
        }

        public void UpdateRamLimit(int maxMemoryPerFileMb)
        {
            if (maxMemoryPerFileMb > 0) Settings.MaxMemoryPerFileMb = maxMemoryPerFileMb;
        }
    }
}
