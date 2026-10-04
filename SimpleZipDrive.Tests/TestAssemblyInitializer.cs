using System.Runtime.CompilerServices;

namespace SimpleZipDrive.Tests;

/// <summary>
///     One-time test-assembly initialization.
/// </summary>
internal static class TestAssemblyInitializer
{
    /// <summary>
    ///     Pins XISOSharp's static log sinks to a stable null writer before any test runs.
    /// </summary>
    /// <remarks>
    ///     <c>XISOSharp.Logger.Out</c>/<c>Error</c> capture <see cref="Console.Out" /> and
    ///     <see cref="Console.Error" /> the first time the type is touched. Tests that
    ///     temporarily redirect the console can otherwise make the library capture a
    ///     <see cref="StringWriter" /> that is disposed moments later, which breaks every
    ///     unrelated XISO test with "Cannot write to a closed TextWriter". Pinning the sinks
    ///     once here removes that cross-test race entirely; no test asserts on XISOSharp log
    ///     output.
    /// </remarks>
    [ModuleInitializer]
    internal static void Initialize()
    {
        XISOSharp.Logger.Out = TextWriter.Null;
        XISOSharp.Logger.Error = TextWriter.Null;
    }
}
