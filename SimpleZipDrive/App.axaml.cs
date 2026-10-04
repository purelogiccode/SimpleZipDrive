using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SimpleZipDrive.Core.Logging;

namespace SimpleZipDrive;

public class App : Application
{
    private static TextWriter? _originalConsoleOut;
    private static TextWriter? _originalConsoleError;
    private static LogTextWriter? _logTextWriter;

    /// <summary>Gets the command-line arguments the application was started with.</summary>
    internal static string[] StartupArgs { get; private set; } = [];

    /// <summary>
    ///     Global cancellation token source for graceful shutdown of background tasks.
    /// </summary>
    internal static CancellationTokenSource ShutdownCts { get; } = new();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            desktop.Exit += OnDesktopExit;
            StartupArgs = desktop.Args ?? [];

            DiagnosticLogger.CleanupOldLogs();
            DiagnosticLogger.Initialize();
            DiagnosticLogger.LogSection("APPLICATION STARTUP");
            DiagnosticLogger.Log($"  Version: {Assembly.GetExecutingAssembly().GetName().Version}");
            DiagnosticLogger.Log($"  Arguments: [{string.Join(", ", StartupArgs)}]");
            DiagnosticLogger.Log($"  Base directory: {AppContext.BaseDirectory}");
            DiagnosticLogger.Log($"  OS: {RuntimeInformation.OSDescription}");
            DiagnosticLogger.Log($"  Framework: {RuntimeInformation.FrameworkDescription}");
            DiagnosticLogger.Log($"  Working directory: {Environment.CurrentDirectory}");

            try
            {
                RegisterAppServices();

                // Setup console redirection
                _originalConsoleOut = Console.Out;
                _originalConsoleError = Console.Error;

                _logTextWriter = new LogTextWriter(_originalConsoleOut);
                Console.SetOut(_logTextWriter);
                Console.SetError(_logTextWriter);

                var loggingService = ServiceProvider.Get<ILoggingService>();

                loggingService.Log("Archive Drive (Streaming Access with In-Memory Entry Cache)");
                loggingService.Log($"Supports: {ArchiveFormats.SupportedFormatsDescription}");
                loggingService.Log("");
                loggingService.Log("Mount backends: WinFsp / Dokan (Windows), FUSE (Linux/macOS)");
                loggingService.Log("");
                loggingService.Log("Usage 1 (Explicit Mount): SimpleZipDrive <PathToArchiveFile> <MountPoint>");
                loggingService.Log("Example: SimpleZipDrive \"C:\\path\\to\\archive.zip\" M");
                loggingService.Log("Example: SimpleZipDrive \"C:\\path\\to\\archive.7z\" N");
                loggingService.Log("Example: SimpleZipDrive \"C:\\path\\to\\archive.rar\" O");
                loggingService.Log("Example: SimpleZipDrive \"C:\\path\\to\\archive.zar\" N");
                loggingService.Log("Example: SimpleZipDrive \"C:\\path\\to\\game.iso\" O");
                loggingService.Log("On Linux/macOS the mount point is a folder, e.g. /mnt/archive");
                loggingService.Log(
                    @"MountPoint can be a drive letter (e.g., M) or a path to an existing empty folder (e.g., C:\mount\zip)");
                loggingService.Log("");
                loggingService.Log(
                    $"Usage 2 (Drag-and-Drop): Drag a {ArchiveFormats.SupportedExtensionsDescription} file onto the SimpleZipDrive icon.");
                loggingService.Log("On Windows it will attempt to mount on M:, then N:, O:, P:, Q: automatically.");
                loggingService.Log("");

                var updateService = ServiceProvider.TryGet<IUpdateService>();
                if (updateService != null)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await updateService.CheckForUpdateAsync(ShutdownCts.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            // Expected during shutdown
                        }
                        catch (Exception ex)
                        {
                            ErrorLoggerStatic.ReportSilentException(ex,
                                "App.OnStartup: Update check failed during startup", true);
                        }
                    });
                }

                loggingService.Log("");

                ErrorLoggerStatic.InitializeGlobalExceptionHandlers();

                // Run background tasks (stats)
                _ = RunBackgroundTasksAsync();

                try
                {
                    var mainWindow = new MainWindow();
                    desktop.MainWindow = mainWindow;
                    mainWindow.Show();
                }
                catch (Exception ex)
                {
                    ErrorLoggerStatic.ReportSilentException(ex, "App.OnStartup: Failed to create the main window");
                    desktop.Shutdown(1);
                }
            }
            catch (Exception ex)
            {
                // Critical startup error - try to log it
                try
                {
                    ErrorLoggerStatic.LogErrorSync(ex, "Critical error during application startup");
                }
                catch
                {
                    // If even error logging fails, show message box as last resort
                    MessageBox.Show($"Critical startup error: {ex.Message}\n\n{ex.StackTrace}",
                        "Startup Error", MessageBoxButton.Ok, MessageBoxImage.Error);
                }

                throw;
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void RegisterAppServices()
    {
        // Register logging service first (other services depend on it)
        var loggingService = new LoggingService();
        ServiceProvider.Register<ILoggingService>(loggingService);

        // Register settings service
        var settingsService = new SettingsService();
        ServiceProvider.Register<ISettingsService>(settingsService);

        // Register password prompt service (used by the mount backends when an archive is encrypted)
        ServiceProvider.Register<IPasswordPromptService>(new PasswordPromptService());

        // Register mount service facade (selects Dokan/WinFsp/FUSE at mount time)
        var mountService = new MountService(loggingService, settingsService);
        ServiceProvider.Register<IMountService>(mountService);

        // Register user notification service
        var userNotificationService = new UserNotificationService(loggingService);
        ServiceProvider.Register<IUserNotificationService>(userNotificationService);

        // Register screenshot service
        var screenshotService = new ScreenshotService(loggingService);
        ServiceProvider.Register<IScreenshotService>(screenshotService);

        // Register update service
        var updateService = new UpdateService(userNotificationService);
        ServiceProvider.Register<IUpdateService>(updateService);

        // Register stats service
        var statsService = new StatsService();
        ServiceProvider.Register<IStatsService>(statsService);
    }

    private static Task RunBackgroundTasksAsync()
    {
        try
        {
            try
            {
                // Report stats (fire and forget, but respect cancellation)
                var statsService = ServiceProvider.TryGet<IStatsService>();
                if (statsService != null)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await statsService.ReportStatsAsync(ShutdownCts.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            // Expected during shutdown - no need to log
                        }
                        catch (Exception ex)
                        {
                            // Stats reporting failure - report silently
                            ErrorLoggerStatic.ReportSilentException(ex, "StatsService.ReportStatsAsync failed", true);
                        }
                    }, ShutdownCts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown - no need to log
            }
            catch (Exception ex)
            {
                // Report any other failures in background task coordination
                ErrorLoggerStatic.ReportSilentException(ex, "RunBackgroundTasksAsync failed", true);
            }

            return Task.CompletedTask;
        }
        catch (Exception exception)
        {
            return Task.FromException(exception);
        }
    }

    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        DiagnosticLogger.LogSection("APPLICATION SHUTDOWN");
        try
        {
            // Signal all background tasks to cancel
            try
            {
                ShutdownCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already disposed
            }

            // Dispose the log text writer to stop its background processing task
            try
            {
                _logTextWriter?.Dispose();
            }
            catch (Exception ex)
            {
                ErrorLoggerStatic.ReportSilentException(ex, "App.OnExit: Failed to dispose LogTextWriter", true);
            }

            // Dispose MainWindow to unsubscribe events before services are disposed
            try
            {
                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: IDisposable disposable })
                    disposable.Dispose();
            }
            catch (Exception ex)
            {
                ErrorLoggerStatic.ReportSilentException(ex, "App.OnExit: Failed to dispose MainWindow", true);
            }

            // Dispose all registered services that implement IDisposable
            try
            {
                ServiceProvider.DisposeAllServices();
            }
            catch (Exception ex)
            {
                ErrorLoggerStatic.ReportSilentException(ex, "App.OnExit: Failed to dispose services", true);
            }

            // Always dispose the shutdown token source to prevent resource leak
            ShutdownCts.Dispose();

            // Restore console
            if (_originalConsoleOut != null) Console.SetOut(_originalConsoleOut);
            if (_originalConsoleError != null) Console.SetError(_originalConsoleError);
        }
        catch (Exception ex)
        {
            ErrorLoggerStatic.ReportSilentException(ex, "App.OnExit: Error during exit cleanup", true);
        }

        // Flush and close the Serilog pipeline (and the per-session diagnostic file) before
        // disposing the ErrorLogger, since the bug report sink forwards through it.
        try
        {
            AppLogger.CloseAndFlush();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to flush loggers: {ex.Message}");
        }

        // Dispose the singleton ErrorLogger (HttpClient connection pool)
        // Must be done AFTER the outer catch, which may still need to report errors
        try
        {
            // Drain any in-flight bug report POSTs first so they are not lost when the HttpClient
            // is disposed below (the bug report sink forwards them fire-and-forget).
            ErrorLoggerStatic.WaitForPendingReports(TimeSpan.FromSeconds(5));
            ErrorLoggerStatic.Instance.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to dispose ErrorLogger: {ex.Message}");
        }
    }
}
