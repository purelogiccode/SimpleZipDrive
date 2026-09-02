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
| OS | Windows 10/11 | WPF + Dokan/WinFsp |
| Drivers (to *run*) | Dokan v2 and/or WinFsp ≥ 2.1 | Only needed for manual mount testing; unit tests do not need drivers |
| IDE | Visual Studio 2022+ or Rider | Solution: `CSharp_SimpleZipDrive.sln` |

## Build and run

```powershell
dotnet build CSharp_SimpleZipDrive.sln -c Release
dotnet run --project SimpleZipDrive          # Dokan variant
dotnet run --project SimpleZipDrive_WinFsp   # WinFsp variant
```

Debug runs mount exactly like packaged builds (in-process driver hosting). F5 in the IDE works as usual; the log pane and session log show the same diagnostics as release builds.

## Tests

```powershell
dotnet test SimpleZipDrive.Tests -c Release
```

- **xUnit 2.9.3**, ~919 facts + 54 theories (~1200 cases), coverlet collector included.
- Layout mirrors production: root classes cover the Dokan variant + Core; `WinFsp\` contains parallel `WinFsp*`-prefixed classes for the WinFsp variant; `Fakes\` provides `FakeDokanFileInfo`, `FakeUserNotificationService`, `MockBugReport`.
- Areas: filesystem core, memory-cache behaviour, error handling, streams/read-ahead, settings, services, update checker (asserts the canonical GitHub endpoint), logging/error filtering, administrator checks, archive formats.
- **Known flaky test:** `AppSettingsAdditionalTests.Save_WritesValidJson` writes the *real* `%LOCALAPPDATA%\SimpleZipDrive\settings.dat` and can race with parallel test classes; it passes in isolation. (A fix should point `AppSettings` at a temp path under test.)

## Code conventions

- **Language level:** default C# for net10.0 — file-scoped namespaces, collection expressions, target-typed `new`, `required` members, `System.Threading.Lock` for lock objects.
- **Analyzers:** Meziantou.Analyzer + the three Roslynator packs run on every project; the build is warning-clean (a few `MA` rules relaxed in `.editorconfig`). Keep it that way.
- **Structure:** shared code lives in `SimpleZipDrive.Core` (`Models\`, `Interfaces\`, `Services\`, `Logging\`, `Views\`); the two app projects stay thin (UI + driver glue only). New functionality goes into Core with an interface if the variants need to differ.
- **Tests:** every new Core/service behaviour gets tests in the existing classes; driver-specific behaviour gets mirrored into the `WinFsp\` classes where relevant.
- `References\` is vendored reference material (other zipfs implementations) — excluded from compilation; do not `#include` from it.

## Debugging tips

- The session log (`%LOCALAPPDATA%\SimpleZipDrive\Temp\Logs\debug_*.log`) is verbose — reproduce, then read.
- WinFsp mounts also write a native driver log per attempt (`winfsp_debug_*.log`).
- To debug a packaged single-file build (e.g. interop issues), use the publish workflow from [Building & Packaging](building-and-packaging) and attach to the exe — `DebugType` is `embedded`, so symbols are inside.

## Child pages

- [Architecture](architecture)
- [Building & Packaging](building-and-packaging)
- [Contributing](contributing)
