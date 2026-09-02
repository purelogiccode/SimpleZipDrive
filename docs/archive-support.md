---
title: Archive Support
permalink: /archive-support
parent: Deep Dives
nav_order: 10
---

# Archive Support

## Supported formats

Detected by file extension (`ArchiveFormats`):

| Extension | Format |
|---|---|
| `.zip` | ZIP |
| `.7z` | 7-Zip |
| `.rar` | RAR |
| `.tar` | TAR |
| `.tar.gz`, `.tgz` | GZIP-compressed TAR |
| `.tar.bz2`, `.tbz2` | BZIP2-compressed TAR |
| `.tar.xz`, `.txz` | XZ-compressed TAR |
| `.cbz`, `.cbr`, `.cb7` | Comic-book archives (ZIP/RAR/7Z containers) |

Everything else is rejected with *"The file 'X' is not a supported archive"* and the list of expected extensions. The mount is **read-only** — archives are never modified.

## Decompression paths

| Situation | Path |
|---|---|
| ZIP **stored** entry (no compression, not encrypted/solid) | Zero-copy direct read — no decompression, no cache ([details](caching#1-zero-copy-path-for-stored-zip-entries)) |
| Compressed entry ≤ per-file RAM limit (512 MB default) | Decompress once into the shared memory cache |
| Compressed entry above the limit | Extract once to the disk cache |
| SharpCompress fails to decompress | **7-Zip fallback** (below) |

## Password-protected archives

- Encryption is detected by entry flags plus a **test read of up to 1 KB** per encrypted entry (some ZIP tools set the encryption flag incorrectly, so the app verifies instead of trusting the flag).
- A verified-encrypted archive shows the **Password Required** dialog before mounting.
- The password is verified by reading 1 KB from *every* encrypted entry — a wrong password is caught at mount time, not mid-file.
- **3 attempts maximum**, then *"Mount aborted after 3 attempts."* Cancelling the dialog cancels the mount.
- Corrupt archives are deliberately **not** treated as password problems: a truncated/corrupt RAR surfaces as a corruption error instead of looping the password dialog (fixed in 2.9.0).
- The password is kept only for the duration of the mount session and cleared after use.

## Corrupt or unsupported archives

- Unparseable archives: *"The archive file appears to be corrupted, incomplete, or uses an unsupported format/feature that could not be parsed."*
- Corruption found while enumerating entries: *"Archive data corruption detected during initialization."* — mounting aborts.
- Individual entries that fail to decompress are marked **failed** and return read errors instead of poisoning the whole mount; the log names the entry and the failure.

## The 7-Zip fallback

When SharpCompress (the primary extraction library) fails to decompress an entry, SimpleZipDrive retries with the **7-Zip** engine via `SharpSevenZip`:

- Requires the native `7z.dll` (x64) or `7z_arm64.dll` (ARM64) **beside the executable** — both ship in every release package, one process-appropriate library is selected automatically.
- Only available when the archive is a real file (not a pipe) so the library can open it by path.
- The fallback receives the same password (if any) as the primary path.
- If the fallback also fails: *"SevenZip fallback also failed for '…'"* and the entry is marked failed.

> Packaging note: the fallback libraries must stay next to the `.exe`. Bundling them *inside* a single-file executable made them invisible to the library-path probe in older releases — fixed in 2.9.0 ([Building & Packaging](building-and-packaging#packaging-internals)).

## Practical notes

- **ZIP64** archives (over 4 GB / 65 535 entries) are handled by SharpCompress.
- **Solid archives** (common in 7Z/RAR) cannot use the zero-copy path; reads decompress through the cache tiers, so first-access cost is higher.
- Multi-volume/split archives are not supported.
- For best performance with large game images, store them as **uncompressed ZIP** (see [Performance](performance)).
