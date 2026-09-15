# Decode-speed bench at context depth, for comparing drafter lanes of the same model family.
#   .\bench-depth.ps1 -Model swift-qwen3.8-27b,swift-qwen3.8-27b-dflash2
# Per model x depth x kind (code 800 tok / essay 1000 tok, forced to full length): 1 prefill, 2 warm, 5 measured.
# Thinking is off so every token is answer text. Writes logs\bench-depth-<stamp>.jsonl and .txt (medians).
# Needs the router on :8410 with nothing else using the GPUs. Leaves the last model loaded.
param(
    [Parameter(Mandatory)][string[]]$Model,
    [int[]]$Depth = @(2000, 52000, 128000),
    [int]$Warm = 2,
    [int]$Runs = 5,
    [string]$BaseUrl = 'http://127.0.0.1:8410'
)
$ErrorActionPreference = 'Stop'

$Stamp = '{0:yyyyMMdd-HHmm}' -f (Get-Date)
$LogDir = Join-Path $PSScriptRoot 'logs'
New-Item -ItemType Directory -Force $LogDir | Out-Null
$Jsonl = Join-Path $LogDir "bench-depth-$Stamp.jsonl"
$Txt = Join-Path $LogDir "bench-depth-$Stamp.txt"
$BodyFile = Join-Path ([IO.Path]::GetTempPath()) "bench-depth-$Stamp.json"

$Kinds = [ordered]@{
    code  = @{ tokens = 800; text = 'Ignore the document above. Write a complete, well-commented Python module implementing an in-place quicksort, a merge sort and a heap sort, with type hints, docstrings and pytest tests for each.' }
    essay = @{ tokens = 1000; text = 'Ignore the document above. Write a long, detailed essay on the history of the printing press and its effects on European science, religion and politics.' }
}

# Seeded pseudo-prose: deterministic across runs and machines, and not repetitive enough to be trivially cached by a drafter.
$Words = ('river market signal copper lantern harvest orbit ledger meadow engine canyon velvet archive thunder pilgrim ' +
    'quartz harbor falcon garden mirror compass winter saddle beacon cobalt timber ember glacier orchard parcel ' +
    'violet anchor bridge candle desert feather glyph hollow island jasper kettle lumber marble needle oyster ' +
    'pepper quiver ribbon silver tunnel umber vessel walnut yarrow zephyr ancient bitter careful distant eager ' +
    'fragile gentle hidden idle jagged keen lucid mellow narrow open patient quiet rapid steady tender urgent vivid ' +
    'weary young carries follows gathers measures builds crosses divides echoes forgets guards holds invites joins ' +
    'keeps lifts moves notes opens passes questions reaches sends turns under beyond before across between within').Split(' ')
function New-Filler([int]$Chars) {
    $rng = [Random]::new(42)
    $sb = [Text.StringBuilder]::new($Chars + 200)
    $n = 0
    while ($sb.Length -lt $Chars) {
        $n++
        $len = $rng.Next(8, 17)
        $w = for ($i = 0; $i -lt $len; $i++) { $Words[$rng.Next($Words.Count)] }
        $s = ($w -join ' ')
        [void]$sb.Append("Entry $n. ").Append($s.Substring(0, 1).ToUpper()).Append($s.Substring(1)).Append('. ')
        if ($n % 6 -eq 0) { [void]$sb.Append("`n`n") }
    }
    $sb.ToString()
}

function Invoke-Chat([hashtable]$Body) {
    $Body | ConvertTo-Json -Depth 10 | Set-Content -NoNewline -Encoding utf8NoBOM $BodyFile
    # curl.exe, not Invoke-RestMethod: IRM hung on the system proxy during long requests.
    $raw = curl.exe -s --noproxy '*' --max-time 3600 -H 'Content-Type: application/json' --data-binary "@$BodyFile" "$BaseUrl/v1/chat/completions"
    if ($LASTEXITCODE -ne 0) { throw "curl exit $LASTEXITCODE" }
    $r = ($raw -join "`n") | ConvertFrom-Json -Depth 50
    if ($r.error) { throw "server error: $($r.error.message)" }
    $r
}
function New-Body([string]$M, [string]$Filler, [string]$Task, [int]$MaxTokens) {
    @{
        model = $M; max_tokens = $MaxTokens; ignore_eos = $true
        chat_template_kwargs = @{ enable_thinking = $false }
        messages = @(@{ role = 'user'; content = "<document>`n$Filler`n</document>`n`n$Task" })
    }
}
function Get-VramMiB { (nvidia-smi --query-gpu=memory.used --format=csv,noheader,nounits) -join ' / ' }
function Get-Median($xs) { $s = @($xs | Sort-Object); if (-not $s.Count) { return $null }; $s[[math]::Floor($s.Count / 2)] }

# Calibrate chars per token on the first model once; every model here shares the Qwen tokenizer family.
$cal = Invoke-Chat (New-Body $Model[0] (New-Filler 40000) 'Reply with OK.' 1)
$CharsPerToken = 40000 / [math]::Max(1, $cal.timings.prompt_n)
Write-Host ("calibration: {0:N2} chars/token" -f $CharsPerToken)

foreach ($m in $Model) {
    foreach ($d in $Depth) {
        # Undershoot slightly: chars/token drifts upward on long filler.
        $filler = New-Filler ([int]($d * $CharsPerToken * 0.97))
        foreach ($kind in $Kinds.Keys) {
            $k = $Kinds[$kind]
            try {
                $sw = [Diagnostics.Stopwatch]::StartNew()
                $pre = Invoke-Chat (New-Body $m $filler $k.text 1)
                $prefill = [ordered]@{ model = $m; depth_target = $d; kind = $kind; phase = 'prefill'; prompt_n = $pre.timings.prompt_n + $pre.timings.cache_n; prompt_tps = [math]::Round($pre.timings.prompt_per_second, 1); wall_s = [math]::Round($sw.Elapsed.TotalSeconds, 1); vram = Get-VramMiB }
                ($prefill | ConvertTo-Json -Compress) | Add-Content -Encoding utf8NoBOM $Jsonl
                Write-Host ($prefill | ConvertTo-Json -Compress)
                for ($i = 1; $i -le $Warm + $Runs; $i++) {
                    $sw.Restart()
                    $r = Invoke-Chat (New-Body $m $filler $k.text $k.tokens)
                    $row = [ordered]@{
                        model = $m; depth_target = $d; kind = $kind; phase = ($i -le $Warm ? 'warm' : 'measured'); run = $i
                        prompt_n = $r.timings.prompt_n + $r.timings.cache_n; predicted_n = $r.timings.predicted_n
                        decode_tps = [math]::Round($r.timings.predicted_per_second, 1)
                        net_tps = [math]::Round($r.timings.predicted_n / $sw.Elapsed.TotalSeconds, 1)
                        draft_n = $r.timings.draft_n; draft_acc = $r.timings.draft_n_accepted
                        wall_s = [math]::Round($sw.Elapsed.TotalSeconds, 1); vram = Get-VramMiB
                    }
                    ($row | ConvertTo-Json -Compress) | Add-Content -Encoding utf8NoBOM $Jsonl
                    Write-Host ($row | ConvertTo-Json -Compress)
                }
            } catch {
                $err = [ordered]@{ model = $m; depth_target = $d; kind = $kind; phase = 'error'; error = "$_" }
                ($err | ConvertTo-Json -Compress) | Add-Content -Encoding utf8NoBOM $Jsonl
                Write-Host ($err | ConvertTo-Json -Compress)
            }
        }
    }
}
Remove-Item -ErrorAction SilentlyContinue $BodyFile

$rows = Get-Content $Jsonl | ConvertFrom-Json
$summary = $rows | Where-Object phase -eq 'measured' | Group-Object model, depth_target, kind | ForEach-Object {
    $g = $_.Group
    $dn = ($g.draft_n | Measure-Object -Sum).Sum
    [pscustomobject]@{
        model = $g[0].model; depth = (Get-Median $g.prompt_n); kind = $g[0].kind; n = $g.Count
        net_tps = (Get-Median $g.net_tps); decode_tps = (Get-Median $g.decode_tps)
        acceptance = ($dn ? [math]::Round(($g.draft_acc | Measure-Object -Sum).Sum / $dn, 2) : $null)
        peak_vram = ($g.vram | Sort-Object { ($_ -split ' / ' | Measure-Object -Maximum).Maximum } | Select-Object -Last 1)
    }
}
$fallbacks = @(Select-String -Path (Join-Path $LogDir 'server.log') -Pattern 'retrying without pipeline' -ErrorAction SilentlyContinue).Count
$errors = @($rows | Where-Object phase -eq 'error')
$report = @(
    "bench-depth $Stamp  models: $($Model -join ', ')  calibration: $([math]::Round($CharsPerToken, 2)) chars/token"
    ($summary | Sort-Object model, depth, kind | Format-Table -AutoSize | Out-String)
    "pipeline fallback hits in server.log: $fallbacks"
    "errors: $($errors.Count)"
    ($errors | ForEach-Object { "  $($_.model) $($_.depth_target) $($_.kind): $($_.error)" })
) -join "`n"
$report | Set-Content -Encoding utf8NoBOM $Txt
$report
