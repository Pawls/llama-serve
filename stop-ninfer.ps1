# Stops ninfer-serve, freeing its GPUs (GPU 1, or both for a two-GPU Inkling profile) and the RAM it pins. An Inkling server is stopped through POST /shutdown so it saves its prefix cache file. Returns once the server and the start-ninfer.ps1
# launcher that logs it have exited; exits 1 if either is still running after its wait.
$ErrorActionPreference = 'Stop'
$StartScript = Join-Path $PSScriptRoot 'start-ninfer.ps1'

function Get-NinferServers {
    Get-Process ninfer-serve -ErrorAction SilentlyContinue | Where-Object { $_.Path -like 'F:\AI\ninfer-3090\*' }
}
# Shells running start-ninfer.ps1 as a script (the tray's launch), and the run.bat that starts ninfer-serve however
# it was launched. A shell that only mentions the script on its command line is not one.
function Get-NinferLaunchers {
    Get-CimInstance Win32_Process -Filter "Name = 'pwsh.exe' OR Name = 'powershell.exe'" |
        Where-Object { $_.ProcessId -ne $PID -and $_.CommandLine -like "*-File*$StartScript*" }
    Get-CimInstance Win32_Process -Filter "Name = 'cmd.exe'" |
        Where-Object { $_.CommandLine -like '*F:\AI\ninfer-3090\*run.bat*' }
}
# Wait-Process cannot find a process that is already exiting (it errors at once), so wait on process objects.
function Wait-Exit([System.Diagnostics.Process[]]$processes, [int]$Seconds = 30) {
    $processes | ForEach-Object { $null = $_.WaitForExit($Seconds * 1000) }
}

$servers = @(Get-NinferServers)
$launchers = @(Get-NinferLaunchers)
if (-not $servers -and -not $launchers) { Write-Host 'ninfer is not running.'; return }

if ($servers) {
    # An Inkling server started with --admin-token stops through POST /shutdown, which writes its --prefix-cache-file
    # (8+ GB, up to a minute or two); a kill loses the cache. Servers without the token, and a /shutdown that fails,
    # fall through to the kill.
    $TokenFile = Join-Path $PSScriptRoot 'logs\ninfer-admin-token'
    $HasToken = Get-CimInstance Win32_Process -Filter "Name = 'ninfer-serve.exe'" |
        Where-Object { $_.CommandLine -like '*--admin-token*' }
    if ($HasToken -and (Test-Path $TokenFile)) {
        try {
            Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:8412/shutdown' -TimeoutSec 10 `
                -Headers @{ Authorization = "Bearer $(Get-Content -Raw $TokenFile)" } | Out-Null
            Write-Host 'Asked ninfer to shut down; waiting for it to save its cache.'
        } catch {
            Write-Host "ninfer's /shutdown request failed ($($_.Exception.Message))."
            # A refusal (wrong token, 4xx) means nothing is stopping; a timeout or a dropped connection can mean the
            # server took the request and is already saving, so wait for it before any kill.
            $Refused = [bool]$_.Exception.Response
        }
        if (-not $Refused) { Wait-Exit $servers -Seconds 300 }
    }
    # run.bat then finishes on its own and deletes the temporary log it keeps in %TEMP%.
    $servers | Where-Object { -not $_.HasExited } | Stop-Process -Force
    Wait-Exit $servers
} else {
    # A launcher with no server is still starting (stopping Strata and the router, or in run.bat) and would start
    # ninfer-serve after this script returned. Killing its tree prevents that; run.bat's %TEMP% log stays behind.
    foreach ($launcher in $launchers) { taskkill /T /F /PID $launcher.ProcessId 2>$null | Out-Null }
}

# The launcher's Tee-Object holds logs\ninfer.log until it exits, and the next start moves that file.
if ($launchers) { Wait-Exit @(Get-Process -Id $launchers.ProcessId -ErrorAction SilentlyContinue) }
if (Get-NinferServers) { throw 'ninfer-serve is still running after its stop wait.' }
if (Get-NinferLaunchers) { throw 'A ninfer launcher is still running 30 s after ninfer stopped.' }
Write-Host 'Stopped ninfer.'
