namespace SimpleZipDrive.Services;

/// <summary>
///     Avalonia implementation of <see cref="IPasswordPromptService" />. Shows the password
///     dialog on the UI thread and blocks the calling (background) thread until it closes.
/// </summary>
internal sealed class PasswordPromptService : IPasswordPromptService
{
    /// <inheritdoc />
    public string? Prompt(string archivePath, string archiveType)
    {
        return ModalDialogHost.Show(
            () => new PasswordWindow(archivePath, archiveType),
            static window =>
            {
                var passwordWindow = (PasswordWindow)window;
                var password = passwordWindow.Password;
                passwordWindow.ClearPassword();
                return password;
            });
    }
}
