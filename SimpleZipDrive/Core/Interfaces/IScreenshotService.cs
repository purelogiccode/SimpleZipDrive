namespace SimpleZipDrive.Core.Interfaces;

/// <summary>
///     Service for capturing screenshots of the active application window.
/// </summary>
public interface IScreenshotService
{
    /// <summary>
    ///     Captures the currently active application window and saves it as a PNG image
    ///     inside the "Screenshot" folder within the application folder. When that folder
    ///     cannot be written to, the image is saved to the "Screenshot" folder under
    ///     <c>%LOCALAPPDATA%\SimpleZipDrive</c> instead.
    /// </summary>
    /// <returns>A <see cref="ScreenshotResult" /> describing the outcome of the operation.</returns>
    ScreenshotResult CaptureActiveWindow();
}