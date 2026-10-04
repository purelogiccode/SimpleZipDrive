using System.Diagnostics;

namespace SimpleZipDrive.Services;

/// <summary>
///     Opens URLs and folders using the platform's default handler.
/// </summary>
internal static class ShellHelper
{
    public static void OpenUrl(string url)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            else if (OperatingSystem.IsMacOS())
                Process.Start("open", url);
            else
                Process.Start("xdg-open", url);
        }
        catch (Exception ex)
        {
            var context = $"ShellHelper.OpenUrl: Could not open URL '{url}'";
            ErrorLoggerStatic.ReportSilentException(ex, context, true);
            ServiceProvider.TryGet<ILoggingService>()?.LogError($"Could not open URL: {ex.Message}");
        }
    }

    public static void OpenFolder(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo("explorer.exe", $"/root,\"{path}\"") { UseShellExecute = true });
            else if (OperatingSystem.IsMacOS())
                Process.Start("open", path);
            else
                Process.Start("xdg-open", path);
        }
        catch (Exception ex)
        {
            var context = $"ShellHelper.OpenFolder: Could not open folder '{path}'";
            ErrorLoggerStatic.ReportSilentException(ex, context, true);
            ServiceProvider.TryGet<ILoggingService>()?.LogError($"Could not open folder: {ex.Message}");
        }
    }
}
