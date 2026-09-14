# Tool-call smoke test: does each preset turn a tool request into parsed OpenAI tool_calls, and use
# the tool result on the next turn? Run after editing presets.ini or upgrading the llama.cpp build.
#   .\test-tool-calls.ps1                      # every preset, 3 runs each
#   .\test-tool-calls.ps1 -Model qwen3.8-27b -Runs 5
# Needs the router on :8410. Loading each model takes 15-60 s; the last one tested stays loaded.
param(
    [string[]]$Model,
    [int]$Runs = 3,
    [string]$BaseUrl = 'http://127.0.0.1:8410'
)
$ErrorActionPreference = 'Stop'

# The magic string only exists in the fake tool result, so echoing it proves the result was read.
$Magic = 'PELICAN-4417'
$Tools = @(
    @{
        type     = 'function'
        function = @{
            name        = 'read_file'
            description = 'Read a text file from the workspace and return its contents.'
            parameters  = @{
                type       = 'object'
                properties = @{
                    path       = @{ type = 'string'; description = 'Workspace-relative file path.' }
                    start_line = @{ type = 'integer'; description = 'First line to read, 1-based.' }
                }
                required   = @('path')
            }
        }
    }
)
$UserMsg = @{ role = 'user'; content = 'Use the read_file tool to read src/config.py, then tell me the value of API_KEY_NAME.' }
# Raw tool-call markup leaking into content means the template parser missed the call.
$LeakPattern = '<tool_call>|</tool_call>|<function=|<\|tool_call|"name"\s*:\s*"read_file"|call:read_file'

$BodyFile = Join-Path ([IO.Path]::GetTempPath()) 'llama-serve-toolcall-body.json'

function Invoke-Chat([hashtable]$Body) {
    $Body | ConvertTo-Json -Depth 20 | Set-Content -NoNewline -Encoding utf8NoBOM $BodyFile
    # curl.exe, not Invoke-RestMethod: IRM hung on the system proxy during long requests.
    $raw = curl.exe -s --noproxy '*' --max-time 900 -H 'Content-Type: application/json' `
        --data-binary "@$BodyFile" "$BaseUrl/v1/chat/completions"
    if ($LASTEXITCODE -ne 0) { throw "curl exit $LASTEXITCODE" }
    $resp = ($raw -join "`n") | ConvertFrom-Json -Depth 50
    if ($resp.error) { throw "server error: $($resp.error.message)" }
    $resp
}

function Test-Once([string]$Id) {
    $fail = [Collections.Generic.List[string]]::new()
    $sw = [Diagnostics.Stopwatch]::StartNew()

    $r1 = Invoke-Chat @{ model = $Id; messages = @($UserMsg); tools = $Tools; tool_choice = 'auto'; max_tokens = 8192 }
    $m1 = $r1.choices[0].message
    $calls = @($m1.tool_calls | Where-Object { $_ })
    if ($r1.choices[0].finish_reason -ne 'tool_calls') { $fail.Add("turn1 finish_reason=$($r1.choices[0].finish_reason)") }
    if ($m1.content -match $LeakPattern) { $fail.Add('turn1 tool markup leaked into content') }
    if ($calls.Count -eq 0) {
        $fail.Add('turn1 no tool_calls')
        return [pscustomobject]@{ ok = $false; secs = $sw.Elapsed.TotalSeconds; failures = $fail }
    }

    $call = $calls[0]
    $args1 = $null
    if ($call.function.name -ne 'read_file') { $fail.Add("turn1 called '$($call.function.name)'") }
    try { $args1 = $call.function.arguments | ConvertFrom-Json -Depth 20 } catch { $fail.Add("turn1 arguments not JSON: $($call.function.arguments)") }
    if ($args1) {
        if ($args1.path -isnot [string] -or -not $args1.path) { $fail.Add('turn1 path missing or not a string') }
        if ($null -ne $args1.start_line -and $args1.start_line -isnot [long] -and $args1.start_line -isnot [int]) {
            $fail.Add('turn1 start_line not an integer')
        }
        $extra = @($args1.PSObject.Properties.Name | Where-Object { $_ -notin 'path', 'start_line' })
        if ($extra) { $fail.Add("turn1 unknown args: $($extra -join ',')") }
    }

    # Replay the assistant turn as the server returned it, so preserve_thinking templates see reasoning_content.
    $assistant = @{ role = 'assistant'; content = $m1.content; tool_calls = @($call) }
    if ($m1.reasoning_content) { $assistant.reasoning_content = $m1.reasoning_content }
    $toolMsg = @{ role = 'tool'; tool_call_id = $call.id; content = "# src/config.py`nAPI_KEY_NAME = `"$Magic`"`nTIMEOUT = 30`n" }

    $r2 = Invoke-Chat @{ model = $Id; messages = @($UserMsg, $assistant, $toolMsg); tools = $Tools; tool_choice = 'auto'; max_tokens = 8192 }
    $m2 = $r2.choices[0].message
    if ($r2.choices[0].finish_reason -ne 'stop') { $fail.Add("turn2 finish_reason=$($r2.choices[0].finish_reason)") }
    if ($m2.content -match $LeakPattern) { $fail.Add('turn2 tool markup leaked into content') }
    if ($m2.content -notmatch [regex]::Escape($Magic)) { $fail.Add('turn2 answer lacks the tool result value') }

    [pscustomobject]@{ ok = $fail.Count -eq 0; secs = $sw.Elapsed.TotalSeconds; failures = $fail }
}

if (-not $Model) {
    $list = curl.exe -s --noproxy '*' --max-time 10 "$BaseUrl/models"
    if ($LASTEXITCODE -ne 0) { throw "Router not reachable at $BaseUrl" }
    $Model = @(($list -join "`n" | ConvertFrom-Json).data.id)
}

$rows = foreach ($id in $Model) {
    $pass = 0
    $notes = [Collections.Generic.List[string]]::new()
    $secs = [Collections.Generic.List[double]]::new()
    for ($i = 1; $i -le $Runs; $i++) {
        try {
            $res = Test-Once $id
            $secs.Add($res.secs)
            if ($res.ok) { $pass++ } else { $notes.AddRange([string[]]$res.failures) }
            Write-Host ("{0} run {1}/{2}: {3} ({4:N0}s) {5}" -f $id, $i, $Runs, ($res.ok ? 'pass' : 'FAIL'), $res.secs, ($res.failures -join '; '))
        } catch {
            $notes.Add("error: $_")
            Write-Host "$id run $i/${Runs}: ERROR $_"
        }
    }
    [pscustomobject]@{
        model  = $id
        passed = "$pass/$Runs"
        # The first run includes the model load.
        med_secs = ($secs.Count ? [math]::Round(@($secs | Sort-Object)[[math]::Floor($secs.Count / 2)], 1) : $null)
        issues = (($notes | Group-Object | ForEach-Object { "$($_.Count)x $($_.Name)" }) -join '; ')
    }
}

Remove-Item -ErrorAction SilentlyContinue $BodyFile
$rows | Format-Table -AutoSize -Wrap
$outDir = Join-Path $PSScriptRoot 'logs'
New-Item -ItemType Directory -Force $outDir | Out-Null
$rows | ConvertTo-Json | Set-Content -Encoding utf8NoBOM (Join-Path $outDir ("tool-calls-{0:yyyyMMdd-HHmm}.json" -f (Get-Date)))
if ($rows | Where-Object { $_.passed -ne "$Runs/$Runs" }) { exit 1 }
