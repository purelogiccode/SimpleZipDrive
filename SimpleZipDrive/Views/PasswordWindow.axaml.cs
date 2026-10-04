using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace SimpleZipDrive.Views;

/// <summary>
///     Dialog that prompts the user for a password to open an encrypted archive.
/// </summary>
// Event handlers are wired from PasswordWindow.axaml (Click/KeyDown attributes), which ReSharper
// does not trace; suppress its unused-member inspection for them.
[SuppressMessage("ReSharper", "UnusedMember.Local")]
public partial class PasswordWindow : Window
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="PasswordWindow" /> class for the XAML loader.
    /// </summary>
    public PasswordWindow()
    {
        InitializeComponent();
    }

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
        Opened += (_, _) => PasswordBox.Focus();
    }

    /// <summary>Gets the password entered by the user, or <see langword="null" /> if the dialog was cancelled.</summary>
    public string? Password { get; private set; }

    /// <summary>
    ///     Clears the stored password and the password input field.
    /// </summary>
    public void ClearPassword()
    {
        Password = null;
        PasswordBox.Text = string.Empty;
    }

    private void Ok_Click(object? sender, RoutedEventArgs e)
    {
        CompleteDialog(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        CompleteDialog(false);
    }

    private void CompleteDialog(bool result)
    {
        Password = result ? PasswordBox.Text : null;
        PasswordBox.Text = string.Empty;
        Close();
    }

    private void PasswordBox_KeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                CompleteDialog(true);
                e.Handled = true;
                break;
            case Key.Escape:
                CompleteDialog(false);
                e.Handled = true;
                break;
        }
    }
}
