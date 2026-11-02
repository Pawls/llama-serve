# Stops the Strata server and its engine, freeing the GPUs and the RAM it pins.
# A loaded model is unloaded first (waiting up to 15 s for a running reply), so the engine exits cleanly and saves
# its expert_profile_save file; then the server is killed. Returns once the server, the engine and the
# start-strata.ps1 launcher that logs them have exited; exits 1 if any is still running after its wait.
$ErrorActionPreference = 'Stop'
$Strata = 'http://127.0.0.1:8411'   # "port" in every F:\AI\strata\strata-*.json

# A venv's python.exe is a redirector that starts the real interpreter as a child, so match the command line,
# not the exe path. The engine and vision encoder sit in the server's job object and end with it.
function Get-StrataServers {
    Get-CimInstance Win32_Process -Filter "Name = 'python.exe'" |
        Where-Object { $_.CommandLine -like '*F:\AI\strata\serve\server.py*' }
}
function Test-StrataRunning {
    [bool]((Get-StrataServers) -or
        (Get-Process strata, strata-vision -ErrorAction SilentlyContinue |
            Where-Object { -not $_.Path -or $_.Path -like 'F:\AI\strata\*' }))   # an exiting process has no Path
}
# The launcher's Tee-Object holds logs\strata.log until it exits; a start before then cannot rotate the log.
function Get-StrataLaunchers {
    Get-CimInstance Win32_Process -Filter "Name = 'pwsh.exe'" |
        Where-Object { $_.CommandLine -like '*start-strata.ps1*' }
}

$servers = Get-StrataServers
if (-not $servers) { Write-Host 'Strata is not running.'; return }

# taskkill skips the engine's QUIT, which is when it saves expert_profile_save; an unload first sends it.
# Best effort: a server that does not answer /health at once, or is still busy after the wait, is killed as before.
try {
    if ((Invoke-RestMethod "$Strata/health" -TimeoutSec 3).loaded) {
        Invoke-RestMethod "$Strata/unload?wait=15" -Method Post -ContentType application/json -Body '{}' -TimeoutSec 25 |
            Out-Null
    }
} catch { Write-Host "Unload failed ($($_.Exception.Message)); killing Strata without saving its expert profile." }

# Both the redirector and its child match; killing the first tree can take the second with it.
foreach ($server in $servers) { taskkill /T /F /PID $server.ProcessId 2>$null | Out-Null }

$deadline = (Get-Date).AddSeconds(60)
while ((Test-StrataRunning) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
if (Test-StrataRunning) { throw 'Strata processes are still running 60 s after taskkill.' }

$launchers = @(Get-StrataLaunchers)
if ($launchers) {
    Wait-Process -Id $launchers.ProcessId -Timeout 30 -ErrorAction SilentlyContinue
    if (Get-StrataLaunchers) { throw 'The start-strata.ps1 launcher is still running 30 s after Strata stopped.' }
}
Write-Host "Stopped $(@($servers).Count) Strata server process(es)."
