using System.Globalization;
using DokanNet.Logging;

namespace SimpleZipDrive.Services;

/// <summary>
///     ILogger wrapper that guarantees the DokanNet prefix is applied to all log messages.
///     Writes directly to Console.Out, which is redirected to LogTextWriter.
/// </summary>
internal sealed class DokanPrefixedLogger : ILogger, IDisposable
{
    private readonly string _prefix;

    public DokanPrefixedLogger(string prefix)
    {
        _prefix = prefix;
    }

    public void Dispose()
    {
    }

    public bool DebugEnabled => true;

    public void Debug(string message, params object[] args)
    {
        Log("DEBUG", message, args);
    }

    public void Info(string message, params object[] args)
    {
        Log("INFO", message, args);
    }

    public void Warn(string message, params object[] args)
    {
        Log("WARN", message, args);
    }

    public void Error(string message, params object[] args)
    {
        Log("ERROR", message, args);
    }

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