using System.Security.AccessControl;
using System.Security.Principal;
using SharpCompress.Archives;
using SharpCompress.Archives.Rar;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Archives.Tar;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Compressors.Deflate;
using SharpCompress.Compressors.ZStandard;
using SharpCompress.Readers;
using CryptographicException = System.Security.Cryptography.CryptographicException;

namespace SimpleZipDrive.Core;

/// <summary>
///     Shared core filesystem logic for both Dokan and WinFsp ZipFs wrappers.
///     Handles archive parsing, entry lookup, caching, throttling, and disposal.
/// </summary>
public class ZipFileSystemCore : IDisposable
{
    /// <summary>Default volume label displayed in Windows Explorer.</summary>
    public const string DefaultVolumeLabel = "SimpleZipDrive";

    /// <summary>Default maximum size (512 MB) for in-memory caching of a single archive entry.</summary>
    public const long DefaultMaxMemorySize = 512L * 1024 * 1024;

    internal readonly Dictionary<string, IArchiveEntry> ArchiveEntries = new(StringComparer.OrdinalIgnoreCase);

    // Cache for large files extracted to disk.
    internal readonly Dictionary<string, string> LargeFileCache = new(StringComparer.OrdinalIgnoreCase);

    // Memory throttling for small files.
    internal readonly long MaxTotalMemoryCache;
    private readonly IArchive _archive;
    private readonly string? _archiveFilePath;
    private readonly Lock _archiveLock = new();
    private readonly Dictionary<string, DateTime> _directoryCreationTimes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _directoryLastAccessTimes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _directoryLastWriteTimes = new(StringComparer.OrdinalIgnoreCase);

    // Per-entry semaphores for extraction synchronization (Fix: reduce global lock contention).
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _entryLocks = new(StringComparer.OrdinalIgnoreCase);

    // Cache for entries that failed to decompress.
    private readonly HashSet<string> _failedEntries = new(StringComparer.OrdinalIgnoreCase);

    private readonly Action<Exception?, string?> _logErrorAction;

    // Shared memory cache for decompressed entries: one decompressed buffer per entry,
    // shared by all open streams (refcounted). Buffers stay warm after the last handle
    // closes and are evicted (LRU) only when the total cache limit would be exceeded.
    private readonly Dictionary<string, MemoryEntryCacheEntry>
        _memoryEntryCache = new(StringComparer.OrdinalIgnoreCase);

    private readonly Lock _memoryLock = new();

    private readonly Func<string?> _passwordProvider;
    private readonly SevenZipFallback? _sevenZipFallback;
    private readonly Stream _sourceArchiveStream;
    internal long CurrentMemoryUsage;
    private int _disposedInt;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ZipFileSystemCore" /> class.
    /// </summary>
    public ZipFileSystemCore(
        Stream archiveStream,
        string mountPoint,
        Action<Exception?, string?> logErrorAction,
        Func<string?> passwordProvider,
        string archiveType,
        long maxMemorySize = DefaultMaxMemorySize,
        string? volumeLabel = null)
    {
        ZipFsHelpers.EnsureCleanupPerformed();

        _sourceArchiveStream = archiveStream;
        _logErrorAction = logErrorAction;
        _passwordProvider = passwordProvider;
        ArchiveType = archiveType.ToLowerInvariant();
        MaxMemorySize = maxMemorySize;
        VolumeLabel = volumeLabel ?? DefaultVolumeLabel;
        var availableMemory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        MaxTotalMemoryCache = (long)(availableMemory * 0.90);

        var tempDirName = ZipFsHelpers.GenerateTempDirectoryName();
        TempDirectoryPath = Path.Combine(ZipFsHelpers.BaseTempPath, tempDirName);
        ZipFsHelpers.RegisterCurrentTempDirectory(tempDirName);

        try
        {
            Directory.CreateDirectory(TempDirectoryPath);

            // Get file path for SevenZip fallback (if stream is a FileStream)
            if (archiveStream is FileStream fs) _archiveFilePath = fs.Name;

            if (archiveStream.CanSeek) archiveStream.Position = 0;

            _archive = OpenArchive(archiveStream);
            InitializeEntries();

            // Initialize SevenZip fallback if 7z.dll is available
            if (_archiveFilePath != null && SevenZipFallback.IsAvailable())
                _sevenZipFallback = new SevenZipFallback(_archiveFilePath, _passwordProvider);

            DiagnosticLogger.LogSection("ZipFs CONSTRUCTED");
            DiagnosticLogger.Log($"  Archive type: {ArchiveType}");
            DiagnosticLogger.Log($"  Mount point: {mountPoint}");
            DiagnosticLogger.Log($"  Total entries: {ArchiveEntries.Count}");
            DiagnosticLogger.Log($"  Implicit directories: {_directoryCreationTimes.Count}");
            DiagnosticLogger.Log($"  Max memory cache: {maxMemorySize / 1024.0 / 1024.0:F0} MB");
            DiagnosticLogger.Log($"  Max total memory: {MaxTotalMemoryCache / 1024.0 / 1024.0:F0} MB");
            DiagnosticLogger.Log($"  Temp directory: {TempDirectoryPath}");
            DiagnosticLogger.Log($"  Source stream CanSeek: {archiveStream.CanSeek}");
            DiagnosticLogger.Log(
                $"  Source stream Length: {(archiveStream.CanSeek ? archiveStream.Length / 1024.0 / 1024.0 : -1):F2} MB");
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogSection("ZipFs CONSTRUCTION FAILED");
            DiagnosticLogger.Log(ex, $"Archive type: {ArchiveType}, Mount: {mountPoint}");
            _logErrorAction(ex, $"Error during ZipFs construction for mount point '{mountPoint}'.");
            throw;
        }
    }

    /// <summary>Gets the volume label displayed for the mounted archive.</summary>
    public string VolumeLabel { get; }

    /// <summary>Gets a value indicating whether this instance has been disposed.</summary>
    public bool IsDisposed => Volatile.Read(ref _disposedInt) != 0;

    /// <summary>Gets the archive type identifier (e.g., "zip", "7z", "rar").</summary>
    public string ArchiveType { get; }

    /// <summary>Gets the path to the temporary directory used for disk-cached entries.</summary>
    public string TempDirectoryPath { get; }

    /// <summary>Gets the maximum size (in bytes) of a single entry that can be cached in memory.</summary>
    public long MaxMemorySize { get; }

    /// <summary>Gets the total size in bytes of the source archive stream, or 0 if the stream is not seekable.</summary>
    public long TotalSize => _sourceArchiveStream.CanSeek ? _sourceArchiveStream.Length : 0;

    /// <summary>
    ///     Releases all resources used by the <see cref="ZipFileSystemCore" />, including the archive,
    ///     source stream, cached temp files, and the temporary directory.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposedInt, 1) != 0)
            return;

        DiagnosticLogger.LogHeader("ZipFs DISPOSE");

        _sevenZipFallback?.Dispose();
        _archive.Dispose();
        _sourceArchiveStream.Dispose();

        lock (_archiveLock)
        {
            foreach (var tempFile in LargeFileCache.Values)
            {
                try
                {
                    if (File.Exists(tempFile)) File.Delete(tempFile);
                }
                catch (Exception ex)
                {
                    try
                    {
                        _logErrorAction(ex, $"Failed to delete temp file on dispose: {tempFile}");
                    }
                    catch
                    {
                        // Best-effort logging during disposal; ignore failures.
                    }
                }
            }

            LargeFileCache.Clear();
        }

        lock (_memoryLock)
        {
            _memoryEntryCache.Clear();
            CurrentMemoryUsage = 0;
        }

        foreach (var semaphore in _entryLocks.Values) semaphore.Dispose();

        _entryLocks.Clear();

        try
        {
            if (Directory.Exists(TempDirectoryPath)) Directory.Delete(TempDirectoryPath, true);
        }
        catch (Exception ex)
        {
            try
            {
                _logErrorAction(ex, $"Failed to delete working directory on dispose: {TempDirectoryPath}");
            }
            catch
            {
                // Best-effort logging during disposal; ignore failures.
            }
        }

        lock (_memoryLock)
        {
            CurrentMemoryUsage = 0;
        }

        DiagnosticLogger.LogHeader("ZipFs DISPOSE complete");
        GC.SuppressFinalize(this);
    }

    private IArchive OpenArchive(Stream stream)
    {
        // Strategy: Try to open and USE the archive without a password first.
        // Only prompt for password if we actually hit a decryption failure.

        bool encryptionConfirmed;
        try
        {
            var archiveWithoutPassword = ArchiveType switch
            {
                "zip" => ZipArchive.OpenArchive(stream, new ReaderOptions { LeaveStreamOpen = true }),
                "7z" => SevenZipArchive.OpenArchive(stream, new ReaderOptions { LeaveStreamOpen = true }),
                "rar" => RarArchive.OpenArchive(stream, new ReaderOptions { LeaveStreamOpen = true }),
                "tar" => TarArchive.OpenArchive(stream, new ReaderOptions { LeaveStreamOpen = true }),
                "zar" => new ZarArchive(stream),
                _ => throw new NotSupportedException($"Archive type '{ArchiveType}' is not supported.")
            };

            // Determine whether the archive can be used without a password
            var usability = GetArchiveUsability(archiveWithoutPassword);
            if (usability == ArchiveUsability.Usable) return archiveWithoutPassword;

            encryptionConfirmed = usability == ArchiveUsability.Encrypted;
            archiveWithoutPassword.Dispose();
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (NotSupportedException)
        {
            throw;
        }
        catch (Exception ex) when (!ZipFsHelpers.IsPasswordRequiredException(ex) && !IsCryptoException(ex))
        {
            throw new InvalidOperationException(
                "The archive file appears to be corrupted, incomplete, or uses an unsupported format/feature that could not be parsed.",
                ex);
        }
        catch (Exception)
        {
            // Password-related or crypto exception during open - fall through to password prompt
            encryptionConfirmed = true;
        }

        if (!encryptionConfirmed)
        {
            // Not confirmed as encrypted (e.g. corrupt or unsupported format): keep the legacy
            // behavior of opening without a password so the genuine parse error surfaces later,
            // instead of showing a pointless password dialog for damaged archives.
            if (stream.CanSeek) stream.Position = 0;

            return OpenArchiveWithPassword(stream, null);
        }

        return PromptAndOpenEncryptedArchive(stream);
    }

    /// <summary>
    ///     Opens the archive using the supplied password without any accessibility verification.
    /// </summary>
    private IArchive OpenArchiveWithPassword(Stream stream, string? password)
    {
        return ArchiveType switch
        {
            "zip" => ZipArchive.OpenArchive(stream, new ReaderOptions { Password = password, LeaveStreamOpen = true }),
            "7z" => SevenZipArchive.OpenArchive(stream,
                new ReaderOptions { Password = password, LeaveStreamOpen = true }),
            "rar" => RarArchive.OpenArchive(stream, new ReaderOptions { Password = password, LeaveStreamOpen = true }),
            "tar" => TarArchive.OpenArchive(stream, new ReaderOptions { Password = password, LeaveStreamOpen = true }),
            "zar" => new ZarArchive(stream),
            _ => throw new NotSupportedException($"Archive type '{ArchiveType}' is not supported.")
        };
    }

    /// <summary>
    ///     Handles a confirmed-encrypted archive: prompts for a password and verifies it by forcing
    ///     entry enumeration before accepting the archive. SharpCompress parses lazily, so without
    ///     this verification a wrong or cancelled password would only surface later as a
    ///     CryptographicException during initialization.
    /// </summary>
    private IArchive PromptAndOpenEncryptedArchive(Stream stream)
    {
        const int maxPasswordAttempts = 3;

        for (var attempt = 1;; attempt++)
        {
            if (stream.CanSeek) stream.Position = 0;

            var password = _passwordProvider();

            if (string.IsNullOrEmpty(password)) throw new OperationCanceledException("Mount cancelled by user.");

            IArchive? archive = null;
            try
            {
                archive = OpenArchiveWithPassword(stream, password);

                if (!VerifyArchiveAccessible(archive)) throw new CryptographicException("The password did not match.");
            }
            catch (Exception ex) when (IsPasswordMismatch(ex))
            {
                archive?.Dispose();

                if (attempt >= maxPasswordAttempts)
                {
                    throw new InvalidOperationException(
                        $"The provided password did not match the encrypted {ArchiveType.ToUpperInvariant()} archive. Mount aborted after {attempt} attempts.",
                        ex);
                }

                _logErrorAction?.Invoke(null,
                    $"Incorrect password for '{ArchiveType}' archive (attempt {attempt} of {maxPasswordAttempts}). Please try again.");
                continue;
            }
            catch
            {
                archive?.Dispose();
                throw;
            }

            return archive;
        }
    }

    /// <summary>
    ///     Determines whether the exception indicates that decryption failed because an incorrect
    ///     password was supplied (as opposed to cancellation or unrelated failures).
    /// </summary>
    private static bool IsPasswordMismatch(Exception ex)
    {
        return IsCryptoException(ex) || ZipFsHelpers.IsPasswordRequiredException(ex);
    }

    /// <summary>
    ///     Forces full entry enumeration to confirm the supplied password can actually decrypt the
    ///     archive. Returns <see langword="false" /> only when a password/crypto failure occurs; when
    ///     accessibility cannot be determined (e.g. no testable entries) the archive is accepted.
    /// </summary>
    private static bool VerifyArchiveAccessible(IArchive archive)
    {
        try
        {
            foreach (var entry in archive.Entries)
            {
                if (entry.IsDirectory || !entry.IsEncrypted || entry.Size <= 0) continue;

                using var entryStream = entry.OpenEntryStream();
                var buffer = new byte[Math.Min(1024, entry.Size)];
                var totalRead = 0;
                while (totalRead < buffer.Length)
                {
                    var bytesRead = entryStream.Read(buffer, totalRead, buffer.Length - totalRead);
                    if (bytesRead == 0) break;

                    totalRead += bytesRead;
                }
            }

            return true;
        }
        catch (Exception ex) when (IsCryptoException(ex) || ZipFsHelpers.IsPasswordRequiredException(ex))
        {
            return false;
        }
        catch
        {
            // Unrelated errors (e.g. corruption discovered during verification) are not password
            // problems - accept the archive and let normal error handling report them.
            return true;
        }
    }

    private static bool IsCryptoException(Exception ex)
    {
        return ex is CryptographicException ||
               ex.GetType().Name.Contains("CryptographicException", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Determines whether an archive can be used without a password by enumerating entries and
    ///     test-reading one. Distinguishes confirmed encryption from indeterminate failures so that
    ///     corrupt archives do not trigger a pointless password prompt.
    /// </summary>
    private static ArchiveUsability GetArchiveUsability(IArchive archive)
    {
        // First check: Does the IsEncrypted flag indicate encryption?
        bool hasEncryptedFlag;
        try
        {
            hasEncryptedFlag = archive.Entries.Any(static e => e.IsEncrypted);
        }
        catch (Exception ex) when (IsCryptoException(ex) || ZipFsHelpers.IsPasswordRequiredException(ex))
        {
            // Enumerating entries itself requires password (e.g., RAR encrypted headers)
            return ArchiveUsability.Encrypted;
        }
        catch
        {
            // Parse failure - not necessarily encrypted
            return ArchiveUsability.Indeterminate;
        }

        if (!hasEncryptedFlag)
        {
            // No entries marked as encrypted - archive is usable
            return ArchiveUsability.Usable;
        }

        // Second check: Try to actually read a file entry to verify encryption is real.
        // Some zip tools incorrectly set the encryption flag.
        var testEntry = archive.Entries.FirstOrDefault(static e => e is { IsDirectory: false, Size: > 0 });
        if (testEntry == null)
        {
            // No file entries to test - trust the flag
            return ArchiveUsability.Encrypted;
        }

        try
        {
            using var entryStream = testEntry.OpenEntryStream();
            var buffer = new byte[Math.Min(1024, testEntry.Size)];
            var totalRead = 0;
            while (totalRead < buffer.Length)
            {
                var bytesRead = entryStream.Read(buffer, totalRead, buffer.Length - totalRead);
                if (bytesRead == 0) break;

                totalRead += bytesRead;
            }

            // If we can read bytes, the entry is not actually encrypted
            return totalRead > 0 ? ArchiveUsability.Usable : ArchiveUsability.Encrypted;
        }
        catch (Exception ex) when (IsCryptoException(ex) || ZipFsHelpers.IsPasswordRequiredException(ex))
        {
            // Password-related or crypto exception confirms encryption
            return ArchiveUsability.Encrypted;
        }
        catch
        {
            // Other errors - trust the encryption flag
            return ArchiveUsability.Encrypted;
        }
    }

    private void InitializeEntries()
    {
        try
        {
            foreach (var entry in _archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Key))
                {
                    _logErrorAction?.Invoke(null, "Skipping invalid archive entry with null/empty name.");
                    continue;
                }

                var normalizedPath = ZipFsHelpers.NormalizePath(entry.Key);
                ArchiveEntries[normalizedPath] = entry;

                var currentPath = "";
                var parts = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                for (var i = 0; i < parts.Length - (ZipFsHelpers.IsDirectory(entry) ? 0 : 1); i++)
                {
                    currentPath += "/" + parts[i];
                    if (_directoryCreationTimes.ContainsKey(currentPath)) continue;

                    var entryTime = entry.LastModifiedTime ?? entry.CreatedTime ?? DateTime.Now;
                    _directoryCreationTimes[currentPath] = entryTime;
                    _directoryLastWriteTimes[currentPath] = entryTime;
                    _directoryLastAccessTimes[currentPath] = DateTime.Now;
                }
            }

            if (_directoryCreationTimes.ContainsKey("/")) return;

            var now = DateTime.Now;
            _directoryCreationTimes["/"] = now;
            _directoryLastWriteTimes["/"] = now;
            _directoryLastAccessTimes["/"] = now;
        }
        catch (Exception ex)
        {
            var exceptionTypeName = ex.GetType().Name;
            var message = ex.Message;
            var stackTrace = ex.StackTrace ?? "";
            var isDataCorruptionError = ex is IndexOutOfRangeException ||
                                        ex is EndOfStreamException ||
                                        exceptionTypeName.Contains("InvalidFormat",
                                            StringComparison.OrdinalIgnoreCase) ||
                                        exceptionTypeName.Contains("DataError", StringComparison.OrdinalIgnoreCase) ||
                                        message.Contains("Data Error", StringComparison.OrdinalIgnoreCase) ||
                                        // SharpCompress throws this for truncated or non-RAR files.
                                        message.Contains("Unknown Rar Header", StringComparison.OrdinalIgnoreCase) ||
                                        // Truncated archives fail with seek-beyond-end-of-stream errors.
                                        message.Contains("Cannot seek to position",
                                            StringComparison.OrdinalIgnoreCase) ||
                                        message.Contains("End of stream reached", StringComparison.OrdinalIgnoreCase) ||
                                        stackTrace.Contains("SharpCompress.Compressors.LZMA",
                                            StringComparison.OrdinalIgnoreCase) ||
                                        stackTrace.Contains("SharpCompress.Archives.Zip.ZipArchive.LoadEntries",
                                            StringComparison.OrdinalIgnoreCase);

            if (isDataCorruptionError)
            {
                var contextMessage =
                    "Archive data corruption detected during initialization. The archive file appears to be damaged, incomplete, or uses an unsupported compression method. " +
                    "Archive type: " + ArchiveType + ". " +
                    "Entries loaded before error: " + ArchiveEntries.Count + ". " +
                    "Exception: " + exceptionTypeName + ": " + ex.Message;
                _logErrorAction?.Invoke(ex, contextMessage);
                throw new InvalidOperationException(contextMessage, ex);
            }

            _logErrorAction?.Invoke(ex,
                "Error during ZipFs.InitializeEntries. Archive type: " + ArchiveType + ", Entries loaded: " +
                ArchiveEntries.Count + ".");
            throw;
        }
    }

    /// <summary>
    ///     Determines whether the specified archive entry is stored (uncompressed) and can be
    ///     read directly from the source stream without extraction.
    /// </summary>
    /// <param name="entry">The archive entry to check.</param>
    /// <returns>
    ///     <see langword="true" /> if the entry is stored uncompressed in a seekable zip archive; otherwise,
    ///     <see langword="false" />.
    /// </returns>
    public bool IsStoredEntry(IArchiveEntry entry)
    {
        if (!string.Equals(ArchiveType, "zip", StringComparison.OrdinalIgnoreCase))
            return false;

        if (entry.IsDirectory || entry.IsEncrypted || entry.IsSolid || entry.Size <= 0)
            return false;

        return entry switch
        {
            ZipArchiveEntry ze => ze.CompressionType == CompressionType.None,
            _ => entry.CompressedSize == entry.Size
        };
    }

    /// <summary>
    ///     Determines whether the specified entry has previously failed to decompress and is
    ///     excluded from further open attempts.
    /// </summary>
    /// <param name="normalizedPath">The normalized archive path of the entry.</param>
    /// <returns><see langword="true" /> if the entry is in the failed list; otherwise, <see langword="false" />.</returns>
    public bool IsFailedEntry(string normalizedPath)
    {
        lock (_archiveLock)
        {
            return _failedEntries.Contains(normalizedPath);
        }
    }

    /// <summary>
    ///     Marks an entry as failed so subsequent open attempts return immediately without retrying extraction.
    /// </summary>
    /// <param name="normalizedPath">The normalized archive path of the entry to mark as failed.</param>
    public void AddFailedEntry(string normalizedPath)
    {
        lock (_archiveLock)
        {
            _failedEntries.Add(normalizedPath);
        }
    }

    /// <summary>
    ///     Gets an entry node for the given normalized path, or null if not found.
    /// </summary>
    public EntryNode? GetEntryNode(string normalizedPath)
    {
        IArchiveEntry? entry;
        bool isImplicitDir;
        var dirCreationTime = DateTime.Now;
        var dirLastWriteTime = DateTime.Now;
        var dirLastAccessTime = DateTime.Now;

        lock (_archiveLock)
        {
            ArchiveEntries.TryGetValue(normalizedPath, out entry);
            isImplicitDir = _directoryCreationTimes.ContainsKey(normalizedPath) ||
                            string.Equals(normalizedPath, "/", StringComparison.OrdinalIgnoreCase);

            if (entry == null && isImplicitDir)
            {
                _directoryCreationTimes.TryGetValue(normalizedPath, out dirCreationTime);
                _directoryLastWriteTimes.TryGetValue(normalizedPath, out dirLastWriteTime);
                _directoryLastAccessTimes.TryGetValue(normalizedPath, out dirLastAccessTime);
            }
        }

        if (entry != null)
        {
            var canonicalPath = ZipFsHelpers.NormalizePath(entry.Key);
            if (ZipFsHelpers.IsDirectory(entry))
            {
                return new EntryNode
                {
                    NormalizedPath = normalizedPath,
                    CanonicalPath = canonicalPath,
                    IsDir = true,
                    Entry = entry,
                    FileSize = 0,
                    CreationTime = entry.CreatedTime ?? DateTime.Now,
                    LastWriteTime = entry.LastModifiedTime ?? DateTime.Now,
                    LastAccessTime = DateTime.Now
                };
            }

            return new EntryNode
            {
                NormalizedPath = normalizedPath,
                CanonicalPath = canonicalPath,
                IsDir = false,
                Entry = entry,
                FileSize = entry.Size,
                CreationTime = entry.CreatedTime ?? DateTime.Now,
                LastWriteTime = entry.LastModifiedTime ?? DateTime.Now,
                LastAccessTime = DateTime.Now
            };
        }

        if (isImplicitDir)
        {
            return new EntryNode
            {
                NormalizedPath = normalizedPath,
                CanonicalPath = normalizedPath,
                IsDir = true,
                Entry = null,
                FileSize = 0,
                CreationTime = dirCreationTime,
                LastWriteTime = dirLastWriteTime,
                LastAccessTime = dirLastAccessTime
            };
        }

        return null;
    }

    /// <summary>
    ///     Resolves a raw file name to a normalized path and entry node.
    /// </summary>
    public static bool TryResolvePath(string fileName, out string normalizedPath)
    {
        normalizedPath = ZipFsHelpers.NormalizePath(fileName);
        normalizedPath = ZipFsHelpers.ResolveSpecialPaths(normalizedPath);
        return true;
    }

    /// <summary>
    ///     Lists the immediate children of a directory as <see cref="EntryNode" /> items.
    ///     Returns an empty list if the path is not a directory.
    /// </summary>
    public List<EntryNode> ListDirectory(string normalizedPath)
    {
        var result = new List<EntryNode>();
        var seenFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var isExplicitDirEntry = ArchiveEntries.TryGetValue(normalizedPath, out var dirEntry) &&
                                 ZipFsHelpers.IsDirectory(dirEntry);
        var isImplicitDir = _directoryCreationTimes.ContainsKey(normalizedPath) ||
                            string.Equals(normalizedPath, "/", StringComparison.OrdinalIgnoreCase);

        if (!isExplicitDirEntry && !isImplicitDir) return result;

        Thread.MemoryBarrier();

        var searchPrefix = string.Equals(normalizedPath, "/", StringComparison.OrdinalIgnoreCase)
            ? "/"
            : normalizedPath.TrimEnd('/') + "/";

        foreach (var kvp in ArchiveEntries)
        {
            var path = kvp.Key;
            if (path.Equals(searchPrefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (!path.StartsWith(searchPrefix, StringComparison.OrdinalIgnoreCase)) continue;

            var remainder = path.Substring(searchPrefix.Length);
            var slashIndex = remainder.IndexOf('/', StringComparison.OrdinalIgnoreCase);
            if (slashIndex != -1)
            {
                if (!(remainder.EndsWith('/') && slashIndex == remainder.Length - 1))
                    continue;
            }

            var entry = kvp.Value;
            string? fileNameOnly = null;
            var isDir = ZipFsHelpers.IsDirectory(entry);

            if (isDir)
            {
                if (entry.Key != null)
                {
                    var tempFullName = entry.Key.TrimEnd('/', '\\');
                    fileNameOnly = Path.GetFileName(tempFullName);
                }
            }
            else
            {
                fileNameOnly = Path.GetFileName(entry.Key);
            }

            if (!string.IsNullOrEmpty(fileNameOnly) && seenFileNames.Add(fileNameOnly))
            {
                var canonicalPath = ZipFsHelpers.NormalizePath(entry.Key);
                result.Add(new EntryNode
                {
                    NormalizedPath = searchPrefix + fileNameOnly,
                    CanonicalPath = canonicalPath,
                    IsDir = isDir,
                    Entry = entry,
                    FileSize = isDir ? 0 : entry.Size,
                    CreationTime = entry.CreatedTime ?? DateTime.Now,
                    LastWriteTime = entry.LastModifiedTime ?? DateTime.Now,
                    LastAccessTime = DateTime.Now
                });
            }
        }

        List<KeyValuePair<string, DateTime>> dirSnapshot;
        lock (_archiveLock)
        {
            dirSnapshot = _directoryCreationTimes.ToList();
        }

        foreach (var dirKvp in dirSnapshot)
        {
            var dirPathKey = dirKvp.Key;
            if (dirPathKey.Equals(searchPrefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (!dirPathKey.StartsWith(searchPrefix, StringComparison.OrdinalIgnoreCase)) continue;

            var remainder = dirPathKey.Substring(searchPrefix.Length);
            if (remainder.Contains('/', StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrEmpty(remainder))
            {
                continue;
            }

            var name = dirPathKey.Split('/').LastOrDefault(static s => !string.IsNullOrEmpty(s));
            if (!string.IsNullOrEmpty(name) && seenFileNames.Add(name))
            {
                DateTime ct, lwt, lat;
                lock (_archiveLock)
                {
                    _directoryCreationTimes.TryGetValue(dirPathKey, out ct);
                    _directoryLastWriteTimes.TryGetValue(dirPathKey, out lwt);
                    _directoryLastAccessTimes.TryGetValue(dirPathKey, out lat);
                }

                var implicitPath = searchPrefix + name;
                result.Add(new EntryNode
                {
                    NormalizedPath = implicitPath,
                    CanonicalPath = implicitPath,
                    IsDir = true,
                    Entry = null,
                    FileSize = 0,
                    CreationTime = ct,
                    LastWriteTime = lwt,
                    LastAccessTime = lat
                });
            }
        }

        return result;
    }

    /// <summary>
    ///     Opens a stream for reading an archive entry with caching.
    ///     Returns null only if the entry is in the failed list.
    ///     Throws <see cref="IOException" /> on disk space or cache file errors.
    ///     May throw other exceptions on extraction errors.
    /// </summary>
    public Stream? OpenEntryStream(IArchiveEntry entry, string normalizedPath)
    {
        if (IsFailedEntry(normalizedPath)) return null;

        var entrySize = entry.Size;

        // Stored (uncompressed) entry fast path.
        if (IsStoredEntry(entry) && _sourceArchiveStream.CanSeek)
        {
            Stream? storedStream = null;
            lock (_archiveLock)
            {
                try
                {
                    using var entryStream = entry.OpenEntryStream();
                    var dataStart = _sourceArchiveStream.Position;
                    storedStream = new StoredEntryStream(_sourceArchiveStream, dataStart, entrySize, _archiveLock);
                }
                catch (Exception storedEx)
                {
                    ErrorLoggerStatic.ReportSilentException(storedEx,
                        $"ZipFs.OpenEntryStream: StoredEntryStream creation failed for '{normalizedPath}'", true);
                }
            }

            if (storedStream != null)
            {
                LogMessage(
                    $"Stored entry detected: '{normalizedPath}' ({entrySize / 1024.0 / 1024.0:F2} MB). Using direct-read mode (no cache).");
                LogMessage("");
                return storedStream;
            }
        }

        // Large file: cache to disk.
        if (entrySize >= MaxMemorySize || entrySize < 0)
            return OpenDiskCachedStream(entry, normalizedPath, entrySize, true);

        // Small file: cache in memory as a shared, refcounted buffer (decompressed once per
        // entry regardless of how many handles are opened concurrently or on demand).
        SharedMemoryStream? sharedStream;
        try
        {
            sharedStream = AcquireSharedMemoryStream(normalizedPath, entrySize, () =>
            {
                lock (_archiveLock)
                {
                    using var entryStream = entry.OpenEntryStream();
                    return DecompressEntryToBuffer(entrySize, entryStream.CopyTo);
                }
            });
        }
        catch (Exception ex)
        {
            if (_sevenZipFallback != null)
            {
                var fallback = TryFallbackExtraction(normalizedPath, entrySize, false);
                if (fallback != null)
                    return fallback;
            }

            LogMessage($"Decompression failed for '{normalizedPath}' ({ex.GetType().Name}), no fallback available.");
            AddFailedEntry(normalizedPath);
            return null;
        }

        if (sharedStream == null)
        {
            // Memory cache limit reached (or decompression ran out of memory): use disk cache.
            LogMessage(
                $"Memory limit approaching. Using disk cache for small file '{normalizedPath}' ({entrySize / 1024.0 / 1024.0:F2} MB).");
            return OpenDiskCachedStream(entry, normalizedPath, entrySize, false);
        }

        LogMessage($"Memory cache: '{normalizedPath}' ({entrySize / 1024.0 / 1024.0:F2} MB) opened from shared cache.");
        LogMessage("");
        return sharedStream;
    }

    /// <summary>
    ///     Decompresses an entry directly into a preallocated, exact-size buffer.
    ///     Writing into the final array instead of a growing <see cref="MemoryStream" /> followed by
    ///     <see cref="MemoryStream.ToArray" /> avoids a transient second copy of the entry, halving
    ///     the peak memory footprint during decompression of large entries. Resizes (copies) only in
    ///     the rare case the decompressed length differs from the declared entry size.
    /// </summary>
    /// <param name="entrySize">Declared uncompressed size of the entry; used to preallocate.</param>
    /// <param name="decompressInto">Callback that writes the decompressed bytes to the given stream.</param>
    /// <returns>Buffer containing exactly the decompressed bytes.</returns>
    private static byte[] DecompressEntryToBuffer(long entrySize, Action<Stream> decompressInto)
    {
        var capacity = entrySize is > 0 and <= int.MaxValue ? (int)entrySize : 4096;
        var buffer = new byte[capacity];

        using var ms = new MemoryStream(buffer, 0, capacity, true, false);
        decompressInto(ms);
        var written = (int)ms.Position;

        return written == capacity ? buffer : buffer[..written];
    }

    /// <summary>
    ///     Acquires a stream over the shared in-memory copy of the entry, decompressing it on first
    ///     use via <paramref name="decompress" />. Concurrent and repeated opens share the single
    ///     decompressed buffer (refcounted). Returns null when the total memory cache limit would
    ///     be exceeded or decompression ran out of memory (caller falls back to disk caching).
    /// </summary>
    private SharedMemoryStream? AcquireSharedMemoryStream(string normalizedPath, long entrySize,
        Func<byte[]> decompress)
    {
        lock (_memoryLock)
        {
            if (TryAcquireCached(normalizedPath, out var stream))
                return stream;
        }

        SemaphoreSlim entrySemaphore;
        try
        {
            entrySemaphore = _entryLocks.GetOrAdd(normalizedPath, static _ => new SemaphoreSlim(1, 1));
            entrySemaphore.Wait();
        }
        catch (ObjectDisposedException)
        {
            // The core is being disposed during shutdown; abort the acquire gracefully
            // so the caller falls back to disk caching (which also fails cleanly).
            return null;
        }

        try
        {
            lock (_memoryLock)
            {
                if (TryAcquireCached(normalizedPath, out var stream))
                    return stream;

                EvictColdMemoryEntries(entrySize);

                if (CurrentMemoryUsage + entrySize > MaxTotalMemoryCache)
                    return null;
            }

            byte[] entryBytes;
            try
            {
                entryBytes = decompress();
            }
            catch (OutOfMemoryException)
            {
                // Caller falls back to disk caching.
                return null;
            }

            lock (_memoryLock)
            {
                if (TryAcquireCached(normalizedPath, out var stream))
                    return stream;

                var entry = new MemoryEntryCacheEntry { Buffer = entryBytes, Size = entryBytes.Length };
                entry.RefCount = 1;
                entry.LastUsed = Environment.TickCount64;
                _memoryEntryCache[normalizedPath] = entry;
                CurrentMemoryUsage += entryBytes.Length;
                return new SharedMemoryStream(entryBytes, () => ReleaseMemoryEntry(normalizedPath));
            }
        }
        finally
        {
            try
            {
                entrySemaphore.Release();
            }
            catch (ObjectDisposedException)
            {
                /* Disposed during shutdown */
            }
        }
    }

    private bool TryAcquireCached(string normalizedPath, out SharedMemoryStream? stream)
    {
        stream = null;
        if (!_memoryEntryCache.TryGetValue(normalizedPath, out var entry))
            return false;

        entry.RefCount++;
        entry.LastUsed = Environment.TickCount64;
        stream = new SharedMemoryStream(entry.Buffer, () => ReleaseMemoryEntry(normalizedPath));
        return true;
    }

    private void ReleaseMemoryEntry(string normalizedPath)
    {
        lock (_memoryLock)
        {
            if (!_memoryEntryCache.TryGetValue(normalizedPath, out var entry))
                return;

            if (entry.RefCount > 0) entry.RefCount--;

            entry.LastUsed = Environment.TickCount64;
            // The buffer stays warm in the cache (RefCount == 0) until memory pressure evicts
            // it, so repeated opens and on-demand reads reuse the single decompressed copy.
        }
    }

    private void EvictColdMemoryEntries(long requiredBytes)
    {
        if (CurrentMemoryUsage + requiredBytes <= MaxTotalMemoryCache)
            return;

        var coldEntries = _memoryEntryCache
            .Where(static kv => kv.Value.RefCount == 0)
            .OrderBy(static kv => kv.Value.LastUsed)
            .ToList();

        foreach (var kv in coldEntries)
        {
            if (CurrentMemoryUsage + requiredBytes <= MaxTotalMemoryCache)
                break;

            _memoryEntryCache.Remove(kv.Key);
            CurrentMemoryUsage -= kv.Value.Size;
            if (CurrentMemoryUsage < 0) CurrentMemoryUsage = 0;
        }
    }

    private FileStream? OpenDiskCachedStream(IArchiveEntry entry, string normalizedPath, long entrySize,
        bool isLargeFile)
    {
        string? cachedPath = null;
        lock (_archiveLock)
        {
            if (LargeFileCache.TryGetValue(normalizedPath, out var path)) cachedPath = path;
        }

        if (cachedPath != null)
        {
            LogMessage($"Reusing existing temporary cache for '{normalizedPath}'.");
        }
        else
        {
            SemaphoreSlim entrySemaphore;
            try
            {
                entrySemaphore = _entryLocks.GetOrAdd(normalizedPath, static _ => new SemaphoreSlim(1, 1));
                entrySemaphore.Wait();
            }
            catch (ObjectDisposedException)
            {
                // The core is being disposed during shutdown; abort the extraction gracefully.
                return null;
            }

            try
            {
                // Double-check after acquiring per-entry lock.
                lock (_archiveLock)
                {
                    if (LargeFileCache.TryGetValue(normalizedPath, out var existingPath)) cachedPath = existingPath;
                }

                if (cachedPath != null)
                {
                    LogMessage($"Reusing existing temporary cache for '{normalizedPath}'.");
                }
                else
                {
                    if (isLargeFile)
                    {
                        LogMessage(
                            $"Large file detected: '{normalizedPath}' ({entrySize / 1024.0 / 1024.0:F2} MB). Extracting to temporary disk cache...");
                        LogMessage("");
                    }

                    var newTempFilePath = CreateSecureTempFile();

                    if (entrySize >= 0)
                    {
                        try
                        {
                            var tempDrivePathRoot = Path.GetPathRoot(newTempFilePath) ?? "C:\\";
                            var tempDrive = new DriveInfo(tempDrivePathRoot);
                            if (tempDrive.AvailableFreeSpace < entrySize)
                            {
                                try
                                {
                                    File.Delete(newTempFilePath);
                                }
                                catch (Exception ex)
                                {
                                    ErrorLoggerStatic.ReportSilentException(ex,
                                        $"ZipFs.OpenDiskCachedStream: Failed to delete temp file '{newTempFilePath}' during disk space check",
                                        true);
                                }

                                var errorMessage =
                                    $"Insufficient disk space to extract file '{normalizedPath}' ({entrySize / 1024.0 / 1024.0:F2} MB). Available: {tempDrive.AvailableFreeSpace / 1024.0 / 1024.0:F2} MB, Required: {entrySize / 1024.0 / 1024.0:F2} MB.";
                                throw new IOException(errorMessage);
                            }
                        }
                        catch (IOException)
                        {
                            throw;
                        }
                        catch (Exception driveEx)
                        {
                            _logErrorAction(driveEx,
                                $"Error checking disk space for file extraction of '{normalizedPath}'.");
                        }
                    }

                    // Extract outside the global archive lock — only per-entry lock is held.
                    var extractionSucceeded = false;
                    try
                    {
                        ExtractEntryToDisk(entry, newTempFilePath);
                        extractionSucceeded = true;
                    }
                    catch (Exception ex) when (IsExtractionFailure(ex))
                    {
                        LogMessage(
                            $"Disk-cached extraction failed for '{normalizedPath}' ({ex.GetType().Name}), trying fallback...");
                        if (_sevenZipFallback != null)
                        {
                            try
                            {
                                using var fallbackOutput = new FileStream(newTempFilePath, FileMode.Truncate,
                                    FileAccess.Write, FileShare.None);
                                if (_sevenZipFallback.TryExtractEntry(normalizedPath, fallbackOutput))
                                    extractionSucceeded = true;
                            }
                            catch (Exception fallbackEx)
                            {
                                LogMessage(
                                    $"SevenZip fallback also failed for '{normalizedPath}': {fallbackEx.Message}");
                            }
                        }

                        if (!extractionSucceeded)
                        {
                            LogMessage(
                                $"Extraction failed for '{normalizedPath}' ({ex.GetType().Name}), no fallback available. Entry marked as failed.");
                            AddFailedEntry(normalizedPath);
                            CleanupTempFile(newTempFilePath);
                            return null;
                        }
                    }
                    catch (Exception ex)
                    {
                        LogMessage(
                            $"Disk-cached extraction failed for '{normalizedPath}' ({ex.GetType().Name}). Entry marked as failed.");
                        AddFailedEntry(normalizedPath);
                        CleanupTempFile(newTempFilePath);
                        _logErrorAction(ex,
                            $"ZipFs.OpenDiskCachedStream: Non-extraction exception during disk caching of '{normalizedPath}'.");
                        return null;
                    }

                    lock (_archiveLock)
                    {
                        LargeFileCache[normalizedPath] = newTempFilePath;
                    }

                    cachedPath = newTempFilePath;
                    LogMessage($"Extraction complete for '{normalizedPath}'. Temp file: '{cachedPath}'");
                }
            }
            finally
            {
                try
                {
                    entrySemaphore.Release();
                }
                catch (ObjectDisposedException)
                {
                    /* Disposed during shutdown */
                }
            }
        }

        if (string.IsNullOrEmpty(cachedPath))
        {
            throw new IOException(
                $"Disk caching failed for file '{normalizedPath}'. The cached path is unexpectedly null after extraction.");
        }

        try
        {
            return new FileStream(cachedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (Exception fsEx)
        {
            throw new IOException(
                $"Failed to open cached temp file '{cachedPath}' for reading file '{normalizedPath}'.", fsEx);
        }
    }

    /// <summary>
    ///     Reads from a cached entry stream, handling StoredEntryStream and seekable/non-seekable streams.
    /// </summary>
    public static int ReadStream(Stream stream, long offset, byte[] buffer, int bufferOffset, int count)
    {
        if (stream is StoredEntryStream stored) return stored.ReadAt(offset, buffer, bufferOffset, count);

        if (stream.CanSeek)
        {
            if (offset >= stream.Length) return 0;

            stream.Position = offset;
        }
        else
        {
            if (offset != stream.Position)
                throw new InvalidOperationException("Non-sequential read requested for non-seekable stream.");
        }

        return stream.Read(buffer, bufferOffset, count);
    }

    /// <summary>
    ///     Validates path length and logs an error if it exceeds limits.
    /// </summary>
    public bool ValidatePathLength(string path, string operationName)
    {
        if (!ZipFsHelpers.IsPathLengthValid(path))
        {
            var isExtended = path.StartsWith(ZipFsHelpers.ExtendedPathPrefix, StringComparison.Ordinal);
            var maxLength = isExtended ? ZipFsHelpers.MaxPathExtended : ZipFsHelpers.MaxPath;
            var pathType = isExtended ? "extended-length" : "standard";

            _logErrorAction(
                new PathTooLongException($"Path exceeds maximum length for {pathType} paths ({maxLength} characters)."),
                $"ZipFs.{operationName}: Path length validation failed - {path.Length} characters.");

            return false;
        }

        return true;
    }

    internal static void LogMessage(string message)
    {
        try
        {
            var loggingService = ServiceProvider.TryGet<ILoggingService>();
            loggingService?.Log(message);
        }
        catch (Exception ex)
        {
            ErrorLoggerStatic.ReportSilentException(ex, "ZipFs.LogMessage: Logging service error", true);
        }
    }

    /// <summary>
    ///     Creates a temporary file with restricted permissions accessible only to the current user.
    /// </summary>
    public string CreateSecureTempFile()
    {
        var tempFilePath = Path.Combine(TempDirectoryPath, Guid.NewGuid().ToString("N") + ".tmp");

        File.Create(tempFilePath).Dispose();

        try
        {
            var fileInfo = new FileInfo(tempFilePath);

            var fileSecurity = fileInfo.GetAccessControl();

            fileSecurity.SetAccessRuleProtection(true, false);

            var existingRules = fileSecurity.GetAccessRules(true, true, typeof(SecurityIdentifier));
            foreach (FileSystemAccessRule rule in existingRules) fileSecurity.RemoveAccessRule(rule);

            var currentUser = WindowsIdentity.GetCurrent();
            var currentUserSid =
                currentUser.User ?? throw new InvalidOperationException("Unable to get current user SID");

            var accessRule = new FileSystemAccessRule(
                currentUserSid,
                FileSystemRights.FullControl,
                AccessControlType.Allow);
            fileSecurity.AddAccessRule(accessRule);

            fileInfo.SetAccessControl(fileSecurity);
        }
        catch (PlatformNotSupportedException ex)
        {
            ErrorLoggerStatic.ReportSilentException(ex,
                $"ZipFs.CreateSecureTempFile: Platform not supported for ACL on '{tempFilePath}'", true);
        }
        catch (InvalidOperationException ex)
        {
            ErrorLoggerStatic.ReportSilentException(ex,
                $"ZipFs.CreateSecureTempFile: Invalid operation setting ACL on '{tempFilePath}'", true);
        }

        return tempFilePath;
    }

    /// <summary>
    ///     Determines whether an exception indicates an extraction failure that should trigger the fallback.
    /// </summary>
    private static bool IsExtractionFailure(Exception ex)
    {
        return ex is ZlibException
                   or ZstdException
                   or ArgumentOutOfRangeException
                   or NullReferenceException
                   or InvalidOperationException
               || ZipFsHelpers.IsDataErrorException(ex)
               || IsCompressionLibraryException(ex);
    }

    /// <summary>
    ///     Checks whether a broad exception type (e.g. ArgumentNullException) originates from a compression library.
    ///     This prevents masking real application bugs — only exceptions from SharpCompress, zlib, zstd, or SevenZip
    ///     are treated as extraction failures.
    /// </summary>
    private static bool IsCompressionLibraryException(Exception ex)
    {
        if (ex is not (ArgumentNullException or ArgumentException or IndexOutOfRangeException))
            return false;

        var source = ex.Source ?? string.Empty;
        var stackTrace = ex.StackTrace ?? string.Empty;
        return source.Contains("SharpCompress", StringComparison.OrdinalIgnoreCase)
               || source.Contains("SevenZip", StringComparison.OrdinalIgnoreCase)
               || source.Contains("zlib", StringComparison.OrdinalIgnoreCase)
               || source.Contains("Zstd", StringComparison.OrdinalIgnoreCase)
               || stackTrace.Contains("SharpCompress", StringComparison.OrdinalIgnoreCase)
               || stackTrace.Contains("SevenZip", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Extracts an entry to a disk file using SharpCompress.
    ///     Throws on failure; the caller is responsible for fallback handling.
    /// </summary>
    private static void ExtractEntryToDisk(IArchiveEntry entry, string tempFilePath)
    {
        try
        {
            using var entryStream = entry.OpenEntryStream();
            using var tempFileStream =
                new FileStream(tempFilePath, FileMode.Truncate, FileAccess.Write, FileShare.None);
            entryStream.CopyTo(tempFileStream);
        }
        catch
        {
            CleanupTempFile(tempFilePath);
            throw;
        }
    }

    /// <summary>
    ///     Tries to extract an entry using the SevenZip fallback to a memory or disk cached stream.
    ///     Returns the stream if successful, null otherwise.
    /// </summary>
    private Stream? TryFallbackExtraction(string normalizedPath, long entrySize, bool isLargeFile)
    {
        if (_sevenZipFallback == null)
            return null;

        try
        {
            // Large file or unknown size: use disk cache
            if (entrySize >= MaxMemorySize || entrySize < 0 || isLargeFile)
            {
                var tempFilePath = CreateSecureTempFile();
                using (var outputStream =
                       new FileStream(tempFilePath, FileMode.Truncate, FileAccess.Write, FileShare.None))
                {
                    if (!_sevenZipFallback.TryExtractEntry(normalizedPath, outputStream))
                    {
                        CleanupTempFile(tempFilePath);
                        return null;
                    }
                }

                lock (_archiveLock)
                {
                    LargeFileCache[normalizedPath] = tempFilePath;
                }

                return new FileStream(tempFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            }

            // Small file: use the shared memory cache.
            return AcquireSharedMemoryStream(normalizedPath, entrySize, () =>
                DecompressEntryToBuffer(entrySize,
                    output =>
                    {
                        if (!_sevenZipFallback.TryExtractEntry(normalizedPath, output))
                            throw new InvalidOperationException("SevenZip fallback extraction failed.");
                    }));
        }
        catch
        {
            return null;
        }
    }

    private static void CleanupTempFile(string tempFilePath)
    {
        try
        {
            File.Delete(tempFilePath);
        }
        catch (Exception cleanupEx)
        {
            ErrorLoggerStatic.ReportSilentException(cleanupEx,
                $"ZipFs.CleanupTempFile: Failed to delete '{tempFilePath}'", true);
        }
    }

    /// <summary>
    ///     Dumps a diagnostic listing of all archive entries, implicit directories, and failed entries
    ///     to the <see cref="DiagnosticLogger" /> output.
    /// </summary>
    /// <param name="maxEntries">Maximum number of entries to display per category.</param>
    public void DumpEntries(int maxEntries = 100)
    {
        try
        {
            DiagnosticLogger.LogHeader("ENTRY DUMP");
            DiagnosticLogger.Log($"  Total entries: {ArchiveEntries.Count}");
            DiagnosticLogger.Log($"  Implicit directories: {_directoryCreationTimes.Count}");
            DiagnosticLogger.Log($"  Failed entries: {_failedEntries.Count}");

            var count = 0;
            lock (_archiveLock)
            {
                foreach (var kvp in ArchiveEntries.OrderBy(static k => k.Key, StringComparer.OrdinalIgnoreCase))
                {
                    if (count >= maxEntries)
                    {
                        DiagnosticLogger.Log($"  ... ({ArchiveEntries.Count - maxEntries} more entries)");
                        break;
                    }

                    var entry = kvp.Value;
                    var isDir = ZipFsHelpers.IsDirectory(entry);
                    var typeLabel = isDir ? "DIR" : "FILE";
                    var sizeStr = isDir ? "" : $" ({entry.Size / 1024.0:F1} KB)";
                    DiagnosticLogger.Log($"  [{typeLabel}] {kvp.Key}{sizeStr}");
                    count++;
                }
            }

            if (_directoryCreationTimes.Count > 0)
            {
                DiagnosticLogger.Log("  --- Implicit directories ---");
                count = 0;
                lock (_archiveLock)
                {
                    foreach (var kvp in _directoryCreationTimes.OrderBy(static k => k.Key,
                                 StringComparer.OrdinalIgnoreCase))
                    {
                        if (count >= maxEntries) break;

                        DiagnosticLogger.Log($"  [IMPLICIT] {kvp.Key}");
                        count++;
                    }
                }
            }

            if (_failedEntries.Count > 0)
            {
                DiagnosticLogger.Log("  --- Failed entries ---");
                string[] failedSnapshot;
                lock (_archiveLock)
                {
                    failedSnapshot = _failedEntries.ToArray();
                }

                foreach (var failed in failedSnapshot) DiagnosticLogger.Log($"  [FAILED] {failed}");
            }

            DiagnosticLogger.LogHeader("END ENTRY DUMP");
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log(ex, "DumpEntries failed");
            _logErrorAction(ex, "ZipFs.DumpEntries failed");
        }
    }

    private enum ArchiveUsability
    {
        /// <summary>The archive can be used without a password.</summary>
        Usable,

        /// <summary>The archive requires a password (encryption confirmed).</summary>
        Encrypted,

        /// <summary>Accessibility could not be determined (e.g. parse failure) - encryption is not confirmed.</summary>
        Indeterminate
    }
}