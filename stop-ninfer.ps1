# Stops ninfer-serve, freeing GPU 1 and the 8 GiB of RAM it pins. Returns once the server and the start-ninfer.ps1
# launcher that logs it have exited; exits 1 if either is still running after its wait.
$ErrorActionPreference = 'Stop'

function Get-NinferServers {
    Get-Process ninfer-serve -ErrorAction SilentlyContinue | Where-Object { $_.Path -like 'F:\AI\ninfer-3090\*' }
}
function Get-NinferLaunchers {
    Get-CimInstance Win32_Process -Filter "Name = 'pwsh.exe'" | Where-Object { $_.CommandLine -like '*start-ninfer.ps1*' }
}

# Wait-Process cannot find a process that is already exiting (it errors at once), so wait on process objects.
function Wait-Exit([System.Diagnostics.Process[]]$processes) {
    $processes | ForEach-Object { $null = $_.WaitForExit(30000) }
}

$servers = @(Get-NinferServers)
$launchers = @(Get-NinferLaunchers)
if (-not $servers -and -not $launchers) { Write-Host 'ninfer is not running.'; return }

if ($servers) {
    # Only the server is killed: run.bat then finishes on its own and deletes the temporary log it keeps in %TEMP%.
    $servers | Stop-Process -Force
    Wait-Exit $servers
} else {
    # A launcher with no server is still starting (stopping Strata and the router, or in run.bat) and would start
    # ninfer-serve after this script returned. Killing its tree prevents that; run.bat's %TEMP% log stays behind.
    foreach ($launcher in $launchers) { taskkill /T /F /PID $launcher.ProcessId 2>$null | Out-Null }
}

# The launcher's Tee-Object holds logs\ninfer.log until it exits, and the next start moves that file.
if ($launchers) { Wait-Exit @(Get-Process -Id $launchers.ProcessId -ErrorAction SilentlyContinue) }
if (Get-NinferServers) { throw 'ninfer-serve is still running 30 s after it was stopped.' }
if (Get-NinferLaunchers) { throw 'The start-ninfer.ps1 launcher is still running 30 s after ninfer stopped.' }
Write-Host 'Stopped ninfer.'
