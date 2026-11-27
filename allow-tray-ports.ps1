# One-time setup for the tray's two listeners on the LAN and Tailscale: the front door (switchboard, port 8409) and
# the model finder (port 8407). Run in an ADMINISTRATOR PowerShell; safe to re-run. Without it both still work on
# this PC, but not from another machine.
#   1. A URL reservation per port, so the unelevated tray may listen on http://+:<port>/ (every interface).
#   2. A firewall rule per port, scoped to the home network and Tailscale like the llama-server rules
#      (README.md § Using it from another machine).
# The ports are Switchboard.Port in tray\Switchboard.cs and ModelFinder.Port in tray\ModelFinder.cs.
$ErrorActionPreference = 'Stop'

$Ports = @{ 8409 = 'llama-serve switchboard (8409)'; 8407 = 'llama-serve model finder (8407)' }
$Me = [Security.Principal.WindowsIdentity]::GetCurrent()

if (-not ([Security.Principal.WindowsPrincipal]$Me).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this in an administrator PowerShell.'
}

foreach ($Port in $Ports.Keys) {
    $Url = "http://+:$Port/"
    $RuleName = $Ports[$Port]
    netsh http delete urlacl url=$Url 2>&1 | Out-Null   # no reservation yet is fine
    netsh http add urlacl url=$Url user=$($Me.Name)
    if ($LASTEXITCODE -ne 0) { throw "netsh could not reserve $Url for $($Me.Name)" }

    Get-NetFirewallRule -DisplayName $RuleName -ErrorAction SilentlyContinue | Remove-NetFirewallRule
    New-NetFirewallRule -DisplayName $RuleName -Direction Inbound -Protocol TCP -LocalPort $Port `
        -RemoteAddress 192.168.1.0/24, 100.64.0.0/10 -Action Allow -Profile Any | Out-Null
    Write-Host "Reserved $Url for $($Me.Name) and allowed TCP $Port from 192.168.1.0/24 and 100.64.0.0/10."
}
Write-Host 'In the tray, tick "Share router on the network" (or untick and tick it) to rebind the front door and the model finder.'
