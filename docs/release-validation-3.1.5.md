---
title: Release Validation 3.1.5
permalink: /release-validation-3-1-5
parent: Development
nav_order: 20
---

# Release Validation 3.1.5

- **Date:** 2026-11-28
- **Version under test:** 3.1.5
- **Release commit:** `914889f` (tag `release_3.1.5`)
- **Artifacts:** published `release_3.1.5` GitHub assets

## Verification

- **CI** (`ci.yml`, run `46150338388`): Windows build with warnings as errors and the full
  **1,503-test** suite, Ubuntu/macOS builds, and the NuGet vulnerability audit - all green.
- **Release** (`release.yml`, run `46150514149`): the version/tag check passed, Windows built and
  tested again, all four bundle jobs (Windows, Linux x64, Linux arm64, macOS) succeeded, and the
  publish job verified the ten expected bundles, rejected any Flatpak asset, and published the
  GitHub release.
- No manual host smoke-test matrix was run for 3.1.5. The change set is limited to the FUSE
  mount-point validation, the Flatpak failure reporting/classification, the version bump and the
  documentation/release guards above.

## Bundles and SHA256

| Bundle | Bytes | SHA256 |
|---|---:|---|
| `release_3.1.5_win-x64.zip` | 15,103,666 | `678FF69ED21F8D5FD806D8368B91449A92FE4CCB726CC475A0B8F5949A30035C` |
| `release_3.1.5_win-arm64.zip` | 14,534,180 | `5C8D9BF4C2489D234E132F621AA56301F3804A6B5ACF27808078DDF11DEAF024` |
| `release_3.1.5_linux-x64.tar.gz` | 13,593,745 | `89B862C4DA4A3CA486D555638CC5846198E436A35DE005FF51375F9500035DE3` |
| `release_3.1.5_linux-x64.deb` | 47,479,230 | `256DC09AFBBC3E92E727471AE046EB03DD8C5255278EE8AC7695677421F97085` |
| `release_3.1.5_linux-x64.AppImage` | 44,575,224 | `6D8B73FFE784937BBB457AC59C7B7CDD9006819F98CB5682FC57FBBC161347CC` |
| `release_3.1.5_linux-arm64.tar.gz` | 13,112,114 | `6BE8A3596F8465C2DBAF007C879515EB8398D9DB29CD56AE182F4E3C1A17E029` |
| `release_3.1.5_linux-arm64.deb` | 44,983,614 | `D2CB50E0E91731657050791048180D819E682978C1D7FB49984A5458114FBBBB` |
| `release_3.1.5_linux-arm64.AppImage` | 42,023,432 | `02AEBF2F52F41C6E38D3AD37C83F276909A94CE62EE9D34A63CCDD2BD8397C05` |
| `release_3.1.5_osx-x64.dmg` | 19,194,681 | `BEFE219DCC56DF1C65CA34A17129CEB7CFAD8E6297F38E743521E1DB65F557C3` |
| `release_3.1.5_osx-arm64.dmg` | 19,098,903 | `00106CA7E156C5FD1F23789621DDF64E9D38ED5AB6198ABFD371803ED31AC28E` |

The hashes are the SHA256 digests recorded by GitHub for the published release assets.

## Flatpak

Flatpak is not a release target and no Flatpak asset is published. The sandbox cannot mount FUSE
without forwarding the mount handshake to the host (`--talk-name=org.freedesktop.Flatpak`, which
permits arbitrary host commands). Linux releases are the `.deb`, `.AppImage` and portable
`.tar.gz`; the release workflow rejects any `*.flatpak` file before publishing.
