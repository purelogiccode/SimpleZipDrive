---
title: Installation
permalink: /installation
nav_order: 2
---

# Installation

## 1. Choose a variant

SimpleZipDrive ships in two variants that differ only in which filesystem driver they use. Pick **one** driver and the matching executable:

| Package | Driver | Choose this if… |
|---|---|---|
| `release_X.Y.Z_Dokan_win-x64.zip` / `..._win-arm64.zip` | [Dokan](https://github.com/dokan-dev/dokany/releases) | You want the battle-tested, widely deployed driver |
| `release_X.Y.Z_WinFsp_win-x64.zip` / `..._win-arm64.zip` | [WinFsp](https://github.com/winfsp/winfsp/releases) | You want the modern driver, cross-integrity mounting, or folder mounts on fresh directories |

- **x64** — for standard 64-bit Intel/AMD systems (the vast majority of PCs).
- **ARM64** — for Windows-on-ARM devices (Snapdragon X, Surface Pro X, etc.).

> If you are unsure, download the **Dokan** variant for your architecture. See [Variants](variants) for a full comparison.

## 2. Install prerequisites

Both variants are **framework-dependent** — the .NET runtime is *not* bundled:

1. **[.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)** — install the x64 *or* ARM64 Desktop Runtime matching your OS architecture.
2. **The filesystem driver** for your variant:
   - **Dokan**: install the latest **Dokan v2** `DokanSetup.exe`. Verify afterwards that the Dokan *user-mode* library is present (`dokan2.dll` is loaded from the Dokan installation, not from the app folder).
   - **WinFsp**: install **WinFsp 2.1 or newer** (2.2.x beta releases are also supported). Mounting is blocked if an older version is detected. The `WinFsp.Launcher` service must be running — it is installed and started automatically by the WinFsp installer.

Administrator rights are **not** required to run SimpleZipDrive (see [Security & Privacy](security#uac-and-elevation)).

## 3. Install the application

1. Download the `.zip` for your variant and architecture from the [Releases page](https://github.com/purelogiccode/SimpleZipDrive/releases).
2. Extract **all files** into a dedicated folder, e.g. `C:\Tools\SimpleZipDrive`. The package contains:
   - the executable (`SimpleZipDrive.exe` or `SimpleZipDrive_WinFsp.exe`),
   - `7z.dll` and `7z_arm64.dll` — the native fallback extraction libraries (required),
   - `winfsp-msil.dll` (WinFsp package only) — the WinFsp .NET interop (required),
   - `ReadMe.md`, `LICENSE.txt`, `WhatsNew.md`.
3. Run the executable. No installer, no registry changes.

> **Do not** separate the DLLs from the executable. The 7z fallback libraries and the WinFsp interop are probed next to the `.exe`; if they are missing, archives that need them will fail to open.

## 4. Verify

1. Start the executable — the main window opens with a log pane.
2. Check **Help → About** for the version.
3. Mount any archive (see [Getting Started](getting-started)):
   - If a *"Dokan Driver Not Found"* or *"WinFsp Not Found"* dialog appears, the driver is missing — install it from the link in the dialog.
   - The WinFsp variant additionally verifies the driver version and the `WinFsp.Launcher` service before mounting and tells you exactly what to fix.

## 5. Upgrade

1. Unmount any mounted drive and close the running instance.
2. Extract the new release over the existing folder (or delete the folder and extract fresh — your settings live in `%LOCALAPPDATA%\SimpleZipDrive`, not in the program folder).
3. Driver upgrades are independent: upgrade Dokan/WinFsp via their own installers whenever you like.

## 6. Uninstall

1. Delete the program folder.
2. Optionally remove residual data: `%LOCALAPPDATA%\SimpleZipDrive` (settings, logs, leftover mount folders — see [Configuration](configuration#data-locations)).
3. Optionally uninstall the Dokan or WinFsp driver via *Settings → Apps* if no other software uses it.
