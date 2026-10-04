---
title: Development
permalink: /development
nav_order: 17
has_children: true
---

# Development

## Prerequisites

| Tool | Version | Notes |
|---|---|---|
| .NET SDK | **10.0** (pinned: `global.json` → `10.0.0`, `rollForward: latestMajor`) | `dotnet --version` must resolve to 10.x |
| OS | Windows, Linux or macOS | Avalonia UI; Windows backends need Dokan/WinFsp, Linux/macOS need libfuse3/macFUSE |
| Drivers (to *run*) | Dokan v2 and/or WinFsp ≥ 2.1 (Windows), libfuse3/macFUSE (Unix) | Only needed for manual mount testing; unit tests do not need drivers |
| IDE | Visual Studio 2022+, Rider, or VS Code | Solution: `CSharp_SimpleZipDrive.sln` |

## Build and run

```powershell
dotnet build CSharp_SimpleZipDrive.sln -c Release
dotnet run --project SimpleZipDrive
```

Debug runs mount exactly like packaged builds (in-process driver hosting). F5 in the IDE works as usual; the log pane and session log show the same diagnostics as release builds.

## Tests

```powershell
dotnet test SimpleZipDrive.Tests -c Release
```

- **xUnit 2.9.3**, 1,300+ test cases, coverlet collector included.
- Layout: root classes cover the archive engine and services; `Mounting\` coverage includes the Dokan and WinFsp backends plus the FUSE adapter; `FuseSharp\` covers the vendored FUSE interop; `Fakes\` provides `FakeDokanFileInfo`, `FakeUserNotificationService`, `MockBugReport`.
- Areas: filesystem core, memory-cache behaviour, error handling, streams/read-ahead, settings, services, update checker (asserts the canonical GitHub endpoint), logging/error filtering, administrator checks, archive formats (including ZArchive/XISO adapters).
- **Settings-file isolation:** the three test classes that exercise the real `settings.dat` path (`AppSettingsAdditionalTests`, `SettingsServiceTests`, `WinFspSettingsServiceTests`) share the `Settings file` xUnit collection so they never race each other (the old `Save_WritesValidJson` flake).

## Continuous integration

`.github/workflows/ci.yml` runs on every push and pull request to `master`: restore, Release build (must stay analyzer-warning-clean), full test suite on Windows, and cross-platform compilation on Ubuntu/macOS. The release workflow (`release.yml`) re-verifies and packages the six platform bundles behind the protected `release` environment; the wiki sync workflow mirrors `docs/` to the repository wiki. See [Building & Packaging](building-and-packaging#automated-builds-github-actions).

## Code conventions

- **Language level:** default C# for net10.0 — file-scoped namespaces, collection expressions, target-typed `new`, `required` members, `System.Threading.Lock` for lock objects.
- **Analyzers:** Meziantou.Analyzer + the three Roslynator packs run on every project; the build is warning-clean (a few `MA` rules relaxed in `.editorconfig`). Keep it that way.
- **Structure:** the app is a single project. The former `SimpleZipDrive.Core` engine lives under `SimpleZipDrive\Core\` (namespaces unchanged), the mount backends under `SimpleZipDrive\Mounting\{Dokan,WinFsp,Fuse}\`, the vendored FUSE binding under `SimpleZipDrive\FuseSharp\`, and the UI under `SimpleZipDrive\Views\`. The backend facade (`Mounting\MountService.cs`) selects the driver at mount time.
- **Tests:** every new engine/service behaviour gets tests in the existing classes; driver-specific behaviour is covered in the backend test classes.
- `References\` is vendored reference material (other zipfs implementations) — excluded from compilation; do not `#include` from it.

## Debugging tips

- The session log (`%LOCALAPPDATA%\SimpleZipDrive\Temp\Logs\debug_*.log`) is verbose — reproduce, then read.
- WinFsp mounts also write a native driver log per attempt (`winfsp_debug_*.log`).
- To debug a packaged single-file build (e.g. interop issues), use the publish workflow from [Building & Packaging](building-and-packaging) and attach to the exe — `DebugType` is `embedded`, so symbols are inside.

## Child pages

- [Architecture](architecture)
- [Building & Packaging](building-and-packaging)
- [Contributing](contributing)
