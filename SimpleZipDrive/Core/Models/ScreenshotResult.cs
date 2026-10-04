namespace SimpleZipDrive.Core.Models;

/// <summary>
///     Represents the result of a screenshot capture operation.
/// </summary>
/// <param name="Success">Whether the screenshot was captured and saved successfully.</param>
/// <param name="FilePath">The full path to the saved screenshot, or <see langword="null" /> on failure.</param>
/// <param name="ErrorMessage">A description of the failure, or <see langword="null" /> on success.</param>
public sealed record ScreenshotResult(bool Success, string? FilePath, string? ErrorMessage);