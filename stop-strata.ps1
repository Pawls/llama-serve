# Stops the Strata server and its engine, freeing the GPUs and the RAM it pins.
# Returns only once the engine processes are gone and the launcher has let go of its files.
$ErrorActionPreference = 'Stop'

# A venv's python.exe is a redirector that starts the real interpreter as a child, so match the command line,
# not the exe path. The engine and vision encoder sit in the server's job object and end with it.
function Get-StrataServers {
    Get-CimInstance Win32_Process -Filter "Name = 'python.exe'" |
        Where-Object { $_.CommandLine -like '*F:\AI\strata\serve\server.py*' }
}
function Test-StrataRunning {
    [bool]((Get-StrataServers) -or (Get-Process strata, strata-vision -ErrorAction SilentlyContinue))
}

$servers = Get-StrataServers
if (-not $servers) { Write-Host 'Strata is not running.'; return }

# Both the redirector and its child match; killing the first tree can take the second with it.
foreach ($server in $servers) { taskkill /T /F /PID $server.ProcessId 2>$null | Out-Null }

$deadline = (Get-Date).AddSeconds(60)
while ((Test-StrataRunning) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
if (Test-StrataRunning) { throw 'Strata processes are still running 60 s after taskkill.' }

# A restart straight after a stop failed 2 of 4 times; after this pause 9 of 9 worked (AGENTS.md, Strata state).
Start-Sleep -Seconds 15
Write-Host "Stopped $(@($servers).Count) Strata server process(es)."
