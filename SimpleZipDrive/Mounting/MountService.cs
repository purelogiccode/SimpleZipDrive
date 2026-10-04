using System.Diagnostics.CodeAnalysis;
using SimpleZipDrive.Mounting.Dokan;
using SimpleZipDrive.Mounting.Fuse;
using SimpleZipDrive.Mounting.WinFsp;

namespace SimpleZipDrive.Mounting;

/// <summary>
///     Facade that selects the configured mount backend (WinFsp or Dokan on Windows,
///     FUSE on Linux/macOS) and delegates all mount operations to it.
/// </summary>
public sealed class MountService : IDisposable, IMountService
{
    private readonly ILoggingService _loggingService;
    private readonly ISettingsService _settingsService;
    private readonly Lock _sync = new();
    private IMountService? _active;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MountService" /> class.
    /// </summary>
    /// <param name="loggingService">The logging service.</param>
    /// <param name="settingsService">The settings service.</param>
    public MountService(ILoggingService loggingService, ISettingsService settingsService)
    {
        _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    }

    /// <inheritdoc />
    public event EventHandler<MountStatusChangedEventArgs>? MountStatusChanged;

    /// <inheritdoc />
    public bool IsMounted => _active?.IsMounted ?? false;

    /// <inheritdoc />
    public string? CurrentMountPoint => _active?.CurrentMountPoint;

    /// <inheritdoc />
    public string? CurrentArchivePath => _active?.CurrentArchivePath;

    /// <summary>Gets the display name of the backend that was selected for the last mount attempt.</summary>
    public string? ActiveBackendName { get; private set; }

    /// <inheritdoc />
    [RequiresAssemblyFiles]
    public async Task MountAsync(string archivePath, string? mountPoint = null)
    {
        var backend = ResolveBackend();
        if (backend is null) return;

        IMountService? previous;
        lock (_sync)
        {
            previous = _active;
            _active = backend;
            ActiveBackendName = backend.GetType().Name;
        }

        if (previous is not null)
        {
            // Replace the previous backend cleanly: unmount it and release its event
            // subscription and resources before starting the new mount. Otherwise the old
            // mount stays active forever and its event keeps the facade alive.
            try
            {
                await previous.UnmountAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ErrorLoggerStatic.ReportSilentException(ex,
                    "MountService.MountAsync: Failed to unmount the previous backend", true);
            }

            previous.MountStatusChanged -= OnBackendMountStatusChanged;

            try
            {
                (previous as IDisposable)?.Dispose();
            }
            catch (Exception ex)
            {
                ErrorLoggerStatic.ReportSilentException(ex,
                    "MountService.MountAsync: Failed to dispose the previous backend", true);
            }
        }

        backend.MountStatusChanged += OnBackendMountStatusChanged;

        try
        {
            await backend.MountAsync(archivePath, mountPoint).ConfigureAwait(false);
        }
        catch
        {
            // A synchronously failing mount must not leave a failed backend attached.
            DetachBackend(backend);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task UnmountAsync()
    {
        IMountService? backend;
        lock (_sync)
        {
            backend = _active;
        }

        if (backend is null) return;

        try
        {
            await backend.UnmountAsync();
        }
        finally
        {
            DetachBackend(backend);
        }
    }

    /// <inheritdoc />
    public string GetArchiveType(string filePath)
    {
        return ArchiveFormats.GetArchiveType(filePath);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        IMountService? backend;
        lock (_sync)
        {
            backend = _active;
            _active = null;
        }

        if (backend is null) return;

        try
        {
            backend.MountStatusChanged -= OnBackendMountStatusChanged;
            (backend as IDisposable)?.Dispose();
        }
        catch (Exception ex)
        {
            ErrorLoggerStatic.ReportSilentException(ex, "MountService.Dispose: Failed to dispose the backend", true);
        }
    }

    private IMountService? ResolveBackend()
    {
        var requested = _settingsService.Settings.MountBackend;

        if (requested == MountBackend.Auto)
        {
            if (OperatingSystem.IsWindows())
            {
                requested = WinFspMountService.IsAvailable(out _)
                    ? MountBackend.WinFsp
                    : MountBackend.Dokan;
                _loggingService.Log($"Mount backend: auto-selected {requested}.");
            }
            else
            {
                requested = MountBackend.Fuse;
                _loggingService.Log("Mount backend: FUSE (only backend available on this platform).");
            }
        }

        switch (requested)
        {
            case MountBackend.WinFsp:
                if (!OperatingSystem.IsWindows())
                {
                    ShowUnavailable("WinFsp", "WinFsp is only available on Windows.");
                    return null;
                }

                if (!WinFspMountService.IsAvailable(out var winFspReason))
                {
                    ShowUnavailable("WinFsp", winFspReason);
                    return null;
                }

                return new WinFspMountService(_loggingService, _settingsService);

            case MountBackend.Dokan:
                if (!OperatingSystem.IsWindows())
                {
                    ShowUnavailable("Dokan", "Dokan is only available on Windows.");
                    return null;
                }

                if (!DokanMountService.IsAvailable(out var dokanReason))
                {
                    ShowUnavailable("Dokan", dokanReason);
                    return null;
                }

                return new DokanMountService(_loggingService, _settingsService);

            case MountBackend.Fuse:
                if (!FuseMountService.IsAvailable(out var fuseReason))
                {
                    ShowUnavailable("FUSE", fuseReason);
                    return null;
                }

                return new FuseMountService(_loggingService, _settingsService);

            default:
                _loggingService.LogError($"Unknown mount backend '{requested}'. Unable to mount archive.");
                return null;
        }
    }

    private void ShowUnavailable(string backendName, string? reason)
    {
        _loggingService.LogError($"{backendName} is not available: {reason} Unable to mount archive.");
        MessageBox.Show(
            $"{backendName} is not available on this system.\n\n{reason}\n\n" +
            "Choose a different mount backend in Settings and try again.",
            $"{backendName} Not Available", MessageBoxButton.Ok, MessageBoxImage.Warning);
    }

    private void OnBackendMountStatusChanged(object? sender, MountStatusChangedEventArgs e)
    {
        MountStatusChanged?.Invoke(this, e);
    }

    private void DetachBackend(IMountService backend)
    {
        lock (_sync)
        {
            if (ReferenceEquals(_active, backend)) _active = null;
        }

        backend.MountStatusChanged -= OnBackendMountStatusChanged;
        (backend as IDisposable)?.Dispose();
    }
}
