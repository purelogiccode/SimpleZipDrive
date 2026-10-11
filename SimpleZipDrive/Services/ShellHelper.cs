using System.Diagnostics;

namespace SimpleZipDrive.Services;

/// <summary>
///     Opens URLs and folders using the platform's default handler.
/// </summary>
internal static class ShellHelper
{
    /// <summary>
    ///     Test hook that replaces the real <see cref="Process.Start(ProcessStartInfo)" /> call.
    ///     When <see langword="null" /> (production), the process is started normally.
    /// </summary>
    internal static Action<ProcessStartInfo>? ProcessLauncherOverride { get; set; }

    /// <summary>
    ///     Opens the supplied URL with the platform's default handler.
    /// </summary>
    /// <param name="url">The URL to open.</param>
    /// <returns><see langword="true" /> when the handler was launched; otherwise <see langword="false" />.</returns>
    public static bool OpenUrl(string url)
    {
        if (OperatingSystem.IsWindows())
            return OpenUrlOnWindows(url);

        try
        {
            Launch(new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open") { Arguments = url });
            return true;
        }
        catch (Exception ex)
        {
            return ReportFailure($"ShellHelper.OpenUrl: Could not open URL '{url}'",
                $"Could not open URL: {ex.Message}", ex);
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
                Launch(new ProcessStartInfo("explorer.exe", $"/root,\"{path}\"") { UseShellExecute = true });
            else if (OperatingSystem.IsMacOS())
                Launch(new ProcessStartInfo("open", path));
            else
                Launch(new ProcessStartInfo("xdg-open", path));

            return true;
        }
        catch (Exception ex)
        {
            return ReportFailure($"ShellHelper.OpenFolder: Could not open folder '{path}'",
                $"Could not open folder: {ex.Message}", ex);
        }
    }

    private static bool OpenUrlOnWindows(string url)
    {
        try
        {
            Launch(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            // Shell execution can fail on partially broken Windows installations ("Class not
            // registered") or when no handler is associated with the URL protocol (bug reports
            // #68197/#68198). Retry through the URL protocol handler (url.dll), which runs
            // outside this process's shell context.
            DiagnosticLogger.Log(ex, "ShellHelper.OpenUrl: shell launch failed; trying the URL protocol handler");
        }

        try
        {
            Launch(new ProcessStartInfo("rundll32.exe")
            {
                ArgumentList = { "url.dll,FileProtocolHandler", url },
                UseShellExecute = false,
                CreateNoWindow = true
            });
            return true;
        }
        catch (Exception ex)
        {
            return ReportFailure($"ShellHelper.OpenUrl: Could not open URL '{url}'",
                $"Could not open URL: {ex.Message}", ex);
        }
    }

    private static void Launch(ProcessStartInfo startInfo)
    {
        var launcherOverride = ProcessLauncherOverride;
        if (launcherOverride is not null)
        {
            launcherOverride(startInfo);
            return;
        }

        Process.Start(startInfo);
    }

    /// <summary>
    ///     Reports an open failure exactly once: the exception (with its stack trace) is sent
    ///     through the silent-exception pipeline, and the message is mirrored to the in-app log at
    ///     information level. An error-level mirror would forward a second, message-only bug report
    ///     to the API for the same event (bug reports #68197/#68198).
    /// </summary>
    /// <returns>Always <see langword="false" />, so callers can return the result directly.</returns>
    private static bool ReportFailure(string context, string logMessage, Exception ex)
    {
        ErrorLoggerStatic.ReportSilentException(ex, context, true);
        ServiceProvider.TryGet<ILoggingService>()?.Log(logMessage);
        return false;
    }
}
