using System.Runtime.InteropServices;
using SharpSevenZip;

namespace SimpleZipDrive.Core;

/// <summary>
///     Fallback archive extractor using SharpSevenZip (native 7z.dll).
///     Used when SharpCompress fails to extract an entry.
/// </summary>
internal sealed class SevenZipFallback : IDisposable
{
    private readonly string _archivePath;
    private readonly Lock _lock = new();
    private readonly Func<string?> _passwordProvider;
    private bool _disposed;
    private Dictionary<string, int>? _entryIndexMap;
    private SharpSevenZipExtractor? _extractor;

    public SevenZipFallback(string archivePath, Func<string?> passwordProvider)
    {
        _archivePath = archivePath;
        _passwordProvider = passwordProvider;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            lock (_lock)
            {
                _extractor?.Dispose();
                _extractor = null;
            }
        }
    }

    /// <summary>
    ///     Tries to extract an entry by its normalized path to the output stream.
    ///     Returns true if extraction succeeded, false otherwise.
    /// </summary>
    public bool TryExtractEntry(string normalizedPath, Stream outputStream)
    {
        if (_disposed) return false;

        try
        {
            EnsureInitialized();

            if (_entryIndexMap == null)
                return false;

            // Normalize path: SharpSevenZip uses backslash-separated paths
            var searchPaths = new[]
            {
                normalizedPath.TrimStart('/'),
                normalizedPath.TrimStart('/').Replace('/', '\\')
            };

            foreach (var searchPath in searchPaths)
            {
                if (_entryIndexMap.TryGetValue(searchPath, out var index))
                {
                    lock (_lock)
                    {
                        if (_extractor == null)
                            return false;

                        _extractor.ExtractFile(index, outputStream);
                    }

                    return true;
                }
            }

            // Case-insensitive fallback search
            var normalizedLower = normalizedPath.TrimStart('/').ToLowerInvariant();
            foreach (var kvp in _entryIndexMap)
            {
                if (kvp.Key.Replace('\\', '/').Equals(normalizedLower, StringComparison.OrdinalIgnoreCase) ||
                    kvp.Key.Equals(normalizedLower, StringComparison.OrdinalIgnoreCase))
                {
                    lock (_lock)
                    {
                        if (_extractor == null)
                            return false;

                        _extractor.ExtractFile(kvp.Value, outputStream);
                    }

                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private void EnsureInitialized()
    {
        if (_entryIndexMap != null)
            return;

        lock (_lock)
        {
            if (_entryIndexMap != null)
                return;

            try
            {
                if (!TrySetLibraryPath())
                    return;

                var password = _passwordProvider();
                _extractor = string.IsNullOrEmpty(password)
                    ? new SharpSevenZipExtractor(_archivePath)
                    : new SharpSevenZipExtractor(_archivePath, password);

                var entries = _extractor.ArchiveFileData;
                _entryIndexMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                foreach (var entry in entries)
                {
                    if (!entry.IsDirectory && !string.IsNullOrEmpty(entry.FileName))
                        _entryIndexMap[entry.FileName] = entry.Index;
                }
            }
            catch
            {
                _entryIndexMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    private static bool TrySetLibraryPath()
    {
        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var isArm64 = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
            var dllName = isArm64 ? "7z_arm64.dll" : "7z.dll";
            var dllPath = Path.Combine(baseDir, dllName);

            if (!File.Exists(dllPath))
                return false;

            SharpSevenZipBase.SetLibraryPath(dllPath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    ///     Returns true if the 7z.dll is available in the application directory.
    /// </summary>
    public static bool IsAvailable()
    {
        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var isArm64 = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
            var dllName = isArm64 ? "7z_arm64.dll" : "7z.dll";
            return File.Exists(Path.Combine(baseDir, dllName));
        }
        catch
        {
            return false;
        }
    }
}