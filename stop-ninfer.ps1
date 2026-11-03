# Stops ninfer-serve, freeing GPU 1 and the 8 GiB of RAM it pins. Returns once the server and the start-ninfer.ps1
# launcher that logs it have exited; exits 1 if either is still running after its wait.
$ErrorActionPreference = 'Stop'

function Get-NinferServers {
    Get-Process ninfer-serve -ErrorAction SilentlyContinue | Where-Object { $_.Path -like 'F:\AI\ninfer-3090\*' }
}
function Get-NinferLaunchers {
    Get-CimInstance Win32_Process -Filter "Name = 'pwsh.exe'" | Where-Object { $_.CommandLine -like '*start-ninfer.ps1*' }
}

$servers = @(Get-NinferServers)
if (-not $servers) { Write-Host 'ninfer is not running.'; return }

# Only the server is killed: run.bat then finishes on its own and deletes the temporary log it keeps in %TEMP%.
# It takes about a second to exit, and Wait-Process cannot find a process that is already exiting (it errors at
# once), so wait on the process objects themselves.
$servers | Stop-Process -Force
$servers | ForEach-Object { $null = $_.WaitForExit(30000) }
if (Get-NinferServers) { throw 'ninfer-serve is still running 30 s after it was killed.' }

# The launcher's Tee-Object holds logs\ninfer.log until it exits, and the next start moves that file.
$launchers = @(Get-NinferLaunchers)
if ($launchers) {
    Wait-Process -Id $launchers.ProcessId -Timeout 30 -ErrorAction SilentlyContinue
    if (Get-NinferLaunchers) { throw 'The start-ninfer.ps1 launcher is still running 30 s after ninfer stopped.' }
}
Write-Host 'Stopped ninfer.'
