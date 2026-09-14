#Requires -Version 5.1
<#
.SYNOPSIS
  Local release for Portal Atlas (Valheim refs are not available on GitHub-hosted runners).

.DESCRIPTION
  1. Validates PluginVersion / csproj / manifest.json agree
  2. Ensures dist\<name>-<version>.zip exists (builds/packages unless -SkipPackage)
  3. Extracts that version's section from CHANGELOG.md for the GitHub release body
  4. Creates git tag v<version>, pushes it, and creates/uploads the GitHub Release with the zip

.PARAMETER SkipPackage
  Use an existing Thunderstore zip in dist\ (you already ran package.ps1).

.PARAMETER SkipPush
  Create the local tag and print commands, but do not push or create the GitHub release.

.PARAMETER DryRun
  Validate and print what would happen; make no git/GitHub changes.
#>
param(
    [string]$LibDir = "E:\Scripts\Valheim Mods\Reqs",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$SkipPackage,
    [switch]$SkipPush,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $ProjectRoot

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Get-ChangelogSection {
    param(
        [Parameter(Mandatory = $true)][string]$ChangelogPath,
        [Parameter(Mandatory = $true)][string]$Version
    )

    Assert-True (Test-Path -LiteralPath $ChangelogPath) "Missing CHANGELOG.md"
    $lines = Get-Content -LiteralPath $ChangelogPath -Encoding UTF8
    $header = "## $Version"
    $start = -1
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i].Trim() -eq $header) {
            $start = $i
            break
        }
    }
    Assert-True ($start -ge 0) "CHANGELOG.md has no section '$header'. Add release notes before releasing."

    $end = $lines.Count
    for ($i = $start + 1; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^##\s+\d+\.\d+\.\d+\s*$') {
            $end = $i
            break
        }
    }

    $section = ($lines[$start..($end - 1)] -join "`n").Trim()
    Assert-True (-not [string]::IsNullOrWhiteSpace($section)) "Changelog section for $Version is empty."
    return $section
}

# --- Version alignment ---
$ManifestPath = Join-Path $ProjectRoot "manifest.json"
$PluginSrc = Join-Path $ProjectRoot "src\PortalAtlasPlugin.cs"
$CsprojPath = Join-Path $ProjectRoot "PortalAtlas.csproj"
$ChangelogPath = Join-Path $ProjectRoot "CHANGELOG.md"

$manifest = Get-Content -LiteralPath $ManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$version = [string]$manifest.version_number
Assert-True ($version -match '^\d+\.\d+\.\d+$') "Invalid manifest version_number: $version"

$pluginSrcRaw = Get-Content -LiteralPath $PluginSrc -Raw -Encoding UTF8
Assert-True ($pluginSrcRaw -match 'PluginVersion\s*=\s*"(\d+\.\d+\.\d+)"') "Could not find PluginVersion in source"
$pluginVersion = $Matches[1]
Assert-True ($pluginVersion -eq $version) "Version mismatch: manifest=$version PluginVersion=$pluginVersion"

$csprojRaw = Get-Content -LiteralPath $CsprojPath -Raw -Encoding UTF8
Assert-True ($csprojRaw -match '<Version>(\d+\.\d+\.\d+)</Version>') "Could not find <Version> in csproj"
$csprojVersion = $Matches[1]
Assert-True ($csprojVersion -eq $version) "Version mismatch: manifest=$version csproj=$csprojVersion"

$modName = [string]$manifest.name
$tag = "v$version"
$zipName = "$modName-$version.zip"
$zipPath = Join-Path $ProjectRoot "dist\$zipName"

Write-Host "Release target: $tag ($modName $version)" -ForegroundColor Cyan

# --- Package ---
if (-not $SkipPackage) {
    if ($DryRun) {
        Write-Host "[dry-run] Would run package.ps1"
    }
    else {
        & (Join-Path $ProjectRoot "package.ps1") -LibDir $LibDir -Configuration $Configuration
        if ($LASTEXITCODE -ne 0) { throw "package.ps1 failed with exit code $LASTEXITCODE" }
    }
}

Assert-True (Test-Path -LiteralPath $zipPath) "Missing package zip: $zipPath (run package.ps1 or omit -SkipPackage)"

$notes = Get-ChangelogSection -ChangelogPath $ChangelogPath -Version $version
$notesFile = Join-Path $ProjectRoot "dist\release-notes-$version.md"
if (-not $DryRun) {
    New-Item -ItemType Directory -Force -Path (Join-Path $ProjectRoot "dist") | Out-Null
    # GitHub release body: section without repeating a top-level title beyond changelog header
    $body = @"
$notes

---
Thunderstore zip: ``$zipName`` (built locally; Valheim managed refs are not available on GitHub-hosted runners).
"@
    Set-Content -LiteralPath $notesFile -Value $body -Encoding UTF8
}

Write-Host "Changelog preview:" -ForegroundColor Green
Write-Host $notes
Write-Host ""

if ($DryRun) {
    Write-Host "[dry-run] Would tag $tag, push, and gh release create with $zipPath"
    exit 0
}

# --- Git checks ---
Assert-True (Test-Path -LiteralPath (Join-Path $ProjectRoot ".git")) "Not a git repository. Initialize and push to GitHub first."
$status = git status --porcelain
if ($status) {
    Write-Host $status
    throw "Working tree is not clean. Commit or stash before releasing."
}

$existingTag = git tag -l $tag
if ($existingTag) {
    throw "Tag $tag already exists locally. Delete it or bump the version."
}

git rev-parse --abbrev-ref HEAD | Out-Null
$branch = (git rev-parse --abbrev-ref HEAD).Trim()

Write-Host "Creating annotated tag $tag on $branch..."
git tag -a $tag -m "Release $version"

if ($SkipPush) {
    Write-Host "SkipPush: tag created locally. Push and publish with:"
    Write-Host "  git push origin $branch"
    Write-Host "  git push origin $tag"
    Write-Host "  gh release create $tag `"$zipPath`" --title `"$modName $version`" --notes-file `"$notesFile`""
    exit 0
}

Write-Host "Pushing $branch and $tag..."
git push -u origin $branch
if ($LASTEXITCODE -ne 0) { throw "git push branch failed" }
git push origin $tag
if ($LASTEXITCODE -ne 0) { throw "git push tag failed" }

# Prefer attaching assets here; Actions may create/update notes if the tag workflow races.
# PowerShell Stop treats "gh release view" stderr ("release not found") as terminating — temporarily Continue.
$releaseExists = $false
$prevEap = $ErrorActionPreference
$ErrorActionPreference = "Continue"
gh release view $tag 2>$null | Out-Null
if ($LASTEXITCODE -eq 0) {
    $releaseExists = $true
}
$ErrorActionPreference = $prevEap

if ($releaseExists) {
    Write-Host "Release $tag already exists; uploading assets and refreshing notes..."
    gh release upload $tag $zipPath --clobber
    if ($LASTEXITCODE -ne 0) { throw "gh release upload failed" }
    gh release edit $tag --title "$modName $version" --notes-file $notesFile
    if ($LASTEXITCODE -ne 0) { throw "gh release edit failed" }
}
else {
    Write-Host "Creating GitHub release $tag..."
    gh release create $tag $zipPath --title "$modName $version" --notes-file $notesFile
    if ($LASTEXITCODE -ne 0) { throw "gh release create failed" }
}

$url = gh release view $tag --json url -q .url
Write-Host "Release published: $url" -ForegroundColor Green
Write-Host "Zip: $zipPath"
