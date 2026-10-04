---
title: Installation
permalink: /installation
nav_order: 2
---

# Installation

## 1. Choose your platform

SimpleZipDrive is a single cross-platform application. Download the package for your OS and architecture:

| Package | Platform | Driver |
|---|---|---|
| `release_X.Y.Z_win-x64.zip` / `..._win-arm64.zip` | Windows (x64 / ARM64) | [WinFsp](https://github.com/winfsp/winfsp/releases) and/or [Dokan](https://github.com/dokan-dev/dokany/releases) |
| `release_X.Y.Z_linux-x64.zip` / `..._linux-arm64.zip` | Linux (x64 / ARM64) | libfuse3 |
| `release_X.Y.Z_osx-x64.zip` / `..._osx-arm64.zip` | macOS (Intel / Apple silicon) | macFUSE |

- **Windows x64** — standard 64-bit Intel/AMD systems (the vast majority of PCs).
- **Windows ARM64** — Windows-on-ARM devices (Snapdragon X, Surface Pro X, etc.).
- **Linux** — install libfuse3 from your distribution (`libfuse3-3` on Debian/Ubuntu, `fuse3` on Fedora).
- **macOS** — install [macFUSE](https://macfuse.github.io/).

> On Windows the mount backend is selected inside the app (Settings → Mount backend). **Auto** prefers WinFsp when installed, otherwise Dokan. See [Mount Backends](variants) for a full comparison.

## 2. Install prerequisites

All packages are **framework-dependent** — the .NET runtime is *not* bundled:

1. **[.NET 10 runtime](https://dotnet.microsoft.com/download/dotnet/10.0)** — install the runtime matching your OS architecture.
2. **The filesystem driver** for your platform:
   - **Windows — Dokan**: install the latest **Dokan v2** `DokanSetup.exe` (**2.3.0 or newer is required** — older `dokan2.dll` versions are refused with a *"Dokan Driver Outdated"* dialog).
   - **Windows — WinFsp**: install **WinFsp 2.1 or newer** (2.2.x beta releases are also supported). Mounting is blocked if an older version is detected. The `WinFsp.Launcher` service must be running — it is installed and started automatically by the WinFsp installer.
   - **Linux**: install libfuse3 (e.g. `sudo apt install libfuse3-3`).
   - **macOS**: install macFUSE.

Administrator rights are **not** required to run SimpleZipDrive (see [Security & Privacy](security#uac-and-elevation)).

## 3. Install the application

1. Download the `.zip` for your platform and architecture from the [Releases page](https://github.com/purelogiccode/SimpleZipDrive/releases).
2. Extract **all files** into a dedicated folder. The package contains:
   - the executable (`SimpleZipDrive.exe` on Windows, `SimpleZipDrive` on Linux/macOS),
   - the bundled 7-Zip fallback extractor — `7za.exe` on Windows, `7zzs` on Linux, `7zz` on macOS (required for the fallback; mark it executable if your archive tool dropped the permission),
   - `7zip-license.txt` — the license for the bundled 7-Zip binaries,
   - `winfsp-msil.dll` (Windows only) — the WinFsp .NET interop (required when using the WinFsp backend),
   - Avalonia native libraries (`libSkiaSharp`, `libHarfBuzzSharp`, and on Windows `av_libglesv2`),
   - `ReadMe.md`, `LICENSE.txt`, `WhatsNew.md`.
3. Run the executable. No installer, no registry changes.

> **Do not** separate the support files from the executable. The 7-Zip fallback binary, the WinFsp interop, and the Avalonia native libraries are probed next to the executable; if they are missing, the app will not start or archives that need the fallback will fail to open.

## 4. Verify

1. Start the executable — the main window opens with a log pane.
2. Check **Help → About** for the version.
3. Mount any archive (see [Getting Started](getting-started)):
   - If a *"Dokan Driver Not Found"* or *"WinFsp Not Found"* dialog appears, the selected driver is missing — install it from the link in the dialog or switch the backend in Settings.
   - The WinFsp backend additionally verifies the driver version and the `WinFsp.Launcher` service before mounting and tells you exactly what to fix.
   - On Linux/macOS a missing libfuse3/macFUSE produces a *"FUSE Not Available"* dialog with install guidance.

## 5. Upgrade

1. Unmount any mounted drive and close the running instance.
2. Extract the new release over the existing folder (or delete the folder and extract fresh — your settings live in `%LOCALAPPDATA%\SimpleZipDrive`, not in the program folder).
3. Driver upgrades are independent: upgrade Dokan/WinFsp via their own installers whenever you like.

## 6. Uninstall

1. Delete the program folder.
2. Optionally remove residual data: `%LOCALAPPDATA%\SimpleZipDrive` (settings, logs, leftover mount folders — see [Configuration](configuration#data-locations)).
3. Optionally uninstall the Dokan or WinFsp driver via *Settings → Apps* if no other software uses it.
