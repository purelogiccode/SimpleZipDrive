using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace SimpleZipDrive.Views;

/// <summary>
///     Main application window: shows the mount toolbar and application log, and handles mounting,
///     unmounting, screenshots, and graceful shutdown.
/// </summary>
// Event handlers are wired from MainWindow.axaml (Click/KeyDown/Closing attributes),
// which ReSharper does not trace; suppress its unused-member inspection for them.
[SuppressMessage("ReSharper", "UnusedMember.Local")]
public partial class MainWindow : Window, IDisposable
{
    private static volatile bool _shutdownCompleted;
    private readonly ILoggingService _loggingService;

    private readonly IMountService _mountService;
    private readonly IScreenshotService _screenshotService;
    private readonly ISettingsService _settingsService;
    private int _isShuttingDown;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MainWindow" /> class and wires it to the
    ///     registered mount, logging, and screenshot services.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();

        // Get services from the service provider
        _mountService = ServiceProvider.Get<IMountService>();
        _loggingService = ServiceProvider.Get<ILoggingService>();
        _screenshotService = ServiceProvider.Get<IScreenshotService>();
        _settingsService = ServiceProvider.Get<ISettingsService>();

        _mountService.MountStatusChanged += OnMountStatusChanged;

        // Subscribe to log entries collection changes
        ((INotifyCollectionChanged)_loggingService.LogEntries).CollectionChanged += OnLogEntriesChanged;

        // Initialize log text
        UpdateLogText();

        Opened += MainWindow_OpenedAsync;
    }

    /// <summary>
    ///     Unsubscribes from service events and disposes the mount service.
    /// </summary>
    public void Dispose()
    {
        try
        {
            // Unsubscribe from log entries collection changes
            if (_loggingService.LogEntries is INotifyCollectionChanged notifyCollection)
                notifyCollection.CollectionChanged -= OnLogEntriesChanged;

            // Unsubscribe from mount service events
            _mountService.MountStatusChanged -= OnMountStatusChanged;

            // Dispose the mount service (which handles unmounting if needed)
            (_mountService as IDisposable)?.Dispose();
        }
        catch (Exception ex)
        {
            ErrorLoggerStatic.ReportSilentException(ex, "MainWindow.Dispose: Error during disposal", true);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int TerminateProcess(nint hProcess, uint uExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetCurrentProcess();

    private void OnMountStatusChanged(object? sender, MountStatusChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(UpdateMountStatus, DispatcherPriority.Background);
    }

    private void OnLogEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                {
                    var current = LogTextBox.Text ?? string.Empty;
                    foreach (var newItem in e.NewItems?.Cast<LogEntry>() ?? [])
                    {
                        if (current.Length > 0) current += Environment.NewLine;
                        current += newItem.ToString();
                    }

                    LogTextBox.Text = current;
                    ScrollLogToEnd();
                    break;
                }
                case NotifyCollectionChangedAction.Remove:
                    UpdateLogText();
                    break;
                case NotifyCollectionChangedAction.Reset:
                    LogTextBox.Text = string.Empty;
                    break;
            }
        }, DispatcherPriority.Background);
    }

    private void UpdateLogText()
    {
        LogTextBox.Text = string.Join(Environment.NewLine,
            _loggingService.LogEntries.Select(static e => e.ToString()));
        ScrollLogToEnd();
    }

    private void ScrollLogToEnd()
    {
        LogTextBox.CaretIndex = LogTextBox.Text?.Length ?? 0;
        LogTextBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault()?.ScrollToEnd();
    }

    private async void MainWindow_OpenedAsync(object? sender, EventArgs e)
    {
        try
        {
            Opened -= MainWindow_OpenedAsync;
            var args = App.StartupArgs;
            if (args.Length > 0)
            {
                await ProcessCommandLineArgsAsync(args);
                return;
            }

            // Interactive start: proactively warn when the selected backend's driver is missing
            // and offer its download page. Command-line/drag-and-drop mounts already show the
            // same warning from the mount path, so this runs only for an idle start.
            Dispatcher.UIThread.Post(WarnIfMountDriverMissing, DispatcherPriority.Background);
        }
        catch (Exception ex)
        {
            const string context = "Error in method MainWindow_OpenedAsync";
            await ErrorLoggerStatic.LogErrorAsync(ex, context);
        }
    }

    /// <summary>
    ///     Warns - with an offer to open the download page - when the driver required by the
    ///     effective mount backend is not installed (Dokan/WinFsp on Windows, FUSE elsewhere).
    /// </summary>
    private void WarnIfMountDriverMissing()
    {
        try
        {
            // Normalize first: a settings file moved between platforms can hold a backend
            // that does not exist here (e.g. FUSE on Windows), which must not be reported as
            // a missing "libfuse3" driver.
            var requested = AppSettings.NormalizeMountBackend(_settingsService.Settings.MountBackend);
            var backend = MountBackendAvailability.ResolveEffective(requested);
            if (MountBackendAvailability.IsAvailable(backend, out var reason))
                return;

            var backendName = MountBackendAvailability.DisplayName(backend);
            var driverName = MountBackendAvailability.DriverName(backend);
            var downloadUrl = MountBackendAvailability.DownloadUrl(backend);

            _loggingService.LogError($"{driverName} not found: {reason}");

            var message =
                $"SimpleZipDrive could not find the {driverName} required by the {backendName} mount backend.\n\n" +
                $"{reason}\n\n" +
                "Archives cannot be mounted until it is installed. " +
                $"Would you like to open the {backendName} download page now?";
            var result = MessageBox.Show(message, $"{driverName} Not Found",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                _loggingService.Log($"User declined to open the {backendName} download page.");
                return;
            }

            if (!ShellHelper.OpenUrl(downloadUrl))
            {
                MessageBox.Show(
                    $"Could not open the browser automatically.\n\nPlease visit:\n{downloadUrl}",
                    "Open Download Page", MessageBoxButton.Ok, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            ErrorLoggerStatic.ReportSilentException(ex, "MainWindow.WarnIfMountDriverMissing failed", true);
        }
    }

    private async Task ProcessCommandLineArgsAsync(string[] args)
    {
        string? zipFilePath;
        string? mountPointArg = null;

        switch (args.Length)
        {
            case 1 when
                !string.IsNullOrWhiteSpace(args[0]) &&
                File.Exists(args[0]) &&
                IsSupportedArchiveExtension(args[0]):
                zipFilePath = args[0].Trim().Trim('"');
                _loggingService.Log($"Drag-and-drop mode: Detected archive file '{zipFilePath}'.");
                break;
            case >= 2 when
                !string.IsNullOrWhiteSpace(args[0]) &&
                !string.IsNullOrWhiteSpace(args[1]):
                zipFilePath = args[0].Trim().Trim('"');
                mountPointArg = args[1].Trim().Trim('"');
                _loggingService.Log($"Standard mode: Archive file '{zipFilePath}', Mount point arg '{mountPointArg}'.");
                break;
            default:
                return;
        }

        if (!File.Exists(zipFilePath))
        {
            _loggingService.LogError($"Error: Archive file not found at '{zipFilePath}'.");
            return;
        }

        if (!IsSupportedArchiveExtension(zipFilePath))
        {
            _loggingService.LogError($"{AppTheme.Section("INVALID FILE TYPE")}");
            _loggingService.LogError($"Error: The file '{Path.GetFileName(zipFilePath)}' is not a supported archive.");
            _loggingService.LogError(
                $"Detected extension: '{Path.GetExtension(zipFilePath)}' (expected: {ArchiveFormats.SupportedExtensionsDescription})");
            _loggingService.LogError(
                "Simple Zip Drive can only mount ZIP, 7Z, RAR, TAR (including compressed variants), comic-book archives (.cbz, .cbr, .cb7), ZAR, and Xbox XISO images.");
            return;
        }

        try
        {
#pragma warning disable IL3002
            await _mountService.MountAsync(zipFilePath, mountPointArg);
#pragma warning restore IL3002
        }
        catch (Exception ex)
        {
            const string context = "Error mounting archive from command line";
            await ErrorLoggerStatic.LogErrorAsync(ex, context);
            _loggingService.LogError($"Error: Failed to mount '{zipFilePath}'. {ex.Message}");
        }
    }

    private void SettingsRamLimit_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow { WindowStartupLocation = WindowStartupLocation.CenterOwner };
        _ = dialog.ShowDialog(this);
    }

    private void OpenConfigPath_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var configPath = AppSettings.SettingsDirectory;
            if (!Directory.Exists(configPath)) Directory.CreateDirectory(configPath);

            ShellHelper.OpenFolder(configPath);
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Error opening configuration path: {ex.Message}");
        }
    }

    private void CleanTempFiles_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            _loggingService.Log($"{AppTheme.Section("CLEANUP")}");
            _loggingService.Log("Cleaning orphaned temporary files...");
            ZipFsHelpers.CleanupOrphanedTempDirectories();
            _loggingService.Log("Temporary files cleaned successfully.");
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Error cleaning temp files: {ex.Message}");
            ErrorLoggerStatic.ReportSilentException(ex, "CleanTempFiles_Click: Error cleaning temp files");
        }
    }

    private void About_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new AboutWindow { WindowStartupLocation = WindowStartupLocation.CenterOwner };
        _ = dialog.ShowDialog(this);
    }

    private void Donate_Click(object? sender, RoutedEventArgs e)
    {
        if (ShellHelper.OpenUrl("https://www.purelogiccode.com/donate"))
        {
            _loggingService.Log("Opened the donation page.");
            return;
        }

        MessageBox.Show(
            "Could not open the browser automatically.\n\nPlease visit:\nhttps://www.purelogiccode.com/donate",
            "Donate", MessageBoxButton.Ok, MessageBoxImage.Information);
    }

    private void Exit_Click(object? sender, RoutedEventArgs e)
    {
        // Trigger the closing event which will handle proper cleanup
        Close();
    }

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.F8) return;

        e.Handled = true;
        TakeScreenshot();
    }

    private void TakeScreenshot()
    {
        try
        {
            var result = _screenshotService.CaptureActiveWindow();
            if (result.Success)
            {
                StatusText.Text = $"Screenshot saved: {result.FilePath}";
            }
            else
            {
                StatusText.Text = "Screenshot failed.";
                MessageBox.Show(
                    $"The screenshot could not be saved.\n\n{result.ErrorMessage ?? "Unknown error."}",
                    "Screenshot Failed", MessageBoxButton.Ok, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            ErrorLoggerStatic.ReportSilentException(ex, "MainWindow.TakeScreenshot: Failed to capture screenshot");
            MessageBox.Show(
                $"The screenshot could not be saved.\n\n{ex.Message}",
                "Screenshot Failed", MessageBoxButton.Ok, MessageBoxImage.Warning);
        }
    }

    private async void Mount_ClickAsync(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_mountService.IsMounted)
            {
                MessageBox.Show("A drive is already mounted. Please unmount it first.", "Drive Already Mounted",
                    MessageBoxButton.Ok, MessageBoxImage.Information);
                return;
            }

            var fileName = await PickArchiveAsync();
            if (fileName is null) return;

            var settings = ServiceProvider.Get<ISettingsService>().Settings;
            if (settings.DefaultMountType == MountType.Folder && OperatingSystem.IsWindows())
            {
                await MountAsFolderAsync(fileName);
            }
            else
            {
#pragma warning disable IL3002
                await _mountService.MountAsync(fileName);
#pragma warning restore IL3002
            }
        }
        catch (Exception ex)
        {
            const string context = "Error in method Mount_ClickAsync";
            await ErrorLoggerStatic.LogErrorAsync(ex, context);
            MessageBox.Show($"Error mounting archive: {ex.Message}", "Mount Error",
                MessageBoxButton.Ok, MessageBoxImage.Error);
        }
    }

    private async void MountAsDrive_ClickAsync(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_mountService.IsMounted)
            {
                MessageBox.Show("A drive is already mounted. Please unmount it first.", "Drive Already Mounted",
                    MessageBoxButton.Ok, MessageBoxImage.Information);
                return;
            }

            var fileName = await PickArchiveAsync();
            if (fileName is null) return;

#pragma warning disable IL3002
            await _mountService.MountAsync(fileName);
#pragma warning restore IL3002
        }
        catch (Exception ex)
        {
            const string context = "Error in method MountAsDrive_ClickAsync";
            await ErrorLoggerStatic.LogErrorAsync(ex, context);
            MessageBox.Show($"Error mounting archive: {ex.Message}", "Mount Error",
                MessageBoxButton.Ok, MessageBoxImage.Error);
        }
    }

    private async void MountAsFolder_ClickAsync(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_mountService.IsMounted)
            {
                MessageBox.Show("A drive is already mounted. Please unmount it first.", "Drive Already Mounted",
                    MessageBoxButton.Ok, MessageBoxImage.Information);
                return;
            }

            var fileName = await PickArchiveAsync();
            if (fileName is null) return;

            await MountAsFolderAsync(fileName);
        }
        catch (Exception ex)
        {
            const string context = "Error in method MountAsFolder_ClickAsync";
            await ErrorLoggerStatic.LogErrorAsync(ex, context);
            MessageBox.Show($"Error mounting archive: {ex.Message}", "Mount Error",
                MessageBoxButton.Ok, MessageBoxImage.Error);
        }
    }

    private async Task MountAsFolderAsync(string archivePath)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Mount Folder",
            AllowMultiple = false
        });

        if (folders.Count == 0) return;

#pragma warning disable IL3002
        await _mountService.MountAsync(archivePath, folders[0].Path.LocalPath);
#pragma warning restore IL3002
    }

    private async Task<string?> PickArchiveAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Archive File",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Archive files") { Patterns = ArchiveFormats.DialogPatterns },
                new FilePickerFileType("All files") { Patterns = ArchiveFormats.AllFilesPatterns }
            ]
        });

        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }

    private async void Unmount_ClickAsync(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (!_mountService.IsMounted) return;

            try
            {
                StatusText.Text = "Unmounting drive...";
                await _mountService.UnmountAsync();
                StatusText.Text = "Drive unmounted";
                _loggingService.Log("Drive unmounted successfully.");
            }
            catch (OperationCanceledException)
            {
                // Expected during unmount - no need to report
                StatusText.Text = "Drive unmount cancelled";
            }
            catch (Exception ex)
            {
                const string context = "Error unmounting drive";
                await ErrorLoggerStatic.LogErrorAsync(ex, context);
                MessageBox.Show($"Error unmounting drive: {ex.Message}", "Unmount Error",
                    MessageBoxButton.Ok, MessageBoxImage.Error);
                StatusText.Text = "Error unmounting drive";
            }
        }
        catch (Exception ex)
        {
            const string context = "Error unmounting drive";
            await ErrorLoggerStatic.LogErrorAsync(ex, context);
        }
    }

    private void CopySelection_Click(object? sender, RoutedEventArgs e)
    {
        var selectedText = LogTextBox.SelectedText;
        if (string.IsNullOrEmpty(selectedText)) return;

        _ = Clipboard?.SetTextAsync(selectedText);
        StatusText.Text = "Selection copied to clipboard.";
    }

    private void CopyLog_Click(object? sender, RoutedEventArgs e)
    {
        if (_loggingService.LogEntries.Count == 0) return;

        var text = string.Join(Environment.NewLine,
            _loggingService.LogEntries.Select(static item => item.ToString()));
        _ = Clipboard?.SetTextAsync(text);
        StatusText.Text = "Log copied to clipboard.";
    }

    private void ClearLog_Click(object? sender, RoutedEventArgs e)
    {
        _loggingService.LogEntries.Clear();
        LogTextBox.Text = string.Empty;
        StatusText.Text = "Log cleared.";
    }

    private void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (Interlocked.Exchange(ref _isShuttingDown, 1) != 0)
            return;

        e.Cancel = true;
        _ = PerformShutdownAsync();
    }

    private async Task PerformShutdownAsync()
    {
        try
        {
            // Update UI directly since we're on the UI thread context
            IsEnabled = false;
            StatusText.Text = _mountService.IsMounted
                ? "Unmounting drive and shutting down..."
                : "Shutting down...";

            if (_mountService.IsMounted)
            {
                var unmountTask = _mountService.UnmountAsync();
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(5));
                var completedTask = await Task.WhenAny(unmountTask, timeoutTask);
                if (completedTask == timeoutTask)
                {
                    // Unmount timed out, force exit
                    await App.ShutdownCts.CancelAsync();
                    ForceExit();
                    return;
                }

                try
                {
                    await unmountTask;
                }
                catch (OperationCanceledException)
                {
                }
            }

            await App.ShutdownCts.CancelAsync();

            Closing -= MainWindow_Closing;

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Exit += static (_, _) => { _shutdownCompleted = true; };
                desktop.Shutdown();
            }

            _ = Task.Run(static async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(3));
                if (_shutdownCompleted)
                    return;

                ForceExit();
            });
        }
        catch (Exception ex)
        {
            ErrorLoggerStatic.ReportSilentException(ex, "MainWindow.PerformShutdownAsync: Shutdown failed", true);
            ForceExit();
        }
    }

    private static void ForceExit()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                // Terminate immediately with a success code. Like Process.Kill() this bypasses managed
                // finalization that can otherwise hang on native filesystem driver threads, but reports
                // exit code 0 instead of Kill()'s hardcoded -1.
                if (TerminateProcess(GetCurrentProcess(), 0) != 0)
                    return;
            }
            catch
            {
                // Fall through to the managed fallbacks below.
            }
        }

        try
        {
            Environment.Exit(0);
        }
        catch
        {
            Environment.FailFast(null);
        }
    }

    private void UpdateMountStatus()
    {
        if (_mountService.IsMounted)
        {
            MountStatusText.Text =
                $"Mounted: {_mountService.CurrentMountPoint} | Archive: {_mountService.CurrentArchivePath}";
            StatusText.Text = "Drive mounted - Click Unmount to unmount";
            UnmountButton.IsEnabled = true;
            MountButton.IsEnabled = false;

            if (ServiceProvider.Get<ISettingsService>().Settings.AutoOpenMountedDrive
                && _mountService.CurrentMountPoint is { } mountPoint
                && !ShellHelper.OpenFolder(mountPoint))
            {
                _loggingService.LogError($"Failed to open the mounted location: {mountPoint}");
            }
        }
        else
        {
            MountStatusText.Text = "";
            UnmountButton.IsEnabled = false;
            MountButton.IsEnabled = true;
        }
    }

    private static bool IsSupportedArchiveExtension(string filePath)
    {
        return ArchiveFormats.IsSupportedArchive(filePath);
    }
}
