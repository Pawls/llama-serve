# Builds the model finder's catalog (plans/model-finder.md § S1): one entry per router preset, Strata size and ninfer
# profile, with context, images, GPUs, thinking and drafter read from the launch files (model-sources.ps1), and the
# description, family, measured speed and code-only flag from model-catalog.json. The tray runs it when the catalog
# is older than a source; run it by hand to see what the page will show.
#   .\model-catalog.ps1                      write logs\model-catalog.json
#   .\model-catalog.ps1 -Out <file>          write elsewhere
# An entry with no model-catalog.json row is still listed and named under "missing"; add its row.
param(
    [string]$Out = (Join-Path $PSScriptRoot 'logs\model-catalog.json'),
    [string]$Presets = (Join-Path $PSScriptRoot 'presets.ini'),
    [string]$NinferScript = (Join-Path $PSScriptRoot 'start-ninfer.ps1'),
    [string]$Metadata = (Join-Path $PSScriptRoot 'model-catalog.json'),
    [string]$StrataDir = (& { . (Join-Path $PSScriptRoot 'strata-dir.ps1'); $StrataDir })
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'model-sources.ps1')

$meta = Get-Content $Metadata -Raw | ConvertFrom-Json -AsHashtable
$missing = [Collections.Generic.List[string]]::new()

function New-Entry([string]$Id, [string]$Engine, [string]$ModelId, [int]$Context, [bool]$Vision, [bool]$Gpu1,
                   [string]$Thinking, [string]$Drafter) {
    $row = $meta.entries[$Id]
    if (-not $row) { $missing.Add($Id); $row = @{} }
    [ordered]@{
        id          = $Id
        engine      = $Engine
        modelId     = $ModelId
        family      = $row.family ?? $Id
        description = $row.description ?? ''
        context     = $Context
        vision      = $Vision
        gpus        = if ($Gpu1) { 'gpu1' } else { 'both' }
        # Router presets carry their own thinking setting; Strata and ninfer have none in their launch files.
        thinking    = if ($Thinking) { $Thinking } else { $row.thinking ?? 'none' }
        drafter     = if ($Drafter) { $Drafter } else { $null }
        codeOnly    = [bool]$row.codeOnly
        speed       = $row.speed
    }
}

function Get-RouterThinking($Preset) {
    if ($Preset['reasoning-budget'] -eq '-1') { return 'always' }
    if ($Preset['reasoning']) { return $Preset['reasoning'] }
    'none'
}

$entries = [Collections.Generic.List[object]]::new()
$routerPresets = Get-RouterPresets $Presets
foreach ($id in $routerPresets.Keys) {
    $p = $routerPresets[$id]
    # The same rule start-router.ps1 uses for Reserve GPU 0: every device line names CUDA1.
    $devices = @($p['device'], $p['spec-draft-device']) | Where-Object { $_ }
    $gpu1 = $devices.Count -gt 0 -and -not ($devices | Where-Object { $_ -ne 'CUDA1' })
    $drafter = switch ($p['spec-type']) { 'draft-mtp' { 'mtp' } 'draft-dflash' { 'dflash' } default { $null } }
    $entries.Add((New-Entry $id 'router' $id ([int]$p['ctx-size']) ([bool]$p['mmproj']) $gpu1 (Get-RouterThinking $p) $drafter))
}
foreach ($s in Get-StrataConfigs $StrataDir) {
    $entries.Add((New-Entry $s.Size 'strata' $s.ModelName $s.Context $s.Vision $s.Gpu1 $null $null))
}
foreach ($n in @(Get-NinferProfiles $NinferScript) + $NinferQwen) {
    $entries.Add((New-Entry $n.Profile 'ninfer' $n.Id $n.Context $n.Vision $n.Gpu1 $null $null))
}

$catalog = [ordered]@{
    generated  = (Get-Date).ToString('o')
    entries    = $entries
    missing    = $missing
    families   = $meta.families ?? @{}
    benchmarks = $meta.benchmarks ?? @{}
}
New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
Set-Content -Path $Out -Value ($catalog | ConvertTo-Json -Depth 8) -Encoding utf8NoBOM
if ($missing.Count) { Write-Warning "No model-catalog.json row for: $($missing -join ', ')" }
