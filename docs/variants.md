---
title: Mount Backends
permalink: /variants
nav_order: 5
---

# Mount Backends: WinFsp, Dokan and FUSE

SimpleZipDrive is a single cross-platform application. The archive engine, caches, settings, logging, and UI are shared everywhere; the only difference is the user-mode filesystem driver used to present the virtual drive. The driver is selected inside the app:

| Platform | Available backends |
|---|---|
| Windows | **WinFsp**, **Dokan** (or **Auto**) |
| Linux / macOS | **FUSE** (libfuse3 on Linux, macFUSE on macOS) |

Choose the backend in **Settings → Mount Settings → Mount backend**:

- **Auto** - uses WinFsp when it is installed, otherwise Dokan on Windows; FUSE on Linux/macOS.
- **Dokan** - the default on Windows; install the Dokan driver to use it.
- **WinFsp** - install the WinFsp driver and select this to use it instead.
- **FUSE** - Linux/macOS only; it is not offered on Windows.

The Settings list shows only the backends for the current platform: **Dokan/WinFsp/Auto on Windows** and **FUSE/Auto on Linux/macOS**. You can install either (or both) Windows drivers and switch between them at any time. If the driver for the selected backend is missing, the app warns on startup and offers to open the matching download page (and reports the reason in the log instead of mounting).

## Windows: Dokan vs. WinFsp

| Capability | Dokan | WinFsp |
|---|---|---|
| Required driver | Dokan v2 **2.3 or newer** (older versions are blocked with a *"Dokan Driver Outdated"* dialog) | WinFsp **2.1 or newer** (older versions are blocked with a *"WinFsp version mismatch"* dialog; 2.2.x betas work) |
| Driver service | Dokan driver loads on demand | Requires the `WinFsp.Launcher` service to be **Running** (checked before every mount) |
| Drive-letter mounts (M-Q) | Yes | Yes |
| Folder mounts | Yes (folder must exist) | Yes (folder is created if missing, write-tested first) |
| Cross-integrity mounting | - | Yes (see below) |
| Mount implementation | In-process via `DokanNet` (`DokanInstanceBuilder`) | In-process via `FileSystemHost.Mount` |
| Mount volume style | `RemovableDrive` | Standard host volume |
| Pre-mount driver check | `DokanVersion()` P/Invoke + architecture check + minimum-version gate (dokan2.dll ≥ 2.3.0) | Native DLL preload, registry version check, service check, `winfsp-msil.dll` interop availability |
| Retries on driver error | 2 retries with 1 s delay (skipped for deterministic `DokanStatus` failures: install, mount-point/letter, version) | Maps NTSTATUS codes to specific messages (see [Mounting](mounting#mount-error-codes)) |
| Admin warning | Logs a warning when not elevated | No warning; elevation triggers cross-integrity mode instead |

## Cross-integrity mounting (WinFsp only)

Windows isolates resources between integrity levels: a drive mounted by an elevated (Administrator) process is normally **invisible or inaccessible** to standard-user processes, and vice versa.

The WinFsp backend solves this with **cross-integrity folder mounts**:

- The archive is mounted on a **folder** instead of a drive letter - by default under `%LOCALAPPDATA%\SimpleZipDrive\Mounts\<ArchiveName>` (configurable, see [Configuration](configuration#settings-reference)).
- A permissive security descriptor (`D:P(A;;FA;;;WD)` - Everyone: Full Access, protected DACL) is applied so both standard and elevated processes can read the mount.
- It is used automatically when:
  - the app **runs as Administrator** (forced, so your standard-user apps can see the drive), or
  - the **Cross-integrity mount** setting is enabled.
- In cross-integrity mode, requested drive letters are redirected to the folder with the log line *"Cross-integrity mode: Drive letter mounts are not supported. Redirecting to folder mount."*

See [Mounting](mounting#cross-integrity-folder-mounts) for the mechanics and [Security & Privacy](security) for the security implications.

## Linux and macOS: FUSE

On Linux and macOS the app mounts archives on **folders** through the bundled FuseSharp library:

- **Linux** requires **libfuse3** (e.g. `sudo apt install libfuse3-3`, `sudo dnf install fuse3`).
- **macOS** requires **macFUSE** (https://macfuse.github.io/).
- When no mount point is supplied, a temporary folder under the system temp directory is created and removed on unmount.
- The volume is exposed read-only, exactly like the Windows backends.

## Which one should you use on Windows?

- **Default: Dokan** - the app defaults to Dokan on Windows. Install the Dokan driver (2.3.0+) and you are ready to mount.
- **Choose Auto** to prefer **WinFsp** when it is installed and fall back to **Dokan**.
- **Choose WinFsp** if you need:
  - mounting by **elevated** processes that must remain accessible to normal apps (or vice versa),
  - automatic creation of fresh mount folders,
  - the WinFsp ecosystem (e.g. you already use other WinFsp-based filesystems).
- **Driver version discipline (WinFsp):** keep the native driver at **2.1 stable** or a **2.2+ beta**. The app deliberately uses the 2.1 managed interop because newer interop packages reject the stable 2.1 driver with *"incorrect dll version (need 2.2, have 2.1)"*. If you upgrade the native driver to a 2.2+ beta, the app continues to work.

You can install both Windows drivers side by side and switch between them in Settings; only **one mount at a time per instance** is supported.
