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
# ReserveGpu0 (tray toggle) keeps GPU 0 free for ComfyUI: only [*] and presets pinned to CUDA1 are served.
$IdleMinutes = Get-TraySetting 'RouterIdleMinutes'
$ReserveGpu0 = Get-TraySetting 'ReserveGpu0'
if ($IdleMinutes -gt 0 -or $ReserveGpu0) {
    $Presets = Join-Path $LogDir 'presets.run.ini'
    $text = Get-Content (Join-Path $Root 'presets.ini') -Raw
    if ($ReserveGpu0) {
        # A preset passes only if it sets `device` and every device/spec-draft-device line is CUDA1.
        $onlyCuda1 = {
            param($section)
            $devices = [regex]::Matches($section, '(?m)^(?:spec-draft-)?device\s*=\s*(\S.*?)\s*$') | ForEach-Object { $_.Groups[1].Value }
            ($section -match '(?m)^device\s*=') -and -not ($devices | Where-Object { $_ -ne 'CUDA1' })
        }
        $sections = $text -split '(?m)^(?=\[)' | Where-Object { $_ -match '^\[\*\]' -or (& $onlyCuda1 $_) }
        $text = $sections -join ''
    }
    if ($IdleMinutes -gt 0) {
        $text = $text -replace '(?m)^\[\*\]\r?$', "[*]`nsleep-idle-seconds = $($IdleMinutes * 60)"
    }
    Set-Content $Presets $text -NoNewline
}

$ArgLine = "`"$Exe`" --models-preset `"$Presets`" --models-max 1 --host 127.0.0.1 --port 8410"
# cmd merges stderr at the OS level, so Tee-Object receives plain lines rather than PowerShell ErrorRecords.
cmd /c "$ArgLine 2>&1" | Tee-Object -FilePath $Log
