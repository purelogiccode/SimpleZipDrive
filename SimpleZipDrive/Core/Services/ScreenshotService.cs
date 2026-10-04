using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace SimpleZipDrive.Core.Services;

/// <summary>
///     Captures the active application window using Avalonia rendering and saves it as a PNG
///     inside the "Screenshot" folder within the application folder. If that folder is not
///     writable, the screenshot is saved to the "Screenshot" folder under
///     <c>%LOCALAPPDATA%\SimpleZipDrive</c> instead.
/// </summary>
public class ScreenshotService : IScreenshotService
{
    private const string ScreenshotFolderName = "Screenshot";
    private const string ApplicationFolderName = "SimpleZipDrive";

    internal static readonly string ScreenshotDirectory = Path.Combine(AppContext.BaseDirectory, ScreenshotFolderName);

    internal static readonly string FallbackScreenshotDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ApplicationFolderName, ScreenshotFolderName);

    private readonly ILoggingService _loggingService;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ScreenshotService" /> class.
    /// </summary>
    /// <param name="loggingService">The logging service used to record screenshot activity.</param>
    public ScreenshotService(ILoggingService loggingService)
    {
        _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
    }

    /// <inheritdoc />
    public ScreenshotResult CaptureActiveWindow()
    {
        if (Application.Current is null)
            return new ScreenshotResult(false, null, "No active application.");

        return Dispatcher.UIThread.CheckAccess()
            ? CaptureCore()
            : Dispatcher.UIThread.Invoke(CaptureCore);
    }

    private ScreenshotResult CaptureCore()
    {
        try
        {
            var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            var window = lifetime?.Windows.FirstOrDefault(static w => w.IsActive)
                         ?? lifetime?.MainWindow;

            if (window == null)
                return new ScreenshotResult(false, null, "No active window to capture.");

            var bitmap = RenderWindow(window);
            if (bitmap == null)
                return new ScreenshotResult(false, null, "The active window has no visible content to capture.");

            using (bitmap)
            {
                return SaveScreenshot(bitmap);
            }
        }
        catch (Exception ex)
        {
            ErrorLoggerStatic.ReportSilentException(ex,
                "ScreenshotService.CaptureCore: Failed to capture the active window");
            _loggingService.LogError($"Screenshot capture failed: {ex.Message}");
            return new ScreenshotResult(false, null, ex.Message);
        }
    }

    private static RenderTargetBitmap? RenderWindow(Window window)
    {
        var width = window.ClientSize.Width;
        var height = window.ClientSize.Height;
        if (width <= 0 || height <= 0)
            return null;

        var scale = window.RenderScaling;
        var pixelSize = new PixelSize(
            (int)Math.Ceiling(width * scale),
            (int)Math.Ceiling(height * scale));

        var renderTarget = new RenderTargetBitmap(pixelSize, new Vector(96 * scale, 96 * scale));
        try
        {
            renderTarget.Render(window);
        }
        catch
        {
            renderTarget.Dispose();
            throw;
        }

        return renderTarget;
    }

    private ScreenshotResult SaveScreenshot(RenderTargetBitmap bitmap)
    {
        return SaveScreenshotCore(path => bitmap.Save(path, new PngBitmapEncoderOptions()));
    }

    /// <summary>
    ///     Saves the screenshot to the application's "Screenshot" folder, falling back to
    ///     the "Screenshot" folder under <c>%LOCALAPPDATA%\SimpleZipDrive</c> when the
    ///     application folder is not writable. The <paramref name="saveToFile" /> callback
    ///     receives the full destination path and performs the actual write.
    /// </summary>
    internal ScreenshotResult SaveScreenshotCore(Action<string> saveToFile)
    {
        return SaveScreenshotCore(saveToFile, ScreenshotDirectory, FallbackScreenshotDirectory);
    }

    internal ScreenshotResult SaveScreenshotCore(Action<string> saveToFile, string primaryDirectory,
        string fallbackDirectory)
    {
        // Two captures within the same millisecond would otherwise overwrite each other.
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var primaryPath = GetUniqueFilePath(primaryDirectory, timestamp);

        try
        {
            Directory.CreateDirectory(primaryDirectory);
            saveToFile(primaryPath);

            _loggingService.Log($"Screenshot saved: {primaryPath}");
            return new ScreenshotResult(true, primaryPath, null);
        }
        catch (Exception ex)
        {
            ErrorLoggerStatic.ReportSilentException(ex,
                "ScreenshotService.SaveScreenshot: Failed to save the screenshot to the application folder");
            _loggingService.LogError(
                $"Failed to save screenshot to '{primaryDirectory}': {ex.Message}. " +
                $"Trying fallback location '{fallbackDirectory}'.");
        }

        var fallbackPath = GetUniqueFilePath(fallbackDirectory, timestamp);
        try
        {
            Directory.CreateDirectory(fallbackDirectory);
            saveToFile(fallbackPath);

            _loggingService.Log($"Screenshot saved to fallback location: {fallbackPath}");
            return new ScreenshotResult(true, fallbackPath, null);
        }
        catch (Exception ex)
        {
            ErrorLoggerStatic.ReportSilentException(ex,
                "ScreenshotService.SaveScreenshot: Failed to save the screenshot to the fallback folder");
            _loggingService.LogError($"Failed to save screenshot to '{fallbackDirectory}': {ex.Message}");
            return new ScreenshotResult(false, null, ex.Message);
        }
    }

    /// <summary>
    ///     Builds a screenshot path, appending a numeric suffix when a file with the same
    ///     timestamp already exists so rapid consecutive captures never overwrite each other.
    /// </summary>
    private static string GetUniqueFilePath(string directory, string timestamp)
    {
        var path = Path.Combine(directory, $"Screenshot_{timestamp}.png");

        for (var suffix = 1; suffix < 1000 && File.Exists(path); suffix++)
            path = Path.Combine(directory, $"Screenshot_{timestamp}_{suffix}.png");

        return path;
    }
}
