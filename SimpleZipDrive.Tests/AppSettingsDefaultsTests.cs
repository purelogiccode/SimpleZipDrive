using SimpleZipDrive.Core.Models;

namespace SimpleZipDrive.Tests;

/// <summary>
///     Additional default-value and clamping tests for <see cref="AppSettings" /> that do not
///     touch the on-disk settings file.
/// </summary>
[Collection("Settings file")]
public class AppSettingsDefaultsTests
{
    [Fact]
    public void MountBackend_Default_IsAuto()
    {
        Assert.Equal(MountBackend.Auto, new AppSettings().MountBackend);
    }

    [Fact]
    public void CrossIntegrityMount_Default_IsFalse()
    {
        Assert.False(new AppSettings().CrossIntegrityMount);
    }

    [Fact]
    public void CrossIntegrityMountFolder_Default_IsEmpty()
    {
        Assert.Equal(string.Empty, new AppSettings().CrossIntegrityMountFolder);
    }

    [Fact]
    public void MaxMemoryPerFileBytes_Default_Is512Megabytes()
    {
        Assert.Equal(512L * 1024 * 1024, new AppSettings().MaxMemoryPerFileBytes);
    }

    [Fact]
    public void MaxMemoryPerFileMb_ExactlySystemLimit_IsAccepted()
    {
        var availableMemoryBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        var availableMemoryMb = availableMemoryBytes / 1024 / 1024;
        var maxAllowedMb = (long)(availableMemoryMb * 0.9);

        var settings = new AppSettings { MaxMemoryPerFileMb = maxAllowedMb };

        Assert.Equal(maxAllowedMb, settings.MaxMemoryPerFileMb);
    }

    [Fact]
    public void MaxMemoryPerFileBytes_TracksPropertyChanges()
    {
        var settings = new AppSettings { MaxMemoryPerFileMb = 123 };

        Assert.Equal(123L * 1024 * 1024, settings.MaxMemoryPerFileBytes);
    }

    [Fact]
    public void Load_ReturnsSettingsWithValidatedMemoryLimit()
    {
        var settings = AppSettings.Load();

        Assert.NotNull(settings);
        Assert.True(settings.MaxMemoryPerFileMb >= 1);
        Assert.Equal(settings.MaxMemoryPerFileMb * 1024L * 1024L, settings.MaxMemoryPerFileBytes);
    }

    [Fact]
    public void MountBackend_EnumValues_AreStable()
    {
        Assert.Equal(0, (int)MountBackend.Auto);
        Assert.Equal(1, (int)MountBackend.Dokan);
        Assert.Equal(2, (int)MountBackend.WinFsp);
        Assert.Equal(3, (int)MountBackend.Fuse);
    }
}
