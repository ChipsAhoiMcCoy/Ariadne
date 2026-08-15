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
$publicProfile = [Environment]::GetEnvironmentVariable("PUBLIC")
if ([string]::IsNullOrWhiteSpace($publicProfile)) {
    throw "Unable to locate the Windows Public profile for an identity-neutral build path."
}

# tModLoader records the absolute source folder in the packaged Info metadata and
# portable PDB. Building directly from a user profile would therefore disclose that
# profile name even when build.txt uses a pseudonym. Stage the source beneath the
# identity-neutral Public profile and remove it after packaging.
$stagingBase = [IO.Path]::GetFullPath((Join-Path $publicProfile "AriadneBuild"))
$stagingRoot = [IO.Path]::GetFullPath((Join-Path $stagingBase ([Guid]::NewGuid().ToString("N"))))
$stagedModSource = Join-Path $stagingRoot "Ariadne"

Write-Host "Using tModLoader: $tmlPath"
Write-Host "Using dotnet: $dotnetPath"

New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null
try {
    Copy-Item -LiteralPath $modSource -Destination $stagedModSource -Recurse

    Push-Location $tmlPath
    try {
        & $dotnetPath "tModLoader.dll" "-build" $stagedModSource
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
}
finally {
    $validatedBase = $stagingBase.TrimEnd([IO.Path]::DirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar
    $validatedTarget = [IO.Path]::GetFullPath($stagingRoot)
    if (-not $validatedTarget.StartsWith($validatedBase, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove an unexpected build staging path: $validatedTarget"
    }
    if (Test-Path -LiteralPath $validatedTarget) {
        Remove-Item -LiteralPath $validatedTarget -Recurse -Force
    }
}

if ($exitCode -ne 0) {
    throw "Ariadne build failed with exit code $exitCode."
}
