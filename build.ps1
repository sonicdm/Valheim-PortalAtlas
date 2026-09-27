param(
    [string]$LibDir = "E:\Scripts\Valheim Mods\Reqs",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$Package
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$Project = Join-Path $ProjectRoot "PortalAtlas.csproj"
$Dist = Join-Path $ProjectRoot "dist"

if (-not (Test-Path -LiteralPath $LibDir)) {
    throw "Valheim reference folder not found: $LibDir"
}

Push-Location $ProjectRoot
try {
    dotnet clean $Project -c $Configuration | Out-Host
    dotnet build $Project -c $Configuration -p:ValheimLibDir="$LibDir" | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE" }

    $Dll = Join-Path $ProjectRoot "bin\$Configuration\PortalAtlas.dll"
    if (-not (Test-Path -LiteralPath $Dll)) {
        throw "Build succeeded but PortalAtlas.dll was not found at $Dll"
    }

    New-Item -ItemType Directory -Force -Path $Dist | Out-Null
    Copy-Item -LiteralPath $Dll -Destination (Join-Path $Dist "PortalAtlas.dll") -Force
    Write-Host "DLL: $(Join-Path $Dist 'PortalAtlas.dll')"

    if ($Package) {
        & (Join-Path $ProjectRoot "package.ps1") -LibDir $LibDir -Configuration $Configuration -SkipBuild -OutDir $Dist
        if ($LASTEXITCODE -ne 0) { throw "package.ps1 failed with exit code $LASTEXITCODE" }
    }
}
finally {
    Pop-Location
}
