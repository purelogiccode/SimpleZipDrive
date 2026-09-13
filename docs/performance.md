---
title: Performance
permalink: /performance
parent: Deep Dives
nav_order: 9
---

# Performance

## What to expect

Indicative numbers from the 2.9.0 test machine (Ryzen-class desktop, NVMe SSD, native driver, 444 MB ZIP containing a 424 MB executable):

| Scenario | Result |
|---|---|
| Mount (central-directory parse) | ~1–2 s |
| First open, **stored** ZIP entry (zero-copy path) | **500–560 MB/s** sustained read throughput |
| First open, **deflated** entry (memory-cache path) | ~2 s total (decompress + first read ≈ 200+ MB/s effective) |
| Re-open of an entry still in the memory cache | ≈ 0 ms (buffer reuse, no decompression) |
| Peak working set, 424 MB entry first open | ~460 MB (≈ app base + one copy of the data) |

Your results depend on: archive compression level, storage speed of the *archive file*, driver version, and antivirus interference.

## Why reads are fast

- **Stored ZIP entries bypass decompression entirely** — data is streamed straight from the archive with positional I/O and 4 MB read-ahead ([Caching](caching#1-zero-copy-path-for-stored-zip-entries)).
- **Seekable formats decode only what a read touches** — `.zar` (Zstd seekable) and Xbox `.cso` images decompress just the blocks they touch, and plain Xbox `.iso` files are read directly from disc sectors ([Archive Support](archive-support#supported-formats)).
- **Decompressed entries are cached once and shared** — repeated opens (typical game launchers re-reading the same file) never re-decompress.
- **Large entries extract once** to a local file, after which the OS file cache takes over.

## Measuring it yourself: FileBenchmark

The repository includes a console tool, `FileBenchmark`, that measures cold-file I/O with the Windows standby list purged between runs (requires admin for the purge; it warns and continues warm otherwise):

```text
FileBenchmark <filepath> [--no-clear]
```

It runs three measurements and appends to `result.txt`:

1. **Raw sequential read** — unbuffered I/O with a 4 MB buffer (the storage ceiling).
2. **XXH3 sequential hash** — via the bundled `xxhsum.exe`.
3. **XXH3 random access** — 1024 random 4 KB offsets (realistic for paging I/O).

To benchmark a *mounted* drive, mount the archive first and run `FileBenchmark M:\file.bin`. Comparing the raw read of the same file on a real drive vs. through the mount isolates the filesystem overhead.

## Tuning tips

- **Choose stored (no compression) ZIPs** for executables and media you mount often — the zero-copy path is dramatically faster and lighter than decompression.
- **Driver version matters.** A user-reported slowdown on a 15 GB stored-zip hash check was driver-dependent; the app itself measured parity between releases. Keep Dokan/WinFsp current and report your driver version with any performance issue (see [Logging](logging#reporting-bugs)).
- **Antivirus real-time scanning** of the mounted volume adds latency; exclusions for the mount letter are a common fix.
- **Memory limit:** if Task Manager shows heavy usage on huge files, reduce `MaxMemoryPerFileMb` so big entries go to the disk cache instead of RAM ([Configuration](configuration#settings-reference)).
- **Keep the app running** between launches — the memory cache stays warm; unmounting clears it.

## Known bottlenecks

- First access to a *compressed* entry always pays one decompression (CPU-bound, typically 100–400 MB/s depending on the method). Block-seekable sources (`.zar`, `.cso`) are the exception — they decode per touched block instead of decompressing the whole entry.
- Very large numbers of entries (100k+) lengthen the central-directory parse at mount time.
- Random access into *deflated* entries requires re-decompression from the buffer — the memory cache makes this cheap, the disk cache bounds it.
