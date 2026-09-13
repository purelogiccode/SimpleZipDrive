---
title: Building & Packaging
permalink: /building-and-packaging
parent: Development
nav_order: 19
---

# Building & Packaging

## Release artifacts

Every release ships **four** zip packages:

```
release_<version>_<Variant>_win-<arch>.zip
     3.0.0     Dokan|WinFsp     x64|arm64
```

| Package | Contents |
|---|---|
| `…_Dokan_win-x64.zip` | `SimpleZipDrive.exe`, `7z.dll`, `7z_arm64.dll`, `ReadMe.md`, `LICENSE.txt`, `WhatsNew.md` |
| `…_Dokan_win-arm64.zip` | same layout |
| `…_WinFsp_win-x64.zip` | `SimpleZipDrive_WinFsp.exe`, **`winfsp-msil.dll`**, `7z.dll`, `7z_arm64.dll`, docs |
| `…_WinFsp_win-arm64.zip` | same layout |

All are **framework-dependent single-file** executables (~4–5 MB): the .NET Desktop Runtime and the filesystem driver are the only prerequisites.

## Automated builds (GitHub Actions)

Three workflows live in `.github/workflows`:

| Workflow | Trigger | What it does |
|---|---|---|
| `ci.yml` | Every push/PR to `master` | Restores, builds the solution in Release (analyzer-warning-clean), runs the full test suite, uploads the `.trx` results |
| `release.yml` | `workflow_dispatch` (version input) or a `release_*` tag push | Verifies the csproj version matches, builds + tests, publishes and packages the four bundles, uploads them as the `release-bundles` artifact, then **waits for approval in the protected `release` environment** before creating/updating the GitHub release |
| `wiki-sync.yml` | Push to `master` touching `docs/**` (or manually) | Mirrors `docs/*.md` into the repository wiki (`index.md` → `Home.md`) |

### Reviewing a release before it is published

1. Run **Release** from the Actions tab (or push a `release_x.y.z` tag).
2. When the *Build bundles* job finishes, download the **release-bundles** artifact from the run summary — the four `release_<version>_<Variant>_win-<arch>.zip` files plus `release-notes.md`. The run summary lists sizes and SHA256 checksums, and nothing is public yet.
3. Inspect the zips and smoke-test at least one packaged exe.
4. Approve the waiting **Publish release** job in the `release` environment (Settings → Environments → `release` → required reviewer). The workflow then creates the GitHub release with the four bundles attached and the matching `WhatsNew.md` section as the release notes.

If the bundles are wrong, do **not** approve: cancel the run, fix, and re-run.

> **First-time setup:** the `release` environment needs at least one required reviewer, and the wiki sync needs a `WIKI_TOKEN` repository secret (a PAT with repository access — `GITHUB_TOKEN` cannot push to the wiki repository). Both are already configured for this repository.

### Local packaging

`scripts/package-release.ps1` performs the same publish/package steps locally:

```powershell
.\scripts\package-release.ps1 -Version 3.0.0
```

It runs the test suite first (pass `-SkipTests` to skip), publishes all four variant/RID combinations, and writes the bundles into `SimpleZipDrive\bin\Release` next to the historical releases. Existing files in that folder are never deleted; only the four bundles for the requested version are written (or overwritten).

## Publish commands

> **Important:** the `.csproj` files contain `<SelfContained>true</SelfContained>`, but releases are built **framework-dependent** — the publish command must override it with `--self-contained false`. Publishing without the override produces huge self-contained bundles that also break the packaging assumptions documented below.

```powershell
# Dokan variant
dotnet publish SimpleZipDrive\SimpleZipDrive.csproj -c Release -r win-x64   --self-contained false -o out\Dokan_x64
dotnet publish SimpleZipDrive\SimpleZipDrive.csproj -c Release -r win-arm64 --self-contained false -o out\Dokan_arm64

# WinFsp variant
dotnet publish SimpleZipDrive_WinFsp\SimpleZipDrive_WinFsp.csproj -c Release -r win-x64   --self-contained false -o out\WinFsp_x64
dotnet publish SimpleZipDrive_WinFsp\SimpleZipDrive_WinFsp.csproj -c Release -r win-arm64 --self-contained false -o out\WinFsp_arm64
```

When packaging by hand, include only the single-file exe, the native runtime files from the publish root (`7z.dll`, `7z_arm64.dll`, and `winfsp-msil.dll` for WinFsp), and `ReadMe.md`/`LICENSE.txt`/`WhatsNew.md` — not the package-provided `x64\`/`x86\` 7z copies that also land in the publish output.

## Packaging internals

Three constraints make the file layout non-negotiable:

1. **`winfsp-msil.dll` must stay a real file beside the exe** (WinFsp variant). Its static initializer (`Fsp.Interop.Api.CheckVersion`) calls `FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location)`; `Assembly.Location` is an empty string inside a single-file bundle, so `Path.GetFullPath("")` throws and **every mount dies with "The path is empty (Parameter 'path')"** before the driver is ever contacted. The csproj contains a target that runs before the bundler computes its file list:

   ```xml
   <Target Name="KeepWinFspInteropOutOfBundle" BeforeTargets="_ComputeFilesToBundle">
       <ItemGroup>
           <ResolvedFileToPublish Update="@(ResolvedFileToPublish)"
               Condition="'%(ResolvedFileToPublish.Filename)%(ResolvedFileToPublish.Extension)' == 'winfsp-msil.dll'"
               ExcludeFromSingleFile="true" />
       </ItemGroup>
   </Target>
   ```

   Timing matters: `AfterTargets="ComputeFilesToPublish"` / `BeforeTargets="BundleFiles"` do **not** work — the SDK splits bundled/non-bundled files in `_ComputeFilesToBundle`.

2. **`7z.dll` / `7z_arm64.dll` must stay real files beside the exe** (both variants). `SevenZipFallback` probes `AppContext.BaseDirectory` for the library matching the process architecture (`SharpSevenZipBase.SetLibraryPath`); bundling them into the exe makes the fallback silently unavailable. Both ship in every package so one zip works on x64 and ARM64.

3. **winfsp.net stays at 2.1.x** (`2.1.25156`). Interop 2.2.x rejects the stable native 2.1 driver (*"incorrect dll version (need 2.2, have 2.1)"*); interop 2.1 accepts both the 2.1 stable driver and 2.2+ betas. Version gates live in `MountService` (`RequiredWinFspVersion = 2.1`).

## Release process checklist

1. Bump `<AssemblyVersion>`/`<FileVersion>` to the new version in **all five** `.csproj` files (there is no explicit `<Version>` property; `AssemblyVersion` drives the published version).
2. Update `WhatsNew.md` (user-facing Added/Fixed/Changed/Internal sections) — the matching `## <version>` section becomes the GitHub release notes automatically.
3. Push to `master` — the **CI** workflow must be green.
4. Run **Release** from the Actions tab with the version, or push a `release_<version>` tag. The workflow verifies the csproj version, runs the tests again, and builds the four bundles.
5. Download the **release-bundles** artifact and **review the zips before approving** — the run summary lists sizes and SHA256 checksums.
6. Smoke-test a downloaded bundle: mount a stored ZIP, a compressed archive, a `.zar` container, and an Xbox `.iso`/`.cso` image through the *packaged* exe (both a drive letter and a folder; elevated *and* non-elevated for WinFsp).
7. Approve the **Publish release** job in the `release` environment. The workflow creates the GitHub release with the four bundles attached (full release — not draft/prerelease).
8. Inform issue reporters whose bugs the release fixes.

Bundles can also be produced locally with `scripts/package-release.ps1 -Version <version>` and uploaded by hand with `gh release create release_<version> --title "<version>" --notes-file … *.zip`, but the Actions path is the supported one.
