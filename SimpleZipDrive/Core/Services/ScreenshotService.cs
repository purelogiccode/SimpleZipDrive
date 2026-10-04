using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace SimpleZipDrive.Core.Services;

/// <summary>
///     Captures the active application window using Avalonia rendering and saves it as a PNG
///     inside the "Screenshot" folder within the application folder.
/// </summary>
public class ScreenshotService : IScreenshotService
{
    private const string ScreenshotFolderName = "Screenshot";
    private static readonly string ScreenshotDirectory = Path.Combine(AppContext.BaseDirectory, ScreenshotFolderName);

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
        renderTarget.Render(window);
        return renderTarget;
    }

    private ScreenshotResult SaveScreenshot(RenderTargetBitmap bitmap)
    {
        var fileName = $"Screenshot_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png";
        var filePath = Path.Combine(ScreenshotDirectory, fileName);

        try
        {
            Directory.CreateDirectory(ScreenshotDirectory);
            bitmap.Save(filePath, new PngBitmapEncoderOptions());

            _loggingService.Log($"Screenshot saved: {filePath}");
            return new ScreenshotResult(true, filePath, null);
        }
        catch (Exception ex)
        {
            ErrorLoggerStatic.ReportSilentException(ex,
                "ScreenshotService.SaveScreenshot: Failed to save the screenshot");
            _loggingService.LogError($"Failed to save screenshot to '{ScreenshotDirectory}': {ex.Message}");
            return new ScreenshotResult(false, filePath, "write permission issues");
        }
    }
}
