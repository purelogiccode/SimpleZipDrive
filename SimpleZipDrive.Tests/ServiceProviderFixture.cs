using SimpleZipDrive.Core.Services;

namespace SimpleZipDrive.Tests;

public class ServiceProviderFixture : IDisposable
{
    public void Dispose()
    {
        // Ensure shared static state is cleaned up after each collection of tests
        ServiceProvider.DisposeAllServices();
        GC.SuppressFinalize(this);
    }
}