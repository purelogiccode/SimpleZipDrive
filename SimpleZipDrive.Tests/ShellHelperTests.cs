using System.ComponentModel;
using System.Diagnostics;
using SimpleZipDrive.Services;

namespace SimpleZipDrive.Tests;

/// <summary>
///     Tests for <see cref="ShellHelper" />: the launch path per platform, the Windows fallback
///     through the URL protocol handler when shell execution fails (bug reports #68197/#68198),
///     and the failure result when no handler can be launched.
/// </summary>
[Collection("ShellHelper")]
public class ShellHelperTests
{
    private const string TestUrl = "https://example.com/archive";
    private const string TestFolder = @"C:\Temp\Archive";

    [Fact]
    public void OpenUrl_LauncherSucceeds_ReturnsTrueAndLaunchesPlatformHandler()
    {
        var started = new List<ProcessStartInfo>();
        try
        {
            ShellHelper.ProcessLauncherOverride = started.Add;

            Assert.True(ShellHelper.OpenUrl(TestUrl));

            var startInfo = Assert.Single(started);
            if (OperatingSystem.IsWindows())
            {
                Assert.Equal(TestUrl, startInfo.FileName);
                Assert.True(startInfo.UseShellExecute);
            }
            else
            {
                Assert.Equal(OperatingSystem.IsMacOS() ? "open" : "xdg-open", startInfo.FileName);
                Assert.Equal(TestUrl, startInfo.Arguments);
            }
        }
        finally
        {
            ShellHelper.ProcessLauncherOverride = null;
        }
    }

    [Fact]
    public void OpenUrl_ShellLaunchFails_OnWindowsFallsBackToUrlProtocolHandler()
    {
        if (!OperatingSystem.IsWindows()) return;

        var started = new List<ProcessStartInfo>();
        try
        {
            ShellHelper.ProcessLauncherOverride = startInfo =>
            {
                started.Add(startInfo);
                if (started.Count == 1)
                    throw new Win32Exception("Class not registered");
            };

            Assert.True(ShellHelper.OpenUrl(TestUrl));

            Assert.Equal(2, started.Count);
            Assert.Equal(TestUrl, started[0].FileName);
            Assert.True(started[0].UseShellExecute);
            Assert.Equal("rundll32.exe", started[1].FileName);
            Assert.False(started[1].UseShellExecute);
            Assert.Equal(new[] { "url.dll,FileProtocolHandler", TestUrl }, started[1].ArgumentList,
                StringComparer.Ordinal);
        }
        finally
        {
            ShellHelper.ProcessLauncherOverride = null;
        }
    }

    [Fact]
    public void OpenUrl_AllLaunchersFail_ReturnsFalse()
    {
        var attempts = 0;
        try
        {
            ShellHelper.ProcessLauncherOverride = _ =>
            {
                attempts++;
                throw new Win32Exception("Class not registered");
            };

            Assert.False(ShellHelper.OpenUrl(TestUrl));

            // Windows retries through the URL protocol handler; the other platforms try once.
            Assert.Equal(OperatingSystem.IsWindows() ? 2 : 1, attempts);
        }
        finally
        {
            ShellHelper.ProcessLauncherOverride = null;
        }
    }

    [Fact]
    public void OpenFolder_LauncherSucceeds_ReturnsTrueAndLaunchesPlatformHandler()
    {
        var started = new List<ProcessStartInfo>();
        try
        {
            ShellHelper.ProcessLauncherOverride = started.Add;

            Assert.True(ShellHelper.OpenFolder(TestFolder));

            var startInfo = Assert.Single(started);
            if (OperatingSystem.IsWindows())
            {
                Assert.Equal("explorer.exe", startInfo.FileName);
                Assert.Contains($"/root,\"{TestFolder}\"", startInfo.Arguments);
            }
            else
            {
                Assert.Equal(OperatingSystem.IsMacOS() ? "open" : "xdg-open", startInfo.FileName);
                Assert.Equal(TestFolder, startInfo.Arguments);
            }
        }
        finally
        {
            ShellHelper.ProcessLauncherOverride = null;
        }
    }

    [Fact]
    public void OpenFolder_LauncherFails_ReturnsFalseAfterSingleAttempt()
    {
        var attempts = 0;
        try
        {
            ShellHelper.ProcessLauncherOverride = _ =>
            {
                attempts++;
                throw new IOException("launch failed");
            };

            Assert.False(ShellHelper.OpenFolder(TestFolder));
            Assert.Equal(1, attempts);
        }
        finally
        {
            ShellHelper.ProcessLauncherOverride = null;
        }
    }
}
