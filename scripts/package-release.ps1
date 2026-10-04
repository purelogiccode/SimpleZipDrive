<#
.SYNOPSIS
    Builds the SimpleZipDrive release bundles for Windows, Linux and macOS.

.DESCRIPTION
    Publishes the single Avalonia application as a framework-dependent single-file
    executable for each requested runtime identifier and packages it with ReadMe.md,
    LICENSE.txt and WhatsNew.md using the release_<version>_<rid>.zip naming convention.

    Windows bundles include the native 7-Zip fallback libraries (7z.dll / 7z_arm64.dll)
    and the loose winfsp-msil.dll interop assembly. Linux and macOS bundles contain the
    native apphost only; the 7-Zip fallback is unavailable there and FUSE (libfuse3 /
    macFUSE) is resolved from the host system at runtime.

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

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
New-Item -ItemType Directory -Force -Path $StagingDirectory | Out-Null

if (-not $SkipTests)
{
    Write-Host 'Running tests before packaging...'
    dotnet test (Join-Path $repoRoot 'SimpleZipDrive.Tests\SimpleZipDrive.Tests.csproj') -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed - release bundles were not created.' }
}

$isWindowsHost = $IsWindows
$bundles = @()

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
        Compress-Archive -Path $bundleFiles -DestinationPath $bundlePath -Force
    }
    else
    {
        # Compress-Archive does not preserve the Unix executable bit; the zip CLI does.
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
