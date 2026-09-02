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
     2.9.0     Dokan|WinFsp     x64|arm64
```

| Package | Contents |
|---|---|
| `…_Dokan_win-x64.zip` | `SimpleZipDrive.exe`, `7z.dll`, `7z_arm64.dll`, `ReadMe.md`, `LICENSE.txt`, `WhatsNew.md` |
| `…_Dokan_win-arm64.zip` | same layout |
| `…_WinFsp_win-x64.zip` | `SimpleZipDrive_WinFsp.exe`, **`winfsp-msil.dll`**, `7z.dll`, `7z_arm64.dll`, docs |
| `…_WinFsp_win-arm64.zip` | same layout |

All are **framework-dependent single-file** executables (~4–5 MB): the .NET Desktop Runtime and the filesystem driver are the only prerequisites.

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

Then zip each output folder together with `ReadMe.md`, `LICENSE.txt`, `WhatsNew.md` using the naming convention above.

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

1. Bump `<Version>`/`<AssemblyVersion>`/`<FileVersion>` to the new version in **all five** `.csproj` files.
2. Update `WhatsNew.md` (user-facing Fixed/Changed/Internal sections).
3. `dotnet test SimpleZipDrive.Tests -c Release` — green.
4. Publish the four artifacts (commands above).
5. Smoke-test each variant: mount a stored ZIP and a compressed archive through the *packaged* exe (both a drive letter and a folder; elevated *and* non-elevated for WinFsp).
6. Create the four zips with the exact naming convention.
7. Tag `release_<version>` and push the tag.
8. `gh release create release_<version> --title "<version>" --notes-file … *.zip` (GitHub CLI, full release — not draft/prerelease).
9. Inform issue reporters whose bugs the release fixes.

There is no CI/CD — releases are produced locally with the exact commands above.
