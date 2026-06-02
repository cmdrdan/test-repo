<#
.SYNOPSIS
Build and package the LiveTV Scheduler plugin into a Jellyfin-installable zip.

.DESCRIPTION
Produces dist/livetv-scheduler-<version>.zip containing:
  - Jellyfin.Plugin.LiveTV.dll
  - meta.json   (Jellyfin reads this from the plugin directory)

Also updates manifest.json with the new checksum (MD5 of the zip) and
timestamp, so the repository file is ready to commit and push.

.PARAMETER Version
Plugin version (4-part). Defaults to the value already in build.yaml / manifest.json.

.PARAMETER SourceUrlBase
Base URL where the released zip will be hosted. The script appends
"v<version>/livetv-scheduler-<version>.zip" to form the final sourceUrl.
Defaults to the GitHub releases pattern derived from build.yaml.owner.

.PARAMETER SkipBuild
Skip "dotnet build -c Release" (use existing artifacts).

.EXAMPLE
pwsh ./package.ps1

.EXAMPLE
pwsh ./package.ps1 -Version 1.0.1.0
#>
[CmdletBinding()]
param(
    [string] $Version,
    [string] $SourceUrlBase,
    [switch] $SkipBuild
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$ProjectDir   = Join-Path $PSScriptRoot 'Jellyfin.Plugin.LiveTV'
$Csproj       = Join-Path $ProjectDir   'Jellyfin.Plugin.LiveTV.csproj'
$BuildYaml    = Join-Path $PSScriptRoot 'build.yaml'
$ManifestPath = Join-Path $PSScriptRoot 'manifest.json'
$DistDir      = Join-Path $PSScriptRoot 'dist'

# ── Read build.yaml (lightweight — no YAML module needed) ─────────────────
function Get-YamlValue([string]$Path, [string]$Key) {
    $line = Select-String -Path $Path -Pattern "^\s*${Key}\s*:\s*(.+)" -AllMatches |
        Select-Object -First 1
    if (-not $line) { throw "build.yaml missing key '$Key'" }
    return ($line.Matches[0].Groups[1].Value.Trim().Trim('"'))
}

$buildName        = Get-YamlValue $BuildYaml 'name'
$buildGuid        = Get-YamlValue $BuildYaml 'guid'
$buildVersion     = Get-YamlValue $BuildYaml 'version'
$buildTargetAbi   = Get-YamlValue $BuildYaml 'targetAbi'
$buildFramework   = Get-YamlValue $BuildYaml 'framework'
$buildOverview    = Get-YamlValue $BuildYaml 'overview'
$buildCategory    = Get-YamlValue $BuildYaml 'category'
$buildOwner       = Get-YamlValue $BuildYaml 'owner'

if (-not $Version)       { $Version       = $buildVersion }
if (-not $SourceUrlBase) { $SourceUrlBase = "https://github.com/$buildOwner/test-repo/releases/download" }

Write-Host "Packaging $buildName v$Version (targetAbi=$buildTargetAbi, framework=$buildFramework)" -ForegroundColor Cyan

# ── Build ─────────────────────────────────────────────────────────────────
if (-not $SkipBuild) {
    Write-Host "Building (dotnet build -c Release)..." -ForegroundColor Cyan
    & dotnet build -c Release $Csproj /p:Version=$Version /p:AssemblyVersion=$Version /p:FileVersion=$Version
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }
}

$DllPath = Join-Path $ProjectDir "bin/Release/$buildFramework/Jellyfin.Plugin.LiveTV.dll"
if (-not (Test-Path $DllPath)) { throw "Built DLL not found at $DllPath" }

# ── Stage meta.json + DLL, then zip ───────────────────────────────────────
$StageDir = Join-Path $DistDir "stage"
if (Test-Path $StageDir) { Remove-Item $StageDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $StageDir | Out-Null

# Description multi-line value from build.yaml — read manually for fidelity.
$descLines = @()
$inDesc = $false
foreach ($line in Get-Content $BuildYaml) {
    if ($line -match '^\s*description\s*:\s*>') { $inDesc = $true; continue }
    if ($inDesc) {
        if ($line -match '^\s*[a-zA-Z_]+\s*:') { break }
        $descLines += $line.Trim()
    }
}
$description = ($descLines -join ' ').Trim()
if (-not $description) { $description = $buildOverview }

$changelogLines = @()
$inCl = $false
foreach ($line in Get-Content $BuildYaml) {
    if ($line -match '^\s*changelog\s*:\s*>') { $inCl = $true; continue }
    if ($inCl) {
        if ($line -match '^\s*[a-zA-Z_]+\s*:') { break }
        $changelogLines += $line.Trim()
    }
}
$changelog = ($changelogLines -join ' ').Trim()

$timestampUtc = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")

$meta = [ordered]@{
    category    = $buildCategory
    changelog   = $changelog
    description = $description
    guid        = $buildGuid
    name        = $buildName
    overview    = $buildOverview
    owner       = $buildOwner
    targetAbi   = $buildTargetAbi
    timestamp   = $timestampUtc
    version     = $Version
}
$metaJson = $meta | ConvertTo-Json -Depth 4
Set-Content -Path (Join-Path $StageDir 'meta.json') -Value $metaJson -Encoding UTF8 -NoNewline

Copy-Item $DllPath (Join-Path $StageDir 'Jellyfin.Plugin.LiveTV.dll')

$ZipName = "livetv-scheduler-$Version.zip"
$ZipPath = Join-Path $DistDir $ZipName
if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }

Compress-Archive -Path (Join-Path $StageDir '*') -DestinationPath $ZipPath -CompressionLevel Optimal
Remove-Item $StageDir -Recurse -Force

# ── Compute checksum (MD5, uppercase hex — what Jellyfin expects) ────────
$md5 = (Get-FileHash -Algorithm MD5 -Path $ZipPath).Hash.ToUpperInvariant()
$sourceUrl = "$SourceUrlBase/v$Version/$ZipName"

Write-Host ""
Write-Host "Packaged:" -ForegroundColor Green
Write-Host "  Zip:       $ZipPath"
Write-Host "  Checksum:  $md5"
Write-Host "  SourceUrl: $sourceUrl"

# ── Update manifest.json in place ─────────────────────────────────────────
# IMPORTANT: Jellyfin requires the top-level structure to be a JSON array of
# plugin entries. Use -AsHashtable so we control serialization shape ourselves.
$manifest = @(Get-Content $ManifestPath -Raw | ConvertFrom-Json)

# manifest.json is an array of plugin entries — find the matching one by guid.
$entry = $manifest | Where-Object { $_.guid -eq $buildGuid } | Select-Object -First 1
if (-not $entry) { throw "manifest.json has no entry for guid $buildGuid" }

# Find or create a version entry matching $Version.
$verEntry = $entry.versions | Where-Object { $_.version -eq $Version } | Select-Object -First 1
if (-not $verEntry) {
    $verEntry = [pscustomobject]@{
        version    = $Version
        changelog  = $changelog
        targetAbi  = $buildTargetAbi
        sourceUrl  = $sourceUrl
        checksum   = $md5
        timestamp  = $timestampUtc
    }
    $entry.versions = @($verEntry) + $entry.versions
} else {
    $verEntry.changelog = $changelog
    $verEntry.targetAbi = $buildTargetAbi
    $verEntry.sourceUrl = $sourceUrl
    $verEntry.checksum  = $md5
    $verEntry.timestamp = $timestampUtc
}

# Force array shape even when there's exactly one entry — ConvertTo-Json
# unwraps single-element arrays by default, which would break Jellyfin's parser.
$manifestJson = ConvertTo-Json -InputObject @($manifest) -Depth 6
if ($manifestJson.TrimStart() -notmatch '^\[') {
    $manifestJson = "[`n$manifestJson`n]"
}
Set-Content -Path $ManifestPath -Value $manifestJson -Encoding UTF8

Write-Host ""
Write-Host "manifest.json updated." -ForegroundColor Green
Write-Host "Next steps:"
Write-Host "  1. Create GitHub release 'v$Version' and upload $ZipName as a release asset."
Write-Host "  2. Commit & push the updated manifest.json so the repository URL serves the new version."
