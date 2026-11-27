# Regenerates the llama-router, strata, ninfer and llama-serve (front door :8409) providers in opencode's config from the files that define them,
# so the model list, context sizes and image/reasoning flags never drift from the servers. Keeps every other key of
# the file (mcp, ...) and keeps a model's display name when the file already has one for that id.
#   .\sync-opencode.ps1            write ~\.config\opencode\opencode.jsonc (restart opencode to pick it up)
#   .\sync-opencode.ps1 -WhatIf    print what would change, write nothing
# Sources: presets.ini (router: ctx-size, mmproj, reasoning, chat-template-kwargs), F:\AI\strata\terminal\strata-*.json
# (Strata: model_name, --max-context) and the $Inkling table in start-ninfer.ps1 (ninfer: context, GPUs, file).
# Run it after editing any of those. Strata's and ninfer's start-up scripts stay the owners of those values.
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$Config = (Join-Path $HOME '.config\opencode\opencode.jsonc'),
    [string]$StrataDir = (& { . (Join-Path $PSScriptRoot 'strata-dir.ps1'); $StrataDir }),
    # Catalog providers opencode would otherwise connect from environment variables (models.dev ids).
    [string[]]$DisableProviders = @('cloudflare-ai-gateway', 'cloudflare-workers-ai', 'huggingface')
)
$ErrorActionPreference = 'Stop'

$existing = if (Test-Path $Config) { Get-Content $Config -Raw | ConvertFrom-Json -AsHashtable } else { @{} }
$oldProviders = if ($existing.providers) { $existing.providers } else { @{} }

function Get-OldName([string]$Provider, [string]$Id) {
    $old = $oldProviders[$Provider]
    if ($old -and $old.models -and $old.models[$Id]) { return $old.models[$Id].name }
}

function New-Model([string]$Name, [int]$Context, [bool]$Image, $Effort) {
    $caps = [ordered]@{ tools = $true; input = @(if ($Image) { 'text', 'image' } else { 'text' }); output = @('text') }
    if ($Effort) { $caps.reasoning = $Effort }
    [ordered]@{ name = $Name; capabilities = $caps; limit = [ordered]@{ context = $Context } }
}

. (Join-Path $PSScriptRoot 'model-sources.ps1')

# Router: one model per preset section.
$router = [ordered]@{}
$presets = Get-RouterPresets (Join-Path $PSScriptRoot 'presets.ini')
foreach ($id in $presets.Keys) {
    $p = $presets[$id]
    $kwargs = [string]$p['chat-template-kwargs']
    $effort = if ($p['reasoning'] -eq 'on' -and $kwargs -match 'reasoning_effort') {
        [ordered]@{ supported = $true; effort = @('low', 'medium', 'xhigh') }
    }
    $fallback = ($id -replace '-', ' ') + ' (router)'
    $name = (Get-OldName 'llama-router' $id) ?? $fallback
    $router[$id] = New-Model $name ([int]$p['ctx-size']) ([bool]$p['mmproj']) $effort
}

# Strata: one alias per installed size, all answered by whichever size is loaded.
$strata = [ordered]@{}
$strataEffort = [ordered]@{ supported = $true; effort = @('low', 'medium', 'high'); toggle = $true }
foreach ($s in Get-StrataConfigs $StrataDir) {
    $fallback = "$($s.ModelName) (Strata)"
    $strata[$s.ModelName] = New-Model ((Get-OldName 'strata' $s.ModelName) ?? $fallback) $s.Context $s.Vision $strataEffort
}

# ninfer: the Inkling profiles.
$ninfer = [ordered]@{}
$ninferEffort = [ordered]@{ supported = $true; effort = @('minimal', 'low', 'medium', 'high', 'xhigh'); toggle = $true }
foreach ($n in Get-NinferProfiles (Join-Path $PSScriptRoot 'start-ninfer.ps1')) {
    $gpus = if ($n.Gpu1) { 'GPU 1' } else { '2 GPUs' }
    $name = "Inkling 2 Mini $($n.Quant) (ninfer, $gpus, $($n.Context / 1024)K)"
    $ninfer[$n.Id] = New-Model $name $n.Context $n.Vision $ninferEffort
}

# Front door: every id of the three engines on one port. The ninfer copy of the router's qwen3.8-27b is listed as
# ninfer/qwen3.8-27b (README § Using the front door); its context is $NinferQwen's (model-sources.ps1).
$door = [ordered]@{}
foreach ($set in $router, $strata, $ninfer) {
    foreach ($id in $set.Keys) {
        $m = $set[$id]
        $door[$id] = New-Model ($m.name -replace '\)$', ', front door)') $m.limit.context ($m.capabilities.input -contains 'image') $m.capabilities.reasoning
    }
}
$door['ninfer/qwen3.8-27b'] = New-Model 'Qwen3.8-27B (ninfer, GPU 1, front door)' $NinferQwen.Context $false $null
$tokenFile = (Join-Path $PSScriptRoot 'logs\switchboard-token') -replace '\\', '/'

$engine = '@opencode/ai/providers/openai-compatible'
$existing.providers = [ordered]@{
    'llama-router' = [ordered]@{ name = 'llama-serve router (llama.cpp, :8410)'; package = $engine
        settings = @{ baseURL = 'http://127.0.0.1:8410/v1' }; models = $router }
    strata = [ordered]@{ name = 'Strata (Qwen3.8-Flash-Next, :8411)'; package = $engine
        settings = @{ baseURL = 'http://127.0.0.1:8411/v1' }; models = $strata }
    ninfer = [ordered]@{ name = 'ninfer (Inkling 2 Mini, :8412)'; package = $engine
        settings = @{ baseURL = 'http://127.0.0.1:8412/v1' }; models = $ninfer }
    # The token is read from its file when opencode loads the config, so it never lands in this file.
    'llama-serve' = [ordered]@{ name = 'llama-serve front door (all engines, :8409)'; package = $engine
        settings = @{ baseURL = 'http://127.0.0.1:8409/v1' }
        headers = [ordered]@{ Authorization = "Bearer {file:$tokenFile}" }; models = $door }
}
$existing.disabled_providers = $DisableProviders
$header = "// Generated by F:\AI\llama-serve\sync-opencode.ps1 (providers only; other keys are kept). Edit the sources, then rerun it.`n" +
    "// The llama-serve provider (:8409) needs the tray's front door on and starts whichever engine serves the model.`n" +
    "// Router autostarts; Strata (:8411) and ninfer (:8412) start from the tray and only one engine holds the GPUs.`n"
$json = $header + ($existing | ConvertTo-Json -Depth 12)

if ($PSCmdlet.ShouldProcess($Config, "write $($router.Count) router, $($strata.Count) Strata, $($ninfer.Count) ninfer, $($door.Count) front-door models")) {
    Set-Content -Path $Config -Value $json -Encoding utf8NoBOM
    "Wrote $Config ($($router.Count) router, $($strata.Count) Strata, $($ninfer.Count) ninfer, $($door.Count) front-door models)"
}
