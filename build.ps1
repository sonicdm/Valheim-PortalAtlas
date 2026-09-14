param(
    [string]$LibDir = "E:\Scripts\Valheim Mods\Reqs",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$Package
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$Project = Join-Path $ProjectRoot "ValheimPortalList.csproj"
$Dist = Join-Path $ProjectRoot "dist"

if (-not (Test-Path -LiteralPath $LibDir)) {
    throw "Valheim reference folder not found: $LibDir"
}

$Required = @(
    "BepInEx.dll",
    "0Harmony.dll",
    "assembly_valheim.dll",
    "Jotunn.dll",
    "UnityEngine.CoreModule.dll",
    "UnityEngine.UI.dll"
)

$Missing = foreach ($Name in $Required) {
    if (-not (Test-Path -LiteralPath (Join-Path $LibDir $Name))) { $Name }
}
if ($Missing) {
    Write-Warning ("Some common Valheim/BepInEx references were not found: " + ($Missing -join ", "))
    Write-Warning "The exact filenames vary by BepInEx/Valheim setup, so the build will still be attempted using every DLL in the Reqs folder."
}

Push-Location $ProjectRoot
try {
    dotnet clean $Project -c $Configuration | Out-Host
    dotnet build $Project -c $Configuration -p:ValheimLibDir="$LibDir" | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE" }

    $Dll = Join-Path $ProjectRoot "bin\$Configuration\ValheimPortalList.dll"
    if (-not (Test-Path -LiteralPath $Dll)) { throw "Build succeeded but DLL was not found at $Dll" }

    New-Item -ItemType Directory -Force -Path $Dist | Out-Null
    Copy-Item -LiteralPath $Dll -Destination (Join-Path $Dist "ValheimPortalList.dll") -Force
    Write-Host "DLL: $(Join-Path $Dist 'ValheimPortalList.dll')"

    if ($Package) {
        & (Join-Path $ProjectRoot "package.ps1") -LibDir $LibDir -Configuration $Configuration -SkipBuild -OutDir $Dist
        if ($LASTEXITCODE -ne 0) { throw "package.ps1 failed with exit code $LASTEXITCODE" }
    }
}
finally {
    Pop-Location
}
