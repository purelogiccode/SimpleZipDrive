---
title: Getting Started
permalink: /getting-started
nav_order: 3
---

# Getting Started

A two-minute walkthrough of your first mount.

## 1. Mount an archive

**Drag-and-drop (easiest):** drag any supported archive file onto the executable in your file manager. The app starts, logs *"Drag-and-drop mode: Detected archive file '…'"* and mounts immediately.

**From the app menu:** start the app, use *Mount* (or *Mount as Drive* / *Mount as Folder*) and pick an archive file.

**From the command line:**

```text
SimpleZipDrive.exe "C:\Games\MyGame.zip" M
```

## 2. Where it mounts

**Windows:** with no mount point specified, the app tries the drive letters **M, N, O, P, Q** in order and uses the first free one — the log shows:

```text
Attempting to mount on 'M:'...
Successfully mounted on 'M:'.
```

If all five letters are taken, an error is logged and you can mount to a folder instead. A bare letter (`M`) is automatically expanded to `M:` (Dokan backend: `M:\`). See [Mounting](mounting) for the full rules.

**Linux/macOS:** archives mount on a folder. With no mount point specified, a temporary folder under the system temp directory is used; pick any folder explicitly to control the location.

## 3. Use the mount

Open **This PC** (Windows) or your file manager (Linux/macOS) — the archive is now a drive/folder named after the archive. Files open directly from the archive:

- Small files are decompressed once into memory and shared by every reader.
- Large files stream straight from the archive (stored ZIP entries, `.zar` containers, and Xbox disc images) or through the disk cache.
- Re-opening a file that is still in the memory cache is effectively instant.

Nothing is written to the archive or the drive — the volume is strictly [read-only](security#read-only-guarantee).

## 4. Unmount

Any of the following:

- Press **Unmount** in the app window,
- close the app window (it unmounts cleanly before exiting), or
- on Windows, eject the drive from Explorer's tray icon; on Linux/macOS, unmount it from the system (`fusermount3 -u <folder>` / `umount <folder>`).

Unmounting deletes the session's temporary disk-cache files automatically.

## 5. Check the log pane

The window's log pane tells you exactly what happened at every step: archive type detection, entry counts, cache limits, mount attempts, and errors. Full detail is written to a session log file (see [Logging](logging)) — if something does not work, that file is the first thing to look at.

## Next steps

- [Usage Guide](usage) — mounting to folders, passwords, command-line details
- [Mount Backends](variants) — decide between Dokan, WinFsp and FUSE
- [Troubleshooting](troubleshooting) — when a mount fails
