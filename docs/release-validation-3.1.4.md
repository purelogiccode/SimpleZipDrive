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
- **Base commit:** `0d5f06c` plus the local 3.1.4 changes (dialog/FUSE fixes and the new Linux
  packaging)
- **Artifacts:** `SimpleZipDrive\bin\Release` (monotonically extended, nothing removed)

## Bundles and SHA256

| Bundle | Bytes | SHA256 |
|---|---:|---|
| `release_3.1.4_win-x64.zip` | 15,102,333 | `13FEEF8B76CC361A692F32F86C30600A0C55C651BCC5148FE745AA5A1CF4AAA8` |
| `release_3.1.4_win-arm64.zip` | 14,532,827 | `F7D25F35EA6C565649D4E15E8F94397B456C2656FCCBD1C73C0B9DB14FB300FB` |
| `release_3.1.4_linux-x64.tar.gz` | 13,592,377 | `5EE8D96C71F2FD6EFA54CCDA43399DD88123BFB95AAE5C70090D681F9DFAE9D2` |
| `release_3.1.4_linux-x64.deb` | 47,476,702 | `3B4B0207D2B1DF6265944172806339D1E6F51312B1F925E136F8D7403666DF3A` |
| `release_3.1.4_linux-x64.AppImage` | 44,591,608 | `B5838F1D452B3EAA85FD3F808D9012A53F1EDF3CBDE5F63A040B611307788C75` |
| `release_3.1.4_linux-arm64.tar.gz` | 13,110,451 | `A3DCF10D4EB3F6B432C9F0BC2C28D949FA68402ED02203355B2295CE6F06AFC7` |
| `release_3.1.4_linux-arm64.deb` | 44,983,108 | `DCC3C6CC1183E56DA4A5EBEB3E5918C591B37D80DF378BC00EBECC146E84153F` |
| `release_3.1.4_linux-arm64.AppImage` | 42,019,336 | `F1A3F104222290D6DF672E59C56591857CEBC6D4179B3A096574210E45B2D5B6` |

The macOS arm64 DMG used for the run was assembled on the Mac with `hdiutil` from the
`osx-arm64` publish output (the packaging script's DMG path); it is not kept locally because the
rented Mac was deleted after the session.

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
