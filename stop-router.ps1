# Stops the router and any model it has loaded, freeing both GPUs. Returns once they have exited; exits 1 if any is
# still running after 30 s.
# The llama-router logon task runs the tray app, which stays up and shows the router as stopped.
$ErrorActionPreference = 'Stop'

# The router runs each loaded model as a child llama-server; stopping the task does not always reach them.
$procs = @(Get-Process llama-server -ErrorAction SilentlyContinue | Where-Object { $_.Path -like 'F:\AI\llama.cpp\*' })
if (-not $procs) { Write-Host 'Router is not running.'; return }
$procs | Stop-Process -Force
# A model's VRAM is free only once its process has exited. Wait-Process cannot find a process that is already
# exiting (it errors at once), so wait on the process objects.
$procs | ForEach-Object { $null = $_.WaitForExit(30000) }
if ($procs | Where-Object { -not $_.HasExited }) { throw 'llama-server is still running 30 s after it was killed.' }
Write-Host "Stopped $($procs.Count) llama-server process(es)."
