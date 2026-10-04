<#
.SYNOPSIS
    Builds the SimpleZipDrive release bundles for Windows, Linux and macOS.

.DESCRIPTION
    Publishes the single Avalonia application as a framework-dependent single-file
    executable for each requested runtime identifier and packages it with ReadMe.md,
    LICENSE.txt and WhatsNew.md using the release_<version>_<rid>.zip naming convention.

    Every bundle contains the 7-Zip command-line fallback binary for its platform
    (7za.exe on Windows, 7zzs on Linux, 7zz on macOS) and the 7-Zip license text.
    Windows bundles additionally contain the loose winfsp-msil.dll interop assembly;
    FUSE (libfuse3 / macFUSE) is resolved from the host system at runtime.

    All six runtime identifiers can be produced on any host. On Windows the script
    writes the zip through System.IO.Compression and records the Unix executable bit
    (external attributes) on the apphost and the 7-Zip binary for Linux/macOS bundles;
    on Linux/macOS it uses the zip CLI, which preserves permissions natively.

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
        $bytes = [System.IO.File]::ReadAllBytes($DestinationPath)
        for ($i = 0; $i -le $bytes.Length - 8; $i++)
        {
            if ($bytes[$i] -eq 0x50 -and $bytes[$i + 1] -eq 0x4B -and
                $bytes[$i + 2] -eq 0x01 -and $bytes[$i + 3] -eq 0x02)
            {
                $bytes[$i + 5] = 3
            }
        }

        [System.IO.File]::WriteAllBytes($DestinationPath, $bytes)
    }
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

    $bundleName = "release_${Version}_$rid.zip"
    $bundlePath = Join-Path $OutputDirectory $bundleName

    if ($isWindowsHost)
    {
        # System.IO.Compression lets us set the Unix external attributes explicitly; neither
        # Compress-Archive nor 7-Zip records them on Windows.
        New-ReleaseZip -Files $bundleFiles -DestinationPath $bundlePath -UnixRid (-not $isWindowsRid)
    }
    else
    {
        # The zip CLI preserves the Unix executable bit natively. Only the exact bundle file
        # being generated is replaced (append-only output directory).
        if (Test-Path -LiteralPath $bundlePath) { Remove-Item -LiteralPath $bundlePath -Force }
        & zip -j -q $bundlePath @bundleFiles
        if ($LASTEXITCODE -ne 0) { throw "zip failed for $bundleName." }
    }

    $bundles += Get-Item -LiteralPath $bundlePath
    Write-Host "Created $bundlePath"
}

Write-Host ''
Write-Host 'Bundles:'
$bundles | ForEach-Object { '{0}  ({1:N0} bytes)' -f $_.FullName, $_.Length }
