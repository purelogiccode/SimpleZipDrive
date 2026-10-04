using System.Diagnostics.CodeAnalysis;
using SimpleZipDrive.Core.Services;

namespace SimpleZipDrive.Tests;

/// <summary>
///     Tests for <see cref="UserNotificationService" />. The dialog path requires a running
///     Avalonia application, so these tests cover construction and the no-application guard.
/// </summary>
[SuppressMessage("ReSharper", "NullableWarningSuppressionIsUsed")]
public class UserNotificationServiceTests
{
    private readonly LoggingService _loggingService = new();

    [Fact]
    public void Constructor_NullLoggingService_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(static () => new UserNotificationService(null!));
    }

    [Fact]
    public void Constructor_ValidLoggingService_CreatesInstance()
    {
        var service = new UserNotificationService(_loggingService);

        Assert.NotNull(service);
    }

    [Fact]
    public void ShowUpdateAvailable_NoApplication_ReturnsFalse()
    {
        // Without an Avalonia application there is no UI thread to show the dialog on.
        var service = new UserNotificationService(_loggingService);

        var result = service.ShowUpdateAvailable(
            new Version(1, 0, 0), new Version(2, 0, 0), "https://example.com/download");

        Assert.False(result);
    }

    [Fact]
    public void ShowUpdateAvailable_NoApplication_DoesNotLogBrowserOpened()
    {
        var service = new UserNotificationService(_loggingService);

        service.ShowUpdateAvailable(new Version(1, 0, 0), new Version(2, 0, 0), "https://example.com/download");

        Assert.DoesNotContain(_loggingService.LogEntries, static entry =>
            entry.Message.Contains("Browser opened", StringComparison.OrdinalIgnoreCase));
    }
}
