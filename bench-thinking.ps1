# Thinking-token and accuracy A/B of logit-bias arms on one preset, thinking on.
#   .\bench-thinking.ps1 -Model qwen3.8-27b-dflash2
#   .\bench-thinking.ps1 -Model qwen3.8-27b-dflash2 -Resume logs\bench-thinking-20260928-1500.jsonl
#   .\bench-thinking.ps1 -Model gemma4-31b -ListTokens     # show the model's resolved bias tokens, run nothing
# Tasks: bench\tasks-math.jsonl (MATH-500 level 4-5, integer answers) and bench\tasks-code.jsonl (HumanEval,
# scored by running its tests). Arms come from bench\logit-bias-arms.json; their words are resolved to the model's
# own token ids, so any preset works. Biases are sent per request, so the model loads once; arms alternate within
# each task so drift hits every arm alike. Writes logs\bench-thinking-<stamp>.jsonl and a .txt summary.
# -Resume appends to an earlier run and skips what it already has.
# Code tests run in a throwaway interpreter (uv run --no-project). Needs the router on :8410.
param(
    [Parameter(Mandatory)][string]$Model,
    [string[]]$Arms = @('off', 'paper', 'narrow'),
    [string[]]$Set = @('math', 'code'),
    [ValidateSet('low', 'medium', 'high', 'xhigh')][string]$Effort = 'medium',
    [int]$Samples = 1,
    [int]$MaxTokens = 32768,
    [int]$Limit = 0,   # first N tasks of each set; 0 = all
    [string]$Resume,
    [switch]$ListTokens,
    [string]$BaseUrl = 'http://127.0.0.1:8410'
)
$ErrorActionPreference = 'Stop'

$BenchDir = Join-Path $PSScriptRoot 'bench'
$LogDir = Join-Path $PSScriptRoot 'logs'
New-Item -ItemType Directory -Force $LogDir | Out-Null
$Jsonl = $Resume ? (Resolve-Path $Resume).Path : (Join-Path $LogDir ('bench-thinking-{0:yyyyMMdd-HHmm}.jsonl' -f (Get-Date)))
$Txt = [IO.Path]::ChangeExtension($Jsonl, '.txt')
$WorkDir = Join-Path ([IO.Path]::GetTempPath()) 'bench-thinking'
New-Item -ItemType Directory -Force $WorkDir | Out-Null

$ArmSpec = Get-Content (Join-Path $BenchDir 'logit-bias-arms.json') -Raw | ConvertFrom-Json -AsHashtable
foreach ($arm in $Arms) {
    if (-not $ArmSpec.ContainsKey($arm)) { throw "Unknown arm '$arm'; see bench\logit-bias-arms.json" }
}

# Marker words counted in the reasoning text, whatever their tokenization.
$NarrowWords = 'wait', 'hmm', 'actually'
$PaperWords = @($ArmSpec.paper.words | ForEach-Object { $_.ToLower() } | Sort-Object -Unique)

# Text placed before a word to put it in each form. The prefix's own tokens are stripped off afterwards.
$FormPrefix = @{ 'leading-space' = @('.', ' '); 'line-start' = @(".`n", '') }

function Read-Jsonl([string]$Path) {
    Get-Content $Path -Encoding utf8 | Where-Object { $_.Trim() } | ForEach-Object { $_ | ConvertFrom-Json }
}

function Get-TokenIds([string]$Text) {
    $body = @{ model = $Model; content = $Text } | ConvertTo-Json
    $result = Invoke-RestMethod "$BaseUrl/tokenize" -Method Post -ContentType 'application/json; charset=utf-8' `
        -Body ([Text.Encoding]::UTF8.GetBytes($body)) -TimeoutSec 600
    @($result.tokens)
}

# The model's single token for $Word in $Form, or $null when the form splits into several tokens.
function Resolve-FormToken([string]$Word, [string]$Form) {
    $prefix, $separator = $FormPrefix[$Form]
    $prefixIds = Get-TokenIds $prefix
    $ids = Get-TokenIds "$prefix$separator$Word"
    $wordIds = @($ids | Select-Object -Skip $prefixIds.Count)
    $prefixKept = ($ids | Select-Object -First $prefixIds.Count) -join ',' -eq ($prefixIds -join ',')
    if ($prefixKept -and $wordIds.Count -eq 1) { return $wordIds[0] }
    $null
}

# Resolves every arm's words to this model's token ids: arm name -> ordered id -> label.
function Resolve-ArmTokens {
    $resolved = @{}
    foreach ($arm in $Arms) {
        $tokens = [ordered]@{}
        $skipped = @()
        foreach ($form in $ArmSpec[$arm].forms) {
            foreach ($word in $ArmSpec[$arm].words) {
                $id = Resolve-FormToken $word $form
                if ($null -eq $id) { $skipped += "$word ($form)"; continue }
                $tokens[[string]$id] = "$word ($form)"
            }
        }
        $resolved[$arm] = $tokens
        Write-Host ('{0}: {1} tokens at {2}{3}' -f $arm, $tokens.Count, $ArmSpec[$arm].bias,
            ($skipped ? "; skipped as multi-token: $($skipped -join ', ')" : ''))
    }
    $resolved
}

function Get-BiasPairs([string]$Arm) {
    foreach ($id in $ArmTokens[$Arm].Keys) { , @([int]$id, $ArmSpec[$Arm].bias) }
}

function Get-TaskPrompt($Task) {
    if ($Task.set -eq 'math') {
        return "$($Task.problem)`n`nPlease reason step by step, and put your final answer within \boxed{}."
    }
    "Complete this Python function. Reply with the whole function, including its imports and signature, " +
    "in one ``````python code block.`n`n``````python`n$($Task.prompt)``````"
}

function Invoke-Chat($Task, [string]$Arm) {
    $body = [ordered]@{
        model                = $Model
        max_tokens           = $MaxTokens
        chat_template_kwargs = @{ enable_thinking = $true; reasoning_effort = $Effort }
        messages             = @(@{ role = 'user'; content = (Get-TaskPrompt $Task) })
    }
    $pairs = @(Get-BiasPairs $Arm)
    if ($pairs.Count) { $body.logit_bias = $pairs }
    $json = $body | ConvertTo-Json -Depth 6
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $response = Invoke-RestMethod "$BaseUrl/v1/chat/completions" -Method Post -ContentType 'application/json; charset=utf-8' `
        -Body ([Text.Encoding]::UTF8.GetBytes($json)) -TimeoutSec 3600
    @{ response = $response; wall = $clock.Elapsed.TotalSeconds }
}

# Prompt tokens of one user turn holding $Text, by the server's Anthropic count_tokens endpoint.
function Get-PromptTokens([string]$Text) {
    $body = @{ model = $Model; max_tokens = 1; messages = @(@{ role = 'user'; content = $Text }) } | ConvertTo-Json -Depth 4
    $result = Invoke-RestMethod "$BaseUrl/v1/messages/count_tokens" -Method Post -ContentType 'application/json; charset=utf-8' `
        -Body ([Text.Encoding]::UTF8.GetBytes($body)) -TimeoutSec 600
    $result.input_tokens
}

# Servers without /tokenize (Strata) fall back to their own reasoning count, then to count_tokens on the text minus
# count_tokens on an empty turn (the chat template's tokens cancel), then $null.
function Measure-Tokens([string]$Text, $Usage) {
    if (-not $Text) { return 0 }
    try { return (Get-TokenIds $Text).Count } catch { }
    $reported = $Usage.completion_tokens_details.reasoning_tokens
    if ($null -ne $reported) { return $reported }
    try { return (Get-PromptTokens $Text) - (Get-PromptTokens '') } catch { $null }
}

function Measure-Words([string]$Text, [string[]]$Words) {
    if (-not $Text) { return 0 }
    $pattern = '(?i)\b(' + (($Words | ForEach-Object { [regex]::Escape($_) }) -join '|') + ')\b'
    [regex]::Matches($Text, $pattern).Count
}

# Contents of the last \boxed{...}, with nested braces.
function Get-LastBoxed([string]$Text) {
    if (-not $Text) { return $null }
    $start = $Text.LastIndexOf('\boxed{')
    if ($start -lt 0) { return $null }
    $open = $start + '\boxed'.Length
    $depth = 0
    for ($i = $open; $i -lt $Text.Length; $i++) {
        if ($Text[$i] -eq '{') {
            $depth++
        }
        elseif ($Text[$i] -eq '}') {
            $depth--
            if ($depth -eq 0) { return $Text.Substring($open + 1, $i - $open - 1) }
        }
    }
    $null
}

# Every answer in the task set is an integer, so strip units, degree marks, spacing and "x=" prefixes.
function ConvertTo-PlainAnswer([string]$Answer) {
    $plain = $Answer -replace '\\text\{[^}]*\}', ''
    $plain = $plain -replace '\^\{?\\circ\}?|°', ''
    $plain = $plain -replace '\\[!,;]|\\%|\$|\s', ''
    $plain = $plain -replace '(?<=\d),(?=\d{3}\b)', ''
    $plain -replace '^[a-zA-Z]+=', ''
}

function Test-MathTask($Task, [string]$Content) {
    $boxed = Get-LastBoxed $Content
    if ($null -eq $boxed) { return @{ ok = $false; detail = 'no \boxed{}' } }
    $given = ConvertTo-PlainAnswer $boxed
    @{ ok = ($given -eq $Task.answer); detail = $given }
}

function Test-CodeTask($Task, [string]$Content, [string]$Arm) {
    $blocks = [regex]::Matches("$Content", '```(?:python|py)?[ \t]*\r?\n(.*?)```', 'Singleline')
    if ($blocks.Count -eq 0) { return @{ ok = $false; detail = 'no code block'; code = '' } }
    $code = $blocks[$blocks.Count - 1].Groups[1].Value
    $imports = ($Task.prompt -split "`n" | Where-Object { $_ -match '^(import|from)\s' }) -join "`n"
    $program = "$imports`n`n$code`n`n$($Task.test)`n`ncheck($($Task.entry_point))`n"
    $stem = Join-Path $WorkDir ('{0}-{1}' -f ($Task.id -replace '\W', '_'), $Arm)
    [IO.File]::WriteAllText("$stem.py", $program, [Text.UTF8Encoding]::new($false))

    $process = Start-Process uv -ArgumentList 'run', '--no-project', '--quiet', '--python', '3.14', "$stem.py" `
        -NoNewWindow -PassThru -RedirectStandardOutput "$stem.out" -RedirectStandardError "$stem.err"
    $null = $process.Handle   # keeps ExitCode readable after exit
    if (-not $process.WaitForExit(30000)) {
        $process.Kill($true)
        return @{ ok = $false; detail = 'timeout'; code = $code }
    }
    $lastError = (Get-Content "$stem.err" -ErrorAction SilentlyContinue | Where-Object { $_.Trim() } | Select-Object -Last 1)
    @{ ok = ($process.ExitCode -eq 0); detail = ($process.ExitCode -eq 0 ? 'pass' : "$lastError"); code = $code }
}

function Invoke-Run($Task, [string]$Arm, [int]$Sample) {
    $row = [ordered]@{ model = $Model; effort = $Effort; set = $Task.set; id = $Task.id; arm = $Arm; sample = $Sample }
    try {
        $call = Invoke-Chat $Task $Arm
    }
    catch {
        $row.finish = 'error'
        $row.ok = $false
        $row.detail = "$_"
        return $row
    }
    $message = $call.response.choices[0].message
    $reasoning = "$($message.reasoning_content)"
    $content = "$($message.content)"
    $check = ($Task.set -eq 'math') ? (Test-MathTask $Task $content) : (Test-CodeTask $Task $content $Arm)

    $row.ok = $check.ok
    $row.detail = $check.detail
    $row.finish = $call.response.choices[0].finish_reason
    $row.completion_tokens = $call.response.usage.completion_tokens
    $row.reasoning_tokens = Measure-Tokens $reasoning $call.response.usage
    $row.markers_narrow = Measure-Words $reasoning $NarrowWords
    $row.markers_paper = Measure-Words $reasoning $PaperWords
    if ($Task.set -eq 'code') { $row.or_in_code = Measure-Words $check.code @('or') }
    $row.wall_s = [math]::Round($call.wall, 1)
    $row.decode_tps = [math]::Round($call.response.timings.predicted_per_second, 1)
    $row.draft_n = $call.response.timings.draft_n
    $row.draft_acc = $call.response.timings.draft_n_accepted
    $row
}

function Get-Median([double[]]$Values) {
    if (-not $Values.Count) { return $null }
    $sorted = $Values | Sort-Object
    $mid = [int][math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2) { return $sorted[$mid] }
    ($sorted[$mid - 1] + $sorted[$mid]) / 2
}

function Get-ArmSummary($Rows, $Baseline) {
    $done = @($Rows | Where-Object finish -ne 'error')
    $counted = @($done | Where-Object { $null -ne $_.reasoning_tokens })   # rows whose server gave a token count
    $row = [ordered]@{
        n           = $done.Count
        pass        = '{0}/{1}' -f @($done | Where-Object ok).Count, $done.Count
        med_think   = $counted.Count ? [int](Get-Median ($counted | ForEach-Object reasoning_tokens)) : $null
        mean_think  = $counted.Count ? [int]($counted | Measure-Object reasoning_tokens -Average).Average : $null
        truncated   = @($done | Where-Object finish -eq 'length').Count
        errors      = $Rows.Count - $done.Count
        narrow_per_k = $null
        paper_per_k = $null
        vs_off      = ''
        flips       = ''
    }
    $thinkTotal = ($counted | Measure-Object reasoning_tokens -Sum).Sum
    if ($thinkTotal) {
        $row.narrow_per_k = [math]::Round(1000 * ($counted | Measure-Object markers_narrow -Sum).Sum / $thinkTotal, 2)
        $row.paper_per_k = [math]::Round(1000 * ($counted | Measure-Object markers_paper -Sum).Sum / $thinkTotal, 1)
    }
    if (-not $Baseline) { return $row }

    # Paired against the off arm on the same task and sample.
    $ratios = @()
    $lost = 0
    $gained = 0
    foreach ($r in $done) {
        $base =$Baseline | Where-Object { $_.id -eq $r.id -and $_.sample -eq $r.sample -and $_.finish -ne 'error' } | Select-Object -First 1
        if (-not $base) { continue }
        if ($base.reasoning_tokens -and $null -ne $r.reasoning_tokens) { $ratios +=$r.reasoning_tokens / $base.reasoning_tokens }
        if ($base.ok -and -not $r.ok) { $lost++ }
        if ($r.ok -and -not $base.ok) { $gained++ }
    }
    $median = Get-Median $ratios
    if ($null -ne $median) { $row.vs_off = '{0:+0;-0;0}% think (median of {1})' -f [math]::Round(100 * ($median - 1)), $ratios.Count }
    $row.flips = "+$gained / -$lost"
    $row
}

function Write-Summary {
    $rows = @(Read-Jsonl $Jsonl)
    $table = foreach ($group in ($rows | Group-Object model, effort, set)) {
        $baseline = @($group.Group | Where-Object arm -eq 'off')
        $byArm = $group.Group | Group-Object arm | Sort-Object { $_.Name -ne 'off' }, Name
        foreach ($armGroup in $byArm) {
            $first = $group.Group[0]
            $line = [ordered]@{ model = $first.model; effort = $first.effort; set = $first.set; arm = $armGroup.Name }
            $summary = Get-ArmSummary $armGroup.Group ($armGroup.Name -eq 'off' ? $null : $baseline)
            foreach ($key in $summary.Keys) { $line[$key] = $summary[$key] }
            [pscustomobject]$line
        }
    }
    $header = "bench-thinking $(Split-Path $Jsonl -Leaf)  max_tokens $MaxTokens`n" +
        "think = reasoning tokens; *_per_k = marker words per 1k reasoning tokens; vs_off = median per-task change in reasoning tokens; flips = +gained / -lost passes vs off`n"
    $text = $header + ($table | Format-Table -Property * -AutoSize | Out-String -Width 300)
    [IO.File]::WriteAllText($Txt, $text, [Text.UTF8Encoding]::new($false))
    $text
}

$ArmTokens = Resolve-ArmTokens
if ($ListTokens) {
    foreach ($arm in $Arms) {
        $pairs = $ArmTokens[$arm].GetEnumerator() | ForEach-Object { '{0}={1}' -f $_.Key, $_.Value }
        if ($pairs) { "`n[$arm]`n$($pairs -join "`n")" }
    }
    return
}

$done = @{}
if ($Resume) {
    foreach ($r in (Read-Jsonl $Jsonl)) { $done["$($r.model)|$($r.effort)|$($r.arm)|$($r.id)|$($r.sample)"] = $true }
}

foreach ($setName in $Set) {
    $tasks = @(Read-Jsonl (Join-Path $BenchDir "tasks-$setName.jsonl"))
    if ($Limit -gt 0) { $tasks = @($tasks | Select-Object -First $Limit) }
    foreach ($task in $tasks) {
        for ($sample = 1; $sample -le $Samples; $sample++) {
            foreach ($arm in $Arms) {
                if ($done.ContainsKey("$Model|$Effort|$arm|$($task.id)|$sample")) { continue }
                $row = Invoke-Run $task $arm $sample
                Add-Content -Path $Jsonl -Value ($row | ConvertTo-Json -Compress) -Encoding utf8
                Write-Host ('{0} {1} {2} #{3}: {4} think={5} wall={6}s {7}' -f $setName, $task.id, $arm, $sample,
                    ($row.ok ? 'ok' : 'FAIL'), $row.reasoning_tokens, $row.wall_s, $row.detail)
            }
        }
    }
}

Write-Summary
