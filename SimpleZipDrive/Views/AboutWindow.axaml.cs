using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SimpleZipDrive.Views;

/// <summary>
///     About dialog showing the application version, description, license, and links to the
///     third-party components used by the application.
/// </summary>
// Event handlers are wired from AboutWindow.axaml (Click attributes), which ReSharper does not
// trace; suppress its unused-member inspection for them.
[SuppressMessage("ReSharper", "UnusedMember.Local")]
public partial class AboutWindow : Window
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="AboutWindow" /> class and displays the
    ///     current application version.
    /// </summary>
    public AboutWindow()
    {
        InitializeComponent();

        // Show the three-part release version (e.g. 3.1.0), not the four-part assembly
        // version (3.1.0.0) used everywhere else in the app and release notes.
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionTextBlock.Text = version is null
            ? "Version Unknown"
            : $"Version {version.Major}.{version.Minor}.{version.Build}";

        // The credits are platform-specific: WinFsp/DokanNet are Windows-only, FUSE is
        // used on Linux/macOS, and SharpCompress everywhere.
        CreditsWindows.IsVisible = OperatingSystem.IsWindows();
        CreditsUnix.IsVisible = !OperatingSystem.IsWindows();
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void WinFspLink_Click(object? sender, RoutedEventArgs e)
    {
        ShellHelper.OpenUrl("https://github.com/winfsp/winfsp");
    }

    private void DokanLink_Click(object? sender, RoutedEventArgs e)
    {
        ShellHelper.OpenUrl("https://github.com/dokan-dev/dokan-dotnet");
    }

    private void FuseLink_Click(object? sender, RoutedEventArgs e)
    {
        ShellHelper.OpenUrl("https://github.com/libfuse/libfuse");
    }

    private void SharpCompressLink_Click(object? sender, RoutedEventArgs e)
    {
        ShellHelper.OpenUrl("https://github.com/adamhathcock/sharpcompress");
    }

    private void GitHubLink_Click(object? sender, RoutedEventArgs e)
    {
        ShellHelper.OpenUrl("https://github.com/purelogiccode/SimpleZipDrive");
    }

    private void WebsiteLink_Click(object? sender, RoutedEventArgs e)
    {
        ShellHelper.OpenUrl("https://www.purelogiccode.com");
    }
}
