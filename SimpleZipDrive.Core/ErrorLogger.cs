using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using Serilog;
using SimpleZipDrive.Core.Logging;

namespace SimpleZipDrive.Core;

/// <summary>
///     Centralized error logging and bug reporting service implementation.
///     Sends all non-user errors to the remote bug report API with full environment and exception details.
/// </summary>
public class ErrorLogger : IDisposable
{
    private const string ApiKey = "hjh7yu6t56tyr540o9u8767676r5674534453235264c75b6t7ggghgg76trf564e";
    private const string BugReportApiUrl = "https://www.purelogiccode.com/bugreport/api/send-bug-report";

    private static volatile bool _suppressApiCalls;

    private readonly string _baseDirectory;

    private readonly HttpClient _httpClient;

    // In-flight fire-and-forget bug report POSTs launched by ForwardLogEventToApi. Tracked so they
    // can be drained during shutdown before the HttpClient is disposed (otherwise a report queued
    // moments before exit would fault with ObjectDisposedException and be lost).
    private readonly ConcurrentDictionary<Task, byte> _pendingApiTasks = new();

    /// <summary>
    ///     Creates a new instance of the ErrorLogger.
    /// </summary>
    /// <param name="logFilePath">Optional custom log file path. If not provided, uses default location.</param>
    public ErrorLogger(string? logFilePath = null)
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        _baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        ErrorLogFilePath = logFilePath ?? Path.Combine(_baseDirectory, "error.log");
    }

    /// <summary>Gets or sets the application name included in bug report payloads.</summary>
    public static string ApplicationName { get; set; } =
        Assembly.GetEntryAssembly()?.GetName().Name ?? "SimpleZipDrive";

    /// <summary>
    ///     When true, all API calls for bug reports are suppressed (no HTTP requests are made).
    ///     Use this in test environments to prevent sending test-generated errors to the bug report API.
    /// </summary>
    public static bool SuppressApiCalls
    {
        get => _suppressApiCalls;
        set => _suppressApiCalls = value;
    }

    /// <summary>
    ///     Gets or sets the error log file path. Used for testing.
    /// </summary>
    internal string ErrorLogFilePath { get; set; }

    /// <summary>
    ///     Disposes the HttpClient instance.
    /// </summary>
    public void Dispose()
    {
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    ///     Initializes global exception handlers to catch all unhandled exceptions.
    ///     Must be called once at application startup.
    /// </summary>
    public void InitializeGlobalExceptionHandlers()
    {
        // Catch UI thread exceptions (WPF) only when running in a WPF context
        var wpfApp = Application.Current;
        if (wpfApp != null)
        {
            // Use async logging to avoid blocking the UI thread for up to 30 seconds
            wpfApp.DispatcherUnhandledException += (_, args) =>
            {
                const string context = "Unhandled exception in UI thread (Dispatcher)";
                FireAndForgetAsync(LogErrorAsync(args.Exception, context));
                args.Handled = true; // Prevent application crash
            };
        }

        // Catch non-UI thread exceptions
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            const string context = "Unhandled exception in AppDomain";
            if (args.ExceptionObject is Exception ex)
                LogErrorSync(ex, context);
            else
                LogErrorSync(null, $"{context} - Exception object: {args.ExceptionObject}");
        };

        // Catch TaskScheduler unobserved task exceptions
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            const string context = "Unobserved task exception";
            LogErrorSync(args.Exception, context);
            args.SetObserved(); // Prevent application crash
        };
    }

    /// <summary>
    ///     Reports an exception that was silently caught. Use this for exceptions that were
    ///     previously being ignored with empty catch blocks.
    /// </summary>
    /// <param name="ex">The exception that was caught.</param>
    /// <param name="context">Description of where/why the exception occurred.</param>
    /// <param name="silent">Retained for backwards compatibility; no longer affects behavior.</param>
    public static void ReportSilentException(Exception ex, string context, bool silent = false)
    {
        _ = silent;
        DiagnosticLogger.Log(ex, $"[SILENT] {context}");

        // Route through the single Serilog pipeline at Warning. BugReportSink forwards the event to the
        // remote API (expected user/environment errors are filtered out inside the sink).
        try
        {
            Log.Warning(ex, "[SILENT] {Context}", context);
        }
        catch
        {
            // Logging must never throw.
        }
    }

    /// <summary>
    ///     Logs an error synchronously. This method blocks until the API call has finished
    ///     (or timed out after 30 seconds). Use this when the application is about to exit or crash,
    ///     where the fire-and-forget <see cref="Logging.BugReportSink" /> could be lost before the process exits.
    /// </summary>
    /// <param name="ex">The exception to log.</param>
    /// <param name="contextMessage">Additional context about where the error occurred.</param>
    public void LogErrorSync(Exception? ex, string? contextMessage = null)
    {
        var originalWasNull = ex == null;
        ex ??= new ArgumentNullException(nameof(ex),
            "ErrorLogger.LogErrorSync was called with a null exception object.");
        contextMessage ??= "No additional context provided.";

        DiagnosticLogger.LogSection("ERROR (SYNC)");
        DiagnosticLogger.Log(ex, contextMessage);

        // Emit to the pipeline (file + UI) but suppress the sink's fire-and-forget forward: this path
        // performs its own *synchronous* POST below so the crash report is not lost when the process exits.
        try
        {
            Log.ForContext(BugReportSink.SkipProperty, true).Fatal(ex, "{Context}", contextMessage);
        }
        catch
        {
            // Logging must never throw.
        }

        if (originalWasNull || SuppressApiCalls || IsUserError(ex))
            return;

        // Synchronously wait for the API call to complete (with timeout). Using Task.Run avoids
        // sync-over-async deadlocks in contexts that carry a synchronization context.
        CancellationTokenSource? cts = null;
        try
        {
            cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            // ReSharper disable once AccessToDisposedClosure
            var apiTask = Task.Run(async () => await SendLogToApiAsync(ex, contextMessage, cts.Token), cts.Token);
            try
            {
                apiTask.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                // Timed out; the event is still captured locally in the session log.
            }
        }
        catch (Exception apiEx)
        {
            WriteToCriticalLog(apiEx, "Exception in SendLogToApiAsync from LogErrorSync.");
        }
        finally
        {
            cts?.Dispose();
        }
    }

    /// <summary>
    ///     Logs an error asynchronously by routing it through the single Serilog pipeline.
    ///     <see cref="Logging.BugReportSink" /> forwards warning-and-above events to the remote API.
    /// </summary>
    /// <param name="ex">The exception to log.</param>
    /// <param name="contextMessage">Additional context about where the error occurred.</param>
    /// <param name="cancellationToken">Unused; retained for backwards compatibility.</param>
    public static Task LogErrorAsync(Exception? ex, string? contextMessage = null,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        var originalWasNull = ex == null;
        ex ??= new ArgumentNullException(nameof(ex),
            "ErrorLogger.LogErrorAsync was called with a null exception object.");
        contextMessage ??= "No additional context provided.";

        try
        {
            // A null exception is a caller bug, not an app error worth reporting: log it but skip the API.
            if (originalWasNull)
                Log.ForContext(BugReportSink.SkipProperty, true).Error(ex, "{Context}", contextMessage);
            else
                Log.Error(ex, "{Context}", contextMessage);
        }
        catch
        {
            // Logging must never throw.
        }

        return Task.CompletedTask;
    }

    /// <summary>
    ///     Fire-and-forget a task without suppressing the compiler warning. Handles any unobserved exceptions.
    /// </summary>
    internal static async void FireAndForgetAsync(Task task)
    {
        try
        {
            await task;
        }
        catch
        {
            // Task failures are silently ignored — the task's own error handling should cover this
        }
    }

    private static (string Version, string OsDescription, string OsArchitecture) GetBasicEnvironmentInfo()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "Unknown";
        var osDescription = RuntimeInformation.OSDescription;
        var osArchitecture = RuntimeInformation.OSArchitecture.ToString();
        return (version, osDescription, osArchitecture);
    }

    internal string GetEnvironmentDetails()
    {
        var (version, osDescription, osArchitecture) = GetBasicEnvironmentInfo();
        var processorCount = Environment.ProcessorCount;
        var tempPath = Path.GetTempPath();

        // Determine Windows version details
        var windowsVersion = Environment.OSVersion.VersionString;
        var bitness = Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit";

        var envDetails = new StringBuilder();
        envDetails.AppendLine("=== Environment Details ===");
        envDetails.AppendLine(CultureInfo.InvariantCulture, $"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}");
        envDetails.AppendLine(CultureInfo.InvariantCulture, $"Application Name: {ApplicationName}");
        envDetails.AppendLine(CultureInfo.InvariantCulture, $"Application Version: {version}");
        envDetails.AppendLine(CultureInfo.InvariantCulture, $"OS Version: {osDescription}");
        envDetails.AppendLine(CultureInfo.InvariantCulture, $"Architecture: {osArchitecture}");
        envDetails.AppendLine(CultureInfo.InvariantCulture, $"Bitness: {bitness}");
        envDetails.AppendLine(CultureInfo.InvariantCulture, $"Windows Version: {windowsVersion}");
        envDetails.AppendLine(CultureInfo.InvariantCulture, $"Processor Count: {processorCount}");
        envDetails.AppendLine(CultureInfo.InvariantCulture, $"Base Directory: {_baseDirectory}");
        envDetails.AppendLine(CultureInfo.InvariantCulture, $"Temp Path: {tempPath}");

        return envDetails.ToString();
    }

    internal static string GetErrorDetails(Exception ex, string contextMessage)
    {
        var errorDetails = new StringBuilder();
        errorDetails.AppendLine("=== Error Details ===");
        errorDetails.AppendLine(CultureInfo.InvariantCulture, $"Error message: {contextMessage} - {ex.Message}");

        return errorDetails.ToString();
    }

    internal static string GetExceptionDetails(Exception ex)
    {
        var exceptionDetails = new StringBuilder();
        exceptionDetails.AppendLine("=== Exception Details ===");
        exceptionDetails.AppendLine(CultureInfo.InvariantCulture, $"Type: {ex.GetType().Name}");
        exceptionDetails.AppendLine(CultureInfo.InvariantCulture, $"Message: {ex.Message}");
        exceptionDetails.AppendLine(CultureInfo.InvariantCulture, $"Source: {ex.Source ?? "Unknown"}");
        exceptionDetails.AppendLine(CultureInfo.InvariantCulture,
            $"StackTrace: {ex.StackTrace ?? "No stack trace available"}");

        if (ex.InnerException != null)
        {
            exceptionDetails.AppendLine(CultureInfo.InvariantCulture, $"\n{AppTheme.Section("Inner Exception")}");
            exceptionDetails.AppendLine(CultureInfo.InvariantCulture, $"Type: {ex.InnerException.GetType().Name}");
            exceptionDetails.AppendLine(CultureInfo.InvariantCulture, $"Message: {ex.InnerException.Message}");
            exceptionDetails.AppendLine(CultureInfo.InvariantCulture,
                $"Source: {ex.InnerException.Source ?? "Unknown"}");
            exceptionDetails.AppendLine(CultureInfo.InvariantCulture,
                $"StackTrace: {ex.InnerException.StackTrace ?? "No stack trace available"}");
        }

        return exceptionDetails.ToString();
    }

    internal static bool IsUserError(Exception? ex)
    {
        switch (ex)
        {
            case null:
                return false;
            // OperationCanceledException is expected during shutdown - not a bug
            case OperationCanceledException or TaskCanceledException:
                return true;
        }

        // Check exception types first (most reliable)
        var exceptionType = ex.GetType();
        var typeName = exceptionType.Name;
        var fullName = exceptionType.FullName ?? string.Empty;

        // SharpCompress archive-related exceptions indicate user-provided invalid files
        if (fullName.StartsWith("SharpCompress.", StringComparison.OrdinalIgnoreCase))
        {
            // ArchiveException, InvalidFormatException, etc. from SharpCompress are user errors
            if (typeName.Contains("Archive", StringComparison.OrdinalIgnoreCase) ||
                typeName.Contains("Format", StringComparison.OrdinalIgnoreCase) ||
                typeName.Contains("Invalid", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // Dokan exceptions related to drive letter assignment
        if (fullName.StartsWith("DokanNet.", StringComparison.OrdinalIgnoreCase))
        {
            // Drive-related Dokan exceptions are typically user/environment errors
            if (typeName.Contains("Drive", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("drive letter", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // WinFsp errors
        if (fullName.StartsWith("Fsp.", StringComparison.OrdinalIgnoreCase))
        {
            if (typeName.Contains("Drive", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("drive letter", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        switch (ex)
        {
            // TypeInitializationException for Fsp.Interop.Api is an environment issue (missing/outdated WinFsp), not an app bug
            case TypeInitializationException:
            {
                var innerType = ex.InnerException?.GetType().FullName ?? string.Empty;
                if (innerType.Contains("Fsp.Interop", StringComparison.OrdinalIgnoreCase))
                    return true;

                break;
            }
            // NullReferenceException from SharpCompress is a known library limitation, not an app bug
            case NullReferenceException
                when (ex.Source?.Contains("SharpCompress", StringComparison.OrdinalIgnoreCase) == true):
                return true;
            case NullReferenceException:
            {
                var stackTrace = ex.StackTrace ?? string.Empty;
                if (stackTrace.Contains("SharpCompress", StringComparison.OrdinalIgnoreCase))
                    return true;

                break;
            }
        }

        switch (ex)
        {
            // File-related exceptions are typically user errors (file not found, access denied, etc.)
            case FileNotFoundException or DirectoryNotFoundException or UnauthorizedAccessException or IOException:
            // A canceled HTTP request (e.g. shutdown racing the update check) is expected and not a
            // bug. Other HttpRequestExceptions may be real application errors and stay reportable -
            // the update check handles its own network noise quietly before reaching this point.
            case HttpRequestException
                when ex.InnerException is OperationCanceledException or TaskCanceledException:
            // Archive decryption failures (wrong password, encrypted data) originate from
            // SharpCompress. A CryptographicException from any other source may be a real
            // application bug (e.g. protected-data misuse) and must stay reportable.
            case CryptographicException
                when ex.Source?.Contains("SharpCompress", StringComparison.OrdinalIgnoreCase) == true ||
                     ex.StackTrace?.Contains("SharpCompress", StringComparison.OrdinalIgnoreCase) == true:
            // Binary/architecture mismatches (e.g. x64 native DLL loaded in an ARM64 process)
            // are environment problems, not application bugs.
            case BadImageFormatException:
                return true;
        }

        // Index/array exceptions originating from SharpCompress are corrupt-archive symptoms
        // (mirrors the existing NullReferenceException handling below).
        if (ex is IndexOutOfRangeException or ArrayTypeMismatchException &&
            (ex.Source?.Contains("SharpCompress", StringComparison.OrdinalIgnoreCase) == true ||
             ex.StackTrace?.Contains("SharpCompress", StringComparison.OrdinalIgnoreCase) == true))
        {
            return true;
        }

        // Fallback to message-based detection for cases where exception types aren't specific enough
        // These patterns cover common SharpCompress errors for corrupt/non-archive files
        var messageLower = ex.Message.ToLowerInvariant();

        // SharpCompress archive format errors
        var isArchiveError =
            messageLower.Contains("cannot find central directory",
                StringComparison.OrdinalIgnoreCase) || // ZIP format error
            messageLower.Contains("invalid archive", StringComparison.OrdinalIgnoreCase) ||
            messageLower.Contains("unknown format", StringComparison.OrdinalIgnoreCase) ||
            messageLower.Contains("not a valid", StringComparison.OrdinalIgnoreCase) ||
            messageLower.Contains("not a supported archive", StringComparison.OrdinalIgnoreCase) ||
            (messageLower.Contains("corrupt", StringComparison.OrdinalIgnoreCase) &&
             (messageLower.Contains("archive", StringComparison.OrdinalIgnoreCase) ||
              messageLower.Contains("file", StringComparison.OrdinalIgnoreCase) ||
              messageLower.Contains("zip", StringComparison.OrdinalIgnoreCase) ||
              messageLower.Contains("format", StringComparison.OrdinalIgnoreCase) ||
              messageLower.Contains("header", StringComparison.OrdinalIgnoreCase))) ||
            (messageLower.Contains("header", StringComparison.OrdinalIgnoreCase) &&
             messageLower.Contains("invalid", StringComparison.OrdinalIgnoreCase));

        // Dokan drive-related errors
        var isDriveError =
            messageLower.Contains("can't assign a drive letter", StringComparison.OrdinalIgnoreCase) ||
            (messageLower.Contains("drive letter", StringComparison.OrdinalIgnoreCase) &&
             messageLower.Contains("in use", StringComparison.OrdinalIgnoreCase)) ||
            (messageLower.Contains("mount point", StringComparison.OrdinalIgnoreCase) &&
             messageLower.Contains("invalid", StringComparison.OrdinalIgnoreCase)) ||
            (messageLower.Contains("mount point", StringComparison.OrdinalIgnoreCase) &&
             messageLower.Contains("already in use", StringComparison.OrdinalIgnoreCase));

        // Archive data problems reported as plain log messages (no exception object attached):
        // truncated/corrupt archives, locked files, missing files.
        var isArchiveDataError =
            messageLower.Contains("unknown rar header", StringComparison.OrdinalIgnoreCase) ||
            (messageLower.Contains("cannot seek to position", StringComparison.OrdinalIgnoreCase) &&
             messageLower.Contains("end of stream reached", StringComparison.OrdinalIgnoreCase)) ||
            messageLower.Contains("being used by another process", StringComparison.OrdinalIgnoreCase) ||
            messageLower.Contains("archive file not found", StringComparison.OrdinalIgnoreCase);

        // Password-related errors (user can retry with correct password)
        var isPasswordError =
            messageLower.Contains("password required", StringComparison.OrdinalIgnoreCase) ||
            messageLower.Contains("wrong password", StringComparison.OrdinalIgnoreCase) ||
            messageLower.Contains("incorrect password", StringComparison.OrdinalIgnoreCase) ||
            messageLower.Contains("invalid password", StringComparison.OrdinalIgnoreCase) ||
            messageLower.Contains("missing password", StringComparison.OrdinalIgnoreCase) ||
            messageLower.Contains("no password", StringComparison.OrdinalIgnoreCase) ||
            messageLower.Contains("password is", StringComparison.OrdinalIgnoreCase) ||
            messageLower.Contains("password did not match",
                StringComparison.OrdinalIgnoreCase) || // SharpCompress CryptographicException text
            messageLower.Contains("requires a password", StringComparison.OrdinalIgnoreCase) ||
            messageLower.Contains("need a password", StringComparison.OrdinalIgnoreCase) ||
            (messageLower.Contains("encrypted", StringComparison.OrdinalIgnoreCase) &&
             (messageLower.Contains("file", StringComparison.OrdinalIgnoreCase) ||
              messageLower.Contains("archive", StringComparison.OrdinalIgnoreCase) ||
              messageLower.Contains("entry", StringComparison.OrdinalIgnoreCase)));

        // Cancellation-related messages
        var isCancellationError =
            messageLower.Contains("canceled", StringComparison.OrdinalIgnoreCase) ||
            messageLower.Contains("cancelled", StringComparison.OrdinalIgnoreCase);

        // Expected environment conditions: missing/outdated file-system drivers (Dokan, WinFsp)
        // and mount-point collisions are user/environment issues, not application bugs. They
        // already surface as clear dialogs, so forwarding them only floods the bug report API.
        var isEnvironmentError =
            (messageLower.Contains("winfsp", StringComparison.OrdinalIgnoreCase) &&
             messageLower.Contains("not found", StringComparison.OrdinalIgnoreCase)) ||
            (messageLower.Contains("winfsp", StringComparison.OrdinalIgnoreCase) &&
             messageLower.Contains("version mismatch", StringComparison.OrdinalIgnoreCase)) ||
            messageLower.Contains("incorrect dll version", StringComparison.OrdinalIgnoreCase) ||
            (messageLower.Contains("winfsp", StringComparison.OrdinalIgnoreCase) &&
             messageLower.Contains("not running", StringComparison.OrdinalIgnoreCase)) ||
            (messageLower.Contains("winfsp", StringComparison.OrdinalIgnoreCase) &&
             messageLower.Contains("could not be loaded", StringComparison.OrdinalIgnoreCase)) ||
            // Assembly-load failure of the WinFsp interop (winfsp-msil.dll missing beside the
            // executable, e.g. removed by antivirus) — an environment issue with its own dialog.
            (messageLower.Contains("could not load file or assembly", StringComparison.OrdinalIgnoreCase) &&
             messageLower.Contains("winfsp-msil", StringComparison.OrdinalIgnoreCase)) ||
            (messageLower.Contains("winfsp", StringComparison.OrdinalIgnoreCase) &&
             messageLower.Contains("mount failed with status", StringComparison.OrdinalIgnoreCase) &&
             (messageLower.Contains("0xc0000035", StringComparison.OrdinalIgnoreCase) ||
              messageLower.Contains("0xc0000038", StringComparison.OrdinalIgnoreCase) ||
              messageLower.Contains("0xc000003a", StringComparison.OrdinalIgnoreCase) ||
              messageLower.Contains("0xc000000e", StringComparison.OrdinalIgnoreCase) ||
              messageLower.Contains("0xc0000022", StringComparison.OrdinalIgnoreCase) ||
              messageLower.Contains("0xc000009a", StringComparison.OrdinalIgnoreCase))) ||
            (messageLower.Contains("winfsp", StringComparison.OrdinalIgnoreCase) &&
             messageLower.Contains("already in use", StringComparison.OrdinalIgnoreCase)) ||
            messageLower.Contains("dokan driver not found", StringComparison.OrdinalIgnoreCase) ||
            // Produced at runtime from DokanNet's DokanException.Message ("Can't install the Dokan driver"),
            // e.g. "Dokan error: Can't install the Dokan driver" and "[Warning] ... - Can't install the Dokan driver".
            messageLower.Contains("can't install the dokan driver", StringComparison.OrdinalIgnoreCase);

        return isArchiveError || isArchiveDataError || isDriveError || isPasswordError || isCancellationError ||
               isEnvironmentError;
    }

    /// <summary>
    ///     Forwards a Serilog log event to the remote bug report API. Used by <see cref="Logging.BugReportSink" />
    ///     to satisfy the "warning and above are reported" requirement. Fire-and-forget; never blocks the caller.
    /// </summary>
    /// <param name="level">The Serilog level name (e.g. Warning, Error, Fatal).</param>
    /// <param name="message">The rendered log message.</param>
    /// <param name="ex">The associated exception, if any.</param>
    /// <param name="context">A short description of the log event source.</param>
    internal void ForwardLogEventToApi(string level, string message, Exception? ex, string context)
    {
        if (SuppressApiCalls)
            return;

        var task = Task.Run(async () =>
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                if (ex != null)
                    await SendLogToApiAsync(ex, $"[{level}] {context}", cts.Token);
                else
                    await SendMessageToApiAsync(level, message, context, cts.Token);
            }
            catch
            {
                // Forwarding is best-effort.
            }
        });

        // Track the task so shutdown can wait for it, and self-remove on completion.
        _pendingApiTasks.TryAdd(task, 0);
        _ = task.ContinueWith(
            static (completed, state) => ((ConcurrentDictionary<Task, byte>)state!).TryRemove(completed, out _),
            _pendingApiTasks,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>
    ///     Blocks until all in-flight bug report POSTs launched by <see cref="ForwardLogEventToApi" /> have
    ///     completed, or the timeout elapses. Call during shutdown before <see cref="Dispose" /> so reports
    ///     queued moments before exit are not lost when the underlying <see cref="HttpClient" /> is disposed.
    /// </summary>
    /// <param name="timeout">Maximum time to wait for the in-flight reports to finish.</param>
    public void WaitForPendingReports(TimeSpan timeout)
    {
        var pending = _pendingApiTasks.Keys.ToArray();
        if (pending.Length == 0)
            return;

        try
        {
            Task.WaitAll(pending, timeout);
        }
        catch
        {
            // Best-effort drain; never throw during shutdown.
        }
    }

    private async Task<bool> SendLogToApiAsync(Exception ex, string contextMessage,
        CancellationToken cancellationToken = default)
    {
        if (SuppressApiCalls)
            return false;

        try
        {
            var environmentDetails = GetEnvironmentDetails();
            var errorDetails = GetErrorDetails(ex, contextMessage);
            var exceptionDetails = GetExceptionDetails(ex);

            // Combine all sections into the message field (API max 4000 chars)
            var fullMessage = new StringBuilder();
            fullMessage.Append(environmentDetails);
            fullMessage.Append(errorDetails);
            fullMessage.Append(exceptionDetails);
            var messageText = fullMessage.ToString();

            return await PostBugReportAsync(messageText, contextMessage, ex.StackTrace ?? "No stack trace available",
                cancellationToken);
        }
        catch (Exception apiEx)
        {
            WriteToCriticalLog(apiEx, "Exception occurred while sending log to API.");
            return false;
        }
    }

    private async Task SendMessageToApiAsync(string level, string message, string contextMessage,
        CancellationToken cancellationToken = default)
    {
        if (SuppressApiCalls) return;

        try
        {
            var fullMessage = new StringBuilder();
            fullMessage.Append(GetEnvironmentDetails());
            fullMessage.AppendLine();
            fullMessage.AppendLine(CultureInfo.InvariantCulture, $"=== Log Event ({level}) ===");
            fullMessage.AppendLine(message);

            await PostBugReportAsync(fullMessage.ToString(), contextMessage, "No stack trace available (log event)",
                cancellationToken);
        }
        catch (Exception apiEx)
        {
            WriteToCriticalLog(apiEx, "Exception occurred while sending log message to API.");
        }
    }

    private async Task<bool> PostBugReportAsync(string messageText, string userInfo, string stackTrace,
        CancellationToken cancellationToken)
    {
        var (version, osDescription, _) = GetBasicEnvironmentInfo();

        // Short environment summary for the environment field (API max 50 chars)
        var bitness = Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit";
        var envSummary = $"{osDescription} {bitness}";
        if (envSummary.Length > 50) envSummary = envSummary[..47] + "...";

        var payload = new
        {
            message = messageText,
            applicationName = ApplicationName,
            version,
            userInfo,
            environment = envSummary,
            stackTrace
        };
        var jsonPayload = JsonSerializer.Serialize(payload);
        var httpContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, BugReportApiUrl);
        request.Headers.Add("X-API-KEY", ApiKey);
        request.Content = httpContent;

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.IsSuccessStatusCode) return true;

        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        WriteToCriticalLog(
            new HttpRequestException(
                $"API request failed with status code {response.StatusCode}. Response: {responseContent}"),
            "Error sending log to API.");
        return false;
    }

    internal void WriteToCriticalLog(Exception ex, string contextMessage)
    {
        // Only write to console - do not attempt to write to file as it will fail
        // if the application is in a protected directory (e.g., C:\Program Files)
        // and running as non-admin. Console output is always available.
        Console.Error.WriteLine($"FATAL: Could not write to error log '{ErrorLogFilePath}'.");
        Console.Error.WriteLine($"Context: {contextMessage}");
        Console.Error.WriteLine($"Exception: {ex.GetType().Name} - {ex.Message}");
    }
}