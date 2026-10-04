using SimpleZipDrive.FuseSharp;

namespace SimpleZipDrive.Tests.FuseSharp;

/// <summary>
///     Tests for <see cref="FuseAvailability" />. The internal candidate-list overload keeps the
///     probe deterministic regardless of whether the host has FUSE 3 installed.
/// </summary>
public class FuseAvailabilityTests
{
    [Fact]
    public void Check_EmptyCandidateList_ReturnsFalse()
    {
        var originalError = Console.Error;
        using var suppressed = new StringWriter();
        Console.SetError(suppressed);

        try
        {
            Assert.False(FuseAvailability.Check(out var libraryPath, []));
            Assert.Null(libraryPath);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public void Check_WhitespaceCandidate_ReturnsFalse()
    {
        var originalError = Console.Error;
        using var suppressed = new StringWriter();
        Console.SetError(suppressed);

        try
        {
            Assert.False(FuseAvailability.Check(out var libraryPath, ["   "]));
            Assert.Null(libraryPath);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public void Check_LoadableLibrary_ReturnsTrueWithLibraryPath()
    {
        // A real, always-present system library stands in for libfuse3. On non-Windows
        // hosts the candidate differs, so only Windows is asserted.
        if (!OperatingSystem.IsWindows()) return;

        var candidate = Path.Combine(Environment.SystemDirectory, "kernel32.dll");

        Assert.True(FuseAvailability.Check(out var libraryPath, [candidate]));
        Assert.Equal(candidate, libraryPath);
    }

    [Fact]
    public void Check_PublicOverload_DoesNotThrow()
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var suppressedOut = new StringWriter();
        using var suppressedError = new StringWriter();
        Console.SetOut(suppressedOut);
        Console.SetError(suppressedError);

        try
        {
            var ex = Record.Exception(static () => _ = FuseAvailability.Check(out _));

            Assert.Null(ex);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    [Fact]
    public void Check_FirstLoadableCandidateWins()
    {
        if (!OperatingSystem.IsWindows()) return;

        var bogus = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dll");
        var candidate = Path.Combine(Environment.SystemDirectory, "kernel32.dll");

        Assert.True(FuseAvailability.Check(out var libraryPath, [bogus, candidate]));
        Assert.Equal(candidate, libraryPath);
    }
}
