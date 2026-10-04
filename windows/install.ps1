<#
.SYNOPSIS
  Installs Acapella for the current user. No administrator rights needed.

.DESCRIPTION
  Copies windows/dist (from publish.ps1) to %LOCALAPPDATA%\Programs\Acapella, creates a Start
  menu shortcut, registers an entry in Settings -> Apps so it can be uninstalled normally,
  and launches it.

  Why per-user and not Program Files: the model download and the log live under
  %LOCALAPPDATA% either way, the keyboard hook needs no elevation, and a per-user install
  never triggers UAC. Run with -Uninstall to remove everything but the model and history.

.PARAMETER Uninstall
  Removes the program folder, the shortcut and the Apps entry. Keeps %LOCALAPPDATA%\Murmur.
#>
param([switch]$Uninstall)
$ErrorActionPreference = 'Stop'

$name      = 'Acapella'
$target    = Join-Path $env:LOCALAPPDATA "Programs\$name"
$startMenu = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\$name.lnk"
$uninstKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$name"
$exe       = Join-Path $target 'Acapella.exe'

$dist = Join-Path $PSScriptRoot 'dist'
if (-not $Uninstall -and -not (Test-Path (Join-Path $dist 'Acapella.exe'))) {
    throw 'Extract the complete download first. Install.cmd, install.ps1 and dist must be in the same folder.'
}
Get-Process -Name $name -ErrorAction SilentlyContinue | ForEach-Object {
    Stop-Process -Id $_.Id -Force
    if (-not $_.WaitForExit(5000)) { throw 'Acapella did not exit. Quit it and retry installation.' }
}

if ($Uninstall) {
    Remove-Item $target -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item $startMenu -Force -ErrorAction SilentlyContinue
    Remove-Item $uninstKey -Recurse -Force -ErrorAction SilentlyContinue
    Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name $name -ErrorAction SilentlyContinue
    Write-Host "removed $name (model and history kept in $env:LOCALAPPDATA\Acapella)"
    return
}

$dist = Join-Path $PSScriptRoot 'dist'
if (-not (Test-Path (Join-Path $dist 'Acapella.exe'))) {
    throw "nothing to install - run publish.ps1 first"
}

New-Item -ItemType Directory -Force $target | Out-Null
Copy-Item (Join-Path $dist '*') $target -Recurse -Force

$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($startMenu)
$link.TargetPath = $exe
$link.WorkingDirectory = $target
$link.IconLocation = "$exe,0"
$link.Description = 'Push-to-talk dictation'
$link.Save()

$size = [math]::Round((Get-ChildItem $target -Recurse | Measure-Object Length -Sum).Sum / 1KB)
New-Item -Path $uninstKey -Force | Out-Null
Set-ItemProperty $uninstKey DisplayName $name
Set-ItemProperty $uninstKey DisplayIcon "$exe,0"
Set-ItemProperty $uninstKey DisplayVersion '1.0.0'
Set-ItemProperty $uninstKey Publisher 'Sidgrove'
Set-ItemProperty $uninstKey InstallLocation $target
Set-ItemProperty $uninstKey EstimatedSize ([int]$size) -Type DWord
Set-ItemProperty $uninstKey NoModify 1 -Type DWord
Set-ItemProperty $uninstKey NoRepair 1 -Type DWord
Set-ItemProperty $uninstKey UninstallString "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$target\install.ps1`" -Uninstall"
Copy-Item $PSCommandPath (Join-Path $target 'install.ps1') -Force

Start-Process $exe
Write-Host "installed to $target"
Write-Host "Start menu: $name   -   uninstall from Settings > Apps"
