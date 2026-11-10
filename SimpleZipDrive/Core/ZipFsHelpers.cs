using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SharpCompress.Archives;
using SharpCompress.Compressors.ZStandard;

namespace SimpleZipDrive.Core;

/// <summary>
///     Shared helper methods and constants used by both Dokan and WinFsp ZipFs implementations.
/// </summary>
public static class ZipFsHelpers
{
    private const int MaxRegexCacheSize = 100; // Limit cache size to prevent unbounded growth

    // Windows path length limits
    internal const int MaxPath = 260;
    internal const int MaxPathExtended = 32767;
    internal const string ExtendedPathPrefix = @"\\?\";

    private const string TempDirNameSeparator = "_";

    private const int MaxVolumeLabelLength = 32;

    private const int MaxFolderNameLength = 200;

    // Cache for compiled regex patterns to avoid recompilation overhead.
    // Uses a Dictionary + LinkedList for LRU eviction: the LinkedList tracks
    // insertion/re-access order (most recent at the end), and when the cache
    // is full the oldest entry (first in LinkedList) is evicted.
    private static readonly Dictionary<string, (Regex Regex, LinkedListNode<string> Node)> RegexCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly LinkedList<string> RegexLruOrder = new();
    private static readonly Lock RegexCacheLock = new();

    private static readonly char[] Separator = ['/'];

    // Static flag to ensure cleanup runs only once
    private static int _cleanupPerformed;

    private static readonly string CurrentProcessName = Process.GetCurrentProcess().ProcessName;
    private static readonly Lock RegistrationLock = new();
    private static volatile string? _currentInstanceDirName;
    private static readonly char[] InvalidVolumeLabelChars = ['\\', '/', ':', '*', '?', '"', '<', '>', '|'];

    /// <summary>
    ///     The base temp directory path for SimpleZipDrive (%LOCALAPPDATA%\SimpleZipDrive\Temp).
    /// </summary>
    internal static string BaseTempPath { get; } = Path.Combine(AppSettings.SettingsDirectory, "Temp");

    /// <summary>
    ///     Fallback temp root used when <see cref="BaseTempPath" /> cannot be created - for example
    ///     when the application data folder is redirected to a volume that is no longer attached
    ///     (bug reports #69557-#69563: "The create operation failed because the name contained at
    ///     least one mount point which resolves to a volume to which the specified device object is
    ///     not attached"). It lives under the system temp directory, which is independent of the
    ///     application data folder.
    /// </summary>
    internal static string FallbackTempPath { get; } =
        Path.Combine(Path.GetTempPath(), "SimpleZipDrive", "Temp");

    /// <summary>
    ///     Gets the temp roots that may hold per-session temp directories, most preferred first.
    /// </summary>
    private static IEnumerable<string> EnumerateTempRoots()
    {
        yield return BaseTempPath;

        if (!string.Equals(FallbackTempPath, BaseTempPath, StringComparison.OrdinalIgnoreCase))
            yield return FallbackTempPath;
    }

    /// <summary>
    ///     Creates the per-session temp directory under the first usable temp root and returns its
    ///     full path. When the preferred root is unusable (permissions, disconnected volume, a
    ///     dangling mount point, an invalid path, ...) the system temp root is tried instead so
    ///     mounting does not fail because of a broken application data folder.
    /// </summary>
    /// <param name="dirName">Per-session directory name (see <see cref="GenerateTempDirectoryName" />).</param>
    /// <param name="roots">
    ///     Optional explicit roots, in preference order. Defaults to <see cref="BaseTempPath" />
    ///     followed by <see cref="FallbackTempPath" />.
    /// </param>
    /// <returns>The full path of the created directory.</returns>
    /// <exception cref="IOException">No temp root could be used.</exception>
    internal static string CreateSessionTempDirectory(string dirName, IReadOnlyList<string>? roots = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(dirName);

        var candidates = roots ?? EnumerateTempRoots().ToList();
        var failures = new List<Exception>(candidates.Count);

        foreach (var root in candidates)
        {
            try
            {
                var path = Path.Combine(root, dirName);
                Directory.CreateDirectory(path);
                return path;
            }
            catch (Exception ex) when (ex is not ArgumentNullException and
                                           (IOException or UnauthorizedAccessException or ArgumentException
                                               or NotSupportedException))
            {
                failures.Add(ex);
            }
        }

        throw new IOException(
            $"Could not create the temporary working directory '{dirName}' in any of these locations: " +
            string.Join(", ", candidates.Select(static root => $"'{root}'")) + ".",
            new AggregateException(failures));
    }

    /// <summary>
    ///     Registers the current instance's temp directory name so cleanup can skip it.
    /// </summary>
    public static void RegisterCurrentTempDirectory(string dirName)
    {
        lock (RegistrationLock)
        {
            _currentInstanceDirName = dirName;
        }
    }

    /// <summary>
    ///     Clears the temp directory registration for <paramref name="dirName" />, e.g. after
    ///     construction failed and the directory was removed. Only clears the registration when
    ///     it still belongs to <paramref name="dirName" />, so a failed instance cannot unprotect
    ///     the directory of a different, healthy instance in the same process.
    /// </summary>
    internal static void ClearCurrentTempDirectory(string dirName)
    {
        lock (RegistrationLock)
        {
            if (string.Equals(_currentInstanceDirName, dirName, StringComparison.OrdinalIgnoreCase))
                _currentInstanceDirName = null;
        }
    }

    /// <summary>
    ///     Generates a unique temporary directory name using the current process ID and a GUID.
    /// </summary>
    /// <returns>A string in the format <c>{pid}_{guid}</c> suitable for use as a directory name.</returns>
    public static string GenerateTempDirectoryName()
    {
        return string.Concat(Environment.ProcessId.ToString(CultureInfo.InvariantCulture), TempDirNameSeparator,
            Guid.NewGuid().ToString("N"));
    }

    /// <summary>
    ///     Attempts to extract the process ID from a temporary directory name previously generated by
    ///     <see cref="GenerateTempDirectoryName" />.
    /// </summary>
    /// <param name="dirName">The directory name to parse.</param>
    /// <param name="processId">When this method returns <see langword="true" />, contains the extracted process ID.</param>
    /// <returns><see langword="true" /> if the directory name was successfully parsed; otherwise, <see langword="false" />.</returns>
    public static bool TryParseProcessIdFromTempDirectoryName(string dirName, out int processId)
    {
        processId = 0;
        var separatorIndex = dirName.IndexOf(TempDirNameSeparator, StringComparison.Ordinal);
        if (separatorIndex <= 0)
            return false;

        return int.TryParse(dirName.AsSpan(0, separatorIndex), CultureInfo.InvariantCulture, out processId);
    }

    /// <summary>
    ///     Cleans up orphaned temp directories from previous crashed sessions.
    ///     Directories associated with non-running processes are deleted.
    ///     For running PIDs, validates the process name to avoid misinterpreting
    ///     PID reuse by an unrelated process as a live SimpleZipDrive instance.
    /// </summary>
    public static void CleanupOrphanedTempDirectories()
    {
        foreach (var tempRoot in EnumerateTempRoots()) CleanupOrphanedTempDirectoriesIn(tempRoot);

        CleanupOrphanedTempDirectoriesIn(AppSettings.SettingsDirectory);
    }

    /// <summary>
    ///     Cleans up orphaned temp directories matching the <c>{pid}_{guid}</c> pattern
    ///     in the specified base directory. Known files (e.g. <c>settings.dat</c>) and
    ///     known subdirectories (e.g. <c>Mounts</c>) are preserved.
    /// </summary>
    private static void CleanupOrphanedTempDirectoriesIn(string baseDir)
    {
        try
        {
            if (!Directory.Exists(baseDir))
                return;

            var tempDirs = Directory.GetDirectories(baseDir);
            foreach (var dir in tempDirs)
            {
                try
                {
                    var dirName = Path.GetFileName(dir);
                    if (!TryParseProcessIdFromTempDirectoryName(dirName, out var pid))
                        continue;

                    // Skip the directory belonging to this running instance
                    if (dirName.Equals(_currentInstanceDirName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    bool shouldDelete;
                    try
                    {
                        using var process = Process.GetProcessById(pid);
                        // PID exists – verify it belongs to a SimpleZipDrive variant. The Dokan
                        // and WinFsp executables share this temp root, so an active directory
                        // owned by either variant must be preserved. If the process name doesn't
                        // match, the original owner crashed and the PID was reassigned by the
                        // OS, so the directory is orphaned.
                        var processName = process.ProcessName;
                        shouldDelete =
                            !processName.Equals(CurrentProcessName, StringComparison.OrdinalIgnoreCase) &&
                            !IsSimpleZipDriveProcessName(processName);
                    }
                    catch (ArgumentException)
                    {
                        // Process no longer exists – safe to clean up
                        shouldDelete = true;
                    }
                    catch (InvalidOperationException)
                    {
                        // Process already exited between check and access
                        shouldDelete = true;
                    }

                    if (shouldDelete) Directory.Delete(dir, true);
                }
                catch (Exception ex)
                {
                    ErrorLoggerStatic.ReportSilentException(ex,
                        $"ZipFsHelpers.CleanupOrphanedTempDirectoriesIn: Error cleaning directory '{dir}'", true);
                }
            }
        }
        catch (Exception ex)
        {
            ErrorLoggerStatic.ReportSilentException(ex,
                $"ZipFsHelpers.CleanupOrphanedTempDirectoriesIn: Error during cleanup of '{baseDir}'", true);
        }
    }

    /// <summary>
    ///     Performs cleanup if not already done for this app domain.
    /// </summary>
    public static void EnsureCleanupPerformed()
    {
        if (Interlocked.Exchange(ref _cleanupPerformed, 1) == 0) CleanupOrphanedTempDirectories();
    }

    /// <summary>
    ///     Determines whether a process name belongs to a SimpleZipDrive variant. The Dokan
    ///     and WinFsp executables share the same temp root, so a temp directory owned by a
    ///     live variant must be preserved regardless of which executable is running.
    /// </summary>
    internal static bool IsSimpleZipDriveProcessName(string processName)
    {
        return processName.StartsWith("SimpleZipDrive", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsDirectory(IArchiveEntry? entry)
    {
        if (entry == null)
            return false;

        return entry.IsDirectory ||
               (!string.IsNullOrEmpty(entry.Key) && (entry.Key.EndsWith('/') || entry.Key.EndsWith('\\')));
    }

    internal static string NormalizePath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "/";

        if (path is "\\" or "/") return "/";

        path = path.Replace('\\', '/');
        if (path is "//") return "/";

        if (!path.StartsWith('/')) path = "/" + path;

        if (path.Length > 1) path = path.TrimEnd('/');
        if (path.Length == 0) return "/";

        try
        {
            // Normalize to FormC so entries from archives created on other platforms (e.g. macOS uses
            // decomposed FormD for accented names) match lookups coming from the OS.
            return path.IsNormalized(NormalizationForm.FormC)
                ? path
                : path.Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            // The path contains invalid Unicode (e.g. unpaired surrogates); use it as-is rather
            // than throwing out of this hot lookup path.
            return path;
        }
    }

    internal static string ResolveSpecialPaths(string normalizedPath)
    {
        if (string.Equals(normalizedPath, "/", StringComparison.OrdinalIgnoreCase))
            return "/";

        var parts = normalizedPath.Split(Separator, StringSplitOptions.RemoveEmptyEntries);
        var resolved = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            switch (part)
            {
                case ".":
                    continue;
                case "..":
                {
                    if (resolved.Count > 0)
                        resolved.RemoveAt(resolved.Count - 1);
                    continue;
                }
                default:
                    resolved.Add(part);
                    break;
            }
        }

        return resolved.Count == 0 ? "/" : "/" + string.Join("/", resolved);
    }

    internal static bool IsPathLengthValid(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return true;

        if (path.StartsWith(ExtendedPathPrefix, StringComparison.Ordinal)) return path.Length <= MaxPathExtended;

        return path.Length <= MaxPath;
    }

    internal static bool IsPasswordRequiredException(Exception ex)
    {
        var message = ex.Message.ToLowerInvariant();
        // Deliberately no ("rar" AND "header") clause: SharpCompress reports corrupt archives as
        // "Unknown Rar Header", which must be classified as corruption, not a password prompt.
        return message.Contains("password", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("encrypted", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsDataErrorException(Exception ex)
    {
        if (ex is ZstdException)
            return true;

        var exceptionTypeName = ex.GetType().Name;
        return exceptionTypeName.Contains("DataError", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("Data Error", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsMatchSimple(string input, string pattern)
    {
        if (pattern.Length > MaxPath)
            return false;

        if (pattern.Equals("*", StringComparison.Ordinal) || pattern.Equals("*.*", StringComparison.Ordinal))
            return true;

        var regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*", StringComparison.OrdinalIgnoreCase)
            .Replace("\\?", ".", StringComparison.OrdinalIgnoreCase) + "$";

        lock (RegexCacheLock)
        {
            if (RegexCache.TryGetValue(regexPattern, out var entry))
            {
                // Move to end of LRU list (most recently used)
                RegexLruOrder.Remove(entry.Node);
                var newNode = RegexLruOrder.AddLast(regexPattern);
                RegexCache[regexPattern] = (entry.Regex, newNode);
                return entry.Regex.IsMatch(input);
            }

            if (RegexCache.Count >= MaxRegexCacheSize)
            {
                // Evict the least recently used (first in the linked list)
                var oldest = RegexLruOrder.First;
                if (oldest != null)
                {
                    RegexLruOrder.RemoveFirst();
                    RegexCache.Remove(oldest.Value);
                }
            }

            var newRegex = new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled,
                TimeSpan.FromMilliseconds(100));
            var lruNode = RegexLruOrder.AddLast(regexPattern);
            RegexCache[regexPattern] = (newRegex, lruNode);
            return newRegex.IsMatch(input);
        }
    }

    internal static string? GetParentPath(string normalizedPath)
    {
        if (string.Equals(normalizedPath, "/", StringComparison.OrdinalIgnoreCase))
            return null;

        var lastSlash = normalizedPath.LastIndexOf('/');
        if (lastSlash <= 0)
            return "/";

        return normalizedPath.Substring(0, lastSlash);
    }

    internal static bool IsNameMatch(string name, string pattern)
    {
        if (string.IsNullOrEmpty(pattern) || string.Equals(pattern, "*", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(pattern, "*.*", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IsMatchSimple(name, pattern);
    }

    /// <summary>
    ///     Sanitizes a string for use as a Windows volume label.
    ///     Strips invalid characters, trims whitespace, truncates to 32 characters,
    ///     and falls back to <see cref="ZipFileSystemCore.DefaultVolumeLabel" /> if the result is empty.
    /// </summary>
    internal static string SanitizeVolumeLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return ZipFileSystemCore.DefaultVolumeLabel;

        Span<char> buffer = stackalloc char[label.Length];
        var written = 0;

        foreach (var ch in label)
        {
            if (Array.IndexOf(InvalidVolumeLabelChars, ch) < 0)
                buffer[written++] = ch;
        }

        // Trim trailing spaces/dots (Windows trims these)
        while (written > 0 && (buffer[written - 1] == ' ' || buffer[written - 1] == '.')) written--;

        // Truncate to NTFS limit
        if (written > MaxVolumeLabelLength) written = MaxVolumeLabelLength;

        // Final trim in case truncation exposed trailing spaces/dots
        while (written > 0 && (buffer[written - 1] == ' ' || buffer[written - 1] == '.')) written--;

        if (written == 0)
            return ZipFileSystemCore.DefaultVolumeLabel;

        return new string(buffer[..written]);
    }

    /// <summary>
    ///     Sanitizes a string for use as a folder name.
    ///     Strips invalid path characters, trims whitespace and trailing dots,
    ///     and truncates to <see cref="MaxFolderNameLength" /> characters.
    ///     Falls back to <see cref="ZipFileSystemCore.DefaultVolumeLabel" /> if the result is empty.
    /// </summary>
    internal static string SanitizeFolderName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return ZipFileSystemCore.DefaultVolumeLabel;

        Span<char> buffer = stackalloc char[name.Length];
        var written = 0;

        foreach (var ch in name)
        {
            if (Array.IndexOf(InvalidVolumeLabelChars, ch) < 0)
                buffer[written++] = ch;
        }

        // Trim trailing spaces/dots
        while (written > 0 && (buffer[written - 1] == ' ' || buffer[written - 1] == '.')) written--;

        if (written > MaxFolderNameLength) written = MaxFolderNameLength;

        // Final trim in case truncation exposed trailing spaces/dots
        while (written > 0 && (buffer[written - 1] == ' ' || buffer[written - 1] == '.')) written--;

        if (written == 0)
            return ZipFileSystemCore.DefaultVolumeLabel;

        return new string(buffer[..written]);
    }

    /// <summary>
    ///     Gets the archive file name without extension(s), handling TAR double extensions like .tar.gz.
    ///     For "archive.tar.gz" returns "archive", for "archive.zip" returns "archive".
    /// </summary>
    internal static string GetArchiveFileNameWithoutExtension(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        var lower = fileName.ToLowerInvariant();

        if (lower.EndsWith(".tar.gz", StringComparison.Ordinal) ||
            lower.EndsWith(".tar.bz2", StringComparison.Ordinal) || lower.EndsWith(".tar.xz", StringComparison.Ordinal))
        {
            return Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(filePath));
        }

        if (lower.EndsWith(".tgz", StringComparison.Ordinal) || lower.EndsWith(".tbz2", StringComparison.Ordinal) ||
            lower.EndsWith(".txz", StringComparison.Ordinal))
        {
            return fileName[..^4]; // Strip 4-char extension
        }

        return Path.GetFileNameWithoutExtension(filePath);
    }
}