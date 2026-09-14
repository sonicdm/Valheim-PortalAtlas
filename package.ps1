#Requires -Version 5.1
<#
.SYNOPSIS
  Builds (optional) and packs Valheim Portal List for Thunderstore / r2modman.

.DESCRIPTION
  Follows Thunderstore package requirements:
  https://thunderstore.io/package/create/docs/

  Zip root must contain:
    - manifest.json
    - README.md
    - icon.png          (exactly 256x256 PNG)
    - CHANGELOG.md      (optional)
    - ValheimPortalList.dll
    - any other mod files

  Files are placed at the ZIP root (not inside a nested folder).

.PARAMETER LibDir
  Folder of Valheim/BepInEx reference DLLs for building.

.PARAMETER Configuration
  Debug or Release. Default Release.

.PARAMETER SkipBuild
  Package the existing bin\<Configuration>\ValheimPortalList.dll without rebuilding.

.PARAMETER OutDir
  Output folder for the zip. Default: .\dist
#>
param(
    [string]$LibDir = "E:\Scripts\Valheim Mods\Reqs",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$SkipBuild,
    [string]$OutDir = ""
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $OutDir) {
    $OutDir = Join-Path $ProjectRoot "dist"
}

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Get-PngSize([string]$Path) {
    # PNG IHDR: width/height are big-endian u32 at bytes 16..23
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    Assert-True ($bytes.Length -ge 24) "icon.png is too small to be a valid PNG."
    Assert-True ($bytes[0] -eq 0x89 -and $bytes[1] -eq 0x50 -and $bytes[2] -eq 0x4E -and $bytes[3] -eq 0x47) "icon.png is not a PNG file."
    $width = ([int]$bytes[16] -shl 24) -bor ([int]$bytes[17] -shl 16) -bor ([int]$bytes[18] -shl 8) -bor [int]$bytes[19]
    $height = ([int]$bytes[20] -shl 24) -bor ([int]$bytes[21] -shl 16) -bor ([int]$bytes[22] -shl 8) -bor [int]$bytes[23]
    return @{ Width = $width; Height = $height }
}

function Test-Utf8File([string]$Path) {
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        return $true
    }
    try {
        $utf8 = New-Object System.Text.UTF8Encoding $false, $true
        [void]$utf8.GetString($bytes)
        return $true
    }
    catch {
        return $false
    }
}

Write-Host "Thunderstore package (https://thunderstore.io/package/create/docs/)" -ForegroundColor Cyan

# --- Build ---
if (-not $SkipBuild) {
    & (Join-Path $ProjectRoot "build.ps1") -LibDir $LibDir -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw "build.ps1 failed with exit code $LASTEXITCODE" }
}

$Dll = Join-Path $ProjectRoot "bin\$Configuration\ValheimPortalList.dll"
Assert-True (Test-Path -LiteralPath $Dll) "DLL not found: $Dll (build first or omit -SkipBuild)."

# --- Required Thunderstore files ---
$ManifestPath = Join-Path $ProjectRoot "manifest.json"
$ReadmePath = Join-Path $ProjectRoot "README.md"
$IconPath = Join-Path $ProjectRoot "icon.png"
$ChangelogPath = Join-Path $ProjectRoot "CHANGELOG.md"

Assert-True (Test-Path -LiteralPath $ManifestPath) "Missing required file: manifest.json"
Assert-True (Test-Path -LiteralPath $ReadmePath) "Missing required file: README.md"
Assert-True (Test-Path -LiteralPath $IconPath) "Missing required file: icon.png (must be 256x256 PNG)"

Assert-True (Test-Utf8File $ManifestPath) "manifest.json must be UTF-8 compatible."
Assert-True (Test-Utf8File $ReadmePath) "README.md must be UTF-8 compatible."

# --- Validate manifest.json ---
$manifestRaw = Get-Content -LiteralPath $ManifestPath -Raw -Encoding UTF8
try {
    $manifest = $manifestRaw | ConvertFrom-Json
}
catch {
    throw "manifest.json is not valid JSON: $($_.Exception.Message)"
}

foreach ($field in @("name", "version_number", "website_url", "description", "author", "dependencies")) {
    Assert-True ($null -ne $manifest.PSObject.Properties[$field]) "manifest.json missing required field: $field"
}

Assert-True ($manifest.name -match '^[a-zA-Z0-9_]{1,128}$') `
    "manifest.json name must be 1-128 chars of [a-zA-Z0-9_] (no spaces). Got: '$($manifest.name)'"

Assert-True (-not [string]::IsNullOrWhiteSpace([string]$manifest.author)) `
    "manifest.json author must be a non-empty string. Got: '$($manifest.author)'"

Assert-True ($manifest.version_number -match '^\d+\.\d+\.\d+$') `
    "manifest.json version_number must be Major.Minor.Patch. Got: '$($manifest.version_number)'"

# Align Thunderstore version with plugin source + csproj
$PluginSrc = Join-Path $ProjectRoot "src\ValheimPortalListPlugin.cs"
$CsprojPath = Join-Path $ProjectRoot "ValheimPortalList.csproj"
Assert-True (Test-Path -LiteralPath $PluginSrc) "Missing plugin source: $PluginSrc"
$pluginSrcRaw = Get-Content -LiteralPath $PluginSrc -Raw -Encoding UTF8
Assert-True ($pluginSrcRaw -match 'PluginVersion\s*=\s*"(\d+\.\d+\.\d+)"') `
    "Could not find PluginVersion in ValheimPortalList.cs"
$pluginVersion = $Matches[1]
Assert-True ($pluginVersion -eq [string]$manifest.version_number) `
    "Version mismatch: manifest.json=$($manifest.version_number) but PluginVersion=$pluginVersion"

$csprojRaw = Get-Content -LiteralPath $CsprojPath -Raw -Encoding UTF8
Assert-True ($csprojRaw -match '<Version>(\d+\.\d+\.\d+)</Version>') `
    "Could not find <Version> in ValheimPortalList.csproj"
$csprojVersion = $Matches[1]
Assert-True ($csprojVersion -eq [string]$manifest.version_number) `
    "Version mismatch: manifest.json=$($manifest.version_number) but csproj Version=$csprojVersion"

Assert-True ($manifest.description.Length -le 250) `
    "manifest.json description max 250 characters. Got: $($manifest.description.Length)"

Assert-True ($manifest.dependencies -is [System.Collections.IEnumerable]) `
    "manifest.json dependencies must be an array."

# website_url may be empty string but must be present (already checked)
if ($null -eq $manifest.website_url) {
    throw "manifest.json website_url must be a string (use empty string if none)."
}

# --- Validate icon.png ---
$size = Get-PngSize $IconPath
Assert-True ($size.Width -eq 256 -and $size.Height -eq 256) `
    "icon.png must be exactly 256x256. Got: $($size.Width)x$($size.Height)"

Write-Host "Validated: manifest.json (author=$($manifest.author)), README.md, icon.png (256x256)" -ForegroundColor Green

# --- Stage flat package root ---
$Stage = Join-Path $ProjectRoot ".package"
if (Test-Path -LiteralPath $Stage) {
    Remove-Item -LiteralPath $Stage -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $Stage | Out-Null

Copy-Item -LiteralPath $Dll -Destination (Join-Path $Stage "ValheimPortalList.dll")
Copy-Item -LiteralPath $ManifestPath -Destination (Join-Path $Stage "manifest.json")
Copy-Item -LiteralPath $ReadmePath -Destination (Join-Path $Stage "README.md")
Copy-Item -LiteralPath $IconPath -Destination (Join-Path $Stage "icon.png")
if (Test-Path -LiteralPath $ChangelogPath) {
    Assert-True (Test-Utf8File $ChangelogPath) "CHANGELOG.md must be UTF-8 compatible."
    Copy-Item -LiteralPath $ChangelogPath -Destination (Join-Path $Stage "CHANGELOG.md")
}

# --- Zip with files at root (do not zip the folder itself) ---
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$ZipName = "$($manifest.name)-$($manifest.version_number).zip"
$ZipPath = Join-Path $OutDir $ZipName
if (Test-Path -LiteralPath $ZipPath) {
    Remove-Item -LiteralPath $ZipPath -Force
}

# Compress-Archive with explicit file list keeps entries at zip root
$StageFiles = Get-ChildItem -LiteralPath $Stage -File | ForEach-Object { $_.FullName }
Compress-Archive -LiteralPath $StageFiles -DestinationPath $ZipPath -CompressionLevel Optimal

# Sanity-check zip root entries (no nested directory wrapper)
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
try {
    $entries = @($zip.Entries | ForEach-Object { $_.FullName })
    $requiredInZip = @("manifest.json", "README.md", "icon.png", "ValheimPortalList.dll")
    foreach ($name in $requiredInZip) {
        Assert-True ($entries -contains $name) "ZIP is missing root entry '$name'. Entries: $($entries -join ', ')"
    }
    foreach ($entry in $entries) {
        Assert-True ($entry -notmatch '[\\/]') "ZIP entry is nested (invalid for Thunderstore): $entry"
    }
}
finally {
    $zip.Dispose()
}

Remove-Item -LiteralPath $Stage -Recurse -Force

# Also refresh loose DLL copy in dist for convenience
Copy-Item -LiteralPath $Dll -Destination (Join-Path $OutDir "ValheimPortalList.dll") -Force

Write-Host ""
Write-Host "Package ready:" -ForegroundColor Green
Write-Host "  $ZipPath"
Write-Host "Contents:"
Get-ChildItem -LiteralPath $OutDir -Filter $ZipName | Out-Null
$zipList = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
try {
    $zipList.Entries | ForEach-Object { Write-Host ("  - " + $_.FullName + " (" + $_.Length + " bytes)") }
}
finally {
    $zipList.Dispose()
}
Write-Host ""
Write-Host "Upload at: https://thunderstore.io/c/valheim/create/package/ (team/author: SonicDM)" -ForegroundColor Cyan
Write-Host "Or import the zip into r2modman as a local mod (installs as SonicDM-ValheimPortalList)."
