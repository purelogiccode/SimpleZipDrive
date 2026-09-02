---
title: Troubleshooting
permalink: /troubleshooting
parent: Operations
nav_order: 15
---

# Troubleshooting

Symptom → cause → fix. If your case is not here, check the session log (`%LOCALAPPDATA%\SimpleZipDrive\Temp\Logs\debug_*.log`) — it names the exact failing step — and see [Logging](logging#reporting-a-bug-manually).

## Driver problems

| Symptom / message | Cause | Fix |
|---|---|---|
| *"Dokan Driver Not Found"* dialog | Dokan not installed, or `dokan2.dll` not loadable | Install [Dokan v2](https://github.com/dokan-dev/dokany/releases); reboot if the mount still fails |
| *"Dokan Driver Incompatible"* | x64/x86 Dokan on an ARM64 system (architecture mismatch) | Install the ARM64 Dokan build, or use the Dokan x64 package on x64 Windows |
| *"Dokan error: …"*, retries *"attempt 1/2"* | Transient driver failure | Usually resolves on retry; if persistent, reinstall Dokan |
| *"WinFsp not found"* dialog | WinFsp missing | Install [WinFsp](https://github.com/winfsp/winfsp/releases) 2.1+ |
| *"WinFsp driver service is not running. Please start the WinFsp.Launcher service."* | `WinFsp.Launcher` service stopped | `sc start WinFsp.Launcher` (admin) or restart via `services.msc` |
| *"WinFsp version mismatch: installed x.y, required 2.1. Mount blocked."* | WinFsp older than 2.1, or interop/driver mismatch | Upgrade WinFsp to ≥ 2.1 stable. 2.2.x betas are fine |
| *"WinFsp native DLL could not be loaded"* | Native driver DLL unreadable | Reinstall WinFsp; check that no AV quarantined `winfsp-x64.dll` |

## Mount-point problems

| Symptom / message | Cause | Fix |
|---|---|---|
| *"The mount point is already in use…"* / `0xC0000035` + *"Mount Point In Use"* dialog | Letter or folder claimed by another drive/process | Pick a different letter/folder; `subst`-mappings and other virtual drives count |
| *"Skipping 'M:\' (already in use)."* for all letters | M–Q all taken | Mount to a folder, or free a letter |
| *"Error: Failed to auto-mount on any preferred drive letters."* | Same | Same |
| `0xC0000034` *"The WinFsp driver was not found or is not running"* on a **folder** mount in versions < 2.9.0 | Historical bug: folder paths were misclassified as drive letters, so the folder was never created | **Upgrade to 2.9.0+** (fixed) |
| `0xC000003A` *"The mount point path was not found"* | Folder path invalid | Check the path |
| `0xC0000022` *"Access denied"* | Permissions on the folder | Choose a folder you can write to |
| *"The path is empty"* on every WinFsp mount in versions < 2.9.0 | WinFsp interop crashed inside the single-file bundle (`Assembly.Location` empty) | **Upgrade to 2.9.0+** — `winfsp-msil.dll` now ships beside the exe |

## Archive problems

| Symptom / message | Cause | Fix |
|---|---|---|
| *"The file 'X' is not a supported archive"* | Extension not in the supported list | Rename to the correct extension if it *is* a supported format; otherwise convert |
| *"Archive file not found at '…'"* | Path wrong / network share unreachable | Check the path |
| Password dialog loops, then *"Mount aborted after 3 attempts"* | Wrong password | Verify the password; cancelling stops the mount cleanly |
| *"The archive file appears to be corrupted, incomplete, or uses an unsupported format/feature"* | Broken download / unsupported feature | Re-download; test the archive in 7-Zip; note that corrupt RARs are reported as corruption (not as password prompts) since 2.9.0 |
| Specific entries unreadable, log shows *"Decompression failed"* / *"SevenZip fallback also failed"* | Entry-level corruption or exotic compression | Verify the archive; re-create it if possible |
| *"Insufficient disk space to extract file '…'"* | Temp volume full | Free space on the drive holding `%LOCALAPPDATA%` |

## Runtime behaviour

| Symptom | Explanation | Action |
|---|---|---|
| *"A drive is already mounted. Please unmount it first."* | One mount per instance | Unmount, then mount again |
| Memory appears to "double" the file size in Task Manager (versions < 2.9.0) | Transient double-buffer during decompression | **Upgrade to 2.9.0+**; also see [Caching](caching) for the new single-buffer path |
| High RAM after opening a huge file | Entry cached in memory up to `MaxMemoryPerFileMb` | Lower the setting to offload big files to the disk cache |
| Leftover `Temp\<pid>_<guid>` folders after a crash | Session died before cleanup | Auto-swept at next startup, or use *Clean Temp Files* |
| Empty folders in `%LOCALAPPDATA%\SimpleZipDrive\Mounts` | Cross-integrity mount points are not auto-deleted | Remove manually when unmounted |
| Update check never appears | Silent when offline, or you are already current | Check the log line *"Update check skipped: …"* |
| App closes instantly on unmount of large drives | Watchdog force-exit if cleanup exceeds ~5 s | Data is safe; temp files are swept next startup |

## Clean reset

1. Close the app (unmount first).
2. Delete `%LOCALAPPDATA%\SimpleZipDrive`.
3. If mount points stay broken, restart Windows (driver state resets).
4. Still broken? Reinstall the driver (Dokan/WinFsp) and the app ([Installation](installation)).
