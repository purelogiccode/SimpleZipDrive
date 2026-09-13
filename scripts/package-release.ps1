<#
.SYNOPSIS
    Builds the four SimpleZipDrive release bundles.

.DESCRIPTION
    Publishes both variants (Dokan, WinFsp) for x64 and arm64 as framework-dependent
    single-file executables and packages each one with ReadMe.md, LICENSE.txt and
    WhatsNew.md, following the release_<version>_<Variant>_win-<arch>.zip naming
    convention used by every published release.

    Existing files in the output directory are never deleted; only the four bundles
    for the requested version are created (or overwritten if they already exist for
    that version). The publish/staging directory is temporary and may be cleaned.

.PARAMETER Version
    Release version, e.g. 3.0.0.

.PARAMETER OutputDirectory
    Where the bundles are written. Defaults to SimpleZipDrive\bin\Release, the folder
    that holds all historical bundles.

.PARAMETER StagingDirectory
    Temporary publish/staging root. Defaults to a folder under the system temp directory.

.PARAMETER SkipTests
    Skips the test run that normally guards a release build (CI runs tests in a
    separate job and passes this switch).

.EXAMPLE
    .\scripts\package-release.ps1 -Version 3.0.0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Version,

    [string] $OutputDirectory,

    [string] $StagingDirectory,

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

$variants = @(
    [pscustomobject]@{
        Name = 'Dokan'
        Project = 'SimpleZipDrive\SimpleZipDrive.csproj'
        Exe = 'SimpleZipDrive.exe'
        RuntimeFiles = @('7z.dll', '7z_arm64.dll')
    }
    [pscustomobject]@{
        Name = 'WinFsp'
        Project = 'SimpleZipDrive_WinFsp\SimpleZipDrive_WinFsp.csproj'
        Exe = 'SimpleZipDrive_WinFsp.exe'
        RuntimeFiles = @('7z.dll', '7z_arm64.dll', 'winfsp-msil.dll')
    }
)
$architectures = @('x64', 'arm64')
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

$bundles = @()
foreach ($variant in $variants)
{
    foreach ($architecture in $architectures)
    {
        $rid = "win-$architecture"
        $publishDirectory = Join-Path $StagingDirectory "$($variant.Name)_$architecture"

        if (Test-Path -LiteralPath $publishDirectory) { Remove-Item -LiteralPath $publishDirectory -Recurse -Force }
        New-Item -ItemType Directory -Force -Path $publishDirectory | Out-Null

        Write-Host "Publishing $($variant.Name) $rid..."
        dotnet publish (Join-Path $repoRoot $variant.Project) -c Release -r $rid --self-contained false -o $publishDirectory --nologo
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $($variant.Name) $rid." }

        # Bundle exactly the files shipped by every historical release: the single-file
        # executable, the native fallback libraries, and the three documentation files.
        # The publish folder can also contain package-provided x64/ and x86/ 7z copies;
        # those are intentionally not shipped (SevenZipFallback probes the root copies).
        $bundleFiles = @()
        foreach ($runtimeFile in @($variant.Exe) + $variant.RuntimeFiles)
        {
            $runtimePath = Join-Path $publishDirectory $runtimeFile
            if (-not (Test-Path -LiteralPath $runtimePath))
            {
                throw "Publish output for $($variant.Name) $rid is missing '$runtimeFile'."
            }
            $bundleFiles += $runtimePath
        }
        foreach ($document in $documents)
        {
            $bundleFiles += Join-Path $repoRoot $document
        }

        $bundleName = "release_${Version}_$($variant.Name)_$rid.zip"
        $bundlePath = Join-Path $OutputDirectory $bundleName

        Compress-Archive -Path $bundleFiles -DestinationPath $bundlePath -Force
        $bundles += Get-Item -LiteralPath $bundlePath
        Write-Host "Created $bundlePath"
    }
}

Write-Host ''
Write-Host 'Bundles:'
$bundles | ForEach-Object { '{0}  ({1:N0} bytes)' -f $_.FullName, $_.Length }
