# Repository Instructions

## Release bundles are append-only - NEVER delete files

`SimpleZipDrive/bin/Release` holds locally produced release bundles (and may hold historical
ones). Treat this folder and everything inside it as append-only:

- **Never delete, clean, wipe, move or recursively remove anything inside
  `SimpleZipDrive\bin\Release`.**
- No `Remove-Item -Recurse`, `git clean`, `rm -rf`, "clear output directory" step or
  equivalent may target that path or its contents.
- Producing or refreshing a bundle may only overwrite the exact bundle file being
  regenerated (`release_<version>_<rid>.zip`); every other file must stay untouched.
- `scripts/package-release.ps1` already follows this rule (it never cleans the output
  directory). Keep it that way when editing the script.
- The bundles are intentionally not tracked by git (`.gitignore` ignores `bin/`), so a
  deleted bundle is gone permanently.

## Build and test

- Rebuild (must stay at 0 analyzer warnings):
  `dotnet build "CSharp_SimpleZipDrive.sln" -c Release --nologo -t:Rebuild`
- Tests:
  `dotnet test "SimpleZipDrive.Tests\SimpleZipDrive.Tests.csproj" -c Release --no-build --nologo`
  (1430 tests, 0 warnings expected)
- Framework-dependent publish for a bundle (never self-contained):
  `dotnet publish SimpleZipDrive\SimpleZipDrive.csproj -c Release -r <rid> --self-contained false -o <dir>`

## Documentation

- `docs/` is published both to the GitHub wiki (side menu: `docs/_Sidebar.md`) and as a
  GitHub Pages site (just-the-docs front matter; `docs/_config.yml`). Keep both in sync
  when adding pages.
- User-visible changes belong in `WhatsNew.md` under the matching `## <version>` section;
  it becomes the GitHub release notes.
