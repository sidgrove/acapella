<#
.SYNOPSIS
  Builds the distributable Acapella.executable.

.DESCRIPTION
  Produces a self-contained, single-file win-x64 build in windows/dist/. The platform
  layer and NAudio travel beside the exe rather than inside it - see Murmur.App.csproj for
  why - so the whole dist folder is the artifact, not the exe alone.

  Run install.ps1 afterwards to put it in Start and on the taskbar, or copy the dist folder
  anywhere and double-click Acapella.exe.

.PARAMETER Runtime
  win-x64 (default) or win-arm64. Use the ARM build on ARM machines: the x64 one runs under
  emulation and transcription is dramatically slower.
#>
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64'
)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$dist = Join-Path $PSScriptRoot 'dist'
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }

dotnet publish src/Murmur.App/Murmur.App.csproj `
    --configuration Release --runtime $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none `
    --output $dist
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

# The published exe must actually start out of the bundle before anyone ships it.
$exe = Join-Path $dist 'Acapella.exe'
$run = Start-Process -FilePath $exe -ArgumentList '--selftest' -Wait -PassThru -NoNewWindow
if ($run.ExitCode -ne 0) { throw "self-test failed with exit code $($run.ExitCode)" }

$size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host ""
Write-Host "published $dist  ($size MB exe)"
Get-ChildItem $dist | Select-Object Name, @{n='MB';e={[math]::Round($_.Length/1MB,2)}} | Format-Table
