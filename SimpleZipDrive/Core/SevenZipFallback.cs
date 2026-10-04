using System.Diagnostics;
using System.Text;

namespace SimpleZipDrive.Core;

/// <summary>
///     Fallback archive extractor that runs the bundled 7-Zip command-line executable
///     (7za.exe on Windows, 7zz/7zzs on Linux and macOS) to extract entries that SharpCompress
///     cannot read. No native library is loaded into the process.
/// </summary>
internal sealed class SevenZipFallback : IDisposable
{
    // A failed initialization is retried a few times before the fallback is permanently
    // disabled for this instance; otherwise a one-off failure (locked executable, transient
    // password-provider error) would silently remove fallback extraction for the whole mount.
    private const int MaxInitializationAttempts = 3;
    private const string EntryPathPrefix = "Path = ";
    private const string FolderPrefix = "Folder = ";

    private static readonly TimeSpan ListingTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ExtractionTimeout = TimeSpan.FromMinutes(30);

    private static readonly string? ExecutablePath = LocateExecutable();

    private readonly string _archivePath;
    private readonly Lock _lock = new();
    private readonly Func<string?> _passwordProvider;
    private bool _disposed;
    private Dictionary<string, string>? _entryPathMap;
    private int _initializationAttempts;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SevenZipFallback" /> class.
    /// </summary>
    /// <param name="archivePath">Full path of the archive to extract from.</param>
    /// <param name="passwordProvider">Callback that supplies the archive password when one is required.</param>
    public SevenZipFallback(string archivePath, Func<string?> passwordProvider)
    {
        _archivePath = archivePath;
        _passwordProvider = passwordProvider;
    }

    /// <summary>
    ///     Marks the fallback as disposed. No native resources are held.
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
    }

    /// <summary>
    ///     Returns true when a 7-Zip command-line executable suitable for the current platform
    ///     is present beside the application.
    /// </summary>
    public static bool IsAvailable()
    {
        return ExecutablePath is not null;
    }

    /// <summary>
    ///     Tries to extract an entry by its normalized path to the output stream.
    ///     Returns true if extraction succeeded, false otherwise.
    /// </summary>
    public bool TryExtractEntry(string normalizedPath, Stream outputStream)
    {
        if (_disposed || ExecutablePath is null)
            return false;

        try
        {
            EnsureInitialized();

            if (_entryPathMap is null || string.IsNullOrEmpty(normalizedPath))
                return false;

            var key = NormalizeEntryKey(normalizedPath);
            if (!_entryPathMap.TryGetValue(key, out var archiveEntryPath))
                return false;

            return ExtractEntry(archiveEntryPath, outputStream);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log(ex, "SevenZipFallback.TryExtractEntry failed");
            return false;
        }
    }

    private void EnsureInitialized()
    {
        if (_entryPathMap != null)
            return;

        lock (_lock)
        {
            if (_entryPathMap != null)
                return;

            try
            {
                _entryPathMap = ListEntries();
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Log(ex, "SevenZipFallback.EnsureInitialized failed");

                // Leave _entryPathMap null so the next call retries; after a few failures
                // treat the fallback as permanently unavailable for this archive.
                _initializationAttempts++;
                if (_initializationAttempts >= MaxInitializationAttempts)
                    _entryPathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    /// <summary>
    ///     Runs <c>7z l -slt</c> and maps every normalized entry path to the exact path stored
    ///     in the archive, so extraction can pass 7-Zip the precise name (including entries
    ///     stored with backslashes).
    /// </summary>
    private Dictionary<string, string> ListEntries()
    {
        var startInfo = CreateStartInfo();
        startInfo.ArgumentList.Add("l");
        startInfo.ArgumentList.Add("-slt");
        startInfo.ArgumentList.Add("-sccUTF-8");
        startInfo.ArgumentList.Add("-bd");
        startInfo.ArgumentList.Add("-y");
        startInfo.ArgumentList.Add("-p" + (_passwordProvider() ?? string.Empty));
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add(_archivePath);

        using var process = Start(startInfo);
        var outputTask = ReadStandardOutputAsTextAsync(process);
        CloseStandardInput(process);

        if (!process.WaitForExit((int)ListingTimeout.TotalMilliseconds))
        {
            KillProcess(process);
            throw new TimeoutException("7-Zip listing timed out.");
        }

        var output = outputTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"7-Zip listing failed with exit code {process.ExitCode}.");

        return ParseListing(output);
    }

    private bool ExtractEntry(string archiveEntryPath, Stream outputStream)
    {
        var startInfo = CreateStartInfo();
        startInfo.ArgumentList.Add("x");
        startInfo.ArgumentList.Add(_archivePath);
        startInfo.ArgumentList.Add("-so"); // write the entry to stdout
        startInfo.ArgumentList.Add("-bd");
        startInfo.ArgumentList.Add("-y");
        startInfo.ArgumentList.Add("-bso0"); // suppress normal output besides -so
        startInfo.ArgumentList.Add("-bsp0"); // suppress progress output
        startInfo.ArgumentList.Add("-spd"); // exact file names, no wildcard expansion
        startInfo.ArgumentList.Add("-p" + (_passwordProvider() ?? string.Empty));
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add(archiveEntryPath);

        using var process = Start(startInfo);
        var errorTask = process.StandardError.ReadToEndAsync();
        CloseStandardInput(process);

        try
        {
            process.StandardOutput.BaseStream.CopyTo(outputStream);
        }
        catch (IOException ex)
        {
            DiagnosticLogger.Log(ex,
                $"SevenZipFallback: output stream closed while extracting '{archiveEntryPath}'.");
        }

        if (!process.WaitForExit((int)ExtractionTimeout.TotalMilliseconds))
        {
            KillProcess(process);
            DiagnosticLogger.Log($"SevenZipFallback: extraction of '{archiveEntryPath}' timed out.");
            return false;
        }

        var errorText = errorTask.GetAwaiter().GetResult();
        if (process.ExitCode == 0)
            return true;

        DiagnosticLogger.Log(
            $"SevenZipFallback: extraction of '{archiveEntryPath}' failed with exit code {process.ExitCode}. {errorText}");
        return false;
    }

    private static ProcessStartInfo CreateStartInfo()
    {
        return new ProcessStartInfo
        {
            FileName = ExecutablePath!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
    }

    private static Process Start(ProcessStartInfo startInfo)
    {
        var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("Failed to start the 7-Zip executable.");

        return process;
    }

    private static void CloseStandardInput(Process process)
    {
        // Closing stdin makes 7-Zip fail immediately instead of prompting for a password
        // when no password is supplied.
        try
        {
            process.StandardInput.Close();
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log(ex, "SevenZipFallback: failed to close the 7-Zip standard input.");
        }
    }

    private static async Task<string> ReadStandardOutputAsTextAsync(Process process)
    {
        using var reader = new StreamReader(process.StandardOutput.BaseStream, Encoding.UTF8);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }

    private static void KillProcess(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log(ex, "SevenZipFallback: failed to terminate the 7-Zip process.");
        }
    }

    /// <summary>
    ///     Parses <c>7z l -slt</c> output. Only the entry list after the
    ///     <c>----------</c> separator is considered; directories and empty names are skipped.
    /// </summary>
    private static Dictionary<string, string> ParseListing(string listing)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var started = false;
        string? currentPath = null;
        var currentIsDirectory = false;

        foreach (var rawLine in listing.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            if (!started)
            {
                if (line.StartsWith("----------", StringComparison.Ordinal))
                    started = true;
                continue;
            }

            if (line.Length == 0)
            {
                AddListingEntry(map, currentPath, currentIsDirectory);
                currentPath = null;
                currentIsDirectory = false;
            }
            else if (line.StartsWith(EntryPathPrefix, StringComparison.Ordinal))
            {
                AddListingEntry(map, currentPath, currentIsDirectory);
                currentPath = line[EntryPathPrefix.Length..];
                currentIsDirectory = false;
            }
            else if (line.StartsWith(FolderPrefix, StringComparison.Ordinal))
            {
                currentIsDirectory = line.Length > FolderPrefix.Length && line[FolderPrefix.Length] == '+';
            }
        }

        AddListingEntry(map, currentPath, currentIsDirectory);
        return map;
    }

    private static void AddListingEntry(Dictionary<string, string> map, string? path, bool isDirectory)
    {
        if (isDirectory || string.IsNullOrEmpty(path))
            return;

        var key = NormalizeEntryKey(path);
        if (key.Length > 0)
            map[key] = path;
    }

    private static string NormalizeEntryKey(string path)
    {
        var normalized = path.Replace('\\', '/').TrimStart('/');
        if (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..];

        return normalized;
    }

    private static string? LocateExecutable()
    {
        try
        {
            var baseDirectory = AppContext.BaseDirectory;
            string[] candidateNames = OperatingSystem.IsWindows()
                ? ["7za.exe", "7z.exe"]
                : ["7zz", "7zzs", "7za", "7z"];

            foreach (var name in candidateNames)
            {
                var candidate = Path.Combine(baseDirectory, name);
                if (File.Exists(candidate) && EnsureExecutablePermission(candidate))
                    return candidate;
            }
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log(ex, "SevenZipFallback.LocateExecutable failed");
        }

        return null;
    }

    private static bool EnsureExecutablePermission(string path)
    {
        if (OperatingSystem.IsWindows())
            return true;

        try
        {
            var mode = File.GetUnixFileMode(path);
            if ((mode & UnixFileMode.UserExecute) == UnixFileMode.None)
            {
                File.SetUnixFileMode(path,
                    mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            }

            return true;
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log(ex, $"SevenZipFallback: could not set the execute permission on '{path}'.");
            return false;
        }
    }
}
