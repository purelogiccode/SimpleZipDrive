# TODO — Bugs & Inconsistencies

Deep review of the solution, performed alongside the screenshot-fallback work and the
coverage-driven unit-test expansion. Findings are ordered by severity and include the
file/line reference, the problem, and the user impact. Items marked **[FIXED]** were
corrected in the same pass.

Review scope: `SimpleZipDrive/Core`, `SimpleZipDrive/Mounting`, `SimpleZipDrive/Views`,
`SimpleZipDrive/Services`, `SimpleZipDrive/App.axaml.cs`, `SimpleZipDrive/Program.cs`,
`SimpleZipDrive/FuseSharp`, `SimpleZipDrive/7z.dll`.

---

## High

1. **The 7z fallback cannot work on x64 builds — the shipped native library is 32-bit.**
   `SimpleZipDrive/7z.dll` is an x86 PE image (`machine = 0x014C`) while the app and test
   host build as `win-x64` (and an `7z_arm64.dll` exists for ARM). `SevenZipFallback.TrySetLibraryPath`
   (`Core/SevenZipFallback.cs:149`) loads `AppContext.BaseDirectory\7z.dll` on every non-ARM64
   process, so `NativeLibrary`/SharpSevenZip fails with "incorrect format" on x64.
   `IsAvailable()` still returns `true` because the file exists, so every 7z fallback path
   silently fails at runtime. The SharpSevenZip NuGet package ships `x64/7z.dll` and
   `x86/7z.dll` under `build/`, but the project overrides the root-level file with the x86
   binary (`SimpleZipDrive.csproj:65`).

2. **Disk-cache extraction reads the shared archive stream without the archive lock.**
   `Core/ZipFileSystemCore.cs:1115` (`ExtractEntryToDisk`, called from `:848`-area flow).
   The in-memory path serializes SharpCompress entry streams with `_archiveLock`, but the
   disk-cache path holds only the per-entry semaphore. SharpCompress seeks/reads the single
   shared source stream with no internal locking, so two concurrent large-file extractions —
   or an extraction racing a memory decompression — can interleave seeks and write silently
   corrupted cache files.

3. **The SevenZip fallback for disk-cached entries is dead code.**
   `Core/ZipFileSystemCore.cs:1130` (fallback) vs `:1361` (catch deletes `newTempFilePath`
   before rethrowing). The fallback reopens the temp file with `FileMode.Truncate`, which
   always throws `FileNotFoundException` because the file was already deleted; the entry is
   then permanently blacklisted even though 7z could have read it.

4. **`MountService.MountAsync` replaces the active backend without unmounting it.**
   `Mounting/MountService.cs:47-60`. A second `MountAsync` overwrites `_active` and subscribes
   to the new backend's event without disposing/unsubscribing the previous backend, so the
   first mount can stay mounted forever and its event keeps the facade alive. The backend is
   also attached before `MountAsync` is invoked, so a synchronous throw leaves a failed
   backend attached and subscribed.

---

## Medium

5. **Dokan/WinFsp unmount "grace delay" never runs — the token is cancelled first.**
   `Mounting/Dokan/DokanMountService.cs:154-174`, `Mounting/WinFsp/WinFspMountService.cs:167-190`.
   `cts.Cancel()` runs before `await Task.Delay(500, cts.Token)`, so the delay throws
   immediately and is swallowed. Dokan then disposes `_currentZipFs` while the driver may
   still be invoking callbacks, creating a use-after-dispose race.

6. **FUSE mount leaks the archive stream and temp mount folder when the core constructor fails.**
   `Mounting/Fuse/FuseMountService.cs:103-139`. If `new ZipFileSystemCore(...)` throws (corrupt
   archive, cancelled password), the opened `fileStream` is never disposed (Dokan/WinFsp wrap
   the constructor and dispose it), and `_tempMountPoint` is assigned after the `try`, so
   `CleanupAfterUnmount()` cannot delete the created `/tmp/simplezipdrive-*` directory.

7. **FUSE unmount is a no-op while the session is starting, and cleanup can dispose the core
   while FUSE is still running.**
   `Mounting/Fuse/FuseMountService.cs:193-226,262-325`. `IsMounted` becomes true only in the
   libfuse `Init` callback, so unmount requests issued after the handle is live but before
   `Init` are ignored. When the join times out, the external unmount is fire-and-forget and
   `CleanupAfterUnmount()` immediately disposes `_core`, so callbacks can still hit disposed
   objects during teardown.

8. **`DecompressEntryToBuffer` uses a fixed-size `MemoryStream` despite its comment.**
   `Core/ZipFileSystemCore.cs:897`. `new MemoryStream(buffer, 0, capacity, true, false)` is
   non-expandable, so a decompressed length larger than the declared `entry.Size` throws
   `NotSupportedException: Memory stream is not expandable`; the XML comment promises a
   resize/copy fallback that does not exist, and the entry is marked failed.

9. **A transient memory-cache failure permanently blacklists an entry.**
   `Core/ZipFileSystemCore.cs:859` + `:1400-1406`. When `TryFallbackExtraction` returns null
   because of the total-memory limit or `OutOfMemoryException`, the caller treats it as "no
   fallback available" and calls `AddFailedEntry`, so the entry returns null forever even
   though the disk-cache path would still work.

10. **The per-mount temp directory is leaked when `ZipFileSystemCore` construction fails.**
    `Core/ZipFileSystemCore.cs:93,119-125`. The constructor creates and registers the temp
    directory, but the catch block only logs and rethrows; the instance is never returned,
    so `Dispose` never removes it (only the next launch's orphan cleanup does).

11. **`ListDirectory` uses the raw entry key, breaking backslash-separated names on Unix.**
    `Core/ZipFileSystemCore.cs:724`. `Path.GetFileName` treats only `/` as a separator on
    Linux/macOS, so Windows-created RAR/TAR entries like `dir\file.txt` produce a listing
    name that no longer resolves (and `FuseVolumeAdapter.FileName` exposes the bad name).

12. **`StatsService.ReportStatsAsync` never disposes the `HttpResponseMessage`.**
    `Core/Services/StatsService.cs:60`. The response is not wrapped in `using`, so neither
    the HTTP 429 early-return nor the success path releases the content/connection;
    `ErrorLogger` does this correctly, making the two services inconsistent.

13. **`IsExtractionFailure` bypasses the compression-library guard it documents.**
    `Core/ZipFileSystemCore.cs:1315` vs `:1331-1344`. `NullReferenceException`,
    `ArgumentOutOfRangeException` and `InvalidOperationException` are treated as extraction
    failures before `IsCompressionLibraryException` can check whether they came from the
    compression library, so genuine application bugs are retried via 7z and hidden.

14. **`UserNotificationService`'s browser-failure handling is unreachable.**
    `Core/Services/UserNotificationService.cs:42-54`. `ShellHelper.OpenUrl` swallows every
    exception internally (`Services/ShellHelper.cs:25-30`), so the catch never runs, the
    "Could not open browser" fallback dialog is dead code, and the log claims the browser
    was opened even when nothing happened.

15. **`UpdateService`'s quiet timeout handling does not match reality.**
    `Core/Services/UpdateService.cs:94-99`. `HttpClient.Timeout` expiry surfaces as
    `TaskCanceledException` (not `TimeoutException`), so timeouts and shutdown cancellation
    fall into the generic catch and are logged at Error via `ErrorLoggerStatic.LogErrorAsync`.
    `BugReportSink` happens to filter `TaskCanceledException`, so the API is not spammed,
    but the intended "log quietly" path never executes and the `TimeoutException` clause is
    dead.

16. **`MainWindow` screenshot failure message always claimed a write-permission problem.**
    `Views/MainWindow.axaml.cs:260-284`. Both failure branches showed "due to write permission
    issues" regardless of the real cause (no active window, rendering failure, disk full) and
    ignored `ScreenshotResult.ErrorMessage`. **[FIXED]** — the actual error is now shown.

17. **`ScreenshotService` had no writable-location fallback and returned a misleading result.**
    `Core/Services/ScreenshotService.cs`. Saving under `Program Files` failed permanently
    with the hardcoded message "write permission issues", and the failed result still carried
    a non-null `FilePath`, contradicting `ScreenshotResult`'s contract. **[FIXED]** — the
    service now falls back to `%LOCALAPPDATA%\SimpleZipDrive\Screenshot`, returns the real
    exception message, returns `null` for `FilePath` on failure, and disposes the
    `RenderTargetBitmap` when rendering throws.

---

## Low

18. **`LogTextWriter.WriteLine()` logged the literal string `System.Char[]`.**
    `Core/Logging/LogTextWriter.cs:61`. `CoreNewLine` is a `char[]`; `.ToString()` returns the
    type name (the `?? Environment.NewLine` fallback could never trigger), so every bare
    `Console.WriteLine()` produced a bogus log entry. **[FIXED]** — uses `new string(CoreNewLine)`.

19. **`LogTextWriter.Dispose` threw `ChannelClosedException` when called twice.**
    `Core/Logging/LogTextWriter.cs:130-153`. Completing an already-completed channel throws,
    violating `TextWriter`'s expected idempotent `Dispose`. **[FIXED]** — guarded with a
    `_disposed` flag.

20. **Each bug-report POST leaks its `HttpRequestMessage`/`StringContent`.**
    `Core/ErrorLogger.cs:656`. Only the response is disposed; the request and its multi-KB
    JSON content are left to the GC on every report.

21. **The bug-report payload is never truncated despite the documented 4000-char API limit.**
    `Core/ErrorLogger.cs:597`. `fullMessage` concatenates environment details, error details
    and a full stack trace with no length check, so oversized reports are rejected and the
    failure is only written to the console.

22. **`ErrorLogger.ErrorLogFilePath` is dead and `WriteToCriticalLog` reports a write it
    never performs.**
    `Core/ErrorLogger.cs:45,674`. Nothing ever writes `error.log`, yet the fatal message
    interpolates the path and `DiagnosticLogger.CleanupOldLogs` deletes an `error.log` that
    never exists.

23. **`XisoArchive`/`ZarArchive` report `ArchiveType.Tar`, and `ZarArchiveEntry.CompressedSize`
    reports the uncompressed size.**
    `Core/XisoArchive.cs:90`, `Core/ZarArchive.cs:51`, `Core/ZarArchiveEntry.cs` (`CompressedSize => Size`).
    Any consumer branching on `IArchive.Type` or using `CompressedSize` gets wrong metadata
    for these formats.

24. **A transient `SevenZipFallback` initialization failure disables the fallback for the whole
    mount.**
    `Core/SevenZipFallback.cs:144`. Any exception during initialization replaces
    `_entryIndexMap` with an empty dictionary, and the non-null guard prevents retrying, so a
    one-off lock or cancelled password prompt silently removes fallback extraction for every
    subsequent entry.

25. **`ErrorLoggerStatic.ReportSilentException`'s `silent` parameter documentation contradicts
    the implementation.**
    `Core/ErrorLoggerStatic.cs:31` says "only logs to file without showing console output", but
    `ErrorLogger.ReportSilentException` discards the value (`_ = silent;` at `ErrorLogger.cs:121`)
    and always routes through `DiagnosticLogger`/Serilog.

26. **Stale `UpdateService` XML doc about a "Core assembly"; `StatsService` uses the entry
    assembly instead.**
    `Core/Services/UpdateService.cs:36-45` claims the version is read from a version-pinned
    Core assembly that does not exist (the class is in `SimpleZipDrive.dll`), while
    `StatsService.cs:56-57` reports `Assembly.GetEntryAssembly()` — the exact inconsistency
    the UpdateService comment says was avoided (under tests it reports the test host).

27. **`WinFspMountService.IsAvailable` reports "not installed" for load failures.**
    `Mounting/WinFsp/WinFspMountService.cs:215-278`. A corrupt install or architecture
    mismatch returns the same "The WinFsp driver is not installed." reason as a genuinely
    absent driver, so the user is told to install something that is already present.

28. **`CurrentArchivePath` is left stale after failed mounts.**
    `Mounting/WinFsp/WinFspMountService.cs:129,704-753`,
    `Mounting/Dokan/DokanMountService.cs:134,444-486`. Several early-return failure paths
    assign `CurrentArchivePath` up front but never reset it, so the facade reports an archive
    for a mount that does not exist.

29. **Unreachable `catch (OperationCanceledException)` in `WinFspMountService.UnmountAsync`.**
    `Mounting/WinFsp/WinFspMountService.cs:198-200`. The only awaited operation already has
    its own cancellation catch, so the outer clause is dead (Dokan has no such clause —
    another backend inconsistency).

30. **`Dispose` leaves `IsMounted`/`CurrentMountPoint` stale in Dokan and WinFsp.**
    `Mounting/Dokan/DokanMountService.cs:47-64`, `Mounting/WinFsp/WinFspMountService.cs:57-76`.
    A backend disposed while mounted continues to report `IsMounted == true`; FUSE resets all
    three properties in `CleanupAfterUnmount`, so the backends behave differently.

31. **FUSE's archive-open retry blocks the UI thread.**
    `Mounting/Fuse/FuseMountService.cs:335-350`. `Thread.Sleep(500 * attempt)` is called
    directly from `MountAsync` on the caller's thread, freezing the UI for up to 1.5 s;
    Dokan/WinFsp use `await Task.Delay` specifically to keep the UI responsive.

32. **Dead catch around `ShellHelper.OpenFolder` in `MainWindow.UpdateMountStatus`.**
    `Views/MainWindow.axaml.cs:572-579`. `ShellHelper` swallows all exceptions, so the catch
    that logs "Failed to open the mounted location" can never execute.

33. **Screenshot file names can collide within the same millisecond.**
    `Core/Services/ScreenshotService.cs` — `Screenshot_yyyyMMdd_HHmmss_fff.png`; two captures
    in the same millisecond overwrite each other. A counter/`FileMode.CreateNew` retry loop
    would make the save collision-proof.

---

## Test-infrastructure inconsistencies

34. **Tests share the static `ServiceProvider` across parallel collections.**
    `Core/ZipFileSystemCore.cs:1248` resolves `ILoggingService` from the static provider, so
    any test that registers a logging service (e.g. `LogTextWriterTests`) receives unrelated
    cache log messages from concurrently running `ZipFileSystemCore` tests. The new
    `LogTextWriterTests` were written content-based and thread-safe to tolerate this, but the
    underlying design makes exact-count assertions unsafe.

35. **Several tests write to the developer's real settings file.**
    `SimpleZipDrive.Tests/AppSettingsAdditionalTests.cs:80-111` (`Save_WritesValidJson`) and
    `SettingsServiceTests` call `AppSettings.Save()`, which writes
    `%LOCALAPPDATA%\SimpleZipDrive\settings.dat` and can clobber a developer's actual
    settings. The settings path is hardcoded, so tests cannot redirect it to a temp folder.

36. **The 7z extraction tests are conditional by necessity.**
    Because the repository ships the 32-bit `7z.dll` (finding 1), the new
    `SevenZipFallbackTests` verify real extraction only when the native library's PE
    architecture matches the test process; on the x64 test host they currently skip the
    positive assertions. Once a matching x64 `7z.dll` is shipped, those assertions will run.

37. **Leftover build-artifact directories for removed projects.**
    `SimpleZipDrive.Core/obj` and `SimpleZipDrive_WinFsp/obj` remain from projects that are no
    longer in the solution; `TestResults/` is not covered by `.gitignore` and was created by
    the coverage run.

---

## Notes

- The 1327 pre-existing tests plus the 101 tests added in this pass (1428 total) all pass;
  the new tests cover `ScreenshotService`, `LogTextWriter`, `UserNotificationService`,
  `ErrorLogger.FireAndForgetAsync`, `Xiso`/`Zar` sequential readers, `SevenZipFallback`,
  `FuseAvailability`, `MountService` facade basics, `AppSettings` defaults and
  `ScreenshotResult`.
- The screenshot service already existed (registered at `App.axaml.cs:170`, invoked on F8 at
  `Views/MainWindow.axaml.cs:252-284`); this pass added the AppData fallback, the real error
  reporting and tests.
