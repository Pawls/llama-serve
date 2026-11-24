# Regenerates the llama-router, strata and ninfer providers in opencode's config from the files that define them,
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

function Get-KeyValueSections([string]$Path) {
    $sections = [ordered]@{}
    $current = $null
    foreach ($line in Get-Content $Path) {
        if ($line -match '^\[(.+)\]\s*$') { $current = $sections[$Matches[1]] = @{} }
        elseif ($current -and $line -match '^([\w-]+)\s*=\s*(.*?)\s*$') { $current[$Matches[1]] = $Matches[2] }
    }
    $sections
}

# Router: one model per preset section.
$router = [ordered]@{}
$presets = Get-KeyValueSections (Join-Path $PSScriptRoot 'presets.ini')
foreach ($id in $presets.Keys | Where-Object { $_ -ne '*' }) {
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
foreach ($file in Get-ChildItem (Join-Path $StrataDir 'strata-*.json') | Sort-Object Name) {
    $cfg = Get-Content $file -Raw | ConvertFrom-Json
    $context = [int]$cfg.args[[array]::IndexOf($cfg.args, '--max-context') + 1]
    $fallback = "$($cfg.model_name) (Strata)"
    $strata[$cfg.model_name] = New-Model ((Get-OldName 'strata' $cfg.model_name) ?? $fallback) $context $true $strataEffort
}

# ninfer: the Inkling profiles; the model id is the profile name with dashes, as start-ninfer.ps1 passes it.
$ninferScript = Get-Content (Join-Path $PSScriptRoot 'start-ninfer.ps1') -Raw
$quants = @{}
[regex]::Matches($ninferScript, '(?m)^\$(\w+)\s*=\s*''models\\[^\\]+\\Inkling-2-Mini-(UD-\w+?)-ninfer') |
    ForEach-Object { $quants[$_.Groups[1].Value] = $_.Groups[2].Value }
$ninfer = [ordered]@{}
$ninferEffort = [ordered]@{ supported = $true; effort = @('minimal', 'low', 'medium', 'high', 'xhigh'); toggle = $true }
$profiles = [regex]::Matches($ninferScript, "(?m)^\s*'(inkling2-[\w-]+)'\s*=\s*@\{ File = \`$(\w+); Context = (\d+); Gpus = ('')?")
foreach ($m in $profiles) {
    $id = $m.Groups[1].Value -replace '^inkling2', 'inkling-2'
    $context = [int]$m.Groups[3].Value
    $gpus = if ($m.Groups[4].Success) { 'GPU 1' } else { '2 GPUs' }
    $name = "Inkling 2 Mini $($quants[$m.Groups[2].Value]) (ninfer, $gpus, $($context / 1024)K)"
    $ninfer[$id] = New-Model $name $context $true $ninferEffort
}
if ($ninfer.Count -eq 0) { throw 'No Inkling profiles found in start-ninfer.ps1; its $Inkling table changed shape.' }

$engine = '@opencode/ai/providers/openai-compatible'
$existing.providers = [ordered]@{
    'llama-router' = [ordered]@{ name = 'llama-serve router (llama.cpp, :8410)'; package = $engine
        settings = @{ baseURL = 'http://127.0.0.1:8410/v1' }; models = $router }
    strata = [ordered]@{ name = 'Strata (Qwen3.8-Flash-Next, :8411)'; package = $engine
        settings = @{ baseURL = 'http://127.0.0.1:8411/v1' }; models = $strata }
    ninfer = [ordered]@{ name = 'ninfer (Inkling 2 Mini, :8412)'; package = $engine
        settings = @{ baseURL = 'http://127.0.0.1:8412/v1' }; models = $ninfer }
}
$existing.disabled_providers = $DisableProviders
$header = "// Generated by F:\AI\llama-serve\sync-opencode.ps1 (providers only; other keys are kept). Edit the sources, then rerun it.`n" +
    "// Router autostarts; Strata (:8411) and ninfer (:8412) start from the tray and only one engine holds the GPUs.`n"
$json = $header + ($existing | ConvertTo-Json -Depth 12)

if ($PSCmdlet.ShouldProcess($Config, "write $($router.Count) router, $($strata.Count) Strata, $($ninfer.Count) ninfer models")) {
    Set-Content -Path $Config -Value $json -Encoding utf8NoBOM
    "Wrote $Config ($($router.Count) router, $($strata.Count) Strata, $($ninfer.Count) ninfer models)"
}
