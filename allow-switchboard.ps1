# One-time setup for the front door (switchboard, port 8409) on the LAN and Tailscale. Run in an ADMINISTRATOR
# PowerShell; safe to re-run. Without it the front door still works on this PC, but not from another machine.
#   1. A URL reservation, so the unelevated tray may listen on http://+:8409/ (every interface).
#   2. A firewall rule for TCP 8409, scoped to the home network and Tailscale like the llama-server rules
#      (README.md § Using it from another machine).
# The port is Switchboard.Port in tray\Switchboard.cs.
$ErrorActionPreference = 'Stop'

$Port = 8409
$Url = "http://+:$Port/"
$RuleName = "llama-serve switchboard ($Port)"
$Me = [Security.Principal.WindowsIdentity]::GetCurrent()

if (-not ([Security.Principal.WindowsPrincipal]$Me).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this in an administrator PowerShell.'
}

netsh http delete urlacl url=$Url 2>&1 | Out-Null   # no reservation yet is fine
netsh http add urlacl url=$Url user=$($Me.Name)
if ($LASTEXITCODE -ne 0) { throw "netsh could not reserve $Url for $($Me.Name)" }

Get-NetFirewallRule -DisplayName $RuleName -ErrorAction SilentlyContinue | Remove-NetFirewallRule
New-NetFirewallRule -DisplayName $RuleName -Direction Inbound -Protocol TCP -LocalPort $Port `
    -RemoteAddress 192.168.1.0/24, 100.64.0.0/10 -Action Allow -Profile Any | Out-Null

Write-Host "Reserved $Url for $($Me.Name) and allowed TCP $Port from 192.168.1.0/24 and 100.64.0.0/10."
Write-Host 'In the tray, tick "Share router on the network", then "Front door" (or untick and tick it) to rebind.'
