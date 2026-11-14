namespace SimpleZipDrive.Core;

/// <summary>
///     Tracks the application shutdown sequence so platform shutdown requests are processed at most
///     once. Avalonia's desktop lifetime keeps its own "shutting down" flag while it runs the
///     synchronous shutdown for a request; a second request that arrives during that window makes it
///     throw <see cref="InvalidOperationException" /> from the native window procedure, which
///     terminates the process as an unhandled <see cref="AppDomain" /> exception (bug reports
///     #68244 and #69869). Duplicate requests are vetoed so the shutdown already in progress (or the
///     graceful unmount that is about to start it) can finish.
/// </summary>
internal sealed class ShutdownCoordinator
{
    private int _shutdownRequested;
    private int _shutdownStarted;

    /// <summary>Gets a value indicating whether the application has started its graceful shutdown.</summary>
    internal bool IsShuttingDown => Volatile.Read(ref _shutdownStarted) != 0;

    /// <summary>
    ///     Marks the start of the graceful shutdown.
    /// </summary>
    /// <returns><see langword="true" /> the first time; <see langword="false" /> if shutdown already started.</returns>
    internal bool TryBeginShutdown() => Interlocked.Exchange(ref _shutdownStarted, 1) == 0;

    /// <summary>
    ///     Decides whether a platform shutdown request may proceed. The first request is accepted;
    ///     every later request is a duplicate - either because Avalonia is still processing the
    ///     previous one or because the application is already unmounting and exiting.
    /// </summary>
    /// <returns><see langword="true" /> when the request may proceed; otherwise <see langword="false" />.</returns>
    internal bool TryAcceptShutdownRequest()
        => !IsShuttingDown && Interlocked.Exchange(ref _shutdownRequested, 1) == 0;
}
