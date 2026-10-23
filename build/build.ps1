param([string]$Configuration = 'Release', [switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
& dotnet build (Join-Path $root 'Astria.slnx') -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
if (-not $SkipTests) {
    & dotnet test (Join-Path $root 'Astria.slnx') -c $Configuration --no-build --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
}
