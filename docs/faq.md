---
title: FAQ
permalink: /faq
parent: Operations
nav_order: 16
---

# FAQ

**Is SimpleZipDrive free?**
Yes — GPL v3 open source. The [license](https://github.com/purelogiccode/SimpleZipDrive/blob/master/LICENSE.txt) covers all releases.

**Does it modify my archives?**
No. Mounted volumes are strictly read-only; the archive file is never written to.

**Can I write files to the mounted drive?**
No — the volume is read-only by design. Copy *out* as much as you like.

**Where did my drive go after I closed the window?**
Closing the window unmounts the drive. Keep the app running while you use the mount.

**Can I mount more than one archive?**
One archive per app instance. Launch a second instance to mount another archive (each instance occupies its own mount point).

**Which variant should I use?**
Dokan for the mature driver; WinFsp if you need cross-integrity mounting or folder mounts on fresh directories. See [Variants](variants).

**Do I need administrator rights?**
No. The apps run unelevated (`asInvoker`). The WinFsp variant *detects* elevation when you choose to run as admin and switches to cross-integrity folder mounts so both integrity levels can see the mount.

**Why drive letters M–Q?**
A reserved pool that rarely collides with real drives. You can request any letter explicitly on the command line, or mount to a folder instead.

**Where does extracted data go?**
`%LOCALAPPDATA%\SimpleZipDrive\Temp\<pid>_<guid>\` for the session's disk cache — deleted automatically on unmount/exit and orphan-swept at startup. See [Configuration](configuration#data-locations).

**How much RAM does it use?**
Roughly *app base + largest cached entry*. Per-entry caching is capped by `MaxMemoryPerFileMb` (512 MB default, clamped to 90 % of installed RAM); oversized entries go to the disk cache instead. See [Caching](caching).

**Why does Task Manager show less than "file size × copies opened"?**
Buffers are shared: opening the same file repeatedly adds no memory after the first decompression. Since 2.9.0 even the first decompression peaks at one copy of the data (previously two).

**Does it support ZIP64 / >4 GB archives?**
Yes, via SharpCompress. Very large *entries* (over the per-file RAM limit) are handled by the disk cache.

**Does it support split/multi-volume archives?**
Split ZIP/7Z/RAR sets: no. Split CISO Xbox images (`.1.cso`, `.2.cso`, …): yes.

**Does it mount `.iso`/`.vhd`/`.exe` installers?**
Xbox XDVDFS images (`.iso`, `.xiso`) and compressed CISO images (`.cso`) mount read-only like any other archive. Generic PC ISO 9660/UDF images, `.vhd`, and `.exe` installers are not supported — a non-Xbox `.iso` is rejected with *"The file is not a valid Xbox XISO disc image"*.

**What is a `.zar` file?**
A Zstd-seekable archive container (ZArchive) used mainly by the Xbox 360 archival scene. Reads decompress only the blocks they touch, so large `.zar` archives seek instantly with no temporary extraction. See [Archive Support](archive-support#supported-formats).

**Antivirus flags the app or the mount is slow — related?**
Real-time scanning can slow mounted reads substantially; excluding the mount letter or the app folder is a common remedy. Only download binaries from official [GitHub releases](https://github.com/purelogiccode/SimpleZipDrive/releases).

**Is any of my data sent anywhere?**
Crash/warning reports (filtered), one startup statistics ping (`applicationId`, `version`), and one update check. No file paths, no contents. Details and opt-out in [Security & Privacy](security#network-communication).

**Where are the logs?**
`%LOCALAPPDATA%\SimpleZipDrive\Temp\Logs\` — see [Logging](logging).

**How do I report a bug?**
Automatic reporting already covers real defects; for anything else open an issue with the session log attached ([Logging](logging#reporting-a-bug-manually)).
