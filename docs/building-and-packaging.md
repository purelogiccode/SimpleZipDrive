---
title: Building & Packaging
permalink: /building-and-packaging
parent: Development
nav_order: 19
---

# Building & Packaging

## Release artifacts

Every release ships **six** zip packages - one per supported runtime identifier:

```
release_<version>_<rid>.zip
     3.1.0     win-x64 | win-arm64 | linux-x64 | linux-arm64 | osx-x64 | osx-arm64
```

| Package | Contents |
|---|---|
| `._win-x64.zip` | `SimpleZipDrive.exe`, `7za.exe`, `7zip-license.txt`, `winfsp-msil.dll`, Avalonia native libraries (`libSkiaSharp.dll`, `libHarfBuzzSharp.dll`, `av_libglesv2.dll`), `ReadMe.md`, `LICENSE.txt`, `WhatsNew.md` |
| `._win-arm64.zip` | same layout (ARM64 `7za.exe`) |
| `._linux-x64.zip` / `._linux-arm64.zip` | `SimpleZipDrive` (apphost), `7zzs`, `7zip-license.txt`, `libSkiaSharp.so`, `libHarfBuzzSharp.so`, docs |
| `._osx-x64.zip` / `._osx-arm64.zip` | `SimpleZipDrive`, universal `7zz`, `7zip-license.txt`, `libSkiaSharp.dylib`, `libHarfBuzzSharp.dylib`, docs |

All are **framework-dependent single-file** executables: the [.NET 10 runtime](https://dotnet.microsoft.com/download) and the filesystem driver (WinFsp/Dokan on Windows, libfuse3/macFUSE on Linux/macOS) are the only prerequisites.

## Automated builds (GitHub Actions)

Three workflows live in `.github/workflows`:

| Workflow | Trigger | What it does |
|---|---|---|
| `ci.yml` | Every push/PR to `master` | Builds and tests the solution on Windows and compiles the app on Ubuntu and macOS |
| `release.yml` | `workflow_dispatch` (version input) or a `release_*` tag push | Verifies the csproj version matches, builds + tests on Windows, publishes and packages the six bundles on Windows/Linux/macOS runners, uploads them as `release-bundles-*` artifacts, then **waits for approval in the protected `release` environment** before creating/updating the GitHub release |
| `wiki-sync.yml` | Push to `master` touching `docs/**` (or manually) | Mirrors `docs/*.md` into the repository wiki (`index.md` → `Home.md`) |

### Reviewing a release before it is published

1. Run **Release** from the Actions tab (or push a `release_x.y.z` tag).
2. When the *Build bundles* jobs finish, download the **release-bundles-*** artifacts from the run summary - the six `release_<version>_<rid>.zip` files. The run summary lists sizes and SHA256 checksums, and nothing is public yet.
3. Inspect the zips and smoke-test at least one packaged executable per OS.
4. Approve the waiting **Publish release** job in the `release` environment (Settings → Environments → `release` → required reviewer). The workflow then creates the GitHub release with the six bundles attached and the matching `WhatsNew.md` section as the release notes.

If the bundles are wrong, do **not** approve: cancel the run, fix, and re-run.

> **First-time setup:** the `release` environment needs at least one required reviewer, and the wiki sync needs a `WIKI_TOKEN` repository secret (a PAT with repository access - `GITHUB_TOKEN` cannot push to the wiki repository). Both are already configured for this repository.

### Local packaging

`scripts/package-release.ps1` performs the same publish/package steps locally:

```powershell
# All six targets (any host can build any runtime identifier)
.\scripts\package-release.ps1 -Version 3.1.0

# Only the targets for one OS
.\scripts\package-release.ps1 -Version 3.1.0 -RuntimeIdentifiers win-x64,win-arm64
```

It runs the test suite first (pass `-SkipTests` to skip), publishes the requested runtime identifiers, and writes the bundles into `SimpleZipDrive\bin\Release` next to the historical releases. On Linux/macOS the script uses the `zip` CLI so the apphost and the 7-Zip binary keep their executable bit; on Windows it writes the zip through `System.IO.Compression`, records the Unix executable bit (external attributes) explicitly for Linux/macOS bundles, and patches the *version-made-by* host byte of every real central-directory header to Unix (located through the end-of-central-directory record, never by scanning for the signature - compressed data can contain the same bytes) - so **all six bundles can be produced from any host** (CI still builds each OS on its matching runner).

> **`SimpleZipDrive\bin\Release` is append-only - never delete files in it.** It holds locally
> produced bundles (and may hold historical ones), and they are intentionally not tracked by
> git (`.gitignore` ignores `bin/`), so a deleted bundle is gone permanently. Never run
> `Remove-Item -Recurse`, `git clean`, `rm -rf` or any "clear output directory" step against
> that path. Packaging may only overwrite the exact bundle file being regenerated
> (`release_<version>_<rid>.zip`); every other file must stay untouched. `scripts/package-release.ps1`
> follows this rule - keep it that way when editing the script.

## Publish commands

> **Important:** the `.csproj` contains `<SelfContained>true</SelfContained>`, but releases are built **framework-dependent** - the publish command must override it with `--self-contained false`. Publishing without the override produces huge self-contained bundles that also break the packaging assumptions documented below.

```powershell
dotnet publish SimpleZipDrive\SimpleZipDrive.csproj -c Release -r win-x64    --self-contained false -o out\win-x64
dotnet publish SimpleZipDrive\SimpleZipDrive.csproj -c Release -r win-arm64  --self-contained false -o out\win-arm64
dotnet publish SimpleZipDrive\SimpleZipDrive.csproj -c Release -r linux-x64  --self-contained false -o out\linux-x64
dotnet publish SimpleZipDrive\SimpleZipDrive.csproj -c Release -r linux-arm64 --self-contained false -o out\linux-arm64
dotnet publish SimpleZipDrive\SimpleZipDrive.csproj -c Release -r osx-x64    --self-contained false -o out\osx-x64
dotnet publish SimpleZipDrive\SimpleZipDrive.csproj -c Release -r osx-arm64  --self-contained false -o out\osx-arm64
```

When packaging by hand, include the single-file executable, every file in the publish root except PDBs, and `ReadMe.md`/`LICENSE.txt`/`WhatsNew.md`. The support files in the publish root (the platform 7-Zip binary and its `7zip-license.txt`, Avalonia's Skia/HarfBuzz/ANGLE libraries, and on Windows `winfsp-msil.dll`) must ship loose beside the executable.

## Packaging internals

Three constraints make the file layout non-negotiable:

1. **`winfsp-msil.dll` must stay a real file beside the exe.** Its static initializer (`Fsp.Interop.Api.CheckVersion`) calls `FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location)`; `Assembly.Location` is an empty string inside a single-file bundle, so `Path.GetFullPath("")` throws and **every WinFsp mount dies with "The path is empty (Parameter 'path')"** before the driver is ever contacted. The csproj contains a target that runs before the bundler computes its file list:

   ```xml
   <Target Name="KeepWinFspInteropOutOfBundle" BeforeTargets="_ComputeFilesToBundle">
       <ItemGroup>
           <ResolvedFileToPublish Update="@(ResolvedFileToPublish)"
               Condition="'%(ResolvedFileToPublish.Filename)%(ResolvedFileToPublish.Extension)' == 'winfsp-msil.dll'"
               ExcludeFromSingleFile="true" />
       </ItemGroup>
   </Target>
   ```

   Timing matters: `AfterTargets="ComputeFilesToPublish"` / `BeforeTargets="BundleFiles"` do **not** work - the SDK splits bundled/non-bundled files in `_ComputeFilesToBundle`.

2. **The 7-Zip fallback binary must stay a real file beside the exe on every platform.** `SevenZipFallback` probes `AppContext.BaseDirectory` for `7za.exe` (Windows), `7zzs` (Linux) or `7zz` (macOS); the csproj copies exactly the file matching the publish `RuntimeIdentifier` and links it under its canonical name. Bundling it into the single file would make the fallback silently unavailable. On Unix the app sets the executable bit at runtime, and the release bundles preserve the bit through the `zip` CLI. The Unix binaries are committed with mode `100755` (`git update-index --chmod=+x`) so a publish from Linux/macOS keeps them executable.

   `7zip-license.txt` must ship beside them: it contains the Windows (`7za.exe`) and Linux/macOS (`7zz`, `7zzs`) license texts, since the Windows and Unix packages carry different notices. The Unix binaries are committed with mode `100755`, and the packaging script restores that mode in the zip even when the bundle is written on Windows (external attributes plus the central-directory host byte).

3. **winfsp.net stays at 2.1.x** (`2.1.25156`). Interop 2.2.x rejects the stable native 2.1 driver (*"incorrect dll version (need 2.2, have 2.1)"*); interop 2.1 accepts both the 2.1 stable driver and 2.2+ betas. Version gates live in `WinFspMountService` (`RequiredWinFspVersion = 2.1`).

## Release process checklist

1. Bump `<AssemblyVersion>`/`<FileVersion>` to the new version in `SimpleZipDrive.csproj` and `SimpleZipDrive.Tests.csproj` (there is no explicit `<Version>` property; `AssemblyVersion` drives the published version).
2. Update `WhatsNew.md` (user-facing Added/Fixed/Changed/Internal sections) - the matching `## <version>` section becomes the GitHub release notes automatically.
3. Push to `master` - the **CI** workflow must be green.
4. Run **Release** from the Actions tab with the version, or push a `release_<version>` tag. The workflow verifies the csproj version, runs the tests again, and builds the six bundles.
5. Download the **release-bundles-*** artifacts and **review the zips before approving** - the run summary lists sizes and SHA256 checksums.
6. Smoke-test a downloaded bundle on each OS: mount a stored ZIP, a compressed archive, a `.zar` container, and an Xbox `.iso`/`.cso` image through the *packaged* executable (both a drive letter and a folder; elevated *and* non-elevated for WinFsp; a folder mount on Linux/macOS).
7. Approve the **Publish release** job in the `release` environment. The workflow creates the GitHub release with the six bundles attached (full release - not draft/prerelease).
8. Inform issue reporters whose bugs the release fixes.

Bundles can also be produced locally with `scripts/package-release.ps1 -Version <version>` and uploaded by hand with `gh release create release_<version> --title "<version>" --notes-file . *.zip`, but the Actions path is the supported one.
