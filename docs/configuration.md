---
title: Configuration
permalink: /configuration
parent: Operations
nav_order: 13
---

# Configuration

## Settings reference

Settings persist as **JSON** in `%LOCALAPPDATA%\SimpleZipDrive\settings.dat` (edited via **Settings** in the app menu). A corrupted file is reported and replaced with defaults.

| Setting | Type | Default | Meaning |
|---|---|---|---|
| `MaxMemoryPerFileMb` | int | **512** | Maximum RAM used to cache a single decompressed entry. Clamped automatically to 1 MB … 90 % of installed RAM; re-validated at every load |
| `DefaultMountType` | enum | `DriveLetter` | What the plain *Mount* menu item does: `DriveLetter` (auto M–Q) or `Folder` (asks for a folder) |
| `AutoOpenMountedDrive` | bool | `false` | Open the mount in a new Explorer window after a successful mount (`explorer /root,"<mountPoint>"`) |
| `CrossIntegrityMount` | bool | `false` | *(WinFsp only)* Use permissive-DACL folder mounts visible to both standard and elevated processes; also forces folder mounting |
| `CrossIntegrityMountFolder` | string | `""` | *(WinFsp only)* Base folder for cross-integrity mounts; empty = `%LOCALAPPDATA%\SimpleZipDrive\Mounts` |

## Settings window

**Settings** in the menu exposes all of the above with validation (invalid RAM values show *"Please enter a valid positive number for the RAM limit."*). **Open Config Path** opens the settings folder directly.

## Data locations

Everything lives under one root — `%LOCALAPPDATA%\SimpleZipDrive`:

| Path | Purpose | Cleanup |
|---|---|---|
| `settings.dat` | Settings JSON | Manual (or delete to reset) |
| `Temp\<pid>_<guid>\` | Per-session disk cache + extraction workspace; name = process ID + random GUID | Deleted on unmount/exit; orphaned dirs swept at startup and via *Clean Temp Files* |
| `Temp\Logs\debug_<timestamp>_<guid>.log` | Current session log | Previous session logs deleted at startup |
| `Temp\Logs\winfsp_debug_<timestamp>.log` | Native WinFsp driver log, one per mount attempt | Kept until the next session's cleanup |
| `Mounts\<ArchiveName>\` | Cross-integrity mount-point folders (WinFsp) | Not auto-deleted; safe to remove when unmounted |

The startup sweep only deletes `Temp\<pid>_<guid>`-shaped directories whose PID is dead **and** whose process name no longer matches (guards against PID reuse) — live sessions and `settings.dat` are never touched.

## Resetting the app

1. Close all instances.
2. Delete `%LOCALAPPDATA%\SimpleZipDrive` (settings, caches, logs, mount folders all live there — nothing is stored in the program folder or the registry).
3. Start the app; it recreates defaults.

## What is *not* configurable

- Drive-letter pool (M–Q) and the mount-point normalization rules ([Mounting](mounting#mount-point-resolution))
- Retry counts and backoffs (3 attempts / 500 ms steps for archive open; 2 retries for Dokan)
- Password attempt limit (3)
- Log retention (previous session's logs are removed at startup)
