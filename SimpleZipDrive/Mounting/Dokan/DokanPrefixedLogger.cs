using System.Globalization;
using DokanNet.Logging;

namespace SimpleZipDrive.Mounting.Dokan;

/// <summary>
///     ILogger wrapper that guarantees the DokanNet prefix is applied to all log messages.
///     Writes directly to Console.Out, which is redirected to LogTextWriter.
/// </summary>
internal sealed class DokanPrefixedLogger : ILogger, IDisposable
{
    private readonly string _prefix;

    /// <summary>
    ///     Initializes a new logger that prefixes every message with <paramref name="prefix" />.
    /// </summary>
    /// <param name="prefix">Prefix prepended to each log line.</param>
    public DokanPrefixedLogger(string prefix)
    {
        _prefix = prefix;
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }

    /// <inheritdoc />
    public bool DebugEnabled => true;

    /// <inheritdoc />
    public void Debug(string message, params object[] args)
    {
        Log("DEBUG", message, args);
    }

    /// <inheritdoc />
    public void Info(string message, params object[] args)
    {
        Log("INFO", message, args);
    }

    /// <inheritdoc />
    public void Warn(string message, params object[] args)
    {
        Log("WARN", message, args);
    }

    /// <inheritdoc />
    public void Error(string message, params object[] args)
    {
        Log("ERROR", message, args);
    }

    /// <inheritdoc />
    public void Fatal(string message, params object[] args)
    {
        Log("FATAL", message, args);
    }

    private void Log(string level, string message, params object[] args)
    {
        var formatted = args.Length > 0 ? string.Format(CultureInfo.InvariantCulture, message, args) : message;
        Console.WriteLine($"{_prefix}[{level}] {formatted}");
    }
}