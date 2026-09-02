using SimpleZipDrive.Services;

namespace SimpleZipDrive.Tests;

public class DokanPrefixedLoggerTests : IDisposable
{
    private readonly StringWriter _consoleOutCapture;
    private readonly DokanPrefixedLogger? _logger;
    private readonly TextWriter _originalOut;

    public DokanPrefixedLoggerTests()
    {
        _originalOut = Console.Out;
        _consoleOutCapture = new StringWriter();
        Console.SetOut(_consoleOutCapture);

        _logger = new DokanPrefixedLogger("[DRIVE] ");
    }

    public void Dispose()
    {
        Console.SetOut(_originalOut);
        _consoleOutCapture.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void DebugEnabled_IsTrue()
    {
        if (_logger == null) return;

        Assert.True(_logger.DebugEnabled);
    }

    [Fact]
    public void Debug_WritesFormattedMessage()
    {
        if (_logger == null) return;

        _logger.Debug("test message");
        Assert.Contains("[DRIVE] [DEBUG] test message", _consoleOutCapture.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Info_WritesFormattedMessage()
    {
        if (_logger == null) return;

        _logger.Info("info message");
        Assert.Contains("[DRIVE] [INFO] info message", _consoleOutCapture.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Warn_WritesFormattedMessage()
    {
        if (_logger == null) return;

        _logger.Warn("warning!");
        Assert.Contains("[DRIVE] [WARN] warning!", _consoleOutCapture.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Error_WritesFormattedMessage()
    {
        if (_logger == null) return;

        _logger.Error("error occurred");
        Assert.Contains("[DRIVE] [ERROR] error occurred", _consoleOutCapture.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Fatal_WritesFormattedMessage()
    {
        if (_logger == null) return;

        _logger.Fatal("fatal crash");
        Assert.Contains("[DRIVE] [FATAL] fatal crash", _consoleOutCapture.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AllLogMethods_FormatWithArgs()
    {
        if (_logger == null) return;

        _logger.Debug("value is {0}", 42);
        Assert.Contains("[DRIVE] [DEBUG] value is 42", _consoleOutCapture.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AllLogMethods_MultipleArgs()
    {
        if (_logger == null) return;

        _logger.Info("{0} {1} {2}", "a", "b", "c");
        Assert.Contains("[DRIVE] [INFO] a b c", _consoleOutCapture.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AllLogMethods_NoArgs_NoFormatting()
    {
        if (_logger == null) return;

        _logger.Error("{0} not formatted");
        Assert.Contains("[DRIVE] [ERROR] {0} not formatted", _consoleOutCapture.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MultipleCalls_AllWritten()
    {
        if (_logger == null) return;

        _logger.Debug("first");
        _logger.Info("second");
        _logger.Warn("third");

        var output = _consoleOutCapture.ToString();
        Assert.Contains("[DRIVE] [DEBUG] first", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[DRIVE] [INFO] second", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[DRIVE] [WARN] third", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        if (_logger == null) return;

        var exception = Record.Exception(() => _logger.Dispose());
        Assert.Null(exception);
    }

    [Fact]
    public void CustomPrefix_IsApplied()
    {
        using var capture = new StringWriter();
        Console.SetOut(capture);

        var logger = new DokanPrefixedLogger("[Custom] ");

        logger.Info("hello");
        Assert.Contains("[Custom] [INFO] hello", capture.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}