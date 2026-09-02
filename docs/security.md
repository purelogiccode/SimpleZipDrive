---
title: Security & Privacy
permalink: /security
parent: Deep Dives
nav_order: 11
---

# Security & Privacy

## Read-only guarantee

The mounted volume is **strictly read-only**:

- Every write/create/delete/rename operation is refused at the filesystem layer (`ACCESS_DENIED`).
- The volume advertises `ReadOnlyVolume`, `CasePreservedNames`, `UnicodeOnDisk`; the filesystem name is `ZipFS`.
- The source archive is opened with `FileShare.ReadWrite` but never written to.

## UAC and elevation

- Both executables run with **`asInvoker`** — they never request elevation and never relaunch themselves as admin.
- The **Dokan** variant logs a warning when not elevated; mounting may still work depending on your system configuration.
- The **WinFsp** variant detects elevation and reacts: when running as Administrator it **forces a cross-integrity folder mount** so that standard-user processes can also access the mount ([Mounting](mounting#cross-integrity-folder-mounts-winfsp)).

## Cross-integrity mounts

When cross-integrity mode is active, the mounted volume carries the security descriptor **`D:P(A;;FA;;;WD)`** — a *protected* DACL granting **Everyone → Full Access**:

- **Why:** UAC integrity isolation otherwise makes a mount created by an elevated process invisible/inaccessible to standard processes (and vice versa).
- **Trade-off:** any local process can read the mounted content. Only enable it when that is acceptable, and only for content you would share with every local user anyway.
- Drive letters are not usable for this purpose (they cannot bypass UAC isolation), hence the folder mount under `%LOCALAPPDATA%\SimpleZipDrive\Mounts` (configurable).

## Data written to your machine

| Location | Contents | Lifetime |
|---|---|---|
| `%LOCALAPPDATA%\SimpleZipDrive\settings.dat` | Your settings (JSON) | Until you delete it |
| `%LOCALAPPDATA%\SimpleZipDrive\Temp\<pid>_<guid>\` | Disk-cached entries extracted from the archive | Deleted on unmount/exit; orphan-swept at next startup |
| `%LOCALAPPDATA%\SimpleZipDrive\Temp\Logs\` | Session + driver debug logs | Old logs deleted at startup; current session kept |
| `%LOCALAPPDATA%\SimpleZipDrive\Mounts\<name>\` | Empty mount-point folders (cross-integrity) | Not auto-deleted |

Temp files are created with an ACL restricted to the current user.

## Network communication

Three outbound calls, none of which include your file paths or archive contents:

| Call | When | Payload |
|---|---|---|
| **Update check** → `api.github.com/repos/purelogiccode/SimpleZipDrive/releases/latest` | Once at startup | Standard GitHub API GET; nothing sent beyond HTTP headers |
| **Statistics** → `www.purelogiccode.com/ApplicationStats/stats` | Once at startup | `{ applicationId, version }` only |
| **Bug report** → `www.purelogiccode.com/bugreport/api/send-bug-report` | On warnings/unhandled errors | `message` (error details), `applicationName`, `version`, `userInfo` (context), `environment` (OS + bitness, ≤ 50 chars), `stackTrace` |

Offline machines fail all three silently.

## Automatic bug reporting

- Serilog events at **Warning level and above** are forwarded to the bug API automatically — there is no consent prompt.
- A filter (`IsUserError`) suppresses *expected* errors so they do not spam the tracker: user cancellations, wrong passwords, corrupt archives, missing drivers, mount points in use (specific NTSTATUS codes), file-not-found/IO errors, and similar. Only genuine defects reach the API.
- Reports never intentionally include archive names, mounted paths, or file contents; the stack trace and environment summary describe the failure only.

If you prefer no telemetry, a firewall rule blocking `www.purelogiccode.com` for the app is sufficient — all calls are fire-and-forget with timeouts.

## Sensitive material

Because disk-cache files contain the *decrypted* content of archive entries, avoid mounting confidential encrypted archives on shared machines, or unmount promptly (which deletes the temp files immediately).
