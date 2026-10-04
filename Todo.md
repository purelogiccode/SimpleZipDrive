# TODO — Bugs & Inconsistencies

Deep review of the solution, performed alongside the screenshot-fallback work and the
coverage-driven unit-test expansion. All findings below have been addressed; each item keeps
its original analysis and records how it was fixed. Items that could not be fixed at the
source are marked with the reason instead.

Review scope: `SimpleZipDrive/Core`, `SimpleZipDrive/Mounting`, `SimpleZipDrive/Views`,
`SimpleZipDrive/Services`, `SimpleZipDrive/App.axaml.cs`, `SimpleZipDrive/Program.cs`,
`SimpleZipDrive/FuseSharp`, `SimpleZipDrive/7zip`.

Status key: **[FIXED]** implemented, **[PARTIAL]** partially implemented / documented
limitation, **[MITIGATED]** root cause constrained, **[NOT APPLICABLE]** analysis was
inaccurate.

---

## High

1. **[FIXED] The 7z fallback cannot work on x64 builds — the shipped native library is 32-bit.**
   The SharpSevenZip package and the native `7z.dll`/`7z_arm64.dll` were removed entirely.
   The fallback now runs the bundled 7-Zip command-line executable (7-Zip 26.03):
   `7za.exe` on Windows (x64 and arm64), statically linked `7zzs` on Linux (x64 and arm64),
   and the universal `7zz` on macOS. No native library is loaded into the process, so the
   architecture mismatch cannot occur; the csproj selects the binary by `RuntimeIdentifier`
   and `SevenZipFallback` locates it next to the app at runtime (setting the Unix execute bit
   if needed). `TryExtractEntry` runs `7z x -so` with stdin closed (no password-prompt hangs),
   `-spd` for literal names, and maps entries through `7z l -slt` so case, separator and
   backslash-stored names are handled. The 7-Zip license ships as `7zip-license.txt`.
   Note: the standalone `7za.exe` supports 7z/xz/lzma/cab/zip/gzip/bzip2/Z/tar but not RAR;
   Linux/macOS use the full `7zz`, which can also read RAR.

2. **[FIXED] Disk-cache extraction reads the shared archive stream without the archive lock.**
   `Core/ZipFileSystemCore.cs` — the call to `ExtractEntryToDisk` in `OpenDiskCachedStream` is
   now wrapped in `lock (_archiveLock)`, matching the in-memory path, so concurrent
   extractions can no longer interleave seeks on the single shared source stream.

3. **[FIXED] The SevenZip fallback for disk-cached entries is dead code.**
   `Core/ZipFileSystemCore.cs` — `ExtractEntryToDisk` deletes the temp file on failure, so the
   fallback now opens it with `FileMode.Create` (creates/truncates) instead of
   `FileMode.Truncate` (which threw `FileNotFoundException`). The same change was applied to
   `TryFallbackExtraction`.

4. **[FIXED] `MountService.MountAsync` replaces the active backend without unmounting it.**
   `Mounting/MountService.cs` — `MountAsync` is now async and, before attaching the new
   backend, awaits the previous backend's `UnmountAsync`, unsubscribes from its
   `MountStatusChanged` event and disposes it. A synchronously failing new mount is detached
   via `DetachBackend` so a failed backend is never left attached.

---

## Medium

5. **[FIXED] Dokan/WinFsp unmount "grace delay" never runs — the token is cancelled first.**
   `Mounting/Dokan/DokanMountService.cs`, `Mounting/WinFsp/WinFspMountService.cs` — the
   cancel-then-delay sequence was replaced with a plain `await Task.Delay(500)` for the grace
   period, so the driver now actually gets time to drain pending callbacks before the core is
   disposed.

6. **[FIXED] FUSE mount leaks the archive stream and temp mount folder when the core constructor fails.**
   `Mounting/Fuse/FuseMountService.cs` — the mount points are registered before the core is
   created (so `CleanupAfterUnmount` can remove the temp directory), and the opened file
   stream is disposed in the failure path because `ZipFileSystemCore` does not take ownership
   of it when its constructor throws.

7. **[FIXED] FUSE unmount is a no-op while the session is starting, and cleanup can dispose the core
   while FUSE is still running.**
   `Mounting/Fuse/FuseMountService.cs` — an `_unmountRequested` flag is set by
   `UnmountAsync`/`Dispose`; `MountAsync` aborts cleanly if the flag is set before the thread
   starts, and `OnMounted` stops the session immediately if it is set. `UnmountAsync`/`Dispose`
   only call `CleanupAfterUnmount` once the session thread has actually exited; otherwise the
   thread's own `finally` performs the cleanup, so the core is never disposed under live FUSE
   callbacks.

8. **[FIXED] `DecompressEntryToBuffer` uses a fixed-size `MemoryStream` despite its comment.**
   `Core/ZipFileSystemCore.cs` — uses a growable `MemoryStream(capacity)` and returns the
   internal buffer via `TryGetBuffer` when no growth occurred (no copy, same peak memory);
   oversized decompression now grows the stream instead of throwing `NotSupportedException`.

9. **[FIXED] A transient memory-cache failure permanently blacklists an entry.**
   `Core/ZipFileSystemCore.cs` — when in-memory decompression fails and the 7z fallback is
   unavailable, the entry is now routed to the disk cache instead of being blacklisted. The
   entry is only marked failed when both extraction paths fail.

10. **[FIXED] The per-mount temp directory is leaked when `ZipFileSystemCore` construction fails.**
    `Core/ZipFileSystemCore.cs` — the constructor's catch block removes the just-created temp
    directory and clears its registration (new `ZipFsHelpers.ClearCurrentTempDirectory`).

11. **[FIXED] `ListDirectory` uses the raw entry key, breaking backslash-separated names on Unix.**
    `Core/ZipFileSystemCore.cs` — a `GetFileNameOnly` helper splits on both `/` and `\` on
    every platform.

12. **[FIXED] `StatsService.ReportStatsAsync` never disposes the `HttpResponseMessage`.**
    `Core/Services/StatsService.cs` — the response is now disposed with `using`.

13. **[FIXED] `IsExtractionFailure` bypasses the compression-library guard it documents.**
    `Core/ZipFileSystemCore.cs` — only data-format exceptions (`ZlibException`,
    `ZstdException`, `DataError`) are unconditionally extraction failures; broad exception
    types (`ArgumentException`, `NullReferenceException`, `InvalidOperationException`, ...)
    count only when `IsCompressionLibraryException` confirms they originated inside a
    compression library.

14. **[FIXED] `UserNotificationService`'s browser-failure handling is unreachable.**
    `Services/ShellHelper.cs` now returns `true`/`false` from `OpenUrl`/`OpenFolder` instead of
    swallowing the outcome, and `Core/Services/UserNotificationService.cs` branches on the
    returned value to show the "Could not open browser" fallback dialog and log accurately.

15. **[FIXED] `UpdateService`'s quiet timeout handling does not match reality.**
    `Core/Services/UpdateService.cs` — a `TaskCanceledException` (which is how
    `HttpClient.Timeout` expiry surfaces) that is not caller cancellation is now logged quietly;
    explicit caller cancellation during shutdown is handled separately.

16. **[FIXED] `MainWindow` screenshot failure message always claimed a write-permission problem.**
    `Views/MainWindow.axaml.cs` — the actual `ScreenshotResult.ErrorMessage`/exception message
    is shown.

17. **[FIXED] `ScreenshotService` had no writable-location fallback and returned a misleading result.**
    `Core/Services/ScreenshotService.cs` — falls back to
    `%LOCALAPPDATA%\SimpleZipDrive\Screenshot`, returns the real exception message, returns a
    null `FilePath` on failure and disposes the render bitmap when rendering throws.

---

## Low

18. **[FIXED] `LogTextWriter.WriteLine()` logged the literal string `System.Char[]`.**
    `Core/Logging/LogTextWriter.cs` — uses `new string(CoreNewLine)`.

19. **[FIXED] `LogTextWriter.Dispose` threw `ChannelClosedException` when called twice.**
    `Core/Logging/LogTextWriter.cs` — guarded with a `_disposed` flag.

20. **[FIXED] Each bug-report POST leaks its `HttpRequestMessage`/`StringContent`.**
    `Core/ErrorLogger.cs` — both are now disposed with `using`.

21. **[FIXED] The bug-report payload is never truncated despite the documented 4000-char API limit.**
    `Core/ErrorLogger.cs` — `PostBugReportAsync` truncates the message field to 4000 characters
    (ending with `...`) before serializing.

22. **[FIXED] `ErrorLogger.ErrorLogFilePath` is dead and `WriteToCriticalLog` reports a write it
    never performs.**
    `Core/ErrorLogger.cs` — `WriteToCriticalLog` now appends the fatal entry to
    `ErrorLogFilePath` best-effort (and still writes to console), so the property is used and
    the message is accurate.

23. **[PARTIAL] `XisoArchive`/`ZarArchive` report `ArchiveType.Tar`, and `ZarArchiveEntry.CompressedSize`
    reports the uncompressed size.**
    `ZarArchiveEntry.CompressedSize` now returns 0 (unknown), which is the documented
    SharpCompress convention; ZArchiveSharp exposes no per-entry compressed size. The
    `ArchiveType.Tar` mapping cannot be fixed: SharpCompress' `ArchiveType` enum has no XISO or
    ZAR values, and nothing in the application branches on `IArchive.Type` (archives are
    identified by the extension-based `ZipFileSystemCore.ArchiveType` string). Both properties
    now carry XML remarks documenting this.

24. **[FIXED] A transient `SevenZipFallback` initialization failure disables the fallback for the
    whole mount.**
    `Core/SevenZipFallback.cs` — a failed initialization leaves the entry map unset and retries
    on the next call, up to three attempts, before giving up.

25. **[FIXED] `ErrorLoggerStatic.ReportSilentException`'s `silent` parameter documentation contradicts
    the implementation.**
    `Core/ErrorLoggerStatic.cs` — the parameter is documented as retained for backwards
    compatibility with no behavioral effect.

26. **[FIXED] Stale `UpdateService` XML doc about a "Core assembly"; `StatsService` uses the entry
    assembly instead.**
    `Core/Services/UpdateService.cs` — the doc now describes the containing assembly correctly.
    `Core/Services/StatsService.cs` now uses `typeof(StatsService).Assembly`, so the reported
    application id/version are the application's even under a test runner (matching
    UpdateService).

27. **[FIXED] `WinFspMountService.IsAvailable` reports "not installed" for load failures.**
    `Mounting/WinFsp/WinFspMountService.cs` — a genuine "not installed" is only reported when no
    install directory can be found; otherwise the reason explains that the installed native DLL
    could not be loaded (corrupted installation or architecture mismatch).

28. **[FIXED] `CurrentArchivePath` is left stale after failed mounts.**
    `Mounting/WinFsp/WinFspMountService.cs`, `Mounting/Dokan/DokanMountService.cs` — the early
    assignment in `MountAsync` was removed; both backends now set `CurrentArchivePath` only when
    the mount succeeds.

29. **[FIXED] Unreachable `catch (OperationCanceledException)` in `WinFspMountService.UnmountAsync`.**
    `Mounting/WinFsp/WinFspMountService.cs` — the dead clause was removed.

30. **[FIXED] `Dispose` leaves `IsMounted`/`CurrentMountPoint` stale in Dokan and WinFsp.**
    Both `Dispose` implementations now reset `IsMounted`, `CurrentMountPoint` and
    `CurrentArchivePath`, matching FUSE's `CleanupAfterUnmount`.

31. **[FIXED] FUSE's archive-open retry blocks the UI thread.**
    `Mounting/Fuse/FuseMountService.cs` — `OpenArchiveFileStreamAsync` awaits `Task.Delay`
    instead of `Thread.Sleep`, so the UI stays responsive.

32. **[FIXED] Dead catch around `ShellHelper.OpenFolder` in `MainWindow.UpdateMountStatus`.**
    `Views/MainWindow.axaml.cs` — the return value of `ShellHelper.OpenFolder` is checked and
    logged when it fails.

33. **[FIXED] Screenshot file names can collide within the same millisecond.**
    `Core/Services/ScreenshotService.cs` — `GetUniqueFilePath` appends a numeric suffix when a
    file with the same timestamp already exists.

---

## Test-infrastructure inconsistencies

34. **[MITIGATED] Tests share the static `ServiceProvider` across parallel collections.**
    `LogTextWriter` now accepts an optional `ILoggingService` and the
    `LogTextWriterTests` pass a recording service directly instead of registering it globally,
    eliminating the concrete cross-talk with `ZipFileSystemCore`'s static `LogMessage` lookup.
    `ZipFileSystemCore.LogMessage` remains a static `ServiceProvider` lookup by design (it is
    called from static backend helpers); nothing in the suite depends on a globally registered
    logger any more.

35. **[FIXED] Several tests write to the developer's real settings file.**
    `AppSettings` exposes `SettingsFilePath` (and a `SIMPLEZIPDRIVE_SETTINGS_DIR` environment
    override for the directory). The "Settings file" test collection now uses a new
    `SettingsFileFixture` that redirects `AppSettings.SettingsFilePath` to a per-run temporary
    file; `Save_WritesValidJson` asserts against that redirected path. Only the file is
    redirected so `ZipFsHelpers.BaseTempPath` (a static snapshot of the directory) keeps its
    expected shape.

36. **[FIXED] The 7z extraction tests are conditional by necessity.**
    The per-RID 7-Zip CLI binary is copied into the test output, so the positive
    `SevenZipFallbackTests` assertions run for real on every supported platform/RID (Windows,
    Linux and macOS). The tests assert availability of the bundled executable and cover
    content extraction, backslash-separated names, wildcard characters in names (`[`/`]`),
    unknown entries and disposed instances.

37. **[FIXED] Leftover build-artifact directories and unignored test output.**
    `SimpleZipDrive.Core/obj` and `SimpleZipDrive_WinFsp/obj` were deleted; `TestResults/` was
    added to `.gitignore`.

### Additional test-stability fixes discovered while running the suite

- **[FIXED] Console redirection race.** `DokanPrefixedLoggerTests.CustomPrefix_IsApplied` left a
  disposed `StringWriter` as `Console.Out`, which broke `XISOSharp`'s static logger and other
  concurrent tests (`Cannot write to a closed TextWriter`). The test now restores the console in
  a `finally`, and `DokanPrefixedLoggerTests`, `XisoArchiveTests` and `XisoEntryReaderTests`
  share a serialized `[Collection("Console redirection")]`.
- **[FIXED] `WinFspDiagnosticLoggerTests` negative assertions.** The two
  "no `[]`/`[null]`" tests asserted over the whole shared diagnostic file, which concurrent
  tests append to through the static Serilog pipeline; they now locate their own operation line
  with a helper and assert on that.

---

## Vendored 7-Zip binaries

`SimpleZipDrive/7zip/` contains the 7-Zip 26.03 console binaries
(<https://github.com/ip7z/7zip/releases/tag/26.03>); the csproj ships exactly one per
`RuntimeIdentifier` and copies `License.txt` as `7zip-license.txt`.

| File | SHA-256 |
| --- | --- |
| `win-x64/7za.exe` | `edbee35370e14030e4c785cf88200f42dc651c1eb4217c1e3963c38a12f099b0` |
| `win-arm64/7za.exe` | `c26764813a01b9714687f29c94412401f2041852634e291c59d48484432e834b` |
| `linux-x64/7zzs` | `eab4c8d7f193e3d6d3237370bbcaa879a160a3f1dc82202207e27baeab79b6ac` |
| `linux-arm64/7zzs` | `277907bc627633ec344757fe47699856cbb6e37f75cbc310d37d62cfacdd73b2` |
| `osx/7zz` (universal) | `74b0910e50ea44d9760a57fada2192cfd530ba8bffbe7b47c412a464b796cabf` |
| `License.txt` | `1790374e5352329cedb46ee3808930a88e9ca2f08b82b10fcf5cf605d2c301b1` |

To update: download the new release packages from the 7-Zip GitHub releases, replace the
five binaries, refresh `License.txt` and the hashes above, and re-run the test suite.

---

## Notes

- The suite now contains 1429 tests (101 added in the screenshot/test batch) with 0 build
  warnings; it was run ten consecutive times green before the 7-Zip CLI change and is re-run
  after each subsequent change.
- The screenshot service already existed (registered at `App.axaml.cs:170`, invoked on F8 at
  `Views/MainWindow.axaml.cs:252-284`); an earlier batch added the AppData fallback, the real
  error reporting and tests.
