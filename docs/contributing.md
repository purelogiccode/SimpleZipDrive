---
title: Contributing
permalink: /contributing
parent: Development
nav_order: 20
---

# Contributing

Thanks for helping improve SimpleZipDrive!

## Reporting issues

- **Crashes and warnings are reported automatically** by the built-in error reporter (filtered to genuine defects — see [Security & Privacy](security#automatic-bug-reporting) for the payload and opt-out).
- For anything else, open an issue at [github.com/purelogiccode/SimpleZipDrive/issues](https://github.com/purelogiccode/SimpleZipDrive/issues) and include:
  - variant (`Dokan`/`WinFsp`) and version (Help → About),
  - Windows version and architecture (x64/ARM64),
  - the **session log** from `%LOCALAPPDATA%\SimpleZipDrive\Temp\Logs\` (and the `winfsp_debug_*.log` for WinFsp mount problems),
  - for performance reports: the **filesystem driver version** — this matters more than the app version.
- Check [Troubleshooting](troubleshooting) and the automatic-report status first — many issues (wrong password, corrupt archive, occupied mount point) are expected behaviour, not bugs.

## Pull requests

1. Fork, create a feature branch from `master`.
2. Follow the [development conventions](development#code-conventions) — the build must stay **analyzer-warning-clean** (Meziantou + Roslynator).
3. Add/adjust tests: shared behaviour → Core test classes; driver-specific behaviour → the mirrored `WinFsp\*` classes. Run `dotnet test SimpleZipDrive.Tests -c Release`.
4. Keep changes focused; update `WhatsNew.md` for user-visible changes.
5. Open the PR against `master` with a short description of the behaviour change and how you verified it (manual mount tests count — mention variant, mount type, elevation).

Good first contributions: documentation fixes, additional test coverage for `ZipFileSystemCore`, troubleshooting entries for error messages not yet covered.

## Licensing

- SimpleZipDrive is **GPL v3** — contributions are accepted under the same license.
- Third-party components and their licenses:
  - [DokanNet](https://github.com/dokan-dev/dokany) — MIT (Dokan driver LGPL/BSD terms per its repository)
  - [WinFsp](https://github.com/winfsp/winfsp) — LGPL-3.0 (GPLv3 with FLOSS exception for the driver)
  - [SharpCompress](https://github.com/adamhathcock/sharpcompress) — MIT
  - [SharpSevenZip](https://github.com/adoconnection/sevenzipsystem) — MIT
  - [Serilog](https://serilog.net) — Apache-2.0
  - 7-Zip (`7z.dll`) — LGPL / unRAR restrictions, per 7-Zip licensing

By contributing you confirm your changes are your own work and may be licensed under GPL v3 as part of this project.
