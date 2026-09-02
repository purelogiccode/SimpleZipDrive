namespace SimpleZipDrive.Core;

/// <summary>
///     Static wrapper for the ErrorLogger instance for backward compatibility.
///     Provides global access to a singleton ErrorLogger instance.
/// </summary>
public static class ErrorLoggerStatic
{
    private static readonly Lazy<ErrorLogger> LazyInstance = new(static () => new ErrorLogger());

    /// <summary>
    ///     Gets the singleton ErrorLogger instance.
    /// </summary>
    public static ErrorLogger Instance => LazyInstance.Value;

    /// <summary>
    ///     Initializes global exception handlers to catch all unhandled exceptions.
    ///     Must be called once at application startup.
    /// </summary>
    public static void InitializeGlobalExceptionHandlers()
    {
        LazyInstance.Value.InitializeGlobalExceptionHandlers();
    }

    /// <summary>
    ///     Reports an exception that was silently caught. Use this for exceptions that were
    ///     previously being ignored with empty catch blocks.
    /// </summary>
    /// <param name="ex">The exception that was caught.</param>
    /// <param name="context">Description of where/why the exception occurred.</param>
    /// <param name="silent">If true, only logs to file without showing console output.</param>
    public static void ReportSilentException(Exception ex, string context, bool silent = false)
    {
        ErrorLogger.ReportSilentException(ex, context, silent);
    }

    /// <summary>
    ///     Blocks until all in-flight bug report POSTs have completed, or the timeout elapses.
    ///     No-op when the singleton has never been created. Call during shutdown before disposing.
    /// </summary>
    /// <param name="timeout">Maximum time to wait for the in-flight reports to finish.</param>
    public static void WaitForPendingReports(TimeSpan timeout)
    {
        if (LazyInstance.IsValueCreated)
            LazyInstance.Value.WaitForPendingReports(timeout);
    }

    /// <summary>
    ///     Logs an error synchronously. This method blocks until logging is complete
    ///     and the API call has finished (or timed out after 30 seconds).
    ///     Use this when the application is about to exit or crash.
    /// </summary>
    /// <param name="ex">The exception to log.</param>
    /// <param name="contextMessage">Additional context about where the error occurred.</param>
    public static void LogErrorSync(Exception? ex, string? contextMessage = null)
    {
        LazyInstance.Value.LogErrorSync(ex, contextMessage);
    }

    /// <summary>
    ///     Logs an error asynchronously. This method returns immediately and logs in the background.
    ///     Use this for normal error handling where the application continues running.
    /// </summary>
    /// <param name="ex">The exception to log.</param>
    /// <param name="contextMessage">Additional context about where the error occurred.</param>
    /// <param name="cancellationToken">Cancellation token for the async operation.</param>
    public static Task LogErrorAsync(Exception? ex, string? contextMessage = null,
        CancellationToken cancellationToken = default)
    {
        return ErrorLogger.LogErrorAsync(ex, contextMessage, cancellationToken);
    }
}