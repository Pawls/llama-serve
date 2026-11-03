# Starts ninfer-3090 on GPU 1 alone at 127.0.0.1:8412, after stopping Strata and the router. Ready in 8-13 s.
#   .\start-ninfer.ps1                  Qwen3.8-27B (run.bat's qwen38-27b profile)
#   .\start-ninfer.ps1 -Model <name>    another run.bat model; its .ninfer file must be in F:\AI\ninfer-3090\models
# The router must be stopped, not just unloaded: any client request would load a two-GPU preset beside ninfer.
# See README § ninfer-3090.
param([string]$Model = 'qwen38-27b')
$ErrorActionPreference = 'Stop'

$Build     = 'ninfer-rtx3090-windows-x64-0.17.1-rtx3090'
$NinferDir = 'F:\AI\ninfer-3090'
$RunScript = Join-Path $NinferDir "$Build\run.bat"
$LogDir    = Join-Path $PSScriptRoot 'logs'
$Log       = Join-Path $LogDir 'ninfer.log'

if (-not (Test-Path $RunScript)) { throw "$RunScript not found." }
if (Get-Process ninfer-serve -ErrorAction SilentlyContinue) { throw 'ninfer is already running.' }

& (Join-Path $PSScriptRoot 'stop-strata.ps1')
& (Join-Path $PSScriptRoot 'stop-router.ps1')

# These apply to this process and its children only. The default port, 8080, is signal-cli's, and
# NINFER_GRAFTS=off stops the downloader from fetching a private graft through the Hugging Face login.
$env:CUDA_DEVICE_ORDER = 'PCI_BUS_ID'
$env:CUDA_VISIBLE_DEVICES = '1'
$env:NINFER_PORT = '8412'
$env:NINFER_GRAFTS = 'off'
$env:NINFER_MODEL_DIR = Join-Path $NinferDir 'models'

New-Item -ItemType Directory -Force $LogDir | Out-Null
if (Test-Path $Log) { Move-Item -Force $Log (Join-Path $LogDir 'ninfer.prev.log') }

# stdin from nul: run.bat must never wait on a prompt while it runs hidden. v0.17.1's run.bat prints
# "FINDSTR: Cannot open >nul" once per NINFER_* variable it checks (ashalliants/ninfer-3090#434); those lines are dropped.
cmd /c "`"$RunScript`" $Model < nul 2>&1" |
    Where-Object { $_ -notlike 'FINDSTR: Cannot open >nul*' } |
    Tee-Object -FilePath $Log
