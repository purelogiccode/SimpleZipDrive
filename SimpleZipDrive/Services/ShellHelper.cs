using System.Diagnostics;

namespace SimpleZipDrive.Services;

/// <summary>
///     Opens URLs and folders using the platform's default handler.
/// </summary>
internal static class ShellHelper
{
    /// <summary>
    ///     Opens the supplied URL with the platform's default handler.
    /// </summary>
    /// <param name="url">The URL to open.</param>
    /// <returns><see langword="true" /> when the handler was launched; otherwise <see langword="false" />.</returns>
    public static bool OpenUrl(string url)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            else if (OperatingSystem.IsMacOS())
                Process.Start("open", url);
            else
                Process.Start("xdg-open", url);

            return true;
        }
        catch (Exception ex)
        {
            var context = $"ShellHelper.OpenUrl: Could not open URL '{url}'";
            ErrorLoggerStatic.ReportSilentException(ex, context, true);
            ServiceProvider.TryGet<ILoggingService>()?.LogError($"Could not open URL: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    ///     Opens the supplied folder in the platform's file manager.
    /// </summary>
    /// <param name="path">The folder path to open.</param>
    /// <returns><see langword="true" /> when the handler was launched; otherwise <see langword="false" />.</returns>
    public static bool OpenFolder(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo("explorer.exe", $"/root,\"{path}\"") { UseShellExecute = true });
            else if (OperatingSystem.IsMacOS())
                Process.Start("open", path);
            else
                Process.Start("xdg-open", path);

            return true;
        }
        catch (Exception ex)
        {
            var context = $"ShellHelper.OpenFolder: Could not open folder '{path}'";
            ErrorLoggerStatic.ReportSilentException(ex, context, true);
            ServiceProvider.TryGet<ILoggingService>()?.LogError($"Could not open folder: {ex.Message}");
            return false;
        }
    }
}
