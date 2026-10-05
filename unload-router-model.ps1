# Unloads whatever model the router has loaded and waits until its child process is gone, so the GPUs are free.
# Strata runs this before it loads its model (before_load in its strata-*.json); start-strata.ps1 runs it too.
$ErrorActionPreference = 'Stop'
$Router = 'http://127.0.0.1:8410'

try { $models = (Invoke-RestMethod "$Router/models" -TimeoutSec 5).data }
catch { Write-Host 'Router is not answering; nothing to unload.'; return }

$active = @($models | Where-Object { $_.status.value -ne 'unloaded' })
if (-not $active) { Write-Host 'Router has no model loaded.'; return }

foreach ($model in $active) {
    $body = @{ model = $model.id } | ConvertTo-Json -Compress
    Invoke-RestMethod "$Router/models/unload" -Method Post -ContentType application/json -Body $body -TimeoutSec 60 | Out-Null
    Write-Host "Unloading $($model.id) ..."
}

# The router answers before the child exits; its VRAM is only free once the status says unloaded.
$deadline = (Get-Date).AddSeconds(60)
while ((Get-Date) -lt $deadline) {
    $still = @((Invoke-RestMethod "$Router/models" -TimeoutSec 5).data | Where-Object { $_.status.value -ne 'unloaded' })
    if (-not $still) { Write-Host 'Router model unloaded.'; return }
    Start-Sleep -Milliseconds 500
}
throw 'The router model did not unload within 60 s.'
