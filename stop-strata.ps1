# Stops the Strata server and its engine, freeing the GPUs and the RAM it pins.
$ErrorActionPreference = 'Stop'

# A venv's python.exe is a redirector that starts the real interpreter as a child, so match the command line,
# not the exe path. The engine and vision encoder sit in the server's job object and end with it.
$servers = Get-CimInstance Win32_Process -Filter "Name = 'python.exe'" |
    Where-Object { $_.CommandLine -like '*F:\AI\strata\serve\server.py*' }
if (-not $servers) { Write-Host 'Strata is not running.'; return }

# Both the redirector and its child match; killing the first tree can take the second with it.
foreach ($server in $servers) { taskkill /T /F /PID $server.ProcessId 2>$null | Out-Null }
Write-Host "Stopped $(@($servers).Count) Strata server process(es)."
