using Avalonia.Threading;

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
        if (Dispatcher.UIThread.CheckAccess()) return PromptCore(archivePath, archiveType);

        return Dispatcher.UIThread.Invoke(() => PromptCore(archivePath, archiveType));
    }

    private static string? PromptCore(string archivePath, string archiveType)
    {
        var window = new PasswordWindow(archivePath, archiveType);
        var owner = MessageBox.GetOwnerWindow();

        using var closed = new CancellationTokenSource();
        window.Closed += (_, _) => closed.Cancel();

        if (owner != null && owner != window)
            _ = window.ShowDialog(owner);
        else
            window.Show();

        Dispatcher.UIThread.MainLoop(closed.Token);

        var password = window.Password;
        window.ClearPassword();
        return password;
    }
}
