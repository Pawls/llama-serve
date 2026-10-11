# Starts the llama.cpp router on 127.0.0.1:8410 with presets.ini.
# After every boot, a hit here means a degraded load that still passes functional checks:
#   Select-String -Path F:\AI\llama-serve\logs\server.log -Pattern 'retrying without pipeline'
$ErrorActionPreference = 'Stop'

$Build   = 'b10951'
$Exe     = "F:\AI\llama.cpp\$Build\llama-server.exe"
$Root    = $PSScriptRoot
$Presets = Join-Path $Root 'presets.ini'
$LogDir  = Join-Path $Root 'logs'
$Log     = Join-Path $LogDir 'server.log'

# CUDA's default order is fastest-first; two identical 3090s tie, so tensor-split could land reversed.
$env:CUDA_DEVICE_ORDER = 'PCI_BUS_ID'

New-Item -ItemType Directory -Force $LogDir | Out-Null
# One log per boot, so the pipeline-fallback grep only sees this boot.
if (Test-Path $Log) { Move-Item -Force $Log (Join-Path $LogDir 'server.prev.log') }

# 8410: signal-cli holds 8080 (WSL mirrored networking shares the port namespace) and club-3090
# compose lanes claim 8020-8213 when up. Pi reaches it through user-scope LLAMA_BASE_URL.

# The router's own --sleep-idle-seconds is not passed to its child servers; only a preset key is. So the idle
# setting goes into a generated copy of presets.ini, leaving the hand-edited file untouched.
. (Join-Path $Root 'tray-settings.ps1')
$IdleMinutes = Get-TraySetting 'RouterIdleMinutes'
if ($IdleMinutes -gt 0) {
    $Presets = Join-Path $LogDir 'presets.run.ini'
    $text = Get-Content (Join-Path $Root 'presets.ini') -Raw
    $text -replace '(?m)^\[\*\]\r?$', "[*]`nsleep-idle-seconds = $($IdleMinutes * 60)" | Set-Content $Presets -NoNewline
}

$ArgLine = "`"$Exe`" --models-preset `"$Presets`" --models-max 1 --host 127.0.0.1 --port 8410"
# cmd merges stderr at the OS level, so Tee-Object receives plain lines rather than PowerShell ErrorRecords.
cmd /c "$ArgLine 2>&1" | Tee-Object -FilePath $Log
