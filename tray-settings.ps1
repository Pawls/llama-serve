# Reads the tray's saved settings (tray-settings.json). A key the user never set is absent.
# RouterIdleMinutes: minutes without a request before a router model frees its VRAM; 0 = never.
# StrataIdleMinutes: the same for Strata's model; 0 = never.
function Get-TraySetting {
    param([string]$Name)
    $path = Join-Path $PSScriptRoot 'tray-settings.json'
    if (-not (Test-Path $path)) { return $null }
    $settings = Get-Content $path -Raw | ConvertFrom-Json
    return $settings.$Name
}
