using SimpleZipDrive.Mounting.Dokan;
using SimpleZipDrive.Mounting.Fuse;
using SimpleZipDrive.Mounting.WinFsp;

namespace SimpleZipDrive.Mounting;

/// <summary>
///     Resolves the effective mount backend for the current platform, reports whether its
///     driver is installed, and supplies the download/installation page to offer the user.
/// </summary>
internal static class MountBackendAvailability
{
    private const string DokanDownloadUrl = "https://github.com/dokan-dev/dokany/releases";
    private const string WinFspDownloadUrl = "https://github.com/winfsp/winfsp/releases";
    private const string MacFuseDownloadUrl = "https://macfuse.github.io/";
    private const string FuseInstallInstructionsUrl =
        "https://github.com/purelogiccode/SimpleZipDrive/blob/master/docs/installation.md";

    /// <summary>
    ///     Resolves <see cref="MountBackend.Auto" /> to the platform's preferred backend
    ///     (WinFsp → Dokan on Windows, FUSE elsewhere) and returns every other value unchanged.
    /// </summary>
    /// <param name="requested">The backend selected in settings.</param>
    /// <returns>The backend that will actually be used.</returns>
    public static MountBackend ResolveEffective(MountBackend requested)
    {
        if (requested != MountBackend.Auto)
            return requested;

        if (!OperatingSystem.IsWindows())
            return MountBackend.Fuse;

        return WinFspMountService.IsAvailable(out _) ? MountBackend.WinFsp : MountBackend.Dokan;
    }

    /// <summary>
    ///     Determines whether the driver for the given backend is installed and usable.
    /// </summary>
    /// <param name="backend">The backend to check.</param>
    /// <param name="reason">When unavailable, a human-readable explanation; otherwise <see langword="null" />.</param>
    /// <returns><see langword="true" /> when the backend can be used.</returns>
    public static bool IsAvailable(MountBackend backend, out string? reason)
    {
        switch (backend)
        {
            case MountBackend.Dokan:
                if (!OperatingSystem.IsWindows())
                {
                    reason = "Dokan is only available on Windows.";
                    return false;
                }

                return DokanMountService.IsAvailable(out reason);

            case MountBackend.WinFsp:
                if (!OperatingSystem.IsWindows())
                {
                    reason = "WinFsp is only available on Windows.";
                    return false;
                }

                return WinFspMountService.IsAvailable(out reason);

            case MountBackend.Fuse:
                return FuseMountService.IsAvailable(out reason);

            default:
                reason = $"Unknown mount backend '{backend}'.";
                return false;
        }
    }

    /// <summary>Returns the user-facing name of a backend (e.g. "Dokan", "FUSE").</summary>
    /// <param name="backend">The backend.</param>
    /// <returns>The display name.</returns>
    public static string DisplayName(MountBackend backend)
    {
        return backend switch
        {
            MountBackend.Dokan => "Dokan",
            MountBackend.WinFsp => "WinFsp",
            MountBackend.Fuse => "FUSE",
            _ => backend.ToString()
        };
    }

    /// <summary>Returns the name of the driver a backend needs (e.g. "Dokan driver", "macFUSE").</summary>
    /// <param name="backend">The backend.</param>
    /// <returns>The driver name.</returns>
    public static string DriverName(MountBackend backend)
    {
        return backend switch
        {
            MountBackend.Dokan => "Dokan driver",
            MountBackend.WinFsp => "WinFsp driver",
            MountBackend.Fuse => OperatingSystem.IsMacOS() ? "macFUSE" : "libfuse3",
            _ => DisplayName(backend)
        };
    }

    /// <summary>Returns the download/installation page to offer when a backend is missing.</summary>
    /// <param name="backend">The backend.</param>
    /// <returns>The URL to open.</returns>
    public static string DownloadUrl(MountBackend backend)
    {
        return backend switch
        {
            MountBackend.Dokan => DokanDownloadUrl,
            MountBackend.WinFsp => WinFspDownloadUrl,
            MountBackend.Fuse => OperatingSystem.IsMacOS() ? MacFuseDownloadUrl : FuseInstallInstructionsUrl,
            _ => FuseInstallInstructionsUrl
        };
    }
}
