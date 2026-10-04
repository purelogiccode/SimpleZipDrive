using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace SimpleZipDrive.Views;

/// <summary>
///     Cross-platform modal message box with an API compatible with the WPF
///     <c>System.Windows.MessageBox</c> surface used by the mount backends.
/// </summary>
public static class MessageBox
{
    public static MessageBoxResult Show(string messageBoxText, string caption)
    {
        return Show(messageBoxText, caption, MessageBoxButton.Ok, MessageBoxImage.None);
    }

    public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button)
    {
        return Show(messageBoxText, caption, button, MessageBoxImage.None);
    }

    public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button,
        MessageBoxImage icon)
    {
        if (Dispatcher.UIThread.CheckAccess()) return ShowCore(messageBoxText, caption, button, icon);

        return Dispatcher.UIThread.Invoke(() => ShowCore(messageBoxText, caption, button, icon));
    }

    private static MessageBoxResult ShowCore(string messageBoxText, string caption, MessageBoxButton button,
        MessageBoxImage icon)
    {
        var window = new MessageBoxWindow(messageBoxText, caption, button, icon);
        var owner = GetOwnerWindow();

        using var closed = new CancellationTokenSource();
        window.Closed += (_, _) => closed.Cancel();

        if (owner != null && owner != window)
            _ = window.ShowDialog(owner);
        else
            window.Show();

        // Nested dispatcher loop: keeps the modal dialog responsive while the calling
        // (possibly background) thread blocks until the user closes it.
        Dispatcher.UIThread.MainLoop(closed.Token);
        return window.Result;
    }

    internal static Window? GetOwnerWindow()
    {
        return (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
    }
}
