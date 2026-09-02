using System.Windows;
using System.Windows.Input;

namespace SimpleZipDrive.Core.Views;

/// <summary>
///     WPF dialog that prompts the user for a password to open an encrypted archive.
/// </summary>
public partial class PasswordWindow
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="PasswordWindow" /> class.
    /// </summary>
    /// <param name="archivePath">Full path to the archive file (used to display the file name).</param>
    /// <param name="archiveType">Archive format identifier (e.g., "zip", "7z", "rar") shown in the dialog.</param>
    public PasswordWindow(string archivePath, string archiveType)
    {
        InitializeComponent();
        ArchiveTypeRun.Text = archiveType.ToUpperInvariant();
        ArchiveNameText.Text = Path.GetFileName(archivePath);
        Loaded += (_, _) => PasswordBox.Focus();
    }

    /// <summary>Gets the password entered by the user, or <see langword="null" /> if the dialog was cancelled.</summary>
    public string? Password { get; private set; }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        CompleteDialog(true);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        CompleteDialog(false);
    }

    /// <summary>
    ///     Completes the dialog with the given result. Guards against repeated invocations
    ///     (e.g. held-down Enter/Escape key auto-repeat firing after the dialog session has
    ///     already ended), which would otherwise throw InvalidOperationException when setting
    ///     DialogResult on a window no longer shown as a dialog.
    /// </summary>
    private void CompleteDialog(bool result)
    {
        Password = result ? PasswordBox.Password : null;
        PasswordBox.Clear();
        try
        {
            DialogResult = result;
        }
        catch (InvalidOperationException)
        {
            // Dialog session already ended; just close below.
        }

        Close();
    }

    /// <summary>
    ///     Clears the stored password and the password input field.
    /// </summary>
    public void ClearPassword()
    {
        Password = null;
        PasswordBox.Clear();
    }

    private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                Ok_Click(sender, e);
                break;
            case Key.Escape:
                Cancel_Click(sender, e);
                break;
        }
    }
}