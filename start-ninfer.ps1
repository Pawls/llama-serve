# Starts ninfer at 127.0.0.1:8412, after stopping Strata and the router. Ready in 8-15 s.
#   .\start-ninfer.ps1                             Qwen3.8-27B on GPU 1 (ninfer-3090 v0.18.0, run.bat's qwen38-27b profile)
#   .\start-ninfer.ps1 -Model inkling2-mini        Inkling 2 Mini UD-IQ4_XS (ninfer-all) on GPU 1, 256K context
#   .\start-ninfer.ps1 -Model inkling2-mini-2gpu   the same file on both GPUs, 768K context
#   .\start-ninfer.ps1 -Model inkling2-mini-q8     Inkling 2 Mini UD-Q8_K_XL on both GPUs, 512K context
#   .\start-ninfer.ps1 -Model <name>               another v0.18.0 run.bat model; its .ninfer file must be in F:\AI\ninfer-3090\models
# Every Inkling profile has images and JSON mode. Its model id is the profile name with dashes (inkling-2-mini-2gpu).
# The router must be stopped, not just unloaded: any client request would load a two-GPU preset beside ninfer.
# See README § ninfer-3090 and § Inkling 2 Mini.
param([string]$Model = 'qwen38-27b')
$ErrorActionPreference = 'Stop'

$NinferDir = 'F:\AI\ninfer-3090'
$Build     = 'ninfer-rtx3090-windows-x64-0.18.0-rtx3090'
# ninfer-all continues the ninfer-3090 line and is the only build with Inkling 2; its run.bat has no profile for it.
$InklingBuild = 'ninfer-windows-x64-cuda12.9-sm70-75-80-86-89-5fdefb3764c9'
$IQ4XS = 'models\inkling2-iq4xs\Inkling-2-Mini-UD-IQ4_XS-ninfer-v3.ninfer'
$Q8KXL = 'models\inkling2-q8kxl\Inkling-2-Mini-UD-Q8_K_XL-ninfer-v3.ninfer'
# The KV cache (MXFP8 by default, 16.5 KiB a token) stays whole on the first device, so context is bounded by GPU 1.
# Two GPUs divide only the routed experts (--tensor-parallel); the split puts most of them on GPU 0 to leave GPU 1
# room for the cache: 768K starts only with 1,7 (1,3 is 71 MiB short). Measured 2026-11-23 (README § Inkling 2 Mini),
# GPU 1 / GPU 0 with the desktop: 1 GPU 23.0 GiB; 2gpu 22.5 / 13.5 GiB; q8 21.8 / 15.6 GiB.
$TwoGpus = '--devices 1,0 --tensor-parallel'
$Inkling = @{
    'inkling2-mini'      = @{ File = $IQ4XS; Context = 262144; Gpus = '' }
    'inkling2-mini-2gpu' = @{ File = $IQ4XS; Context = 786432; Gpus = "$TwoGpus --tensor-split 1,7" }
    'inkling2-mini-q8'   = @{ File = $Q8KXL; Context = 524288; Gpus = "$TwoGpus --tensor-split 1,3" }
}
$LogDir = Join-Path $PSScriptRoot 'logs'
$Log    = Join-Path $LogDir 'ninfer.log'

$VisibleGpus = '1'
if ($Inkling.ContainsKey($Model)) {
    $Spec = $Inkling[$Model]
    $Server = Join-Path $NinferDir "$InklingBuild\ninfer-serve.exe"
    $Artifact = Join-Path $NinferDir $Spec.File
    foreach ($path in $Server, $Artifact) { if (-not (Test-Path $path)) { throw "$path not found." } }
    if ($Spec.Gpus) {
        . (Join-Path $PSScriptRoot 'tray-settings.ps1')
        if (Get-TraySetting 'ReserveGpu0') { throw 'GPU 0 is reserved for ComfyUI (tray: Reserve GPU 0). This profile uses both GPUs; untick it first.' }
        $VisibleGpus = '0,1'   # ninfer's device 1 is then GPU 1 under PCI_BUS_ID order
    }
    $Command = "`"$Server`" `"$Artifact`" --model-id $($Model -replace '^inkling2', 'inkling-2')" +
        " --host 127.0.0.1 --port 8412 --max-context $($Spec.Context) --vision --structured-output $($Spec.Gpus)"
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
$env:CUDA_VISIBLE_DEVICES = $VisibleGpus
$env:NINFER_PORT = '8412'
$env:NINFER_GRAFTS = 'off'
$env:NINFER_MODEL_DIR = Join-Path $NinferDir 'models'

New-Item -ItemType Directory -Force $LogDir | Out-Null
if (Test-Path $Log) { Move-Item -Force $Log (Join-Path $LogDir 'ninfer.prev.log') }

# stdin from nul: run.bat or ninfer-serve must never wait on a prompt while it runs hidden.
cmd /c "$Command < nul 2>&1" | Tee-Object -FilePath $Log
