using WinFspErrorLoggerStatic = SimpleZipDrive.Core.ErrorLoggerStatic;

namespace SimpleZipDrive.Tests.WinFsp;

[Collection("Logging")]
public class WinFspErrorLoggerStaticTests
{
    [Fact]
    public void Instance_IsSingleton()
    {
        var instance1 = WinFspErrorLoggerStatic.Instance;
        var instance2 = WinFspErrorLoggerStatic.Instance;

        Assert.Same(instance1, instance2);
    }

    [Fact]
    public void ReportSilentException_DelegatesToInstance()
    {
        var ex = Record.Exception(static () =>
            WinFspErrorLoggerStatic.ReportSilentException(new IOException("Static test"), "Static context", true));

        Assert.Null(ex);
    }

    [Fact]
    public void LogErrorSync_DelegatesToInstance()
    {
        var ex = Record.Exception(static () =>
            WinFspErrorLoggerStatic.LogErrorSync(new InvalidOperationException("Static sync"), "Static sync context"));

        Assert.Null(ex);
    }

    [Fact]
    public void LogErrorAsync_DelegatesToInstance()
    {
        var task = WinFspErrorLoggerStatic.LogErrorAsync(
            new InvalidOperationException("Static async"), "Static async context");

        Assert.NotNull(task);
        Assert.True(task is not null);
    }
}