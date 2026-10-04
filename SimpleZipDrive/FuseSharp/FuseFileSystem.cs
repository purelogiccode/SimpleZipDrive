using System.Runtime.InteropServices;
using Serilog;

namespace SimpleZipDrive.FuseSharp;

/// <summary>
/// Serves an <see cref="IFuseVolume"/> through the FUSE 3 high-level API on Linux
/// (libfuse3) and macOS (macFUSE's libfuse3). The volume is exposed read-only:
/// <c>open</c> rejects write access, macOS <c>setattr</c> returns <c>EROFS</c> and
/// every unimplemented operation fails with <c>ENOSYS</c> by default.
/// </summary>
public sealed class FuseFileSystem
{
    private const int BlockSize = 4096;

    private const uint SIfDir = 0x4000;
    private const uint SIfReg = 0x8000;
    private const uint DirectoryMode = SIfDir | 0x16D; // r-xr-xr-x
    private const uint FileMode = SIfReg | 0x124; // r--r--r--

    private readonly IFuseVolume _vfs;
    private readonly bool _isMacOs = OperatingSystem.IsMacOS();
    private readonly bool _isArm64 = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;

    // The delegates are stored so the GC keeps them alive for the whole mount.
    private readonly GetAttrDelegate _getAttr;
    private readonly SetAttrMacDelegate _setAttrMac;
    private readonly OpenDelegate _open;
    private readonly ReadDelegate _read;
    private readonly StatFsDelegate _statFs;
    private readonly ReadDirDelegate _readDir;
    private readonly InitDelegate _init;

    private string _mountPoint = string.Empty;
    private Action? _onMounted;
    private IntPtr _fuseHandle;

    /// <summary>
    /// Initializes a new instance of the <see cref="FuseFileSystem"/> class over a volume.
    /// </summary>
    /// <param name="vfs">The volume to expose.</param>
    public FuseFileSystem(IFuseVolume vfs)
    {
        try
        {
            // The DllImport("fuse3") declarations only resolve after the resolver is
            // registered; doing it here keeps direct library use working without a
            // preceding FuseAvailability.Check. Registration is idempotent.
            FuseInterop.RegisterResolver();

            _vfs = vfs;
            _getAttr = GetAttr;
            _setAttrMac = SetAttrMac;
            _open = Open;
            _read = Read;
            _statFs = StatFs;
            _readDir = ReadDir;
            _init = Init;
        }
        catch (Exception ex)
        {
            // Setup failures are environment conditions, not application defects.
            Log.Information(ex, "Failed to create the FUSE file system");
            throw;
        }
    }

    /// <summary>
    /// Mounts the volume and blocks until it is unmounted (Ctrl+C, SIGTERM or
    /// <c>umount</c>). The FUSE session is driven manually instead of via
    /// <c>fuse_main_real</c> so that managed signal handlers can request a clean
    /// exit through <c>fuse_exit</c>.
    /// </summary>
    /// <param name="mountPoint">The existing directory to mount on.</param>
    /// <param name="debug">When <see langword="true"/>, enables libfuse debug output.</param>
    /// <param name="onMounted">Invoked once the FUSE session has started.</param>
    /// <returns>Zero on a clean unmount; otherwise, a non-zero exit code.</returns>
    public int Run(string mountPoint, bool debug, Action? onMounted)
    {
        try
        {
            return RunCore(mountPoint, debug, onMounted);
        }
        catch (Exception ex)
        {
            // A mount/session setup failure is an environment condition.
            Log.Information(ex, "FUSE session failed for '{MountPoint}'", mountPoint);
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Requests a clean unmount of the running session. Safe to call from any thread;
    /// the blocked <see cref="Run"/> call returns once libfuse exits its loop.
    /// </summary>
    public void Stop()
    {
        if (RequestExit())
            WakeUpLoop();
    }

    /// <summary>
    ///     Requests a clean session exit without waking the FUSE loop with a mount-point
    ///     request. Safe to call from the FUSE callback thread (for example inside the
    ///     <c>init</c> callback): the loop re-checks the exited flag once the callback
    ///     returns, so poking the mount point there would deadlock.
    /// </summary>
    /// <returns><see langword="true" /> when the exit request was issued.</returns>
    public bool RequestExit()
    {
        var fuse = _fuseHandle;
        if (fuse == IntPtr.Zero)
            return false;

        try
        {
            FuseInterop.FuseExit(fuse);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "fuse_exit failed");
            return false;
        }
    }

    private int RunCore(string mountPoint, bool debug, Action? onMounted)
    {
        _mountPoint = mountPoint;
        _onMounted = onMounted;

        var arguments = BuildArguments(debug);
        var strings = new IntPtr[arguments.Length];
        var argv = Marshal.AllocHGlobal(arguments.Length * IntPtr.Size);
        try
        {
            for (var i = 0; i < arguments.Length; i++)
            {
                strings[i] = Marshal.StringToCoTaskMemUTF8(arguments[i]);
                Marshal.WriteIntPtr(argv, i * IntPtr.Size, strings[i]);
            }

            var args = new FuseArgs { Argc = arguments.Length, Argv = argv, Allocated = 0 };
            try
            {
                return _isMacOs ? RunMac(ref args) : RunLinux(ref args);
            }
            finally
            {
                FuseInterop.FuseOptFreeArgs(ref args);
            }
        }
        finally
        {
            foreach (var pointer in strings)
            {
                Marshal.FreeCoTaskMem(pointer);
            }

            Marshal.FreeHGlobal(argv);
        }
    }

    private string[] BuildArguments(bool debug)
    {
        return BuildMountArguments(_isMacOs, debug, _vfs.VolumeLabel);
    }

    /// <summary>
    /// Builds the libfuse argument vector for a mount. macOS exposes the volume label
    /// through <c>volname</c>; Linux has no such option, so the label is exposed through
    /// <c>fsname</c>, which <c>mount</c> and file managers display.
    /// </summary>
    /// <param name="isMacOs">Whether the process targets macOS.</param>
    /// <param name="debug">When <see langword="true"/>, enables libfuse debug output.</param>
    /// <param name="volumeLabel">The volume label reported by the VFS.</param>
    /// <returns>The argument vector passed to <c>fuse_new</c>.</returns>
    internal static string[] BuildMountArguments(bool isMacOs, bool debug, string volumeLabel)
    {
        var arguments = new List<string> { "SimpleZipDrive", "-o", "ro" };
        if (debug)
        {
            arguments.Add("-d");
        }

        arguments.Add("-o");
        arguments.Add(isMacOs
            ? $"volname={SanitizeVolumeLabel(volumeLabel)}"
            : $"fsname={SanitizeVolumeLabel(volumeLabel)}");

        return [.. arguments];
    }

    private int RunLinux(ref FuseArgs args)
    {
        var operations = new FuseOperationsLinux
        {
            GetAttr = Marshal.GetFunctionPointerForDelegate(_getAttr),
            Open = Marshal.GetFunctionPointerForDelegate(_open),
            Read = Marshal.GetFunctionPointerForDelegate(_read),
            StatFs = Marshal.GetFunctionPointerForDelegate(_statFs),
            ReadDir = Marshal.GetFunctionPointerForDelegate(_readDir),
            Init = Marshal.GetFunctionPointerForDelegate(_init)
        };

        var fuse = FuseInterop.FuseNew(ref args, ref operations, (nuint)Marshal.SizeOf<FuseOperationsLinux>(),
            IntPtr.Zero);
        return RunSession(fuse);
    }

    private int RunMac(ref FuseArgs args)
    {
        var operations = new FuseOperationsMac
        {
            GetAttr = Marshal.GetFunctionPointerForDelegate(_getAttr),
            SetAttr = Marshal.GetFunctionPointerForDelegate(_setAttrMac),
            Open = Marshal.GetFunctionPointerForDelegate(_open),
            Read = Marshal.GetFunctionPointerForDelegate(_read),
            StatFs = Marshal.GetFunctionPointerForDelegate(_statFs),
            ReadDir = Marshal.GetFunctionPointerForDelegate(_readDir),
            Init = Marshal.GetFunctionPointerForDelegate(_init)
        };

        var fuse = FuseInterop.FuseNew(ref args, ref operations, (nuint)Marshal.SizeOf<FuseOperationsMac>(),
            IntPtr.Zero);
        return RunSession(fuse);
    }

    private int RunSession(IntPtr fuse)
    {
        if (fuse == IntPtr.Zero)
        {
            Console.Error.WriteLine("Error: the FUSE library rejected the file system setup.");
            Log.Information("fuse_new failed");
            return 1;
        }

        try
        {
            var mountResult = FuseInterop.FuseMount(fuse, _mountPoint);
            if (mountResult != 0)
            {
                Console.Error.WriteLine($"Error: failed to mount at '{_mountPoint}' (code {mountResult}).");
                Log.Information("fuse_mount failed with {Result}", mountResult);
                return 1;
            }

            try
            {
                _fuseHandle = fuse;
                using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, OnPosixSignal);
                using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, OnPosixSignal);
                using var sighup = PosixSignalRegistration.Create(PosixSignal.SIGHUP, OnPosixSignal);

                var loopResult = FuseInterop.FuseLoop(fuse);
                if (loopResult != 0)
                {
                    Log.Error("fuse_loop failed with {Result}", loopResult);
                    return 1;
                }

                return 0;
            }
            finally
            {
                _fuseHandle = IntPtr.Zero;
                FuseInterop.FuseUnmount(fuse);
            }
        }
        finally
        {
            FuseInterop.FuseDestroy(fuse);
        }
    }

    private void OnPosixSignal(PosixSignalContext context)
    {
        // Suppress the default termination so the FUSE session can be shut down cleanly.
        context.Cancel = true;

        if (_fuseHandle == IntPtr.Zero)
        {
            return;
        }

        Log.Information("{Signal} received. Unmounting...", context.Signal);
        Stop();
    }

    private void WakeUpLoop()
    {
        // A single pending FUSE request wakes the loop so it observes the exit flag.
        // statfs is used because the kernel never serves it from cache.
        for (var attempt = 0; attempt < 20 && _fuseHandle != IntPtr.Zero; attempt++)
        {
            try
            {
                FuseInterop.PokeMountPoint(_mountPoint);
            }
            catch (Exception ex)
            {
                // The mount may already be gone; the loop then exits on its own.
                Log.Debug(ex, "FUSE loop wake-up poke failed for '{MountPoint}'", _mountPoint);
            }

            Thread.Sleep(50);
        }
    }

    private IntPtr Init(IntPtr connectionInfo, IntPtr fuseConfig)
    {
        try
        {
            Log.Information("Mount successful: '{MountPoint}'", _mountPoint);
            Log.Information("Press Ctrl+C or run 'umount' to unmount.");
            _onMounted?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Mount notification failed for '{MountPoint}'", _mountPoint);
        }

        return IntPtr.Zero;
    }

    private int GetAttr(IntPtr path, IntPtr stat, IntPtr fileInfo)
    {
        try
        {
            var entry = _vfs.GetEntry(ToFusePath(path));
            if (entry is null)
            {
                return -PosixError.Enoent;
            }

            if (_isMacOs)
            {
                WriteMacStat(stat, entry);
            }
            else
            {
                WriteLinuxStat(stat, entry);
            }

            return 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "FUSE getattr failed for '{Path}'", SafePath(path));
            return -PosixError.Eio;
        }
    }

    private static int SetAttrMac(IntPtr path, IntPtr darwinAttr, int toSet, IntPtr fileInfo)
    {
        return -PosixError.Erofs;
    }

    private int Open(IntPtr path, IntPtr fileInfo)
    {
        try
        {
            var entry = _vfs.GetEntry(ToFusePath(path));
            if (entry is null)
            {
                return -PosixError.Enoent;
            }

            if (entry.IsDirectory)
            {
                return -PosixError.Eisdir;
            }

            // O_ACCMODE: 0 = O_RDONLY. Everything else is rejected.
            var flags = Marshal.ReadInt32(fileInfo);
            return (flags & 3) != 0 ? -PosixError.Eacces : 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "FUSE open failed for '{Path}'", SafePath(path));
            return -PosixError.Eio;
        }
    }

    private int Read(IntPtr path, IntPtr buffer, nuint size, long offset, IntPtr fileInfo)
    {
        try
        {
            var entry = _vfs.GetEntry(ToFusePath(path));
            if (entry is null)
            {
                return -PosixError.Enoent;
            }

            if (entry.IsDirectory)
            {
                return -PosixError.Eisdir;
            }

            if (offset < 0)
            {
                return -PosixError.Einval;
            }

            if (offset >= entry.Size)
            {
                return 0;
            }

            var bytesToRead = (int)Math.Min((long)size, entry.Size - offset);
            if (bytesToRead <= 0)
            {
                return 0;
            }

            unsafe
            {
                var span = new Span<byte>((void*)buffer, bytesToRead);
                return _vfs.ReadFile(entry, span, offset);
            }
        }
        catch (IOException ex)
        {
            // A data read failure maps to EIO instead of a silent EOF; the detail stays
            // in the debug log so corrupted media does not auto-report as a bug.
            Log.Debug(ex, "FUSE read failed for '{Path}' at offset {Offset}", SafePath(path), offset);
            return -PosixError.Eio;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "FUSE read failed for '{Path}' at offset {Offset}", SafePath(path), offset);
            return -PosixError.Eio;
        }
    }

    private int StatFs(IntPtr path, IntPtr statvfs)
    {
        try
        {
            // struct statvfs starts with the same layout on Linux and macOS.
            var blocks = (long)(_vfs.VolumeSize / BlockSize);
            Marshal.WriteInt64(statvfs, 0, BlockSize); // f_bsize
            Marshal.WriteInt64(statvfs, 8, BlockSize); // f_frsize
            Marshal.WriteInt64(statvfs, 16, blocks); // f_blocks
            Marshal.WriteInt64(statvfs, 24, 0); // f_bfree
            Marshal.WriteInt64(statvfs, 32, 0); // f_bavail
            Marshal.WriteInt64(statvfs, 40, 0); // f_files
            Marshal.WriteInt64(statvfs, 48, 0); // f_ffree
            Marshal.WriteInt64(statvfs, 56, 0); // f_favail
            Marshal.WriteInt64(statvfs, 72, 0); // f_flag
            Marshal.WriteInt64(statvfs, 80, 255); // f_namemax
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "FUSE statfs failed for '{Path}'", SafePath(path));
            return -PosixError.Eio;
        }
    }

    private int ReadDir(IntPtr path, IntPtr buffer, IntPtr filler, long offset, IntPtr fileInfo, int flags)
    {
        try
        {
            var names = GetDirectoryNames(path);
            if (names is null)
            {
                return -PosixError.Enoent;
            }

            var fill = Marshal.GetDelegateForFunctionPointer<FillDirDelegate>(filler);
            return FillDirectory(names, offset, (name, nextOffset) => fill(buffer, name, IntPtr.Zero, nextOffset, 0));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "FUSE readdir failed for '{Path}'", SafePath(path));
            return -PosixError.Eio;
        }
    }

    internal static int FillDirectory(List<string> names, long offset, Func<IntPtr, long, int> fill)
    {
        var start = offset <= 0 ? 0 : (int)offset;
        for (var i = start; i < names.Count; i++)
        {
            var name = Marshal.StringToCoTaskMemUTF8(names[i]);
            try
            {
                if (fill(name, i + 1) != 0)
                {
                    break;
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(name);
            }
        }

        return 0;
    }

    private List<string>? GetDirectoryNames(IntPtr path)
    {
        var fusePath = ToFusePath(path);
        if (_vfs.GetEntry(fusePath) is not { IsDirectory: true })
        {
            return null;
        }

        var names = new List<string> { "." };
        if (!string.Equals(fusePath, "/", StringComparison.Ordinal))
        {
            names.Add("..");
        }

        foreach (var child in _vfs.GetFolderList(fusePath))
        {
            if (string.IsNullOrEmpty(child.FileName) ||
                string.Equals(child.FileName, "\\", StringComparison.Ordinal) ||
                string.Equals(child.FileName, "/", StringComparison.Ordinal))
            {
                continue;
            }

            names.Add(child.FileName);
        }

        return names;
    }

    private void WriteLinuxStat(IntPtr stat, IFuseEntry entry)
    {
        var time = ToUnixTime(_vfs.VolumeCreationTime);
        var mode = (int)(entry.IsDirectory ? DirectoryMode : FileMode);
        var size = entry.IsDirectory ? 0 : entry.Size;

        if (_isArm64)
        {
            Marshal.WriteInt32(stat, 16, mode); // st_mode
            Marshal.WriteInt32(stat, 20, entry.IsDirectory ? 2 : 1); // st_nlink
            Marshal.WriteInt64(stat, 48, size); // st_size
            Marshal.WriteInt32(stat, 56, BlockSize); // st_blksize
        }
        else
        {
            Marshal.WriteInt64(stat, 16, entry.IsDirectory ? 2 : 1); // st_nlink
            Marshal.WriteInt32(stat, 24, mode); // st_mode
            Marshal.WriteInt64(stat, 48, size); // st_size
            Marshal.WriteInt64(stat, 56, BlockSize); // st_blksize
        }

        Marshal.WriteInt64(stat, 64, (size + 511) / 512); // st_blocks
        WriteTimespec(stat, 72, time); // st_atim
        WriteTimespec(stat, 88, time); // st_mtim
        WriteTimespec(stat, 104, time); // st_ctim
    }

    private void WriteMacStat(IntPtr stat, IFuseEntry entry)
    {
        var time = ToUnixTime(_vfs.VolumeCreationTime);
        var size = entry.IsDirectory ? 0 : entry.Size;

        Marshal.WriteInt32(stat, 0, 0); // st_dev
        Marshal.WriteInt16(stat, 4, unchecked((short)(entry.IsDirectory ? DirectoryMode : FileMode))); // st_mode
        Marshal.WriteInt16(stat, 6, unchecked((short)(entry.IsDirectory ? 2 : 1))); // st_nlink
        Marshal.WriteInt64(stat, 8, 0); // st_ino
        Marshal.WriteInt32(stat, 16, 0); // st_uid
        Marshal.WriteInt32(stat, 20, 0); // st_gid
        Marshal.WriteInt32(stat, 24, 0); // st_rdev
        WriteTimespec(stat, 32, time); // st_atimespec
        WriteTimespec(stat, 48, time); // st_mtimespec
        WriteTimespec(stat, 64, time); // st_ctimespec
        WriteTimespec(stat, 80, time); // st_birthtimespec
        Marshal.WriteInt64(stat, 96, size); // st_size
        Marshal.WriteInt64(stat, 104, (size + 511) / 512); // st_blocks
        Marshal.WriteInt32(stat, 112, BlockSize); // st_blksize
    }

    private static void WriteTimespec(IntPtr buffer, int offset, long seconds)
    {
        Marshal.WriteInt64(buffer, offset, seconds);
        Marshal.WriteInt64(buffer, offset + 8, 0);
    }

    internal static string ToFusePath(IntPtr path)
    {
        var value = Marshal.PtrToStringUTF8(path);
        if (string.IsNullOrEmpty(value))
        {
            return "/";
        }

        return value[0] == '/' ? value : "/" + value;
    }

    private static string SafePath(IntPtr path)
    {
        try
        {
            return Marshal.PtrToStringUTF8(path) ?? "<null>";
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not read a FUSE path pointer");
            return "<invalid>";
        }
    }

    internal static long ToUnixTime(DateTime value)
    {
        try
        {
            var utc = value.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
                : value.ToUniversalTime();
            return new DateTimeOffset(utc).ToUnixTimeSeconds();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not convert '{Value}' to a Unix timestamp", value);
            return 0;
        }
    }

    /// <summary>
    /// Makes a volume label safe for the macOS <c>volname</c> mount option.
    /// </summary>
    /// <param name="label">The raw volume label.</param>
    /// <returns>A short label without option separators or control characters.</returns>
    private static string SanitizeVolumeLabel(string label)
    {
        try
        {
            var filtered = label.Where(static c => !char.IsControl(c) && c is not ',' and not '/').ToArray();
            var result = new string(filtered).Trim();
            if (string.IsNullOrEmpty(result))
            {
                return "SimpleZipDrive";
            }

            return result.Length > 32 ? result[..32] : result;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to sanitize the volume label '{Label}'", label);
            return "SimpleZipDrive";
        }
    }
}