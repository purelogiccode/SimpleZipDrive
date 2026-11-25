<#
.SYNOPSIS
    Builds the SimpleZipDrive release bundles for Windows, Linux and macOS.

.DESCRIPTION
    Publishes the single Avalonia application as a framework-dependent single-file
    executable for each requested runtime identifier and packages it with ReadMe.md,
    LICENSE.txt and WhatsNew.md.

    Bundle formats follow the platform:
      - Windows: release_<version>_<rid>.zip
      - Linux:   release_<version>_<rid>.tar.gz (framework-dependent) plus
                 release_<version>_<rid>.deb and .AppImage (self-contained, so the
                 Linux installer formats do not require a separate .NET runtime)
      - macOS:   release_<version>_<rid>.dmg on a macOS host, otherwise a .tar.gz
                 fallback (a DMG can only be produced by hdiutil on macOS).

    The .deb package is assembled on every host (ar + tar are written through .NET).
    AppImage requires a Linux host with appimagetool; when it is missing the format is
    skipped unless -RequireAllLinuxFormats is set, which turns the skip into an error
    (used by the release workflow).

    Every bundle contains the 7-Zip command-line fallback binary for its platform
    (7za.exe on Windows, 7zzs on Linux, 7zz on macOS) and the 7-Zip license text.
    Windows bundles additionally contain the loose winfsp-msil.dll interop assembly;
    FUSE (libfuse3 / macFUSE) is resolved from the host system at runtime.

    ZIP bundles are written through System.IO.Compression on Windows (with the Unix
    executable bit recorded in external attributes) or the zip CLI on Linux/macOS.
    TAR.GZ bundles are written through System.Formats.Tar on every host and always
    preserve the Unix executable bit on the apphost and the 7-Zip binary. DMG bundles
    contain a top-level SimpleZipDrive-<version>-<rid> folder and are compressed (UDZO).

    Existing files in the output directory are never deleted; only the bundles for the
    requested version and runtime identifiers are created (or overwritten if they already
    exist). The publish/staging directory is temporary and may be cleaned.

.PARAMETER Version
    Release version, e.g. 3.0.0.

.PARAMETER OutputDirectory
    Where the bundles are written. Defaults to SimpleZipDrive\bin\Release.

.PARAMETER StagingDirectory
    Temporary publish/staging root. Defaults to a folder under the system temp directory.

.PARAMETER RuntimeIdentifiers
    Runtime identifiers to publish. Defaults to all supported targets.

.PARAMETER SkipTests
    Skips the test run that normally guards a release build (CI runs tests in a
    separate job and passes this switch).

.EXAMPLE
    .\scripts\package-release.ps1 -Version 3.0.0 -RuntimeIdentifiers win-x64,win-arm64
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Version,

    [string] $OutputDirectory,

    [string] $StagingDirectory,

    [string[]] $RuntimeIdentifiers = @('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64'),

    [ValidateSet('deb', 'appimage')]
    [string[]] $LinuxFormats = @('deb', 'appimage'),

    [switch] $RequireAllLinuxFormats,

    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'

if ($Version -notmatch '^\d+\.\d+\.\d+$')
{
    throw "Invalid version '$Version' - expected a three-part version such as 3.0.0."
}

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'SimpleZipDrive\bin\Release' }
if (-not $StagingDirectory) { $StagingDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "SimpleZipDrive-release-$Version" }

$project = Join-Path $repoRoot 'SimpleZipDrive\SimpleZipDrive.csproj'
$documents = @('ReadMe.md', 'LICENSE.txt', 'WhatsNew.md')

foreach ($document in $documents)
{
    if (-not (Test-Path -LiteralPath (Join-Path $repoRoot $document)))
    {
        throw "Required bundle file '$document' was not found in the repository root."
    }
}

# SAFETY - the release output directory is append-only: NEVER delete, clean or recursively
# remove anything inside it (historical bundles live there). Only the exact bundle file being
# generated for this version/rid may be replaced. Do not add a "clear output directory" step.
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
New-Item -ItemType Directory -Force -Path $StagingDirectory | Out-Null

if (-not $SkipTests)
{
    Write-Host 'Running tests before packaging...'
    dotnet test (Join-Path $repoRoot 'SimpleZipDrive.Tests\SimpleZipDrive.Tests.csproj') -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed - release bundles were not created.' }
}

# $IsWindows only exists in PowerShell 6+; in Windows PowerShell 5.1 it is $null and the
# host is always Windows, so a null value must be treated as a Windows host.
$isWindowsHost = $null -eq $IsWindows -or $IsWindows
$isLinuxHost = $null -ne $IsLinux -and $IsLinux
$isMacOSHost = $null -ne $IsMacOS -and $IsMacOS
$iconPath = Join-Path $repoRoot 'SimpleZipDrive\icon\zip.png'
$bundles = @()

# Writes a flat zip and (for Unix runtime identifiers) records the executable bit on the
# apphost and the 7-Zip binary through the zip external attributes. Needed when packaging on
# a Windows host, where neither Compress-Archive nor the zip CLI can preserve Unix modes.
function New-ReleaseZip
{
    param(
        [Parameter(Mandatory = $true)][string[]] $Files,
        [Parameter(Mandatory = $true)][string] $DestinationPath,
        [Parameter(Mandatory = $true)][bool] $UnixRid
    )

    # Only the exact destination bundle file is replaced; nothing else in the output
    # directory is ever touched (see the append-only safety note above).
    if (Test-Path -LiteralPath $DestinationPath) { Remove-Item -LiteralPath $DestinationPath -Force }

    Add-Type -AssemblyName System.IO.Compression | Out-Null
    Add-Type -AssemblyName System.IO.Compression.FileSystem | Out-Null

    $archive = [System.IO.Compression.ZipFile]::Open($DestinationPath, [System.IO.Compression.ZipArchiveMode]::Create)
    try
    {
        foreach ($file in $Files)
        {
            $name = [System.IO.Path]::GetFileName($file)
            $entry = $archive.CreateEntry($name, [System.IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = (Get-Item -LiteralPath $file).LastWriteTime

            # Zip stores Unix permissions in the high 16 bits of the external attributes:
            # 0100644 (0x81A4) for regular files, 0100755 (0x81ED) for executables.
            $mode = 0x81A4
            if ($UnixRid -and ($name -eq 'SimpleZipDrive' -or $name -eq '7zzs' -or $name -eq '7zz'))
            {
                $mode = 0x81ED
            }

            # Normalize the 32-bit two's-complement value explicitly (PowerShell's [int]
            # cast would overflow on the raw 0x81xx0000 value).
            $external = [int64]$mode * 0x10000
            if ($external -gt [int]::MaxValue) { $external -= 0x100000000 }
            $entry.ExternalAttributes = [int]$external

            $input = [System.IO.File]::OpenRead($file)
            try
            {
                $output = $entry.Open()
                try { $input.CopyTo($output) } finally { $output.Dispose() }
            }
            finally { $input.Dispose() }
        }
    }
    finally
    {
        $archive.Dispose()
    }

    if ($UnixRid)
    {
        # System.IO.Compression stamps every entry with "version made by = MS-DOS (0)",
        # which makes Linux/macOS extraction tools ignore the Unix external attributes
        # above. Patch the host byte of each central-directory header to Unix (3) so
        # unzip/7-Zip restore the executable bit.
        #
        # Walk the real central directory instead of scanning the file for the
        # PK\x01\x02 signature: compressed entry data can contain the same byte
        # sequence, and patching such a false positive would corrupt the bundle.
        $bytes = [System.IO.File]::ReadAllBytes($DestinationPath)

        # The end-of-central-directory record (PK\x05\x06) is at most 22 bytes plus a
        # 64 KiB comment from the end of the file; locate it from the back.
        $eocd = -1
        $searchStart = $bytes.Length - 22
        $searchEnd = [Math]::Max(0, $bytes.Length - 22 - 65535)
        for ($i = $searchStart; $i -ge $searchEnd; $i--)
        {
            if ($bytes[$i] -eq 0x50 -and $bytes[$i + 1] -eq 0x4B -and
                $bytes[$i + 2] -eq 0x05 -and $bytes[$i + 3] -eq 0x06)
            {
                $eocd = $i
                break
            }
        }

        if ($eocd -lt 0) { throw "Could not locate the zip central directory in '$DestinationPath'." }

        $entryCount = [System.BitConverter]::ToUInt16($bytes, $eocd + 10)
        $offset = [int][System.BitConverter]::ToUInt32($bytes, $eocd + 16)

        for ($entry = 0; $entry -lt $entryCount; $entry++)
        {
            if ($offset + 46 -gt $bytes.Length -or
                $bytes[$offset] -ne 0x50 -or $bytes[$offset + 1] -ne 0x4B -or
                $bytes[$offset + 2] -ne 0x01 -or $bytes[$offset + 3] -ne 0x02)
            {
                throw "Malformed zip central directory in '$DestinationPath' (entry $entry)."
            }

            $bytes[$offset + 5] = 3 # "version made by" high byte = Unix (3)

            $nameLength = [System.BitConverter]::ToUInt16($bytes, $offset + 28)
            $extraLength = [System.BitConverter]::ToUInt16($bytes, $offset + 30)
            $commentLength = [System.BitConverter]::ToUInt16($bytes, $offset + 32)
            $offset += 46 + $nameLength + $extraLength + $commentLength
        }

        [System.IO.File]::WriteAllBytes($DestinationPath, $bytes)
    }
}

# Writes a .tar.gz with a top-level directory so extracting it never scatters files into the
# current directory (a "tarbomb"). System.Formats.Tar records Unix modes on every host, so the
# apphost and the 7-Zip binary stay executable even when the bundle is produced on Windows.
function New-ReleaseTarGz
{
    param(
        [Parameter(Mandatory = $true)][string[]] $Files,
        [Parameter(Mandatory = $true)][string] $DestinationPath,
        [Parameter(Mandatory = $true)][string] $RootDirectoryName,
        [Parameter(Mandatory = $true)][bool] $UnixRid
    )

    # Only the exact destination bundle file is replaced; nothing else in the output
    # directory is ever touched (see the append-only safety note above).
    if (Test-Path -LiteralPath $DestinationPath) { Remove-Item -LiteralPath $DestinationPath -Force }

    Add-Type -AssemblyName System.Formats.Tar | Out-Null

    $fileStream = [System.IO.File]::Create($DestinationPath)
    try
    {
        $gzip = [System.IO.Compression.GZipStream]::new(
            $fileStream, [System.IO.Compression.CompressionLevel]::SmallestSize, $true)
        try
        {
            $tar = [System.Formats.Tar.TarWriter]::new(
                $gzip, [System.Formats.Tar.TarEntryFormat]::Pax, $true)
            try
            {
                foreach ($file in $Files)
                {
                    $name = [System.IO.Path]::GetFileName($file)
                    $entry = [System.Formats.Tar.PaxTarEntry]::new(
                        [System.Formats.Tar.TarEntryType]::RegularFile, "$RootDirectoryName/$name")
                    $entry.ModificationTime = (Get-Item -LiteralPath $file).LastWriteTime

                    # 0100644 for regular files, 0100755 for the apphost and the 7-Zip binary.
                    $mode = 0x1A4
                    if ($UnixRid -and ($name -eq 'SimpleZipDrive' -or $name -eq '7zzs' -or $name -eq '7zz'))
                    {
                        $mode = 0x1ED
                    }

                    $entry.Mode = [System.IO.UnixFileMode]$mode
                    $entry.DataStream = [System.IO.File]::OpenRead($file)
                    try { $tar.WriteEntry($entry) } finally { $entry.DataStream.Dispose() }
                }
            }
            finally
            {
                $tar.Dispose()
            }
        }
        finally
        {
            $gzip.Dispose()
        }
    }
    finally
    {
        $fileStream.Dispose()
    }
}

# Writes a compressed macOS disk image with a top-level bundle folder. hdiutil only exists on
# macOS, which is why a non-macOS host falls back to the portable .tar.gz format instead.
function New-ReleaseDmg
{
    param(
        [Parameter(Mandatory = $true)][string[]] $Files,
        [Parameter(Mandatory = $true)][string] $DestinationPath,
        [Parameter(Mandatory = $true)][string] $VolumeName,
        [Parameter(Mandatory = $true)][string] $FolderName,
        [Parameter(Mandatory = $true)][string] $StagingDirectory,
        [Parameter(Mandatory = $true)][string] $Rid
    )

    if (-not $isMacOSHost)
    {
        throw 'DMG bundles can only be produced on macOS (hdiutil is required).'
    }

    if (Test-Path -LiteralPath $DestinationPath) { Remove-Item -LiteralPath $DestinationPath -Force }

    $dmgRoot = Join-Path $StagingDirectory "dmg-$Rid"
    if (Test-Path -LiteralPath $dmgRoot) { Remove-Item -LiteralPath $dmgRoot -Recurse -Force }

    $bundleFolder = Join-Path $dmgRoot $FolderName
    New-Item -ItemType Directory -Force -Path $bundleFolder | Out-Null

    foreach ($file in $Files)
    {
        Copy-Item -LiteralPath $file -Destination $bundleFolder -Force
    }

    # Copying normally preserves the mode on macOS, but set it explicitly so the apphost and
    # the 7-Zip binary are executable no matter how the staging directory was created.
    foreach ($name in @('SimpleZipDrive', '7zz'))
    {
        $path = Join-Path $bundleFolder $name
        if (Test-Path -LiteralPath $path)
        {
            [System.IO.File]::SetUnixFileMode($path, [System.IO.UnixFileMode]0x1ED)
        }
    }

    & hdiutil create -volname $VolumeName -srcfolder $dmgRoot -ov -format UDZO $DestinationPath
    if ($LASTEXITCODE -ne 0) { throw "hdiutil failed to create '$DestinationPath'." }
}

# Adds one entry to a tar writer, optionally with explicit Unix permissions. Directory
# entries are declared by passing a name ending in '/'.
function Add-TarEntry
{
    param(
        [Parameter(Mandatory = $true)][System.Formats.Tar.TarWriter] $Writer,
        [Parameter(Mandatory = $true)][string] $EntryName,
        [string] $SourcePath,
        [byte[]] $Content,
        [int] $Mode = 0x1A4
    )

    if ($EntryName.EndsWith('/'))
    {
        $entry = [System.Formats.Tar.UstarTarEntry]::new([System.Formats.Tar.TarEntryType]::Directory, $EntryName)
        $entry.Mode = [System.IO.UnixFileMode]$Mode
        $Writer.WriteEntry($entry)
        return
    }

    $entry = [System.Formats.Tar.UstarTarEntry]::new([System.Formats.Tar.TarEntryType]::RegularFile, $EntryName)
    $entry.Mode = [System.IO.UnixFileMode]$Mode
    if ($null -ne $Content)
    {
        $stream = [System.IO.MemoryStream]::new($Content, $false)
        try
        {
            $entry.DataStream = $stream
            $Writer.WriteEntry($entry)
        }
        finally
        {
            $stream.Dispose()
        }
    }
    else
    {
        $entry.DataStream = [System.IO.File]::OpenRead($SourcePath)
        try { $Writer.WriteEntry($entry) } finally { $entry.DataStream.Dispose() }
    }
}

# Writes a .tar.gz from a list of entry descriptors. Used for the two members of a .deb.
function New-TarGzFromEntries
{
    param(
        [Parameter(Mandatory = $true)][object[]] $Entries,
        [Parameter(Mandatory = $true)][string] $DestinationPath
    )

    if (Test-Path -LiteralPath $DestinationPath) { Remove-Item -LiteralPath $DestinationPath -Force }

    Add-Type -AssemblyName System.Formats.Tar | Out-Null
    $fileStream = [System.IO.File]::Create($DestinationPath)
    try
    {
        $gzip = [System.IO.Compression.GZipStream]::new(
            $fileStream, [System.IO.Compression.CompressionLevel]::SmallestSize, $true)
        try
        {
            $tar = [System.Formats.Tar.TarWriter]::new(
                $gzip, [System.Formats.Tar.TarEntryFormat]::Ustar, $true)
            try
            {
                foreach ($item in $Entries)
                {
                    Add-TarEntry -Writer $tar -EntryName $item.EntryName -SourcePath $item.SourcePath `
                        -Content $item.Content -Mode $item.Mode
                }
            }
            finally { $tar.Dispose() }
        }
        finally { $gzip.Dispose() }
    }
    finally { $fileStream.Dispose() }
}

# Writes the "ar" container used by Debian packages (magic, 60-byte headers, 2-byte alignment).
function New-ArArchive
{
    param(
        [Parameter(Mandatory = $true)][object[]] $Members,
        [Parameter(Mandatory = $true)][string] $DestinationPath
    )

    if (Test-Path -LiteralPath $DestinationPath) { Remove-Item -LiteralPath $DestinationPath -Force }

    $stream = [System.IO.File]::Create($DestinationPath)
    try
    {
        $writer = [System.IO.BinaryWriter]::new($stream)
        try
        {
            $writer.Write([System.Text.Encoding]::ASCII.GetBytes("!<arch>`n"))
            $mtime = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
            foreach ($member in $Members)
            {
                if ($member.Name.Length -gt 16) { throw "ar member name '$($member.Name)' is too long." }
                $header = '{0,-16}{1,-12}{2,-6}{3,-6}{4,-8}{5,-10}' -f `
                    $member.Name, $mtime, '0', '0', ([Convert]::ToString([int]$member.Mode, 8)), $member.Bytes.Length
                $writer.Write([System.Text.Encoding]::ASCII.GetBytes($header))
                $writer.Write([byte]0x60)
                $writer.Write([byte]0x0A)
                $writer.Write([byte[]]$member.Bytes)
                if ($member.Bytes.Length % 2 -ne 0) { $writer.Write([byte]0x0A) }
            }
        }
        finally { $writer.Dispose() }
    }
    finally { $stream.Dispose() }
}

# Writes a Debian package: ar(debian-binary, control.tar.gz, data.tar.gz) with the app under
# /opt/simplezipdrive, a launcher in /usr/bin and a desktop entry + icon. Works on every host
# because ar and tar are produced in managed code.
function New-ReleaseDeb
{
    param(
        [Parameter(Mandatory = $true)][string[]] $Files,
        [Parameter(Mandatory = $true)][string] $DestinationPath,
        [Parameter(Mandatory = $true)][string] $Version,
        [Parameter(Mandatory = $true)][string] $Rid,
        [Parameter(Mandatory = $true)][string] $StagingDirectory,
        [Parameter(Mandatory = $true)][string] $IconPath
    )

    $arch = if ($Rid -eq 'linux-arm64') { 'arm64' } else { 'amd64' }
    $root = Join-Path $StagingDirectory "deb-$Rid"
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }

    $entries = [System.Collections.Generic.List[object]]::new()
    foreach ($dir in @('./', './opt/', './opt/simplezipdrive/', './usr/', './usr/bin/',
                      './usr/share/', './usr/share/applications/', './usr/share/doc/',
                      './usr/share/doc/simplezipdrive/', './usr/share/icons/',
                      './usr/share/icons/hicolor/', './usr/share/icons/hicolor/256x256/',
                      './usr/share/icons/hicolor/256x256/apps/'))
    {
        $entries.Add(@{ EntryName = $dir; Mode = 0x1ED })
    }

    $totalBytes = 0
    foreach ($file in $Files)
    {
        $name = [System.IO.Path]::GetFileName($file)
        $size = (Get-Item -LiteralPath $file).Length
        $totalBytes += $size
        $mode = if ($name -eq 'SimpleZipDrive' -or $name -eq '7zzs') { 0x1ED } else { 0x1A4 }
        $entries.Add(@{ EntryName = "./opt/simplezipdrive/$name"; SourcePath = $file; Mode = $mode })
    }

    $launcher = "#!/bin/sh`nexec /opt/simplezipdrive/SimpleZipDrive `"`$@`"`n"
    $entries.Add(@{
        EntryName = './usr/bin/simplezipdrive'
        Content   = [System.Text.Encoding]::UTF8.GetBytes($launcher)
        Mode      = 0x1ED
    })

    $desktop = New-DesktopEntry -IconName 'simplezipdrive' -Exec 'simplezipdrive'
    $entries.Add(@{
        EntryName = './usr/share/applications/simplezipdrive.desktop'
        Content   = [System.Text.Encoding]::UTF8.GetBytes($desktop)
        Mode      = 0x1A4
    })
    $entries.Add(@{ EntryName = './usr/share/icons/hicolor/256x256/apps/simplezipdrive.png'; SourcePath = $IconPath; Mode = 0x1A4 })

    foreach ($document in $documents)
    {
        $entries.Add(@{ EntryName = "./usr/share/doc/simplezipdrive/$document"; SourcePath = (Join-Path $repoRoot $document); Mode = 0x1A4 })
    }

    $control = @"
Package: simplezipdrive
Version: $Version
Section: utils
Priority: optional
Architecture: $arch
Maintainer: PureLogicCode <support@purelogiccode.com>
Installed-Size: $([math]::Ceiling($totalBytes / 1024))
Depends: libc6, libfuse3-3 | libfuse3-4, libx11-6, libxext6, libxrandr2, libxi6, libxcursor1, libxinerama1, libsm6, libice6, libfontconfig1, libfreetype6, libgl1, libegl1, libicu74 | libicu76, libssl3 | libssl3t64
Description: Mount archive files as virtual drives
 SimpleZipDrive exposes ZIP, 7Z, RAR, TAR, ZAR and Xbox XISO archives as
 read-only virtual drives or folders through FUSE on Linux.
"@

    New-Item -ItemType Directory -Force -Path $root | Out-Null
    $dataTar = Join-Path $root 'data.tar.gz'
    $controlTar = Join-Path $root 'control.tar.gz'
    $debianBinary = Join-Path $root 'debian-binary'
    New-TarGzFromEntries -Entries $entries.ToArray() -DestinationPath $dataTar
    New-TarGzFromEntries -Entries @(@{
        EntryName = './control'
        Content   = [System.Text.Encoding]::UTF8.GetBytes($control + "`n")
        Mode      = 0x1A4
    }) -DestinationPath $controlTar
    [System.IO.File]::WriteAllText($debianBinary, "2.0`n")

    New-ArArchive -Members @(
        @{ Name = 'debian-binary/'; Bytes = [System.IO.File]::ReadAllBytes($debianBinary); Mode = 0x1A4 },
        @{ Name = 'control.tar.gz/'; Bytes = [System.IO.File]::ReadAllBytes($controlTar); Mode = 0x1A4 },
        @{ Name = 'data.tar.gz/'; Bytes = [System.IO.File]::ReadAllBytes($dataTar); Mode = 0x1A4 }
    ) -DestinationPath $DestinationPath
}

# Returns a freedesktop desktop entry for the application.
function New-DesktopEntry
{
    param(
        [Parameter(Mandatory = $true)][string] $IconName,
        [Parameter(Mandatory = $true)][string] $Exec
    )

    return @"
[Desktop Entry]
Type=Application
Name=SimpleZipDrive
GenericName=Archive Mounter
Comment=Mount archives as read-only virtual drives
Exec=$Exec %F
Icon=$IconName
Terminal=false
Categories=Utility;FileTools;
MimeType=application/zip;application/x-7z-compressed;application/x-rar;application/x-tar;application/gzip;application/x-cbz;application/x-cbr;application/x-cb7;
Keywords=archive;zip;7z;rar;mount;fuse;
"@
}

# Copies the self-contained publish output into a staging AppDir used by the AppImage.
function New-LinuxAppDirectory
{
    param(
        [Parameter(Mandatory = $true)][string[]] $Files,
        [Parameter(Mandatory = $true)][string] $DestinationPath
    )

    if (Test-Path -LiteralPath $DestinationPath) { Remove-Item -LiteralPath $DestinationPath -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $DestinationPath | Out-Null
    foreach ($file in $Files)
    {
        Copy-Item -LiteralPath $file -Destination $DestinationPath -Force
    }

    return $DestinationPath
}

# Builds an AppImage. Requires appimagetool on a Linux host; the tool is downloaded from the
# official AppImage project when it is not already on PATH. APPIMAGE_EXTRACT_AND_RUN avoids
# needing FUSE (GitHub runners and containers usually do not provide it).
function New-ReleaseAppImage
{
    param(
        [Parameter(Mandatory = $true)][string[]] $Files,
        [Parameter(Mandatory = $true)][string] $DestinationPath,
        [Parameter(Mandatory = $true)][string] $Version,
        [Parameter(Mandatory = $true)][string] $Rid,
        [Parameter(Mandatory = $true)][string] $StagingDirectory,
        [Parameter(Mandatory = $true)][string] $IconPath
    )

    if (-not $isLinuxHost) { throw 'AppImage bundles can only be produced on Linux.' }

    $appArch = if ($Rid -eq 'linux-arm64') { 'aarch64' } else { 'x86_64' }
    $appDir = Join-Path $StagingDirectory "AppDir-$Rid"
    $appFilesDir = Join-Path $appDir 'usr/bin'
    New-LinuxAppDirectory -Files $Files -DestinationPath $appFilesDir | Out-Null
    [System.IO.File]::SetUnixFileMode((Join-Path $appFilesDir 'SimpleZipDrive'), [System.IO.UnixFileMode]0x1ED)
    [System.IO.File]::SetUnixFileMode((Join-Path $appFilesDir '7zzs'), [System.IO.UnixFileMode]0x1ED)

    $appRun = "#!/bin/sh`nHERE=`"`$(dirname `"`$(readlink -f `"`$0`")`")`"`nexec `"`$HERE/usr/bin/SimpleZipDrive`" `"`$@`"`n"
    $appRunPath = Join-Path $appDir 'AppRun'
    [System.IO.File]::WriteAllText($appRunPath, $appRun)
    [System.IO.File]::SetUnixFileMode($appRunPath, [System.IO.UnixFileMode]0x1ED)

    [System.IO.File]::WriteAllText((Join-Path $appDir 'simplezipdrive.desktop'),
        (New-DesktopEntry -IconName 'simplezipdrive' -Exec 'SimpleZipDrive'))
    Copy-Item -LiteralPath $IconPath -Destination (Join-Path $appDir 'simplezipdrive.png') -Force
    Copy-Item -LiteralPath $IconPath -Destination (Join-Path $appDir '.DirIcon') -Force

    # dotnet publish can leave native libraries at 0744. appimagetool warns that group/other
    # users then cannot execute them (and future versions may fail the build), so make the
    # payload world-readable and add execute where a file is already executable.
    & chmod -R a+rX $appDir
    if ($LASTEXITCODE -ne 0) { throw "chmod failed for the AppDir of $Rid." }

    $tool = Get-Command appimagetool -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source
    if (-not $tool)
    {
        $tool = Join-Path $StagingDirectory "appimagetool-$appArch.AppImage"
        if (-not (Test-Path -LiteralPath $tool))
        {
            $url = "https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-$appArch.AppImage"
            Write-Host "Downloading $url..."
            Invoke-WebRequest -Uri $url -OutFile $tool -UseBasicParsing
        }
    }

    if (Test-Path -LiteralPath $DestinationPath) { Remove-Item -LiteralPath $DestinationPath -Force }
    $previousArch = $env:ARCH
    $previousExtract = $env:APPIMAGE_EXTRACT_AND_RUN
    try
    {
        $env:ARCH = $appArch
        $env:APPIMAGE_EXTRACT_AND_RUN = '1'
        & chmod +x $tool
        & $tool --no-appstream $appDir $DestinationPath
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $DestinationPath))
        {
            throw "appimagetool failed for $Rid."
        }
    }
    finally
    {
        $env:ARCH = $previousArch
        $env:APPIMAGE_EXTRACT_AND_RUN = $previousExtract
    }
}

# Reports an unavailable Linux packaging format: fatal in release builds (-RequireAllLinuxFormats),
# a warning for local runs on Windows/macOS.
function Stop-Or-WarnSkippedLinuxFormat
{
    param(
        [Parameter(Mandatory = $true)][string] $FormatName,
        [Parameter(Mandatory = $true)][string] $Reason
    )

    $message = "Skipping the $FormatName bundle: $Reason."
    if ($RequireAllLinuxFormats) { throw $message }
    Write-Warning $message
}

foreach ($rid in $RuntimeIdentifiers)
{
    $isWindowsRid = $rid.StartsWith('win-', [System.StringComparison]::OrdinalIgnoreCase)
    $isLinuxRid = $rid.StartsWith('linux-', [System.StringComparison]::OrdinalIgnoreCase)
    $publishDirectory = Join-Path $StagingDirectory $rid

    if (Test-Path -LiteralPath $publishDirectory) { Remove-Item -LiteralPath $publishDirectory -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $publishDirectory | Out-Null

    Write-Host "Publishing $rid..."
    dotnet publish $project -c Release -r $rid --self-contained false -o $publishDirectory --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $rid." }

    # The single-file executable embeds the managed assemblies; everything else in the
    # publish root is a native dependency that must ship loose beside it (7-Zip fallback,
    # WinFsp interop, Avalonia's Skia/HarfBuzz/ANGLE native libraries). PDBs and the
    # package-provided x64/ and x86/ 7z copies are intentionally not shipped
    # (SevenZipFallback probes the root copies).
    $mainExecutable = if ($isWindowsRid) { 'SimpleZipDrive.exe' } else { 'SimpleZipDrive' }
    if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory $mainExecutable)))
    {
        throw "Publish output for $rid is missing '$mainExecutable'."
    }

    $bundleFiles = Get-ChildItem -LiteralPath $publishDirectory -File |
        Where-Object { $_.Extension -ne '.pdb' } |
        Select-Object -ExpandProperty FullName

    foreach ($document in $documents)
    {
        $bundleFiles += Join-Path $repoRoot $document
    }

    # The Linux installer formats are self-contained so they work on machines without the
    # .NET 11 runtime. The framework-dependent tar.gz above stays the small portable bundle.
    $linuxInstallerFiles = @()
    if ($isLinuxRid -and $LinuxFormats.Count -gt 0)
    {
        $selfContainedDirectory = Join-Path $StagingDirectory "$rid-selfcontained"
        if (Test-Path -LiteralPath $selfContainedDirectory)
        {
            Remove-Item -LiteralPath $selfContainedDirectory -Recurse -Force
        }

        New-Item -ItemType Directory -Force -Path $selfContainedDirectory | Out-Null

        Write-Host "Publishing $rid (self-contained) for Linux installers..."
        dotnet publish $project -c Release -r $rid --self-contained true -p:PublishSingleFile=true `
            -o $selfContainedDirectory --nologo
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish (self-contained) failed for $rid." }

        $linuxInstallerFiles = Get-ChildItem -LiteralPath $selfContainedDirectory -File |
            Where-Object { $_.Extension -ne '.pdb' } |
            Select-Object -ExpandProperty FullName
        foreach ($document in $documents)
        {
            $linuxInstallerFiles += Join-Path $repoRoot $document
        }
    }

    $isMacOsRid = $rid.StartsWith('osx-', [System.StringComparison]::OrdinalIgnoreCase)

    # macOS gets a DMG when packaged on macOS and a portable tarball everywhere else.
    $archiveKind = if ($isWindowsRid) { 'zip' } elseif ($isMacOsRid -and $isMacOSHost) { 'dmg' } else { 'tar.gz' }
    $bundleName = "release_${Version}_$rid.$archiveKind"
    $bundlePath = Join-Path $OutputDirectory $bundleName
    $bundleFolderName = "SimpleZipDrive-$Version-$rid"

    switch ($archiveKind)
    {
        'zip'
        {
            if ($isWindowsHost)
            {
                # System.IO.Compression lets us set the Unix external attributes explicitly;
                # neither Compress-Archive nor 7-Zip records them on Windows.
                New-ReleaseZip -Files $bundleFiles -DestinationPath $bundlePath -UnixRid (-not $isWindowsRid)
            }
            else
            {
                # The zip CLI preserves the Unix executable bit natively. Only the exact bundle
                # file being generated is replaced (append-only output directory).
                if (Test-Path -LiteralPath $bundlePath) { Remove-Item -LiteralPath $bundlePath -Force }
                & zip -j -q $bundlePath @bundleFiles
                if ($LASTEXITCODE -ne 0) { throw "zip failed for $bundleName." }
            }
        }
        'tar.gz'
        {
            New-ReleaseTarGz -Files $bundleFiles -DestinationPath $bundlePath `
                -RootDirectoryName $bundleFolderName -UnixRid $true
        }
        'dmg'
        {
            New-ReleaseDmg -Files $bundleFiles -DestinationPath $bundlePath `
                -VolumeName "SimpleZipDrive $Version" -FolderName $bundleFolderName `
                -StagingDirectory $StagingDirectory -Rid $rid
        }
        default
        {
            throw "Unsupported bundle format '$archiveKind' for runtime identifier '$rid'."
        }
    }

    $bundles += Get-Item -LiteralPath $bundlePath
    Write-Host "Created $bundlePath"

    if ($isLinuxRid -and $LinuxFormats.Count -gt 0)
    {
        if ($linuxInstallerFiles.Count -eq 0)
        {
            throw "Self-contained publish output for $rid is empty."
        }

        foreach ($format in $LinuxFormats)
        {
            switch ($format)
            {
                'deb'
                {
                    $debPath = Join-Path $OutputDirectory "release_${Version}_$rid.deb"
                    New-ReleaseDeb -Files $linuxInstallerFiles -DestinationPath $debPath `
                        -Version $Version -Rid $rid -StagingDirectory $StagingDirectory -IconPath $iconPath
                    $bundles += Get-Item -LiteralPath $debPath
                    Write-Host "Created $debPath"
                }
                'appimage'
                {
                    if (-not $isLinuxHost)
                    {
                        Stop-Or-WarnSkippedLinuxFormat -FormatName 'AppImage' -Reason 'appimagetool runs only on Linux'
                    }
                    else
                    {
                        $appImagePath = Join-Path $OutputDirectory "release_${Version}_$rid.AppImage"
                        New-ReleaseAppImage -Files $linuxInstallerFiles -DestinationPath $appImagePath `
                            -Version $Version -Rid $rid -StagingDirectory $StagingDirectory -IconPath $iconPath
                        $bundles += Get-Item -LiteralPath $appImagePath
                        Write-Host "Created $appImagePath"
                    }
                }
            }
        }
    }
}

Write-Host ''
Write-Host 'Bundles:'
$bundles | ForEach-Object { '{0}  ({1:N0} bytes)' -f $_.FullName, $_.Length }
