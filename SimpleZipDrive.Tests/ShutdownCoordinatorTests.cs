using SimpleZipDrive.Core;

namespace SimpleZipDrive.Tests;

public class ShutdownCoordinatorTests
{
    [Fact]
    public void TryAcceptShutdownRequest_FirstRequest_IsAccepted()
    {
        var coordinator = new ShutdownCoordinator();

        Assert.True(coordinator.TryAcceptShutdownRequest());
    }

    [Fact]
    public void TryAcceptShutdownRequest_SecondRequest_IsRejectedBeforeGracefulShutdownStarts()
    {
        // Avalonia is still processing the first platform request (its own "shutting down" flag is
        // set); a duplicate would make its lifetime throw from the native window procedure. The
        // application's graceful-shutdown flag is not set yet at that point, so the request itself
        // must be remembered (bug report #69869).
        var coordinator = new ShutdownCoordinator();

        Assert.True(coordinator.TryAcceptShutdownRequest());
        Assert.False(coordinator.TryAcceptShutdownRequest());
    }

    [Fact]
    public void TryAcceptShutdownRequest_GracefulShutdownStarted_IsRejected()
    {
        // A platform request that arrives while the application is already unmounting and exiting
        // must be vetoed (bug report #68244).
        var coordinator = new ShutdownCoordinator();

        Assert.True(coordinator.TryBeginShutdown());
        Assert.True(coordinator.IsShuttingDown);
        Assert.False(coordinator.TryAcceptShutdownRequest());
    }

    [Fact]
    public void TryBeginShutdown_CalledMoreThanOnce_OnlyFirstCallSucceeds()
    {
        var coordinator = new ShutdownCoordinator();

        Assert.True(coordinator.TryBeginShutdown());
        Assert.False(coordinator.TryBeginShutdown());
        Assert.True(coordinator.IsShuttingDown);
    }

    [Fact]
    public void TryAcceptShutdownRequest_ConcurrentRequests_AcceptsExactlyOne()
    {
        var coordinator = new ShutdownCoordinator();
        var accepted = 0;

        Parallel.For(0, 32, _ =>
        {
            if (coordinator.TryAcceptShutdownRequest())
                Interlocked.Increment(ref accepted);
        });

        Assert.Equal(1, accepted);
    }
}
