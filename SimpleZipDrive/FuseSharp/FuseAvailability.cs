using Serilog;

namespace FuseSharp;

/// <summary>
/// Verifies that the FUSE 3 runtime needed for mounting is present and prints
/// installation guidance when it is not.
/// </summary>
public static class FuseAvailability
{
    /// <summary>
    /// Checks whether the FUSE 3 library and kernel support are available.
    /// </summary>
    /// <param name="libraryPath">When this method returns, the FUSE library that was found.</param>
    /// <returns><see langword="true"/> when mounting can be attempted; otherwise <see langword="false"/>.</returns>
    public static bool Check(out string? libraryPath)
    {
        return Check(out libraryPath, candidates: null);
    }

    /// <summary>
    /// Checks availability against an explicit candidate list. Used by tests so the probe
    /// is deterministic regardless of whether the host has FUSE 3 installed.
    /// </summary>
    /// <param name="libraryPath">When this method returns, the FUSE library that was found.</param>
    /// <param name="candidates">The library candidates to probe, or <see langword="null"/> for the platform list.</param>
    /// <returns><see langword="true"/> when mounting can be attempted; otherwise <see langword="false"/>.</returns>
    internal static bool Check(out string? libraryPath, IEnumerable<string>? candidates)
    {
        try
        {
            return CheckCore(out libraryPath, candidates);
        }
        catch (Exception ex)
        {
            // A probe failure is genuinely unexpected (not a missing installation).
            Log.Error(ex, "FUSE availability check failed");
            libraryPath = null;
            return false;
        }
    }

    private static bool CheckCore(out string? libraryPath, IEnumerable<string>? candidates)
    {
        FuseInterop.RegisterResolver();

        var loaded = candidates is null
            ? FuseInterop.TryLoadLibrary(out libraryPath)
            : FuseInterop.TryLoadLibrary(candidates, out libraryPath);

        if (!loaded)
        {
            PrintMissingLibraryInstructions();
            return false;
        }

        Log.Information("FUSE library loaded: {LibraryPath}", libraryPath);

        try
        {
            Log.Information("FUSE API version: {FuseVersion}", FuseInterop.FuseVersion());
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not query the FUSE API version");
        }

        if (!OperatingSystem.IsLinux())
        {
            return true;
        }

        if (!File.Exists("/dev/fuse"))
        {
            Console.Error.WriteLine("Error: /dev/fuse was not found, so FUSE mounts cannot work.");
            Console.Error.WriteLine("Load the FUSE kernel module (for example: sudo modprobe fuse) and re-run.");
            // A missing kernel module is an expected user-setup condition, not an
            // application error; log it below the bug-report threshold.
            Log.Information("FUSE check failed: /dev/fuse is missing.");
            return false;
        }

        if (!Environment.IsPrivilegedProcess && !IsOnPath("fusermount3"))
        {
            Console.WriteLine("Warning: fusermount3 was not found on PATH. Mounting may fail.");
            Console.WriteLine("Install the FUSE tools package (for example: sudo apt install fuse3).");
            Log.Information("fusermount3 not found on PATH; mounting may fail.");
        }

        return true;
    }

    private static bool IsOnPath(string executable)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                if (File.Exists(Path.Combine(directory, executable)))
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                // Ignore malformed PATH entries.
                Log.Debug(ex, "Ignoring malformed PATH entry '{Directory}'", directory);
            }
        }

        return false;
    }

    private static void PrintMissingLibraryInstructions()
    {
        Console.Error.WriteLine();

        if (OperatingSystem.IsMacOS())
        {
            Console.Error.WriteLine("Error: macFUSE (libfuse3) was not found.");
            Console.Error.WriteLine();
            Console.Error.WriteLine("SimpleZipDrive needs macFUSE to mount images on macOS.");
            Console.Error.WriteLine();
            Console.Error.WriteLine("To fix this:");
            Console.Error.WriteLine("  1. Download and install macFUSE from: https://macfuse.io");
            Console.Error.WriteLine("  2. On macOS 15.4 or later, choose the FSKit backend when prompted");
            Console.Error.WriteLine("     (no kernel extension required).");
            Console.Error.WriteLine("  3. Re-run SimpleZipDrive.");
            Log.Information("FUSE check failed: macFUSE libfuse3 was not found.");
        }
        else
        {
            Console.Error.WriteLine("Error: the FUSE 3 library (libfuse3) was not found.");
            Console.Error.WriteLine();
            Console.Error.WriteLine("SimpleZipDrive needs FUSE 3 to mount images on Linux.");
            Console.Error.WriteLine();
            Console.Error.WriteLine("To fix this, install FUSE 3 with your package manager:");
            Console.Error.WriteLine("  Debian/Ubuntu: sudo apt install libfuse3-3 fuse3");
            Console.Error.WriteLine("  Fedora:        sudo dnf install fuse3 fuse3-libs");
            Console.Error.WriteLine("  Arch:          sudo pacman -S fuse3");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Then Re-run SimpleZipDrive.");
            Log.Information("FUSE check failed: libfuse3 was not found.");
        }

        Console.Error.WriteLine();
    }
}