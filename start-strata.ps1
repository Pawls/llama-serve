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

New-Item -ItemType Directory -Force $LogDir | Out-Null
if (Test-Path $Log) { Move-Item -Force $Log (Join-Path $LogDir 'strata.prev.log') }

# The run script ends with "if errorlevel 1 pause"; stdin from nul lets that return instead of hanging hidden.
cmd /c "`"$RunScript`" < nul 2>&1" | Tee-Object -FilePath $Log
