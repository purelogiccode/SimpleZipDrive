using SimpleZipDrive.Core.Models;
using SimpleZipDrive.Mounting;

namespace SimpleZipDrive.Tests;

/// <summary>
///     Tests for <see cref="MountBackendAvailability" />: effective-backend resolution, the
///     availability probe, and the user-facing names and download pages used by the startup
///     driver warning.
/// </summary>
public class MountBackendAvailabilityTests
{
    [Theory]
    [InlineData(MountBackend.Dokan)]
    [InlineData(MountBackend.WinFsp)]
    [InlineData(MountBackend.Fuse)]
    public void ResolveEffective_ExplicitBackend_IsReturnedUnchanged(MountBackend backend)
    {
        Assert.Equal(backend, MountBackendAvailability.ResolveEffective(backend));
    }

    [Fact]
    public void ResolveEffective_Auto_MatchesPlatformPreference()
    {
        var resolved = MountBackendAvailability.ResolveEffective(MountBackend.Auto);

        if (OperatingSystem.IsWindows())
        {
            Assert.Contains(resolved, new[] { MountBackend.Dokan, MountBackend.WinFsp });
        }
        else
        {
            Assert.Equal(MountBackend.Fuse, resolved);
        }
    }

    [Fact]
    public void IsAvailable_FuseOnWindows_ReturnsFalseWithReason()
    {
        if (!OperatingSystem.IsWindows()) return;

        Assert.False(MountBackendAvailability.IsAvailable(MountBackend.Fuse, out var reason));
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    [Fact]
    public void IsAvailable_UnknownBackend_ReturnsFalseWithReason()
    {
        Assert.False(MountBackendAvailability.IsAvailable((MountBackend)99, out var reason));
        Assert.Contains("Unknown mount backend", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(MountBackend.Dokan, "Dokan")]
    [InlineData(MountBackend.WinFsp, "WinFsp")]
    [InlineData(MountBackend.Fuse, "FUSE")]
    public void DisplayName_ReturnsExpectedName(MountBackend backend, string expected)
    {
        Assert.Equal(expected, MountBackendAvailability.DisplayName(backend));
    }

    [Theory]
    [InlineData(MountBackend.Dokan, "Dokan driver")]
    [InlineData(MountBackend.WinFsp, "WinFsp driver")]
    public void DriverName_WindowsDrivers_ReturnsExpectedName(MountBackend backend, string expected)
    {
        Assert.Equal(expected, MountBackendAvailability.DriverName(backend));
    }

    [Fact]
    public void DriverName_Fuse_MatchesPlatform()
    {
        var expected = OperatingSystem.IsMacOS() ? "macFUSE" : "libfuse3";
        Assert.Equal(expected, MountBackendAvailability.DriverName(MountBackend.Fuse));
    }

    [Theory]
    [InlineData(MountBackend.Dokan, "https://github.com/dokan-dev/dokany/releases")]
    [InlineData(MountBackend.WinFsp, "https://github.com/winfsp/winfsp/releases")]
    public void DownloadUrl_WindowsDrivers_ReturnsOfficialPage(MountBackend backend, string expected)
    {
        Assert.Equal(expected, MountBackendAvailability.DownloadUrl(backend));
    }

    [Fact]
    public void DownloadUrl_Fuse_MatchesPlatform()
    {
        var url = MountBackendAvailability.DownloadUrl(MountBackend.Fuse);

        if (OperatingSystem.IsMacOS())
            Assert.Equal("https://macfuse.github.io/", url);
        else
            Assert.Contains("installation", url, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DefaultMountBackend_IsDokanOnWindowsAndAutoElsewhere()
    {
        var expected = OperatingSystem.IsWindows() ? MountBackend.Dokan : MountBackend.Auto;
        Assert.Equal(expected, AppSettings.DefaultMountBackend);
    }

    [Fact]
    public void NormalizeMountBackend_PlatformInvalidValue_IsMappedToAvailableBackend()
    {
        if (OperatingSystem.IsWindows())
        {
            // FUSE is not offered on Windows; it falls back to the Windows default (Dokan).
            Assert.Equal(AppSettings.DefaultMountBackend, AppSettings.NormalizeMountBackend(MountBackend.Fuse));
            Assert.Equal(MountBackend.Dokan, AppSettings.NormalizeMountBackend(MountBackend.Dokan));
            Assert.Equal(MountBackend.WinFsp, AppSettings.NormalizeMountBackend(MountBackend.WinFsp));
            Assert.Equal(MountBackend.Auto, AppSettings.NormalizeMountBackend(MountBackend.Auto));
        }
        else
        {
            // Dokan/WinFsp are not offered on Linux/macOS; they fall back to FUSE.
            Assert.Equal(MountBackend.Fuse, AppSettings.NormalizeMountBackend(MountBackend.Dokan));
            Assert.Equal(MountBackend.Fuse, AppSettings.NormalizeMountBackend(MountBackend.WinFsp));
            Assert.Equal(MountBackend.Fuse, AppSettings.NormalizeMountBackend(MountBackend.Fuse));
            Assert.Equal(MountBackend.Auto, AppSettings.NormalizeMountBackend(MountBackend.Auto));
        }
    }
}
