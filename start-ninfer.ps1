# Starts ninfer on GPU 1 alone at 127.0.0.1:8412, after stopping Strata and the router. Ready in 8-15 s.
#   .\start-ninfer.ps1                        Qwen3.8-27B (ninfer-3090 v0.18.0, run.bat's qwen38-27b profile)
#   .\start-ninfer.ps1 -Model inkling2-mini   Inkling 2 Mini UD-IQ4_XS (ninfer-all), 128K context, images, JSON mode
#   .\start-ninfer.ps1 -Model <name>          another v0.18.0 run.bat model; its .ninfer file must be in F:\AI\ninfer-3090\models
# The router must be stopped, not just unloaded: any client request would load a two-GPU preset beside ninfer.
# See README § ninfer-3090 and § Inkling 2 Mini.
param([string]$Model = 'qwen38-27b')
$ErrorActionPreference = 'Stop'

$NinferDir = 'F:\AI\ninfer-3090'
$Build     = 'ninfer-rtx3090-windows-x64-0.18.0-rtx3090'
# ninfer-all continues the ninfer-3090 line and is the only build with Inkling 2; its run.bat has no profile for it.
$InklingBuild    = 'ninfer-windows-x64-cuda12.9-sm70-75-80-86-89-bd8195f42e87'
$InklingArtifact = Join-Path $NinferDir 'models\inkling2-iq4xs\Inkling-2-Mini-UD-IQ4_XS-ninfer-v3.ninfer'
$LogDir    = Join-Path $PSScriptRoot 'logs'
$Log       = Join-Path $LogDir 'ninfer.log'

if ($Model -eq 'inkling2-mini') {
    $Server = Join-Path $NinferDir "$InklingBuild\ninfer-serve.exe"
    foreach ($path in $Server, $InklingArtifact) { if (-not (Test-Path $path)) { throw "$path not found." } }
    # 128K with the vision tower leaves ~600 MiB of GPU 1 free (measured 2026-11-21); the KV cache is F16 only.
    $Command = "`"$Server`" `"$InklingArtifact`" --model-id inkling-2-mini --host 127.0.0.1 --port 8412" +
        ' --max-context 131072 --vision --structured-output'
} else {
    $RunScript = Join-Path $NinferDir "$Build\run.bat"
    if (-not (Test-Path $RunScript)) { throw "$RunScript not found." }
    $Command = "`"$RunScript`" $Model"
}
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

# stdin from nul: run.bat must never wait on a prompt while it runs hidden.
cmd /c "$Command < nul 2>&1" | Tee-Object -FilePath $Log
