# Tests for model-catalog.ps1: the builder's rules on tray\tests\fixtures, then a few entries of the real files
# checked against values read from them by hand. Run by test-model-finder.ps1; exits 1 when a check fails.
$ErrorActionPreference = 'Stop'
$Root = Split-Path (Split-Path $PSScriptRoot)
$Fixtures = Join-Path $PSScriptRoot 'fixtures'
$script:Failures = 0

function Check([string]$Name, $Actual, $Expected) {
    $a = ConvertTo-Json -InputObject $Actual -Compress -Depth 5
    $e = ConvertTo-Json -InputObject $Expected -Compress -Depth 5
    if ($a -ceq $e) { Write-Host "  ok    $Name" }
    else { Write-Host "  FAIL  $Name`n        expected $e`n        actual   $a"; $script:Failures++ }
}

function Build([hashtable]$Arguments) {
    $out = Join-Path ([IO.Path]::GetTempPath()) "model-catalog-test-$PID.json"
    & pwsh -NoProfile -File (Join-Path $Root 'model-catalog.ps1') @Arguments -Out $out 2>&1 | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "model-catalog.ps1 exited $LASTEXITCODE" }
    $catalog = Get-Content $out -Raw | ConvertFrom-Json
    Remove-Item $out
    $catalog
}

function Pick($Catalog, [string]$Id) {
    $entry = $Catalog.entries | Where-Object id -eq $Id
    if (-not $entry) { return $null }
    [ordered]@{ engine = $entry.engine; modelId = $entry.modelId; context = $entry.context; vision = $entry.vision
        gpus = $entry.gpus; thinking = $entry.thinking; drafter = $entry.drafter; codeOnly = $entry.codeOnly }
}

Write-Host 'Fixtures'
$fx = Build @{ Presets = (Join-Path $Fixtures 'presets.ini'); NinferScript = (Join-Path $Fixtures 'start-ninfer.ps1')
    Metadata = (Join-Path $Fixtures 'model-catalog.json'); StrataDir = (Join-Path $Fixtures 'strata') }

Check 'entry order: router, Strata, ninfer' ($fx.entries.id) @('vision-mtp', 'short-dflash', 'gpu1-always', 'half-gpu1',
    'not-in-json', 'budget-zero', 'off-unlimited', 'auto-thinking', 'big-gpu1', 'big', 'blind', 'inkling2-mini',
    'inkling2-mini-q8', 'qwen38-27b')
Check 'router: mmproj, mtp, reasoning on' (Pick $fx 'vision-mtp') ([ordered]@{ engine = 'router'; modelId = 'vision-mtp'
    context = 262144; vision = $true; gpus = 'both'; thinking = 'on'; drafter = 'mtp'; codeOnly = $false })
Check 'router: dflash, reasoning off, codeOnly from the JSON' (Pick $fx 'short-dflash') ([ordered]@{ engine = 'router'
    modelId = 'short-dflash'; context = 65536; vision = $false; gpus = 'both'; thinking = 'off'; drafter = 'dflash'; codeOnly = $true })
Check 'router: reasoning-budget 0 is off, whatever reasoning says' ((Pick $fx 'budget-zero').thinking) 'off'
Check 'router: reasoning off wins over an unlimited budget' ((Pick $fx 'off-unlimited').thinking) 'off'
Check 'router: reasoning auto is on' ((Pick $fx 'auto-thinking').thinking) 'on'
Check 'router: every device line CUDA1 is gpu1; no reasoning key takes the JSON thinking'(Pick $fx 'gpu1-always') ([ordered]@{ engine = 'router'
    modelId = 'gpu1-always'; context = 131072; vision = $false; gpus = 'gpu1'; thinking = 'always'; drafter = $null; codeOnly = $false })
Check 'router: one device line off CUDA1 is both; no reasoning key is none' (Pick $fx 'half-gpu1') ([ordered]@{ engine = 'router'
    modelId = 'half-gpu1'; context = 32768; vision = $false; gpus = 'both'; thinking = 'none'; drafter = $null; codeOnly = $false })
Check 'Strata: model_name is the API id, gpu [0,1] is both, bare --vision is images' (Pick $fx 'big') ([ordered]@{ engine = 'strata'
    modelId = 'big-model'; context = 262144; vision = $true; gpus = 'both'; thinking = 'on'; drafter = $null; codeOnly = $false })
Check 'Strata: gpu 1 is gpu1, --vision with a path is images' (Pick $fx 'big-gpu1') ([ordered]@{ engine = 'strata'
    modelId = 'big-model'; context = 131072; vision = $true; gpus = 'gpu1'; thinking = 'on'; drafter = $null; codeOnly = $false })
Check 'Strata: --vision off is no images' ((Pick $fx 'blind').vision) $false
Check 'Strata: a config without run-*.bat is left out' (Pick $fx 'orphan') $null
Check 'ninfer: one-GPU Inkling' (Pick $fx 'inkling2-mini') ([ordered]@{ engine = 'ninfer'; modelId = 'inkling-2-mini'
    context = 262144; vision = $true; gpus = 'gpu1'; thinking = 'on'; drafter = $null; codeOnly = $false })
Check 'ninfer: two-GPU Inkling' ((Pick $fx 'inkling2-mini-q8').gpus) 'both'
Check 'ninfer: Qwen3.8-27B, text, GPU 1, 188416' (Pick $fx 'qwen38-27b') ([ordered]@{ engine = 'ninfer'; modelId = 'qwen3.8-27b'
    context = 188416; vision = $false; gpus = 'gpu1'; thinking = 'on'; drafter = $null; codeOnly = $false })
Check 'metadata: description, family and speed carried' (($fx.entries | Where-Object id -eq 'vision-mtp') |
    ForEach-Object { @($_.family, $_.description, $_.speed.code, $_.speed.prose) }) @('fam-a', 'A vision model with MTP.', 57, 50)
Check 'an id with no metadata row is listed under missing, family = its id' (@($fx.missing) + @(($fx.entries |
    Where-Object id -eq 'not-in-json').family)) @('not-in-json', 'not-in-json')
Check 'families and benchmarks reach the built catalog whole, down to a score and its setting' @(
    $fx.families.'fam-a'.source, $fx.families.'fam-a'.measuredOn, $fx.families.'fam-a'.scores.quiz.value,
    $fx.families.'fam-a'.scores.quiz.setting, $fx.benchmarks.quiz.name, $fx.benchmarks.quiz.higher) @(
    'https://example.test/fam-a', 'Fam A', 71.5, 'no tools', 'Quiz', 'More right.')

$noContextDir = Join-Path ([IO.Path]::GetTempPath()) "model-catalog-test-strata-$PID"
Copy-Item (Join-Path $Fixtures 'strata') $noContextDir -Recurse
Set-Content (Join-Path $noContextDir 'strata-nocontext.json') '{ "model_name": "x", "gpu": 1, "args": ["--gpus", "1"] }'
Set-Content (Join-Path $noContextDir 'run-nocontext.bat') '@echo off'
try {
    $nc = Build @{ Presets = (Join-Path $Fixtures 'presets.ini'); NinferScript = (Join-Path $Fixtures 'start-ninfer.ps1')
        Metadata = (Join-Path $Fixtures 'model-catalog.json'); StrataDir = $noContextDir }
    Check 'Strata: a config without --max-context is left out, the rest still listed' (@($nc.entries.id | Where-Object {
        $_ -in 'nocontext', 'big' })) @('big')
}
catch { Check 'Strata: a config without --max-context does not break the build' $_.Exception.Message 'no error' }
finally { Remove-Item $noContextDir -Recurse -Force }

Write-Host 'Real files'
$real = Build @{}
Check 'gemma4-31b: 65536 with images, both GPUs' ((Pick $real 'gemma4-31b') | ForEach-Object { @($_.context, $_.vision, $_.gpus) }) @(65536, $true, 'both')
Check 'qwen3.8-27b-single: 131072, text, GPU 1' ((Pick $real 'qwen3.8-27b-single') | ForEach-Object { @($_.context, $_.vision, $_.gpus) }) @(131072, $false, 'gpu1')
Check 'muse-glimmer-30b: always reasons, dflash, GPU 1' ((Pick $real 'muse-glimmer-30b') | ForEach-Object { @($_.thinking, $_.drafter, $_.gpus) }) @('always', 'dflash', 'gpu1')
Check 'Strata swift-iq3_s: 262144 with images' ((Pick $real 'swift-iq3_s') | ForEach-Object { @($_.modelId, $_.context, $_.vision) }) @('swift-1.5-iq3_s', 262144, $true)
Check 'Strata inkling-2-mini-sq-20: 368640 on both GPUs' ((Pick $real 'inkling-2-mini-sq-20') | ForEach-Object { @($_.context, $_.gpus) }) @(368640, 'both')
Check 'ninfer inkling2-mini-2gpu: 786432 on both GPUs' ((Pick $real 'inkling2-mini-2gpu') | ForEach-Object { @($_.context, $_.gpus) }) @(786432, 'both')
Check 'every real entry has a model-catalog.json row' (@($real.missing)) @()

if ($script:Failures) { Write-Host "$script:Failures check(s) failed."; exit 1 }
Write-Host 'All catalog checks passed.'
