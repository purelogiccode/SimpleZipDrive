using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using DokanNet;
using DokanNet.Logging;
using FileAccess = System.IO.FileAccess;

namespace SimpleZipDrive.Services;

/// <summary>
///     Implementation of the mount service.
/// </summary>
public class MountService : IDisposable, IMountService
{
    private static bool _dokanArchitectureMismatch;

    /// <summary>
    ///     Minimum dokan2.dll library version required by DokanNet 2.3.0, which is the first
    ///     native release exporting <c>DokanRegisterWaitForFileSystemClosed</c>. Older Dokan
    ///     installations (2.2.x and earlier) still pass the <see cref="IsDokanInstalled" />
    ///     probe, but crash the process inside DokanNet with an uncatchable
    ///     <see cref="EntryPointNotFoundException" /> as soon as a file system instance is
    ///     created, so mounting must be refused up front.
    /// </summary>
    private const uint MinimumDokanLibraryVersion = 230;

    private readonly ILoggingService _loggingService;
    private readonly ISettingsService _settingsService;
    private ZipFs? _currentZipFs;
    private CancellationTokenSource? _mountCancellation;

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
    public void Dispose()
    {
        try
        {
            _mountCancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        // Give the driver time to finish pending callbacks before disposing resources
        Thread.Sleep(500);

        _mountCancellation?.Dispose();
        _currentZipFs?.Dispose();
        _currentZipFs = null;
        CurrentArchivePath = null;
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    public event EventHandler<MountStatusChangedEventArgs>? MountStatusChanged;

    /// <inheritdoc />
    public bool IsMounted { get; private set; }

    /// <inheritdoc />
    public string? CurrentMountPoint { get; private set; }

    /// <inheritdoc />
    public string? CurrentArchivePath { get; private set; }

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

        var archiveType = GetArchiveType(archivePath);

        if (!ArchiveFormats.IsSupportedArchive(archivePath))
        {
            _loggingService.Log($"\n{AppTheme.Section("INVALID FILE TYPE")}");
            _loggingService.Log($"Error: The file '{Path.GetFileName(archivePath)}' is not a supported archive.");
            throw new ArgumentException(
                $"The file '{Path.GetFileName(archivePath)}' is not a supported archive format (expected {ArchiveFormats.SupportedExtensionsDescription}).",
                nameof(archivePath));
        }

        if (!CheckForAdministratorRole.IsAdministrator())
        {
            _loggingService.Log("");
            _loggingService.Log("Warning: Running without Administrator privileges.");
            _loggingService.Log("Mounting to drive letters or certain paths may require elevated permissions.");
            _loggingService.Log("");
        }

        if (!IsDokanInstalled())
        {
            _loggingService.LogError("Dokan driver not found. Unable to mount archive.");
            ShowDokanNotInstalledDialog();
            return Task.CompletedTask;
        }

        if (!IsDokanLibraryVersionSupported(out var dokanLibraryVersion))
        {
            if (_dokanArchitectureMismatch)
            {
                _loggingService.LogError(
                    "Dokan driver DLL (dokan2.dll) could not be loaded into this process (architecture mismatch). Unable to mount archive.");
                ShowDokanNotInstalledDialog();
                return Task.CompletedTask;
            }

            _loggingService.LogError(
                $"Dokan driver is outdated (found version {FormatDokanVersion(dokanLibraryVersion)}, " +
                $"minimum required is {FormatDokanVersion(MinimumDokanLibraryVersion)}). Unable to mount archive.");
            ShowDokanOutdatedDialog(dokanLibraryVersion);
            return Task.CompletedTask;
        }

        ILogger logger = new DokanPrefixedLogger(AppTheme.DokanLogPrefix);
        CurrentArchivePath = archivePath;

        if (string.IsNullOrEmpty(mountPoint))
        {
            // Auto-select drive letter
            return MountWithAutoDriveLetterAsync(archivePath, archiveType, logger);
        }

        // Use specified mount point
        return MountWithSpecifiedPointAsync(archivePath, mountPoint, archiveType, logger);
    }

    /// <inheritdoc />
    public async Task UnmountAsync()
    {
        if (!IsMounted) return;

        try
        {
            _loggingService.Log("Unmounting drive...");
            var cts = _mountCancellation;
            try
            {
                cts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            IsMounted = false;

            try
            {
                if (cts != null) await Task.Delay(500, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Expected if cancellation completes before delay
            }

            _currentZipFs?.Dispose();
            _currentZipFs = null;

            CurrentMountPoint = null;
            CurrentArchivePath = null;

            _loggingService.Log("Drive unmounted successfully.");
            OnMountStatusChanged();
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Error unmounting drive: {ex.Message}");
            ErrorLoggerStatic.LogErrorSync(ex, "MountService.UnmountAsync: Error unmounting drive");
            throw;
        }
    }

    /// <inheritdoc />
    public string GetArchiveType(string filePath)
    {
        return ArchiveFormats.GetArchiveType(filePath);
    }

    [DllImport("dokan2.dll", ExactSpelling = true)]
    private static extern uint DokanVersion();

    private static bool IsDokanUnavailable(Exception ex)
    {
        switch (ex)
        {
            case DllNotFoundException or EntryPointNotFoundException:
                return true;
            case BadImageFormatException or TypeInitializationException:
                _dokanArchitectureMismatch = ex is not TypeInitializationException ||
                                             ex.InnerException is BadImageFormatException;
                return _dokanArchitectureMismatch;
            default:
                return false;
        }
    }

    /// <summary>
    ///     Opens the archive file for reading. Uses <see cref="FileShare.ReadWrite" /> so mounting
    ///     succeeds even when another process currently holds the archive open (e.g. antivirus,
    ///     download managers, torrent clients), with a short retry loop for transient sharing
    ///     violations.
    /// </summary>
    private static async Task<FileStream> OpenArchiveFileStreamAsync(string archivePath)
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
                // Backoff before retrying a transiently locked file; awaited so the UI thread
                // stays responsive while waiting.
                await Task.Delay(500 * attempt);
            }
        }
    }

    private static bool IsDokanInstalled()
    {
        try
        {
            return DokanVersion() > 0;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
        catch (BadImageFormatException)
        {
            // dokan2.dll exists but cannot be loaded into this process - typically an x64/x86
            // DLL on an ARM64 system (or vice versa). Treat as not installed so the user gets
            // actionable guidance instead of an unhandled BadImageFormatException.
            _dokanArchitectureMismatch = true;
            return false;
        }
    }

    private static bool IsDokanLibraryVersionSupported(out uint version)
    {
        version = 0;
        try
        {
            version = DokanVersion();
            return version >= MinimumDokanLibraryVersion;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
        catch (BadImageFormatException)
        {
            _dokanArchitectureMismatch = true;
            return false;
        }
    }

    /// <summary>
    ///     Formats a Dokan version number (e.g. 230) as a dotted version string ("2.3.0").
    /// </summary>
    private static string FormatDokanVersion(uint version)
    {
        return $"{version / 100}.{version % 100 / 10}.{version % 10}";
    }

    private static void ShowDokanOutdatedDialog(uint foundVersion)
    {
        var message = "The installed Dokan file system driver (dokan2.dll) is too old for this application.\n\n" +
                      $"Installed version: {FormatDokanVersion(foundVersion)}\n" +
                      $"Required version : {FormatDokanVersion(MinimumDokanLibraryVersion)} or newer\n\n" +
                      "Please update to the latest Dokan release and try again.\n\n" +
                      "Would you like to open the Dokan download page?";

        var result = MessageBox.Show(message, "Dokan Driver Outdated",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/dokan-dev/dokany/releases",
                UseShellExecute = true
            });
        }
    }

    private static void ShowDokanNotInstalledDialog()
    {
        string message;
        string title;

        if (_dokanArchitectureMismatch)
        {
            title = "Dokan Driver Incompatible";
            message = "The Dokan file system driver (dokan2.dll) was found but could not be loaded " +
                      $"into this process ({RuntimeInformation.ProcessArchitecture}). This usually means the installed " +
                      "Dokan driver does not support this system architecture (e.g. an x64 driver on an ARM64 device).\n\n" +
                      "Please install the latest Dokan release and verify it supports your architecture.\n\n" +
                      "Would you like to open the Dokan download page?";
        }
        else
        {
            title = "Dokan Driver Not Found";
            message = "The Dokan file system driver (dokan2.dll) is required to mount archives as virtual drives. " +
                      "It does not appear to be installed on this system.\n\n" +
                      "Would you like to open the Dokan download page?";
        }

        var result = MessageBox.Show(message, title,
            MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/dokan-dev/dokany/releases",
                UseShellExecute = true
            });
        }
    }

    private static void ShowDokanDriverErrorDialog(string errorMessage)
    {
        const string message = "The Dokan file system driver encountered an error:\n\n" +
                               "\"{0}\"\n\n" +
                               "Your Dokan driver may be outdated, corrupted, or not configured correctly. " +
                               "Please try reinstalling or updating the Dokan driver.\n\n" +
                               "Would you like to open the Dokan download page?";

        var formattedMessage = string.Format(CultureInfo.CurrentCulture, message, errorMessage);

        var result = MessageBox.Show(formattedMessage, "Dokan Driver Error",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/dokan-dev/dokany/releases",
                UseShellExecute = true
            });
        }
    }

    private async Task MountWithAutoDriveLetterAsync(string archivePath, string archiveType, ILogger logger)
    {
        char[] preferredDriveLetters = ['M', 'N', 'O', 'P', 'Q'];
        var existingDrives = DriveInfo.GetDrives().Select(static d => d.Name).ToList();

        foreach (var letter in preferredDriveLetters)
        {
            var currentMountPoint = letter + @":\";

            if (existingDrives.Any(d => string.Equals(d, currentMountPoint, StringComparison.OrdinalIgnoreCase)))
            {
                _loggingService.Log($"Skipping '{currentMountPoint}' (already in use).");
                continue;
            }

            Dokan dokan;
            try
            {
                dokan = new Dokan(logger);
            }
            catch (Exception ex) when (IsDokanUnavailable(ex))
            {
                ErrorLoggerStatic.ReportSilentException(ex, "Dokan driver not found during auto-mount");
                _loggingService.LogError("Dokan driver not found. Unable to mount archive.");
                ShowDokanNotInstalledDialog();
                return;
            }

            using (dokan)
            {
                _loggingService.Log($"Dokan Library Version: {dokan.Version}");
                _loggingService.Log($"Dokan Driver Version: {dokan.DriverVersion}");
                _loggingService.Log("");
                _loggingService.Log($"Attempting to mount on '{currentMountPoint}'...");

                if (await AttemptMountLifecycleAsync(archivePath, currentMountPoint, dokan, archiveType))
                {
                    // Event is already fired inside AttemptMountLifecycleAsync when mount succeeds
                    return;
                }
            }
        }

        _loggingService.Log("Error: Failed to auto-mount on any preferred drive letters.");
    }

    private async Task MountWithSpecifiedPointAsync(string archivePath, string mountPoint, string archiveType,
        ILogger logger)
    {
        if (mountPoint.Length == 1 && char.IsLetter(mountPoint[0])) mountPoint = mountPoint.ToUpperInvariant() + @":\";

        Dokan dokan;
        try
        {
            dokan = new Dokan(logger);
        }
        catch (Exception ex) when (IsDokanUnavailable(ex))
        {
            ErrorLoggerStatic.ReportSilentException(ex, "Dokan driver not found during specified mount");
            _loggingService.LogError("Dokan driver not found. Unable to mount archive.");
            ShowDokanNotInstalledDialog();
            return;
        }

        using (dokan)
        {
            _loggingService.Log($"Dokan Library Version: {dokan.Version}");
            _loggingService.Log($"Dokan Driver Version: {dokan.DriverVersion}");
            _loggingService.Log("");

            if (!await AttemptMountLifecycleAsync(archivePath, mountPoint, dokan, archiveType))
                _loggingService.Log($"Error: Failed to mount on '{mountPoint}'.");
            // Event is already fired inside AttemptMountLifecycleAsync when mount succeeds
        }
    }

    private async Task<bool> AttemptMountLifecycleAsync(string archivePath, string mountPoint, Dokan dokan,
        string archiveType)
    {
        _mountCancellation?.Dispose();
        _mountCancellation = new CancellationTokenSource();

        try
        {
            var fileInfo = new FileInfo(archivePath);
            _loggingService.Log(
                $"Processing {archiveType.ToUpperInvariant()} file: '{archivePath}', Size: {fileInfo.Length / 1024.0 / 1024.0:F2} MB");
            _loggingService.Log("");

            // Log the effective RAM cache setting (validation happens in AppSettings)
            var effectiveMaxMemoryBytes = _settingsService.Settings.MaxMemoryPerFileBytes;
            var effectiveMaxMemoryMb = effectiveMaxMemoryBytes / 1024.0 / 1024.0;
            var availableMemoryMb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024.0 / 1024.0;
            _loggingService.Log(
                $"RAM cache limit: {effectiveMaxMemoryMb:F0} MB (Available system memory: {availableMemoryMb:F0} MB)");
            _loggingService.Log("");

            Stream fileStream = await OpenArchiveFileStreamAsync(archivePath);

            try
            {
                var volumeLabel =
                    ZipFsHelpers.SanitizeVolumeLabel(ZipFsHelpers.GetArchiveFileNameWithoutExtension(archivePath));
                _currentZipFs = new ZipFs(
                    fileStream,
                    mountPoint,
                    ErrorLoggerStatic.LogErrorSync,
                    () => PromptForPassword(archivePath, archiveType),
                    archiveType,
                    effectiveMaxMemoryBytes,
                    volumeLabel);
            }
            catch
            {
                await fileStream.DisposeAsync();
                throw;
            }

            const int maxRetries = 2;
            const int retryDelayMs = 1000;

            DokanInstance? dokanInstance;
            try
            {
                var builder = new DokanInstanceBuilder(dokan)
                    .ConfigureOptions(options =>
                    {
                        options.Options = DokanOptions.RemovableDrive;
                        options.MountPoint = mountPoint;
                    });

                for (var attempt = 0;; attempt++)
                {
                    try
                    {
                        dokanInstance = builder.Build(_currentZipFs);
                        break;
                    }
                    catch (DokanException ex) when (attempt < maxRetries &&
                                                    !ex.Message.Contains("Can't install",
                                                        StringComparison.OrdinalIgnoreCase))
                    {
                        var delay = retryDelayMs * (attempt + 1);
                        _loggingService.Log(
                            $"Dokan driver error, retrying in {delay / 1000}s... (attempt {attempt + 1}/{maxRetries})");
                        await Task.Delay(delay);
                    }
                }
            }
            catch
            {
                _currentZipFs?.Dispose();
                _currentZipFs = null;
                throw;
            }

            using (dokanInstance)
            {
                _loggingService.Log($"Successfully mounted on '{mountPoint}'.");
                _loggingService.Log("");
                _loggingService.Log("Use the Unmount button or close the window to unmount.");
                _loggingService.Log("");

                IsMounted = true;
                CurrentMountPoint = mountPoint;
                CurrentArchivePath = archivePath;
                OnMountStatusChanged();

                try
                {
                    await Task.Delay(Timeout.Infinite, _mountCancellation.Token);
                }
                catch (OperationCanceledException)
                {
                }

                _loggingService.Log($"Unmounting '{mountPoint}'...");
            }

            return true;
        }
        catch (OperationCanceledException ex)
        {
            // User cancelled the password prompt - expected behavior, not an error.
            _loggingService.Log($"Mount cancelled: {ex.Message}");
            CurrentArchivePath = null;
            return false;
        }
        catch (DokanException ex)
        {
            _loggingService.LogError($"Dokan error: {ex.Message}");
            ErrorLoggerStatic.ReportSilentException(ex,
                $"MountService.AttemptMountLifecycleAsync: DokanException mounting '{archivePath}' to '{mountPoint}'",
                true);
            ShowDokanDriverErrorDialog(ex.Message);
            CurrentArchivePath = null;
            return false;
        }
        catch (Exception ex) when (ex.Message.Contains("drive", StringComparison.OrdinalIgnoreCase) ||
                                   ex.Message.Contains("mount", StringComparison.OrdinalIgnoreCase))
        {
            _loggingService.LogError($"Mount error: {ex.Message}");
            ErrorLoggerStatic.ReportSilentException(ex,
                $"MountService.AttemptMountLifecycleAsync: Drive/mount error for '{archivePath}' to '{mountPoint}'",
                true);
            CurrentArchivePath = null;
            return false;
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Mount error: {ex.Message}");
            ErrorLoggerStatic.LogErrorSync(ex,
                $"MountService.AttemptMountLifecycleAsync: Error mounting archive '{archivePath}' to '{mountPoint}'");
            CurrentArchivePath = null;
            return false;
        }
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

    /// <summary>
    ///     Prompts the user for a password using a WPF dialog.
    ///     This method is thread-safe and will marshal to the UI thread if necessary.
    /// </summary>
    /// <param name="archivePath">The path to the archive file.</param>
    /// <param name="archiveType">The type of archive (zip, 7z, rar).</param>
    /// <returns>The password entered by the user, or null if cancelled.</returns>
    private static string? PromptForPassword(string archivePath, string archiveType)
    {
        // Use Dispatcher to show dialog on UI thread
        return Application.Current?.Dispatcher.Invoke(() =>
        {
            var passwordWindow = new PasswordWindow(archivePath, archiveType)
            {
                Owner = Application.Current.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            var result = passwordWindow.ShowDialog();
            var password = result == true ? passwordWindow.Password : null;
            passwordWindow.ClearPassword();
            return password;
        });
    }
}