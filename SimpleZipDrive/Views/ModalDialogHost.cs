using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace SimpleZipDrive.Views;

/// <summary>
///     Hosts the application's synchronous modal dialogs (message boxes and password prompts)
///     on the Avalonia UI thread.
/// </summary>
/// <remarks>
///     The dialogs keep a synchronous API because they are raised from backend filesystem
///     callbacks. The UI thread runs a nested dispatcher loop until the dialog closes;
///     concurrent callers from background threads are serialized by a gate acquired before the
///     dispatcher is entered, so two dialogs never nest their loops (which would leave the
///     first caller blocked until the second dialog closes as well).
/// </remarks>
internal static class ModalDialogHost
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>
    ///     Creates and shows a modal dialog, blocking until it is closed.
    /// </summary>
    /// <typeparam name="T">The dialog result type.</typeparam>
    /// <param name="createDialog">Factory that creates the dialog (runs on the UI thread).</param>
    /// <param name="readResult">Reads the result from the closed dialog (runs on the UI thread).</param>
    /// <returns>The value returned by <paramref name="readResult" />.</returns>
    public static T Show<T>(Func<Window> createDialog, Func<Window, T> readResult)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            // Acquire the gate on the calling thread, before entering the UI thread, so a
            // second background prompt waits instead of nesting a dispatcher loop.
            Gate.Wait();
            try
            {
                return Dispatcher.UIThread.Invoke(() => Run(createDialog, readResult));
            }
            finally
            {
                Gate.Release();
            }
        }

        // Already on the UI thread (for example a dialog raised from a UI event handler):
        // never block on the gate, because another modal dialog may own the nested loop.
        if (!Gate.Wait(0))
            return Run(createDialog, readResult);

        try
        {
            return Run(createDialog, readResult);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    ///     Returns a window Avalonia can use as a dialog owner: the most recently active visible
    ///     window (so a dialog raised from a modal settings window is owned by it), falling back
    ///     to the main window and then to any other visible window.
    /// </summary>
    /// <returns>
    ///     The owner window, or <see langword="null" /> when no desktop lifetime exists or no
    ///     visible window can own a dialog.
    /// </returns>
    /// <remarks>
    ///     Avalonia throws <see cref="InvalidOperationException" /> ("Cannot show window with
    ///     non-visible owner.") when the owner is hidden or already closed. That happens when a
    ///     mount starts before the main window is shown or after it was hidden/closed (bug
    ///     reports #70017-#70042), so only windows that can actually host a dialog are returned.
    /// </remarks>
    private static Window? GetOwnerWindow()
    {
        var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        if (lifetime is null)
            return null;

        for (var i = lifetime.Windows.Count - 1; i >= 0; i--)
        {
            var window = lifetime.Windows[i];
            if (window.IsActive && CanOwnDialog(window))
                return window;
        }

        if (CanOwnDialog(lifetime.MainWindow))
            return lifetime.MainWindow;

        for (var i = lifetime.Windows.Count - 1; i >= 0; i--)
        {
            var window = lifetime.Windows[i];
            if (CanOwnDialog(window))
                return window;
        }

        return null;
    }

    /// <summary>
    ///     Determines whether a window is currently able to own a dialog.
    /// </summary>
    /// <param name="window">The candidate owner.</param>
    /// <returns>
    ///     <see langword="true" /> when the window is visible and still has a platform window;
    ///     otherwise <see langword="false" />.
    /// </returns>
    private static bool CanOwnDialog(Window? window)
    {
        return window is { IsVisible: true, PlatformImpl: not null };
    }

    private static T Run<T>(Func<Window> createDialog, Func<Window, T> readResult)
    {
        if (!Dispatcher.UIThread.SupportsRunLoops)
            throw new InvalidOperationException("Modal dialogs require a desktop application lifetime.");

        var dialog = createDialog();
        var owner = GetOwnerWindow();

        var closed = new CancellationTokenSource();
        var closeHandler = new DialogCloseHandler(closed);

        dialog.Closed += closeHandler.OnClosed;

        try
        {
            if (owner != null && owner != dialog && CanOwnDialog(owner))
                _ = dialog.ShowDialog(owner);
            else
                ShowWithoutOwner(dialog);

            // Nested dispatcher loop: keeps the modal dialog responsive while the calling
            // (possibly background) thread blocks until the user closes it. The token is
            // cancelled from the Closed handler, so the loop cannot outlive the dialog.
            Dispatcher.UIThread.MainLoop(closed.Token);

            return readResult(dialog);
        }
        finally
        {
            dialog.Closed -= closeHandler.OnClosed;
            closed.Dispose();
        }
    }

    /// <summary>
    ///     Shows a dialog when no visible window can own it (for example a command-line mount
    ///     that starts before the main window is shown, or a mount still finishing after the
    ///     window was closed). The window is made topmost so the prompt cannot get lost behind
    ///     other applications; <see cref="WindowStartupLocation.CenterOwner" /> dialogs fall
    ///     back to the screen center when they have no owner.
    /// </summary>
    /// <param name="dialog">The dialog to show.</param>
    private static void ShowWithoutOwner(Window dialog)
    {
        dialog.Topmost = true;
        dialog.Show();
    }

    /// <summary>
    ///     Cancels the nested-loop token when the dialog closes. A handler object is used instead
    ///     of a lambda so the disposable token source is not captured by a closure that outlives
    ///     the method scope.
    /// </summary>
    private sealed class DialogCloseHandler(CancellationTokenSource closed)
    {
        public void OnClosed(object? sender, EventArgs e)
        {
            closed.Cancel();
        }
    }
}
