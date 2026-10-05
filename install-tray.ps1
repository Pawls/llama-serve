# Builds the tray app (tray\LlamaTray.cs -> tray\bin\LlamaTray.exe), adds a "Llama Router" Start menu
# shortcut, points the llama-router logon task at the tray, and launches it. Safe to re-run after edits.
param([switch]$NoLaunch)
$ErrorActionPreference = 'Stop'

$Root     = $PSScriptRoot
$Source   = Join-Path $Root 'tray\LlamaTray.cs'
$BinDir   = Join-Path $Root 'tray\bin'
$Exe      = Join-Path $BinDir 'LlamaTray.exe'
$Ico      = Join-Path $BinDir 'LlamaTray.ico'
$Shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Llama Router.lnk'
$TaskName = 'llama-router'

# The .NET Framework compiler ships with Windows, so no SDK is needed. It speaks C# 5.
$FrameworkDir = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$Csc = Join-Path $FrameworkDir 'csc.exe'
$References = 'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll',
    'System.Net.Http.dll', 'System.Web.Extensions.dll', 'Microsoft.CSharp.dll' |
    ForEach-Object { "/reference:$(Join-Path $FrameworkDir $_)" }

function Invoke-Csc([string[]]$ExtraArgs) {
    $output = & $Csc /nologo /target:winexe /optimize+ "/out:$Exe" @References @ExtraArgs $Source
    if ($LASTEXITCODE -ne 0) { $output; throw "csc failed with exit code $LASTEXITCODE" }
}

# A running tray locks the exe. Stopping it leaves the router running; the new tray adopts it.
Get-Process LlamaTray -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $Exe } | Stop-Process -Force
Start-Sleep -Milliseconds 300

New-Item -ItemType Directory -Force $BinDir | Out-Null
# The icon is drawn by the app itself, so build once to render it, then again to embed it.
Invoke-Csc @()
& $Exe --write-icon $Ico | Out-Null
Invoke-Csc @("/win32icon:$Ico")
Write-Host "Built $Exe"

$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($Shortcut)
$link.TargetPath = $Exe
$link.WorkingDirectory = $Root
$link.Description = 'llama.cpp router in the system tray'
$link.Save()
Write-Host "Start menu shortcut: $Shortcut"

$action = New-ScheduledTaskAction -Execute $Exe -WorkingDirectory $Root
if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
    Set-ScheduledTask -TaskName $TaskName -Action $action | Out-Null
} else {
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
    $settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit 0 -MultipleInstances IgnoreNew -AllowStartIfOnBatteries
    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings | Out-Null
}
Write-Host "Logon task '$TaskName' now starts the tray."

if (-not $NoLaunch) { Start-Process $Exe; Write-Host 'Tray launched.' }
