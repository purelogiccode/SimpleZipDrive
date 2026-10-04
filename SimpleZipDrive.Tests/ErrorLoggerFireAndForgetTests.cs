using SimpleZipDrive.Core;

namespace SimpleZipDrive.Tests;

/// <summary>
///     Tests for <see cref="ErrorLogger.FireAndForgetAsync" />: the helper must observe the
///     task's completion and swallow any fault so fire-and-forget logging never crashes the
///     process.
/// </summary>
[Collection("Logging")]
public class ErrorLoggerFireAndForgetTests
{
    [Fact]
    public void FireAndForgetAsync_CompletedTask_DoesNotThrow()
    {
        var ex = Record.Exception(static () => ErrorLogger.FireAndForgetAsync(Task.CompletedTask));

        Assert.Null(ex);
    }

    [Fact]
    public void FireAndForgetAsync_FaultedTask_SwallowsException()
    {
        var faulted = Task.FromException(new InvalidOperationException("boom"));

        var ex = Record.Exception(() => ErrorLogger.FireAndForgetAsync(faulted));

        Assert.Null(ex);
    }

    [Fact]
    public void FireAndForgetAsync_CancelledTask_SwallowsCancellation()
    {
        var cancelled = Task.FromCanceled(new CancellationToken(true));

        var ex = Record.Exception(() => ErrorLogger.FireAndForgetAsync(cancelled));

        Assert.Null(ex);
    }

    [Fact]
    public async Task FireAndForgetAsync_FaultedTask_KeepsFaultedStateAfterHandling()
    {
        var faulted = Task.FromException(new InvalidOperationException("boom"));

        ErrorLogger.FireAndForgetAsync(faulted);
        await Task.Delay(50);

        Assert.True(faulted.IsFaulted);
        Assert.IsType<InvalidOperationException>(faulted.Exception!.InnerException);
    }
}
