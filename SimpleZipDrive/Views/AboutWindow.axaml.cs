using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SimpleZipDrive.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";
        VersionTextBlock.Text = $"Version {version}";
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
}
