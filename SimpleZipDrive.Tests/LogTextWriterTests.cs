using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using SimpleZipDrive.Core.Interfaces;
using SimpleZipDrive.Core.Logging;
using SimpleZipDrive.Core.Models;
using SimpleZipDrive.Core.Services;

namespace SimpleZipDrive.Tests;

/// <summary>
///     Tests for <see cref="LogTextWriter" />: console output is buffered per line and
///     forwarded to the registered <see cref="ILoggingService" />, or to the fallback
///     writer when no logging service is available.
/// </summary>
/// <remarks>
///     Most tests pass a recording logging service directly to the writer instead of
///     registering it in the shared static <see cref="ServiceProvider" />, so concurrently
///     running tests that resolve <see cref="ILoggingService" /> globally cannot pollute the
///     recorded output. The late-registration test still uses the shared provider.
/// </remarks>
[Collection("ServiceProvider")]
public class LogTextWriterTests : IDisposable
{
    public LogTextWriterTests()
    {
        ServiceProvider.DisposeAllServices();
    }

    public void Dispose()
    {
        ServiceProvider.DisposeAllServices();
    }

    private static bool WaitUntil(Func<bool> condition, int timeoutMs = 3000)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < timeoutMs)
        {
            if (condition()) return true;
            Thread.Sleep(10);
        }

        return condition();
    }

    // ─── Encoding and simple writes ───

    [Fact]
    public void Encoding_IsUtf8()
    {
        using var writer = new LogTextWriter();

        Assert.Equal(Encoding.UTF8, writer.Encoding);
    }

    [Fact]
    public void WriteLine_WithRegisteredService_ForwardsLine()
    {
        var loggingService = new RecordingLoggingService();
        using var writer = new LogTextWriter(loggingService: loggingService);

        writer.WriteLine("hello world");

        Assert.True(WaitUntil(() => loggingService.Contains("hello world")));
        Assert.Contains(loggingService.Snapshot, static e => string.Equals(e.Message, "hello world", StringComparison.Ordinal));
    }

    [Fact]
    public void WriteLine_StripsTrailingNewlines()
    {
        var loggingService = new RecordingLoggingService();
        using var writer = new LogTextWriter(loggingService: loggingService);

        writer.WriteLine("line with newline\r\n");

        Assert.True(WaitUntil(() => loggingService.Contains("line with newline")));
    }

    [Fact]
    public void WriteLine_BareNewLine_DoesNotLogSpuriousEntry()
    {
        // Regression test: the parameterless WriteLine used to enqueue the literal
        // string "System.Char[]" (char[].ToString()) and log it as a message.
        var loggingService = new RecordingLoggingService();
        using var writer = new LogTextWriter(loggingService: loggingService);

        writer.WriteLine();
        writer.WriteLine("after blank line");

        Assert.True(WaitUntil(() => loggingService.Contains("after blank line")));
        Assert.DoesNotContain(loggingService.Snapshot, static e =>
            string.Equals(e.Message, "System.Char[]", StringComparison.Ordinal));
    }

    [Fact]
    public void WriteLine_ReadOnlySpan_ForwardsLine()
    {
        var loggingService = new RecordingLoggingService();
        using var writer = new LogTextWriter(loggingService: loggingService);

        writer.WriteLine("span line".AsSpan());

        Assert.True(WaitUntil(() => loggingService.Contains("span line")));
    }

    [Fact]
    public void WriteLine_MultipleLines_AreForwardedInOrder()
    {
        var loggingService = new RecordingLoggingService();
        using var writer = new LogTextWriter(loggingService: loggingService);

        writer.WriteLine("first");
        writer.WriteLine("second");
        writer.WriteLine("third");

        Assert.True(WaitUntil(() =>
            loggingService.Contains("first") && loggingService.Contains("second") && loggingService.Contains("third")));

        var messages = loggingService.Snapshot.Select(static e => e.Message).ToList();
        var first = messages.IndexOf("first");
        var second = messages.IndexOf("second");
        var third = messages.IndexOf("third");

        Assert.True(first < second && second < third,
            $"Expected first < second < third but got {first}, {second}, {third}.");
    }

    // ─── Partial writes are buffered until the line ends ───

    [Fact]
    public void Write_Strings_AreBufferedUntilLineEnd()
    {
        var loggingService = new RecordingLoggingService();
        using var writer = new LogTextWriter(loggingService: loggingService);

        writer.Write("part1");
        writer.Write("part2");
        writer.WriteLine();

        Assert.True(WaitUntil(() => loggingService.Contains("part1part2")));
    }

    [Fact]
    public void Write_Char_IsBufferedUntilLineEnd()
    {
        var loggingService = new RecordingLoggingService();
        using var writer = new LogTextWriter(loggingService: loggingService);

        writer.Write('a');
        writer.Write('b');
        writer.WriteLine("c");

        Assert.True(WaitUntil(() => loggingService.Contains("abc")));
    }

    [Fact]
    public void Write_CharArray_IsBufferedUntilLineEnd()
    {
        var loggingService = new RecordingLoggingService();
        using var writer = new LogTextWriter(loggingService: loggingService);

        var buffer = "xxHELLOyy".ToCharArray();
        writer.Write(buffer, 2, 5);
        writer.WriteLine();

        Assert.True(WaitUntil(() => loggingService.Contains("HELLO")));
    }

    [Fact]
    public void Write_NullOrEmptyString_IsIgnored()
    {
        var loggingService = new RecordingLoggingService();
        using var writer = new LogTextWriter(loggingService: loggingService);

        writer.Write((string?)null);
        writer.Write(string.Empty);
        writer.WriteLine("content");

        Assert.True(WaitUntil(() => loggingService.Contains("content")));
        Assert.DoesNotContain(loggingService.Snapshot, static e =>
            string.Equals(e.Message, "null", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Write_CharArrayWithZeroCount_IsIgnored()
    {
        var loggingService = new RecordingLoggingService();
        using var writer = new LogTextWriter(loggingService: loggingService);

        writer.Write(new[] { 'a', 'b', 'c' }, 0, 0);
        writer.WriteLine("content");

        Assert.True(WaitUntil(() => loggingService.Contains("content")));
        Assert.DoesNotContain(loggingService.Snapshot, static e =>
            string.Equals(e.Message, "abc", StringComparison.Ordinal));
    }

    [Fact]
    public void WriteLine_WhitespaceOnlyLine_IsNotLogged()
    {
        var loggingService = new RecordingLoggingService();
        using var writer = new LogTextWriter(loggingService: loggingService);

        writer.WriteLine("   ");
        writer.WriteLine("real content");

        Assert.True(WaitUntil(() => loggingService.Contains("real content")));
        Assert.DoesNotContain(loggingService.Snapshot, static e =>
            string.Equals(e.Message, "   ", StringComparison.Ordinal));
    }

    // ─── Fallback writer ───

    [Fact]
    public void WriteLine_NoLoggingService_UsesFallbackWriter()
    {
        using var fallback = new StringWriter();
        using var writer = new LogTextWriter(fallback);

        writer.WriteLine("fallback line");

        Assert.True(WaitUntil(() => fallback.ToString().Contains("fallback line", StringComparison.Ordinal)));
    }

    [Fact]
    public void WriteLine_ServiceRegisteredAfterConstruction_UsesService()
    {
        using var fallback = new StringWriter();
        using var writer = new LogTextWriter(fallback);
        var loggingService = new RecordingLoggingService();
        ServiceProvider.Register<ILoggingService>(loggingService);

        writer.WriteLine("late registration");

        Assert.True(WaitUntil(() => loggingService.Contains("late registration")));
        Assert.DoesNotContain("late registration", fallback.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void WriteLine_NoServiceAndNoFallback_DoesNotThrow()
    {
        using var writer = new LogTextWriter();

        var ex = Record.Exception(() => writer.WriteLine("nowhere"));

        Assert.Null(ex);
    }

    // ─── Dispose ───

    [Fact]
    public void Dispose_FlushesBufferedContentWithoutLineEnding()
    {
        var loggingService = new RecordingLoggingService();
        var writer = new LogTextWriter(null, loggingService);

        writer.Write("buffered without newline");
        writer.Dispose();

        Assert.Contains(loggingService.Snapshot,
            static e => string.Equals(e.Message, "buffered without newline", StringComparison.Ordinal));
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var writer = new LogTextWriter();

        var ex = Record.Exception(() =>
        {
            writer.Dispose();
            writer.Dispose();
        });

        Assert.Null(ex);
    }

    [Fact]
    public void Dispose_ThenWrite_DoesNotThrow()
    {
        var writer = new LogTextWriter();
        writer.Dispose();

        var ex = Record.Exception(() => writer.WriteLine("after dispose"));

        Assert.Null(ex);
    }

    /// <summary>
    ///     Thread-safe <see cref="ILoggingService" /> fake that records every message; the
    ///     writer calls it from its background processing task, so all access is synchronized.
    /// </summary>
    private sealed class RecordingLoggingService : ILoggingService
    {
        private readonly ConcurrentQueue<LogEntry> _entries = new();

        public ObservableCollection<LogEntry> LogEntries { get; } = [];

        public IReadOnlyList<LogEntry> Snapshot => _entries.ToArray();

        public void Log(string message)
        {
            _entries.Enqueue(new LogEntry
            {
                Timestamp = DateTime.Now,
                Message = message.TrimEnd('\r', '\n'),
                IsError = false
            });
        }

        public void LogError(string message)
        {
            _entries.Enqueue(new LogEntry
            {
                Timestamp = DateTime.Now,
                Message = message.TrimEnd('\r', '\n'),
                IsError = true
            });
        }

        public void Clear()
        {
            _entries.Clear();
        }

        public string GetAllLogsAsText()
        {
            return string.Join(Environment.NewLine, Snapshot.Select(static e => e.ToString()));
        }

        public bool Contains(string message)
        {
            return _entries.Any(e => string.Equals(e.Message, message, StringComparison.Ordinal));
        }
    }
}
