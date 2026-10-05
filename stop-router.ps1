# Stops the router and any model it has loaded, freeing both GPUs.
# The llama-router logon task runs the tray app, which stays up and shows the router as stopped.
$ErrorActionPreference = 'Stop'

# The router runs each loaded model as a child llama-server; stopping the task does not always reach them.
$procs = Get-Process llama-server -ErrorAction SilentlyContinue | Where-Object { $_.Path -like 'F:\AI\llama.cpp\*' }
if (-not $procs) { Write-Host 'Router is not running.'; return }
$procs | Stop-Process -Force
Write-Host "Stopped $(@($procs).Count) llama-server process(es)."
