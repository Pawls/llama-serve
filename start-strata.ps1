# Starts Strata (Qwen3.8-Flash-Next) on 127.0.0.1:8411 after freeing the GPUs from the router's model.
# Strata's own flags live in the run-<model>.bat its setup wrote; this script only picks and launches it.
param([string]$Model = 'iq2_xs')
$ErrorActionPreference = 'Stop'

$StrataDir = 'F:\AI\strata'
$RunScript = Join-Path $StrataDir "run-$Model.bat"
$LogDir    = Join-Path $PSScriptRoot 'logs'
$Log       = Join-Path $LogDir 'strata.log'

if (-not (Test-Path $RunScript)) { throw "$RunScript not found. Install the model with $StrataDir\SETUP.bat first." }

# Strata fills both cards' VRAM with its expert caches; a router model loaded beside it fails or slows both.
& (Join-Path $PSScriptRoot 'unload-router-model.ps1')

. (Join-Path $PSScriptRoot 'tray-settings.ps1')
$IdleMinutes = Get-TraySetting 'StrataIdleMinutes'
if ($null -ne $IdleMinutes) {
    # Strata reads idle_unload_s from its config at start; a missing key means it never unloads.
    $ConfigPath = Join-Path $StrataDir "strata-$Model.json"
    $strataConfig = Get-Content $ConfigPath -Raw | ConvertFrom-Json
    $wanted = $IdleMinutes * 60
    if ($strataConfig.idle_unload_s -ne $wanted -and ($wanted -gt 0 -or $strataConfig.idle_unload_s)) {
        Copy-Item $ConfigPath "$ConfigPath.bak" -Force
        $strataConfig.PSObject.Properties.Remove('idle_unload_s')
        if ($wanted -gt 0) { $strataConfig | Add-Member -NotePropertyName idle_unload_s -NotePropertyValue $wanted }
        $strataConfig | ConvertTo-Json -Depth 20 | Set-Content $ConfigPath
    }
}

New-Item -ItemType Directory -Force $LogDir | Out-Null
if (Test-Path $Log) { Move-Item -Force $Log (Join-Path $LogDir 'strata.prev.log') }

# The run script ends with "if errorlevel 1 pause"; stdin from nul lets that return instead of hanging hidden.
cmd /c "`"$RunScript`" < nul 2>&1" | Tee-Object -FilePath $Log
