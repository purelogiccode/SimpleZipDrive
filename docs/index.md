---
title: Home
permalink: /
nav_order: 1
---

# SimpleZipDrive Documentation

![SimpleZipDrive screenshot](https://github.com/purelogiccode/SimpleZipDrive/raw/master/screenshot.png)

**SimpleZipDrive** is a Windows application that mounts `ZIP`, `7Z`, `RAR`, and `TAR` archives (including compressed TAR variants and comic-book archives), Zstd-seekable `.zar` containers, and Xbox XISO disc images as **virtual drives or NTFS directory mount points**. Mounted archives appear and behave exactly like a regular drive in File Explorer and every other application — files can be browsed, opened, and streamed directly from the archive without extracting it first.

**Current version: 3.0.0** · License: [GPL v3](https://github.com/purelogiccode/SimpleZipDrive/blob/master/LICENSE.txt) · Downloads: [Releases](https://github.com/purelogiccode/SimpleZipDrive/releases)

---

## Key Features

- **Multi-format support** — `.zip`, `.7z`, `.rar`, `.tar`, `.tar.gz`, `.tar.bz2`, `.tar.xz`, `.tgz`, `.tbz2`, `.txz`, `.cbz`, `.cbr`, `.cb7`, `.zar`, Xbox disc images `.iso` / `.xiso` / `.cso`.
- **Drive-letter or folder mounting** — mount on the first free drive letter from `M–Q`, a letter you choose, or any folder path.
- **Hybrid caching engine** — zero-copy direct reads for stored ZIP entries, per-block random access for `.zar` (Zstd seekable) and Xbox images (`.iso`/`.cso`), a shared in-memory cache for small files with decompress-once/share-across-handles semantics, and a disk cache for very large entries.
- **Read-only and safe** — the mounted volume is strictly read-only; the underlying archive is never modified.
- **Encrypted archives** — password-protected archives prompt for a password, with verification and up to three attempts.
- **Cross-integrity mounting (WinFsp)** — makes a mounted drive visible to both standard and elevated (Administrator) processes.
- **Drag-and-drop** — drop an archive onto the executable and it mounts automatically.
- **Configurable memory limit** — per-file RAM cache limit (512 MB default), clamped automatically to 90 % of available system memory.
- **Built-in diagnostics** — session logs, native driver debug logs, orphaned temp-file cleanup, and automatic bug reporting.

## The Two Variants

SimpleZipDrive ships as two separate executables that share the same core engine and user interface:

| | **SimpleZipDrive** (Dokan variant) | **SimpleZipDrive_WinFsp** (WinFsp variant) |
|---|---|---|
| Filesystem driver | [Dokan v2 ≥ 2.3](https://github.com/dokan-dev/dokany) | [WinFsp ≥ 2.1](https://github.com/winfsp/winfsp) |
| Executable | `SimpleZipDrive.exe` | `SimpleZipDrive_WinFsp.exe` |
| Drive-letter mounts | ✔ | ✔ |
| Folder mounts | ✔ | ✔ |
| Cross-integrity mounts | — | ✔ |
| Maturity | Mature, widely deployed | Modern, actively developed driver |

See [Variants](variants) for a detailed comparison and help choosing.

## Documentation

### Getting Started
- [Installation](installation) — prerequisites, which package to download, install/upgrade/uninstall
- [Getting Started](getting-started) — your first mount in two minutes
- [Usage Guide](usage) — every way to mount, unmount, and operate the app
- [Variants](variants) — Dokan vs. WinFsp in depth

### Deep Dives — [overview](guides)
- [Mounting](mounting) — mount-point resolution, drive letters, folder and cross-integrity mounts, error codes
- [Caching](caching) — the memory and disk cache architecture and tuning
- [Performance](performance) — expected performance, benchmarks, and tuning tips
- [Archive Support](archive-support) — supported formats, compression, encryption, and the 7-Zip fallback
- [Security & Privacy](security) — read-only guarantees, UAC, DACLs, and what data leaves your machine

### Operations — [overview](operations)
- [Configuration](configuration) — settings reference and data locations
- [Logging](logging) — log files, bug reporting, and how to collect diagnostics
- [Troubleshooting](troubleshooting) — solutions for every common error
- [FAQ](faq) — frequently asked questions

### Development — [overview](development)
- [Architecture](architecture) — how the software works inside
- [Building & Packaging](building-and-packaging) — build, publish, and release process
- [Development Setup](development) — environment, tests, and conventions
- [Contributing](contributing) — issues, pull requests, and licensing

---

## System Requirements

| Requirement | Details |
|---|---|
| Operating system | Windows 10 or 11 (x64 or ARM64) |
| Runtime | [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) |
| Filesystem driver | **Dokan v2 2.3+** (Dokan variant) or **WinFsp 2.1+** (WinFsp variant) — install the matching driver for the variant you use |
| Disk space | ~10 MB for the application; temp space equal to the largest file you open when the disk cache is used |
| Memory | The memory cache is clamped to 90 % of installed RAM; the per-entry default is 512 MB |
