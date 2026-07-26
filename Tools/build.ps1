Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-TModLoaderPath {
    $candidates = [System.Collections.Generic.List[string]]::new()

    foreach ($name in @("TML_INSTALL_PATH", "TERRARIA_TML_PATH", "TMLSteamPath")) {
        $value = [Environment]::GetEnvironmentVariable($name)
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            $candidates.Add($value)
        }
    }

    foreach ($programFilesName in @("ProgramFiles(x86)", "ProgramFiles")) {
        $programFiles = [Environment]::GetEnvironmentVariable($programFilesName)
        if (-not [string]::IsNullOrWhiteSpace($programFiles)) {
            $candidates.Add((Join-Path $programFiles "Steam\steamapps\common\tModLoader"))
        }
    }

    foreach ($candidate in $candidates) {
        $expanded = [Environment]::ExpandEnvironmentVariables($candidate)
        if (Test-Path -LiteralPath (Join-Path $expanded "tModLoader.dll")) {
            return (Resolve-Path -LiteralPath $expanded).Path
        }
    }

    throw "Unable to locate tModLoader. Set TML_INSTALL_PATH to its installation directory."
}

function Resolve-DotNetPath([string]$TModLoaderPath) {
    $embedded = Join-Path $TModLoaderPath "dotnet\dotnet.exe"
    if (Test-Path -LiteralPath $embedded) {
        return $embedded
    }

    $globalDotNet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -ne $globalDotNet) {
        return $globalDotNet.Path
    }

    throw "Unable to locate a dotnet host."
}

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$modSource = (Resolve-Path -LiteralPath (Join-Path $repoRoot "Mods\Ariadne")).Path
$tmlPath = Resolve-TModLoaderPath
$dotnetPath = Resolve-DotNetPath -TModLoaderPath $tmlPath

Write-Host "Using tModLoader: $tmlPath"
Write-Host "Using dotnet: $dotnetPath"

Push-Location $tmlPath
try {
    & $dotnetPath "tModLoader.dll" "-build" $modSource
    $exitCode = $LASTEXITCODE
}
finally {
    Pop-Location
}

if ($exitCode -ne 0) {
    throw "Ariadne build failed with exit code $exitCode."
}
