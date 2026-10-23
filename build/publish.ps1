param([string]$Configuration = 'Release', [switch]$FrameworkDependent)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$output = Join-Path $root 'artifacts/win-x64'
$contained = if ($FrameworkDependent) { 'false' } else { 'true' }
& dotnet publish (Join-Path $root 'src/Astria/Astria.csproj') -c $Configuration -r win-x64 --self-contained $contained -p:IncludeSourceRevisionInInformationalVersion=false -o $output --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
Write-Output $output
