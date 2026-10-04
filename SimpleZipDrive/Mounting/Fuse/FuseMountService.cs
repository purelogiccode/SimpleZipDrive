using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using SimpleZipDrive.FuseSharp;

namespace SimpleZipDrive.Mounting.Fuse;

/// <summary>
///     Mount service implementation backed by FUSE (libfuse3 on Linux, macFUSE on macOS).
///     The FUSE session runs on a dedicated background thread; <see cref="UnmountAsync" />
///     requests a clean exit through the vendored FuseSharp library.
/// </summary>
public sealed class FuseMountService : IDisposable, IMountService
{
    private readonly ILoggingService _loggingService;
    private readonly ISettingsService _settingsService;
    private readonly Lock _sync = new();

    private ZipFileSystemCore? _core;
    private FuseFileSystem? _fileSystem;
    private Thread? _mountThread;
    private string? _mountPoint;
    private string? _tempMountPoint;
    private int _cleanedUp;

    /// <summary>
    ///     Initializes a new instance of the <see cref="FuseMountService" /> class.
    /// </summary>
    /// <param name="loggingService">The logging service used to record mount activity.</param>
    /// <param name="settingsService">The settings service that supplies mount preferences.</param>
    public FuseMountService(ILoggingService loggingService, ISettingsService settingsService)
    {
        _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    }

    /// <inheritdoc />
    public event EventHandler<MountStatusChangedEventArgs>? MountStatusChanged;

    /// <inheritdoc />
    public bool IsMounted { get; private set; }

    /// <inheritdoc />
    public string? CurrentMountPoint { get; private set; }

    /// <inheritdoc />
    public string? CurrentArchivePath { get; private set; }

    /// <summary>
    ///     Determines whether the FUSE backend can be used on this machine (Linux/macOS with
    ///     libfuse3 or macFUSE available).
    /// </summary>
    /// <param name="reason">When unavailable, a human-readable explanation; otherwise <see langword="null" />.</param>
    public static bool IsAvailable(out string? reason)
    {
        if (OperatingSystem.IsWindows())
        {
            reason = "FUSE is only available on Linux and macOS.";
            return false;
        }

        if (!FuseAvailability.Check(out _))
        {
            reason = "The FUSE runtime library (libfuse3 on Linux, macFUSE on macOS) is not available.";
            return false;
        }

        reason = null;
        return true;
    }

    /// <inheritdoc />
    [RequiresAssemblyFiles]
    public Task MountAsync(string archivePath, string? mountPoint = null)
    {
        if (IsMounted) throw new InvalidOperationException("A drive is already mounted. Please unmount it first.");

        if (!File.Exists(archivePath))
        {
            _loggingService.Log($"Error: Archive file not found at '{archivePath}'.");
            throw new FileNotFoundException($"Archive file not found at '{archivePath}'.", archivePath);
        }

        if (!ArchiveFormats.IsSupportedArchive(archivePath))
        {
            _loggingService.Log($"\n{AppTheme.Section("INVALID FILE TYPE")}");
            _loggingService.Log($"Error: The file '{Path.GetFileName(archivePath)}' is not a supported archive.");
            throw new ArgumentException(
                $"The file '{Path.GetFileName(archivePath)}' is not a supported archive format (expected {ArchiveFormats.SupportedExtensionsDescription}).",
                nameof(archivePath));
        }

        if (!IsAvailable(out var reason))
        {
            _loggingService.LogError($"{reason} Unable to mount archive.");
            MessageBox.Show(
                $"{reason}\n\nOn Linux install libfuse3 (e.g. 'sudo apt install libfuse3-3'), " +
                "on macOS install macFUSE from https://macfuse.github.io/.",
                "FUSE Not Available", MessageBoxButton.Ok, MessageBoxImage.Warning);
            return Task.CompletedTask;
        }

        var archiveType = GetArchiveType(archivePath);
        var isTempMountPoint = string.IsNullOrEmpty(mountPoint);
        var resolvedMountPoint = isTempMountPoint ? CreateTempMountPoint() : Path.GetFullPath(mountPoint!);

        try
        {
            if (!isTempMountPoint) Directory.CreateDirectory(resolvedMountPoint);
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Error: Failed to create mount directory '{resolvedMountPoint}'. {ex.Message}");
            throw;
        }

        Interlocked.Exchange(ref _cleanedUp, 0);

        var effectiveMaxMemoryBytes = _settingsService.Settings.MaxMemoryPerFileBytes;
        var volumeLabel =
            ZipFsHelpers.SanitizeVolumeLabel(ZipFsHelpers.GetArchiveFileNameWithoutExtension(archivePath));

        try
        {
            var fileStream = OpenArchiveFileStream(archivePath);
            _core = new ZipFileSystemCore(
                fileStream,
                resolvedMountPoint,
                ErrorLoggerStatic.LogErrorSync,
                () => PromptForPassword(archivePath, archiveType),
                archiveType,
                effectiveMaxMemoryBytes,
                volumeLabel);
            _fileSystem = new FuseFileSystem(new FuseVolumeAdapter(_core));
        }
        catch
        {
            CleanupAfterUnmount();
            throw;
        }

        lock (_sync)
        {
            _mountPoint = resolvedMountPoint;
            _tempMountPoint = isTempMountPoint ? resolvedMountPoint : null;
        }

        CurrentArchivePath = archivePath;

        _loggingService.Log(
            $"Processing {archiveType.ToUpperInvariant()} file: '{archivePath}', Size: {new FileInfo(archivePath).Length / 1024.0 / 1024.0:F2} MB");
        _loggingService.Log(
            $"RAM cache limit: {effectiveMaxMemoryBytes / 1024.0 / 1024.0:F0} MB (Available system memory: {GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024.0 / 1024.0:F0} MB)");
        _loggingService.Log("");

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fileSystem = _fileSystem;

        var thread = new Thread(() =>
        {
            try
            {
                var result = fileSystem.Run(resolvedMountPoint, debug: false, OnMounted);
                if (result != 0)
                    _loggingService.LogError($"FUSE mount failed for '{resolvedMountPoint}' (exit code {result}).");
            }
            catch (Exception ex)
            {
                ErrorLoggerStatic.ReportSilentException(ex,
                    $"FuseMountService: FUSE session error for '{archivePath}' on '{resolvedMountPoint}'", true);
                _loggingService.LogError($"Mount error: {ex.Message}");
            }
            finally
            {
                CleanupAfterUnmount();
                completion.TrySetResult();
            }
        })
        {
            IsBackground = true,
            Name = "SimpleZipDrive FUSE"
        };

        lock (_sync)
        {
            _mountThread = thread;
        }

        thread.Start();
        return completion.Task;
    }

    /// <inheritdoc />
    public async Task UnmountAsync()
    {
        if (!IsMounted) return;

        try
        {
            _loggingService.Log("Unmounting drive...");
            IsMounted = false;

            _fileSystem?.Stop();

            var thread = _mountThread;
            if (thread is { IsAlive: true })
            {
                var exited = await Task.Run(() => thread.Join(TimeSpan.FromSeconds(5)));
                if (!exited)
                {
                    _loggingService.Log("FUSE session did not exit in time; requesting an external unmount...");
                    TryExternalUnmount(CurrentMountPoint ?? _mountPoint);
                }
            }

            CleanupAfterUnmount();

            _loggingService.Log("Drive unmounted successfully.");
            OnMountStatusChanged();
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Error unmounting drive: {ex.Message}");
            ErrorLoggerStatic.LogErrorSync(ex, "FuseMountService.UnmountAsync: Error unmounting drive");
            throw;
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
        try
        {
            _fileSystem?.Stop();
            var thread = _mountThread;
            if (thread is { IsAlive: true }) thread.Join(TimeSpan.FromSeconds(2));
            CleanupAfterUnmount();
        }
        catch (Exception ex)
        {
            ErrorLoggerStatic.ReportSilentException(ex, "FuseMountService.Dispose failed", true);
        }
    }

    private void OnMounted()
    {
        IsMounted = true;
        CurrentMountPoint = _mountPoint;

        _loggingService.Log($"Successfully mounted on '{_mountPoint}'.");
        _loggingService.Log("");
        _loggingService.Log("Use the Unmount button or close the window to unmount.");
        _loggingService.Log("");
        OnMountStatusChanged();
    }

    private void CleanupAfterUnmount()
    {
        if (Interlocked.Exchange(ref _cleanedUp, 1) != 0) return;

        IsMounted = false;
        CurrentMountPoint = null;
        CurrentArchivePath = null;

        string? tempMountPoint;
        lock (_sync)
        {
            tempMountPoint = _tempMountPoint;
            _tempMountPoint = null;
            _mountPoint = null;
            _mountThread = null;
            _fileSystem = null;
        }

        try
        {
            _core?.Dispose();
        }
        catch (Exception ex)
        {
            ErrorLoggerStatic.ReportSilentException(ex, "FuseMountService: Failed to dispose the archive core", true);
        }

        _core = null;

        if (tempMountPoint != null)
        {
            try
            {
                if (Directory.Exists(tempMountPoint) && !Directory.EnumerateFileSystemEntries(tempMountPoint).Any())
                    Directory.Delete(tempMountPoint);
            }
            catch (Exception ex)
            {
                ErrorLoggerStatic.ReportSilentException(ex,
                    $"FuseMountService: Failed to remove temporary mount point '{tempMountPoint}'", true);
            }
        }
    }

    private static void TryExternalUnmount(string? mountPoint)
    {
        if (string.IsNullOrEmpty(mountPoint)) return;

        try
        {
            var fileName = OperatingSystem.IsMacOS() ? "umount" : "fusermount3";
            var arguments = OperatingSystem.IsMacOS() ? $"\"{mountPoint}\"" : $"-u \"{mountPoint}\"";
            Process.Start(new ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch (Exception ex)
        {
            ErrorLoggerStatic.ReportSilentException(ex,
                $"FuseMountService: External unmount failed for '{mountPoint}'", true);
        }
    }

    private static string CreateTempMountPoint()
    {
        var path = Path.Combine(Path.GetTempPath(),
            $"simplezipdrive-{Environment.ProcessId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static FileStream OpenArchiveFileStream(string archivePath)
    {
        const int maxAttempts = 3;

        for (var attempt = 1;; attempt++)
        {
            try
            {
                return new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                Thread.Sleep(500 * attempt);
            }
        }
    }

    private static string? PromptForPassword(string archivePath, string archiveType)
    {
        return ServiceProvider.TryGet<IPasswordPromptService>()?.Prompt(archivePath, archiveType);
    }

    private void OnMountStatusChanged()
    {
        MountStatusChanged?.Invoke(this, new MountStatusChangedEventArgs
        {
            IsMounted = IsMounted,
            MountPoint = CurrentMountPoint,
            ArchivePath = CurrentArchivePath
        });
    }
}
