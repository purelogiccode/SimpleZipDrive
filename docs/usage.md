---
title: Usage Guide
permalink: /usage
nav_order: 4
---

# Usage Guide

## Mounting methods

### Drag-and-drop

Drag an archive file from Explorer onto the executable. One archive is detected and mounted automatically on the first free drive letter from **M–Q**.

### From the menu

| Menu item | Behaviour |
|---|---|
| **Mount** | Opens a file dialog and mounts using your *Default mount type* setting (drive letter or folder — see [Configuration](configuration#settings-reference)) |
| **Mount as Drive** | Always mounts on a drive letter (auto-selected from M–Q) |
| **Mount as Folder** | Asks for an existing folder and mounts there |

### Command line

```text
Usage 1 (Explicit Mount):  SimpleZipDrive.exe <PathToArchiveFile> <MountPoint>
Usage 2 (Drag-and-Drop):   drag a supported archive onto the .exe icon
```

Examples:

```text
SimpleZipDrive.exe "C:\Data\backup.zip" M        → drive M:
SimpleZipDrive.exe "C:\Data\backup.7z" N         → drive N:
SimpleZipDrive.exe "C:\Data\photos.rar" O        → drive O:
SimpleZipDrive.exe "C:\Data\docs.zip" "C:\mount\zip"   → folder mount
SimpleZipDrive.exe "C:\Data\docs.zip"            → auto-mount on M:–Q:
```

- The **mount point** may be a bare drive letter (`M`), a drive with colon (`M:`), or a path to a folder. The Dokan variant also accepts `M:\`.
- With **one argument** the app behaves exactly like drag-and-drop.
- With **no arguments** the app starts idle; mount via the menu.
- Errors are shown in the log pane; there are no distinct shell exit codes (the app is a GUI).

## Mount points in detail

- **Drive letters M–Q** are tried in order; occupied letters are skipped with a log line. A letter outside the pool can be requested explicitly via the command line.
- **Folder mounts** use the folder you provide. The WinFsp variant verifies the folder is writable with a probe file before mounting; the folder is created if it does not exist.
- **One mount at a time.** Mounting while a drive is already mounted shows *"A drive is already mounted. Please unmount it first."* Unmount first, then mount something else.

## Encrypted archives

When an archive requires a password, a **Password Required** dialog appears. The password is verified by test-reading every encrypted entry before the mount proceeds.

- Up to **3 attempts** are allowed; each mismatch logs *"Incorrect password for 'X' archive (attempt n of 3)."*
- Cancelling the dialog cancels the mount — nothing is mounted.
- After 3 failures the mount aborts with *"The provided password did not match the encrypted X archive. Mount aborted after 3 attempts."*

See [Archive Support](archive-support#password-protected-archives) for format-specific behaviour.

## Unmounting

- **Unmount button** in the app window.
- **Closing the window** triggers a clean shutdown: the drive is unmounted with a 5-second grace period, temporary files are deleted, then the app exits. If shutdown hangs, a watchdog force-exits the process.
- Unmounting waits briefly for in-flight driver callbacks, deletes all disk-cache files for the session, and releases the memory cache.

## Built-in utilities

| Feature | Where | What it does |
|---|---|---|
| **Open drive in Explorer** | Automatically after mount (optional setting) or menu | Opens the mounted drive rooted in a new Explorer window |
| **Clean Temp Files** | Menu | Deletes orphaned temporary cache directories left behind by crashed sessions (normally automatic at startup) |
| **Screenshot (F8)** | Global hotkey while the app runs | Captures the app window to a file — handy for bug reports |
| **Open Config Path** | Menu | Opens `%LOCALAPPDATA%\SimpleZipDrive` in Explorer |
| **Update check** | Automatic at startup | Compares against the latest GitHub release and offers to open the download page; silent when offline |

## Tips

- Mount the same archive again later — the second mount re-parses the central directory but files still in the OS file cache open instantly.
- Keep the app window open while using the drive; closing it unmounts the drive.
- The log pane is your friend: every decision (stored-entry fast path, disk-cache offload, memory limits, retries) is logged.
- For heavy repeated access to huge files, prefer the **Dokan** variant's zero-copy stored-entry path or ensure entries fit within the memory limit — see [Performance](performance).
