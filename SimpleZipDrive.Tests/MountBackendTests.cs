using System.Text.Json;
using SimpleZipDrive.Core.Interfaces;
using SimpleZipDrive.Core.Models;
using SimpleZipDrive.Mounting.Dokan;
using SimpleZipDrive.Mounting.Fuse;
using SimpleZipDrive.Mounting.WinFsp;
using FacadeMountService = SimpleZipDrive.Mounting.MountService;

namespace SimpleZipDrive.Tests;

/// <summary>
///     Tests for the mount-backend facade and backend availability probes.
/// </summary>
public class MountBackendTests
{
    private readonly FakeLoggingService _loggingService = new();
    private readonly FakeSettingsService _settingsService = new();

    [Fact]
    public void Facade_Constructor_NullLogging_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new FacadeMountService(null!, _settingsService));
    }

    [Fact]
    public void Facade_Constructor_NullSettings_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new FacadeMountService(_loggingService, null!));
    }

    [Fact]
    public void Facade_InitiallyNotMounted()
    {
        using var service = new FacadeMountService(_loggingService, _settingsService);

        Assert.False(service.IsMounted);
        Assert.Null(service.CurrentMountPoint);
        Assert.Null(service.CurrentArchivePath);
        Assert.Null(service.ActiveBackendName);
    }

    [Fact]
    public void Facade_GetArchiveType_DelegatesToArchiveFormats()
    {
        using var service = new FacadeMountService(_loggingService, _settingsService);

        Assert.Equal("zip", service.GetArchiveType("archive.zip"));
        Assert.Equal("7z", service.GetArchiveType("archive.7z"));
        Assert.Equal("rar", service.GetArchiveType("archive.cbr"));
    }

    [Fact]
    public async Task Facade_UnmountWithoutMount_DoesNotThrow()
    {
        using var service = new FacadeMountService(_loggingService, _settingsService);

        var exception = await Record.ExceptionAsync(service.UnmountAsync);

        Assert.Null(exception);
    }

    [Fact]
    public void AppSettings_MountBackend_DefaultsToAuto()
    {
        Assert.Equal(MountBackend.Auto, new AppSettings().MountBackend);
    }

    [Fact]
    public void AppSettings_MountBackend_RoundTripsThroughJson()
    {
        var settings = new AppSettings { MountBackend = MountBackend.WinFsp };

        var json = JsonSerializer.Serialize(settings);
        var restored = JsonSerializer.Deserialize<AppSettings>(json);

        Assert.NotNull(restored);
        Assert.Equal(MountBackend.WinFsp, restored.MountBackend);
    }

    [Fact]
    public void DokanBackend_IsAvailable_DoesNotThrow()
    {
        var exception = Record.Exception(() => DokanMountService.IsAvailable(out _));

        Assert.Null(exception);
    }

    [Fact]
    public void WinFspBackend_IsAvailable_DoesNotThrow()
    {
        var exception = Record.Exception(() => WinFspMountService.IsAvailable(out _));

        Assert.Null(exception);
    }

    [Fact]
    public void FuseBackend_OnWindows_IsUnavailableWithReason()
    {
        if (!OperatingSystem.IsWindows()) return;

        var available = FuseMountService.IsAvailable(out var reason);

        Assert.False(available);
        Assert.NotNull(reason);
    }

    private sealed class FakeLoggingService : ILoggingService
    {
        public System.Collections.ObjectModel.ObservableCollection<LogEntry> LogEntries { get; } = [];

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
        public AppSettings Settings { get; private set; } = new();

        public void SaveSettings()
        {
        }

        public void ReloadSettings()
        {
        }

        public void UpdateRamLimit(int megabytes)
        {
        }
    }
}
