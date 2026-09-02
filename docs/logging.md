---
title: Logging
permalink: /logging
parent: Operations
nav_order: 14
---

# Logging & Diagnostics

## Where logs live

All logs are in `%LOCALAPPDATA%\SimpleZipDrive\Temp\Logs\` (menu: **Open Config Path**):

| File | Content |
|---|---|
| `debug_<yyyyMMdd_HHmmss>_<guid>.log` | The current session: everything from startup banner to mount/unmount. **This is the file to attach to bug reports.** |
| `winfsp_debug_<yyyyMMdd_HHmmss>.log` | Native WinFsp driver trace for one mount attempt (enabled via `SetDebugLogFile` with `DebugLog=-1`) |
| `error.log` | Legacy error file (still cleaned up at startup) |

**Retention:** at every startup the app deletes all previous `debug_*.log` and `error.log` files, so after a crash you have one session to reproduce and one log to submit. The WinFsp driver logs from earlier mount attempts survive until the next startup.

## What is logged

- **Startup:** version banner, supported formats, usage, update check, service registration.
- **Mount:** sections like `MOUNT START: <archive> -> <mountPoint>`, archive type, mount point, entry dump (total entries, implicit directories, per-entry list), cache configuration (*"Max memory cache: X MB"*, *"Max total memory: X MB"*, temp directory), driver version (*"WinFsp Driver Version: M.m.b"* — or *"Could not determine installed WinFsp version."*), every pre-mount check and retry.
- **Runtime:** entry opens (*"Stored entry detected … Using direct-read mode"*, *"Using disk cache for small file …"*), fallback extraction, failures with exact exception details at `[ERR]`/`[FTL]` level.
- **Unmount:** cleanup steps and temp-directory deletion.

Format: `[LEVEL] [HH:mm:ss.fff][T<threadId>] message` (Serilog, verbose minimum level). The UI log pane mirrors Information+ events (capped at 5000 entries, duplicate messages within 100 ms suppressed).

## Automatic bug reporting

Warnings and errors are forwarded automatically to the project's bug-report service (`www.purelogiccode.com/bugreport`):

- **Payload:** error message (≤ 4000 chars), application name + version, context (`userInfo`), environment summary (OS + bitness, ≤ 50 chars), stack trace. No archive paths or contents.
- **Filtering:** expected user errors are suppressed — cancellations, wrong passwords, corrupt archives, missing/incompatible drivers, occupied mount points, IO/file-not-found errors, etc. Only genuine defects are reported.
- Unhandled exceptions on any thread (WPF dispatcher, `AppDomain`, unobserved tasks) are captured, logged as fatal, and reported synchronously before shutdown.

See [Security & Privacy](security#automatic-bug-reporting) for exactly what is sent and how to opt out.

## Reporting a bug manually

1. Reproduce the problem.
2. Grab the current `debug_*.log` (and the matching `winfsp_debug_*.log` for WinFsp mount issues).
3. Open an issue at [github.com/purelogiccode/SimpleZipDrive/issues](https://github.com/purelogiccode/SimpleZipDrive/issues) with:
   - app variant + version (Help → About),
   - what you did and what happened,
   - the log file(s) as attachments,
   - for performance issues: your **driver version** (WinFsp: log line or `winfsp-x64.dll` file properties; Dokan: `DokanVersion` shown in some dialogs / installed version).
4. Press **F8** while the app runs to capture a screenshot of the window if it helps.
