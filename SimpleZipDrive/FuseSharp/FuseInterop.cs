using System.Reflection;
using System.Runtime.InteropServices;
using Serilog;

#pragma warning disable MA0048 // Interop declarations share this file intentionally.

namespace SimpleZipDrive.FuseSharp;

/// <summary>
/// Native FUSE 3 interop shared by the Linux and macOS mount backends. Only the
/// operations needed by a read-only volume are declared; the operation table is
/// passed with its exact prefix size, so libfuse leaves the remaining callbacks
/// unset (and therefore reports <c>ENOSYS</c> for them).
/// </summary>
internal static class FuseInterop
{
    /// <summary>
    /// The logical library name used by every <see cref="DllImportAttribute"/> in this class.
    /// </summary>
    private const string LibraryName = "fuse3";

    private static readonly Lock ResolverLock = new();
    private static bool _resolverRegistered;

    /// <summary>
    /// Registers the DLL import resolver that maps <see cref="LibraryName"/> to the
    /// platform's FUSE 3 library (libfuse3 on Linux, macFUSE's libfuse3 on macOS).
    /// </summary>
    internal static void RegisterResolver()
    {
        try
        {
            // Serialized: NativeLibrary.SetDllImportResolver throws if a resolver is
            // already registered for the assembly, so two concurrent callers must not
            // both attempt it. The flag is set only after success, so a transient
            // failure can be retried instead of disabling resolution permanently.
            lock (ResolverLock)
            {
                if (_resolverRegistered)
                {
                    return;
                }

                NativeLibrary.SetDllImportResolver(typeof(FuseInterop).Assembly, Resolve);
                _resolverRegistered = true;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to register the FUSE library resolver");
            throw;
        }
    }

    /// <summary>
    /// Tries to load the FUSE 3 library from the known platform locations.
    /// </summary>
    /// <param name="libraryPath">When this method returns, the path or name that was loaded.</param>
    /// <returns><see langword="true"/> when the library can be loaded; otherwise <see langword="false"/>.</returns>
    internal static bool TryLoadLibrary(out string? libraryPath)
    {
        return TryLoadLibrary(EnumerateCandidates(), out libraryPath);
    }

    /// <summary>
    /// Tries to load the first loadable library from the supplied candidates. The probe
    /// handle is released immediately (the DllImport resolver loads the real handle), and
    /// the list can be supplied directly so tests probe a deterministic set on any OS.
    /// </summary>
    /// <param name="candidates">The library names or paths to try, in order.</param>
    /// <param name="libraryPath">When this method returns, the candidate that was loaded.</param>
    /// <returns><see langword="true"/> when a candidate could be loaded; otherwise <see langword="false"/>.</returns>
    internal static bool TryLoadLibrary(IEnumerable<string> candidates, out string? libraryPath)
    {
        try
        {
            foreach (var candidate in candidates)
            {
                if (NativeLibrary.TryLoad(candidate, out var handle))
                {
                    // The probe only checks loadability; release the reference so repeated
                    // probes do not accumulate native handles.
                    NativeLibrary.Free(handle);
                    libraryPath = candidate;
                    return true;
                }
            }

            libraryPath = null;
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to probe the FUSE library locations");
            libraryPath = null;
            return false;
        }
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, LibraryName, StringComparison.Ordinal))
        {
            return IntPtr.Zero;
        }

        foreach (var candidate in EnumerateCandidates())
        {
            if (NativeLibrary.TryLoad(candidate, out var handle))
            {
                return handle;
            }
        }

        return IntPtr.Zero;
    }

    private static IEnumerable<string> EnumerateCandidates()
    {
        var overridePath = Environment.GetEnvironmentVariable("SIMPLEZIPDRIVE_FUSE_LIBRARY");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            yield return overridePath;
        }

        if (OperatingSystem.IsMacOS())
        {
            yield return "/usr/local/lib/libfuse3.dylib";
            yield return "/usr/local/lib/libfuse3.4.dylib";
            yield return "/opt/homebrew/lib/libfuse3.dylib";
            yield return "libfuse3.dylib";
            yield break;
        }

        yield return "libfuse3.so.4";
        yield return "libfuse3.so.3";
        yield return "libfuse3.so";

        foreach (var directory in LinuxLibraryDirectories)
        {
            string[] files;
            try
            {
                files = Directory.GetFiles(directory, "libfuse3.so.*");
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Could not scan '{Directory}' for FUSE libraries", directory);
                continue;
            }

            Array.Sort(files, CompareLibraryFileNames);
            for (var i = files.Length - 1; i >= 0; i--)
            {
                yield return files[i];
            }
        }
    }

    private static readonly string[] LinuxLibraryDirectories =
    [
        "/usr/lib/x86_64-linux-gnu",
        "/usr/lib/aarch64-linux-gnu",
        "/usr/lib64",
        "/usr/lib",
        "/lib/x86_64-linux-gnu",
        "/lib/aarch64-linux-gnu",
        "/usr/local/lib",
        "/lib"
    ];

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "fuse_new")]
    private static extern IntPtr FuseNewLinux(ref FuseArgs args, ref FuseOperationsLinux operations,
        nuint operationSize, IntPtr userData);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "fuse_new_31")]
    private static extern IntPtr FuseNew31Linux(ref FuseArgs args, ref FuseOperationsLinux operations,
        nuint operationSize, IntPtr userData);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "fuse_new")]
    private static extern IntPtr FuseNewMac(ref FuseArgs args, ref FuseOperationsMac operations,
        nuint operationSize, IntPtr userData);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "fuse_new_31")]
    private static extern IntPtr FuseNew31Mac(ref FuseArgs args, ref FuseOperationsMac operations,
        nuint operationSize, IntPtr userData);

    /// <summary>
    /// Creates the FUSE session through the 4-argument ABI-compat entry point.
    /// </summary>
    /// <remarks>
    /// Linux's libfuse exports that entry point as <c>fuse_new</c> on every 3.x release and as
    /// <c>fuse_new_31</c> since 3.13. macFUSE's libfuse3 is built without ELF symbol versioning, so
    /// only <c>fuse_new_31</c> is exported there (plain <c>fuse_new</c> is a header macro). The
    /// platform-preferred symbol is tried first and the other one is the fallback, so every
    /// supported libfuse 3 build is covered.
    /// </remarks>
    /// <param name="args">The mount arguments.</param>
    /// <param name="operations">The operation table.</param>
    /// <param name="operationSize">The managed size of the operation table.</param>
    /// <param name="userData">The private data passed to the FUSE callbacks.</param>
    /// <returns>The FUSE session handle, or zero on failure.</returns>
    internal static IntPtr FuseNew(ref FuseArgs args, ref FuseOperationsLinux operations, nuint operationSize,
        IntPtr userData)
    {
        if (OperatingSystem.IsMacOS())
        {
            try
            {
                return FuseNew31Linux(ref args, ref operations, operationSize, userData);
            }
            catch (EntryPointNotFoundException)
            {
                return FuseNewLinux(ref args, ref operations, operationSize, userData);
            }
        }

        try
        {
            return FuseNewLinux(ref args, ref operations, operationSize, userData);
        }
        catch (EntryPointNotFoundException)
        {
            return FuseNew31Linux(ref args, ref operations, operationSize, userData);
        }
    }

    /// <summary>
    /// Creates the FUSE session through the 4-argument ABI-compat entry point.
    /// See the Linux overload for the platform-specific symbol selection.
    /// </summary>
    /// <param name="args">The mount arguments.</param>
    /// <param name="operations">The operation table.</param>
    /// <param name="operationSize">The managed size of the operation table.</param>
    /// <param name="userData">The private data passed to the FUSE callbacks.</param>
    /// <returns>The FUSE session handle, or zero on failure.</returns>
    internal static IntPtr FuseNew(ref FuseArgs args, ref FuseOperationsMac operations, nuint operationSize,
        IntPtr userData)
    {
        if (OperatingSystem.IsMacOS())
        {
            try
            {
                return FuseNew31Mac(ref args, ref operations, operationSize, userData);
            }
            catch (EntryPointNotFoundException)
            {
                return FuseNewMac(ref args, ref operations, operationSize, userData);
            }
        }

        try
        {
            return FuseNewMac(ref args, ref operations, operationSize, userData);
        }
        catch (EntryPointNotFoundException)
        {
            return FuseNew31Mac(ref args, ref operations, operationSize, userData);
        }
    }

    // libfuse and libc take NUL-terminated UTF-8 paths, never UTF-16: keep LPUTF8Str
    // (a CA2101 "use LPWStr" suggestion would corrupt the path on Linux/macOS).
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "fuse_mount")]
    internal static extern int FuseMount(IntPtr fuse, [MarshalAs(UnmanagedType.LPUTF8Str)] string mountPoint);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "fuse_unmount")]
    internal static extern void FuseUnmount(IntPtr fuse);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "fuse_destroy")]
    internal static extern void FuseDestroy(IntPtr fuse);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "fuse_loop")]
    internal static extern int FuseLoop(IntPtr fuse);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "fuse_exit")]
    internal static extern void FuseExit(IntPtr fuse);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "fuse_opt_free_args")]
    internal static extern void FuseOptFreeArgs(ref FuseArgs args);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "fuse_version")]
    internal static extern int FuseVersion();

    [DllImport("libc", CallingConvention = CallingConvention.Cdecl, EntryPoint = "statfs", SetLastError = true)]
    private static extern int NativeStatFs([MarshalAs(UnmanagedType.LPUTF8Str)] string path, IntPtr buffer);

    /// <summary>
    /// Buffer size for the native <c>statfs</c> result. The structure is 120 bytes on
    /// Linux but 2168 bytes on macOS, where it embeds two 1024-byte mount-path buffers,
    /// so the allocation must cover the larger layout (the API has no size parameter).
    /// </summary>
    private const int StatFsBufferSize = 4096;

    /// <summary>
    /// Issues a <c>statfs</c> syscall for the mount point. Unlike <c>stat</c>, the
    /// kernel never serves this from cache, so it reliably wakes a blocked FUSE loop.
    /// </summary>
    /// <param name="path">The mounted path to poke.</param>
    internal static void PokeMountPoint(string path)
    {
        var buffer = Marshal.AllocHGlobal(StatFsBufferSize);
        try
        {
            var result = NativeStatFs(path, buffer);
            if (result != 0)
            {
                // The poke still woke the loop in most cases; log for diagnostics.
                Log.Debug("statfs poke for '{MountPoint}' returned {Result}", path, result);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "statfs poke failed for '{MountPoint}'", path);
            throw;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// Orders two FUSE library file names by their numeric version suffixes (ascending);
    /// callers iterate the sorted array in reverse so the newest library is tried first.
    /// </summary>
    /// <param name="left">The first file name or path.</param>
    /// <param name="right">The second file name or path.</param>
    /// <returns>A signed comparison result for an ascending version sort.</returns>
    internal static int CompareLibraryFileNames(string left, string right)
    {
        var comparison = CompareVersions(ParseLibraryVersion(left), ParseLibraryVersion(right));
        return comparison != 0
            ? comparison
            : string.CompareOrdinal(Path.GetFileName(left), Path.GetFileName(right));
    }

    private static int CompareVersions(int[] left, int[] right)
    {
        var sharedLength = Math.Min(left.Length, right.Length);
        for (var i = 0; i < sharedLength; i++)
        {
            var comparison = left[i].CompareTo(right[i]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return left.Length.CompareTo(right.Length);
    }

    private static int[] ParseLibraryVersion(string path)
    {
        var name = Path.GetFileName(path);
        var marker = name.IndexOf(".so.", StringComparison.Ordinal);
        if (marker < 0)
        {
            return [];
        }

        var parts = name[(marker + 4)..].Split('.');
        var version = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out version[i]))
            {
                return [];
            }
        }

        return version;
    }
}

/// <summary>
/// The libfuse <c>fuse_args</c> structure. <see cref="Allocated"/> is zero because the
/// arguments are owned by the managed caller.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct FuseArgs
{
    public int Argc;
    public IntPtr Argv;
    public int Allocated;
}

/// <summary>
/// The upstream libfuse 3 <c>fuse_operations</c> layout (Linux), truncated after
/// <c>destroy</c>. The order matches <c>include/fuse.h</c> exactly; unused callbacks
/// are left as null pointers.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct FuseOperationsLinux
{
    public IntPtr GetAttr;
    public IntPtr ReadLink;
    public IntPtr MkNod;
    public IntPtr MkDir;
    public IntPtr Unlink;
    public IntPtr RmDir;
    public IntPtr SymLink;
    public IntPtr Rename;
    public IntPtr Link;
    public IntPtr Chmod;
    public IntPtr Chown;
    public IntPtr Truncate;
    public IntPtr Open;
    public IntPtr Read;
    public IntPtr Write;
    public IntPtr StatFs;
    public IntPtr Flush;
    public IntPtr Release;
    public IntPtr FSync;
    public IntPtr SetXAttr;
    public IntPtr GetXAttr;
    public IntPtr ListXAttr;
    public IntPtr RemoveXAttr;
    public IntPtr OpenDir;
    public IntPtr ReadDir;
    public IntPtr ReleaseDir;
    public IntPtr FSyncDir;
    public IntPtr Init;
    public IntPtr Destroy;
}

/// <summary>
/// The macFUSE <c>fuse_operations</c> layout, truncated after <c>destroy</c>.
/// macFUSE inserts a Darwin-only <c>setattr</c> callback after <c>getattr</c>.
/// The library is entered through the exported <c>fuse_new</c> symbol, which selects
/// the vanilla (non-Darwin) signatures for <c>getattr</c>, <c>statfs</c> and
/// <c>readdir</c>, so the remaining fields match the upstream layout.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct FuseOperationsMac
{
    public IntPtr GetAttr;
    public IntPtr SetAttr;
    public IntPtr ReadLink;
    public IntPtr MkNod;
    public IntPtr MkDir;
    public IntPtr Unlink;
    public IntPtr RmDir;
    public IntPtr SymLink;
    public IntPtr Rename;
    public IntPtr Link;
    public IntPtr Chmod;
    public IntPtr Chown;
    public IntPtr Truncate;
    public IntPtr Open;
    public IntPtr Read;
    public IntPtr Write;
    public IntPtr StatFs;
    public IntPtr Flush;
    public IntPtr Release;
    public IntPtr FSync;
    public IntPtr SetXAttr;
    public IntPtr GetXAttr;
    public IntPtr ListXAttr;
    public IntPtr RemoveXAttr;
    public IntPtr OpenDir;
    public IntPtr ReadDir;
    public IntPtr ReleaseDir;
    public IntPtr FSyncDir;
    public IntPtr Init;
    public IntPtr Destroy;
}

/// <summary>
/// Native callback that fills <c>struct stat</c> for a path (FUSE <c>getattr</c>).
/// </summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int GetAttrDelegate(IntPtr path, IntPtr stat, IntPtr fileInfo);

/// <summary>
/// Native callback that applies attributes on macOS (macFUSE Darwin-only <c>setattr</c>).
/// </summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int SetAttrMacDelegate(IntPtr path, IntPtr darwinAttr, int toSet, IntPtr fileInfo);

/// <summary>
/// Native callback invoked when a file handle is opened (FUSE <c>open</c>).
/// </summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int OpenDelegate(IntPtr path, IntPtr fileInfo);

/// <summary>
/// Native callback that reads file data into the supplied buffer (FUSE <c>read</c>).
/// </summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int ReadDelegate(IntPtr path, IntPtr buffer, nuint size, long offset, IntPtr fileInfo);

/// <summary>
/// Native callback that fills <c>struct statvfs</c> for the volume (FUSE <c>statfs</c>).
/// </summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int StatFsDelegate(IntPtr path, IntPtr statvfs);

/// <summary>
/// Native callback that enumerates a directory through the filler callback (FUSE <c>readdir</c>).
/// </summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int ReadDirDelegate(IntPtr path, IntPtr buffer, IntPtr filler, long offset, IntPtr fileInfo,
    int flags);

/// <summary>
/// Native callback that adds one directory entry to the readdir buffer (the FUSE filler).
/// </summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int FillDirDelegate(IntPtr buffer, IntPtr name, IntPtr stat, long offset, int flags);

/// <summary>
/// Native callback invoked when the file system is initialized (FUSE <c>init</c>); returns the
/// (possibly replaced) fuse configuration pointer.
/// </summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate IntPtr InitDelegate(IntPtr connectionInfo, IntPtr fuseConfig);

/// <summary>
/// POSIX error numbers returned by the FUSE callbacks. The values used here are
/// identical on Linux and macOS.
/// </summary>
internal static class PosixError
{
    /// <summary>No such file or directory.</summary>
    public const int Enoent = 2;

    /// <summary>Input/output error.</summary>
    public const int Eio = 5;

    /// <summary>Permission denied.</summary>
    public const int Eacces = 13;

    /// <summary>Is a directory.</summary>
    public const int Eisdir = 21;

    /// <summary>Invalid argument.</summary>
    public const int Einval = 22;

    /// <summary>Read-only file system.</summary>
    public const int Erofs = 30;
}