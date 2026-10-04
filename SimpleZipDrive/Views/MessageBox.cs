namespace SimpleZipDrive.Views;

/// <summary>
///     Cross-platform modal message box with an API compatible with the WPF
///     <c>System.Windows.MessageBox</c> surface used by the mount backends.
/// </summary>
public static class MessageBox
{
    /// <summary>
    ///     Shows a message box with an OK button and no icon.
    /// </summary>
    /// <param name="messageBoxText">The message to display.</param>
    /// <param name="caption">The window title.</param>
    /// <returns>The button the user clicked.</returns>
    public static MessageBoxResult Show(string messageBoxText, string caption)
    {
        return Show(messageBoxText, caption, MessageBoxButton.Ok, MessageBoxImage.None);
    }

    /// <summary>
    ///     Shows a message box with the specified buttons and no icon.
    /// </summary>
    /// <param name="messageBoxText">The message to display.</param>
    /// <param name="caption">The window title.</param>
    /// <param name="button">The buttons to show.</param>
    /// <returns>The button the user clicked.</returns>
    public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button)
    {
        return Show(messageBoxText, caption, button, MessageBoxImage.None);
    }

    /// <summary>
    ///     Shows a message box with the specified buttons and icon.
    /// </summary>
    /// <param name="messageBoxText">The message to display.</param>
    /// <param name="caption">The window title.</param>
    /// <param name="button">The buttons to show.</param>
    /// <param name="icon">The icon to show.</param>
    /// <returns>The button the user clicked.</returns>
    public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button,
        MessageBoxImage icon)
    {
        return ModalDialogHost.Show(
            () => new MessageBoxWindow(messageBoxText, caption, button, icon),
            static window => ((MessageBoxWindow)window).Result);
    }
}
