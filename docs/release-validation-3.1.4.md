---
title: Release Validation 3.1.4
permalink: /release-validation-3-1-4
parent: Development
nav_order: 20
---

# Release Validation 3.1.4

This is the first recorded bundle roundtrip for SimpleZipDrive. No earlier validation report,
host log, or test artifact was found in the repository, in `docs/`, or in the historical
release folder - the 3.1.0-3.1.2 bundles on disk have no accompanying test record.

- **Date:** 2026-11-25
- **Version under test:** 3.1.4
- **Release commit:** `b537dab` (tag `release_3.1.4`)
- **Artifacts:** published `release_3.1.4` GitHub assets (the matching local files live in
  `SimpleZipDrive\bin\Release`, which is append-only)

## Bundles and SHA256

| Bundle | Bytes | SHA256 |
|---|---:|---|
| `release_3.1.4_win-x64.zip` | 15,102,476 | `BC5D393FF664638A68A029BD8A509E6C5BF1CBF8C6271E7B5B5798567747D77E` |
| `release_3.1.4_win-arm64.zip` | 14,533,013 | `1C39D48C51679858EBBA24DFD515F851E44305FF44AF480EF613B325A8250FC7` |
| `release_3.1.4_linux-x64.tar.gz` | 13,592,355 | `B77FE1BEB283B14CF06307B9FDECBDFEF45F236B153375ECC581A033A10CEB96` |
| `release_3.1.4_linux-x64.deb` | 47,476,870 | `C46C7D8ECFAE79960794E329942C129E3A8067C80AC6B1DF39D2E0E0C27CCA08` |
| `release_3.1.4_linux-x64.AppImage` | 44,591,608 | `81B2FA6A404FD9154C7A23F16C9A8EA451CC66F24169D50D0EF0CD3C901EBD46` |
| `release_3.1.4_linux-arm64.tar.gz` | 13,110,297 | `5B1C7FF1354EC98EFE271622D7FB31919D337981E04F9F51B208B45BF8E0171D` |
| `release_3.1.4_linux-arm64.deb` | 44,982,202 | `B39D1A87D515F44BFF929104F31C899F828BABF334F2A50DDB9E9ECE53CADB6A` |
| `release_3.1.4_linux-arm64.AppImage` | 42,019,336 | `9C3F9184318DA3ED5A59FCC60D84818F9FAB0D9EB76D82CD7FD193C071A90CA5` |
| `release_3.1.4_osx-x64.dmg` | 19,192,919 | `C710E2C0BDFFEC2C340A4F97B6C2A9DE7615A09DBD12D79D61F0E802C0E31DEB` |
| `release_3.1.4_osx-arm64.dmg` | 19,097,264 | `81480DC7AA2524F4A7997442B4E967F4CDFE5236D8392244D7991FDAA3C76F79` |

The macOS arm64 run used a DMG assembled on the Mac with `hdiutil` from the `osx-arm64` publish
output (the same packaging path the CI release job uses); the published DMGs are CI-built.

## Results

| Host | OS / arch | Bundle(s) | Result |
|---|---|---|---|
| PETERSONPC | Windows 11 x64, native WinFsp 2.2.26112; bundled interop 2.1.25156 | `win-x64.zip` | Launched from the extracted folder; mounted `sample.zip` on `Q:` and read `hello.txt`. |
| winarm-test (Azure) | Windows 11 Pro 25H2 ARM64 | `win-arm64.zip` | Launched in the interactive console session via Task Scheduler; window rendered. No Dokan/WinFsp installed, so the expected *Dokan Not Available* dialog appeared instead of a mount. |
| LinuxMintZip (Hyper-V) | Linux Mint 22.3 x64 | `linux-x64.tar.gz` | Extracted, launched with the .NET 11 SDK runtime, mounted `sample.zip` through FUSE and read `hello.txt`. |
| LinuxMintZip | Linux Mint 22.3 x64 | `linux-x64.deb` | Installed with `apt` (no dependency errors), launched from `/opt/simplezipdrive`, mounted and read the sample. |
| LinuxMintZip | Linux Mint 22.3 x64 | `linux-x64.AppImage` | Launched after `chmod +x`, mounted and read the sample. |
| linuxarm-test (Azure) | Ubuntu 24.04.5 LTS aarch64 | `linux-arm64.tar.gz` | Extracted, launched with the .NET 11 SDK runtime, mounted and read the sample. |
| linuxarm-test | Ubuntu 24.04.5 LTS aarch64 | `linux-arm64.deb` | Installed with `apt`, launched, mounted and read the sample. |
| linuxarm-test | Ubuntu 24.04.5 LTS aarch64 | `linux-arm64.AppImage` | Launched after `chmod +x`, mounted and read the sample. |
| NoMac (rented) | macOS 26.6.2 arm64 | `osx-arm64` DMG | Installed the .NET 11 runtime, attached the DMG, launched the app from the mounted volume and captured the window. macFUSE is not installed on the host, so the app showed its *macFUSE Not Found* dialog and mounting was not exercised. |

Linux GUI checks ran under Xvfb with `xdotool` window verification and screenshots; the sample
archive contained a single `hello.txt` that was read back from every mounted volume. Every bundle
in the tested matrix (Windows x64/ARM64, Linux x64/ARM64, macOS ARM64) was produced by
`scripts/package-release.ps1` (the Linux arm64 set was built on the arm64 host; the x64 set on
Windows plus the Linux host for AppImage). Eight are retained in `SimpleZipDrive\bin\Release`; the
macOS arm64 DMG was not kept because the rented Mac was deleted. The `osx-x64` DMG was outside
this roundtrip.

## Dropped: Flatpak

A Flatpak bundle was built and mounted successfully during this roundtrip on both architectures
(Freedesktop 25.08), but the target was removed afterwards. Flatpak sandboxes have no mount
capabilities, so it required forwarding the mount handshake to the host `fusermount3` and the
`--talk-name=org.freedesktop.Flatpak` permission, which allows arbitrary host commands. That
trade-off is not acceptable for a release, so only `.deb` and `.AppImage` are shipped. The two
test `.flatpak` files remain in the local append-only release folder but are not release assets.

## Cleanup

The Azure hosts (`linuxarm-test`, `winarm-test`) were deallocated and the rented Mac was stopped
after the run. The local `LinuxMintZip` VM was shut down; its configuration is kept in the
maintainer's private host notes.
