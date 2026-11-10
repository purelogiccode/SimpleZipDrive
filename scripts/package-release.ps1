<#
.SYNOPSIS
    Builds the SimpleZipDrive release bundles for Windows, Linux and macOS.

.DESCRIPTION
    Publishes the single Avalonia application as a framework-dependent single-file
    executable for each requested runtime identifier and packages it with ReadMe.md,
    LICENSE.txt and WhatsNew.md.

    Bundle formats follow the platform:
      - Windows: release_<version>_<rid>.zip
      - Linux:   release_<version>_<rid>.tar.gz
      - macOS:   release_<version>_<rid>.dmg on a macOS host, otherwise a .tar.gz
                 fallback (a DMG can only be produced by hdiutil on macOS).

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
$isMacOSHost = $null -ne $IsMacOS -and $IsMacOS
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

foreach ($rid in $RuntimeIdentifiers)
{
    $isWindowsRid = $rid.StartsWith('win-', [System.StringComparison]::OrdinalIgnoreCase)
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
}

Write-Host ''
Write-Host 'Bundles:'
$bundles | ForEach-Object { '{0}  ({1:N0} bytes)' -f $_.FullName, $_.Length }
