# Dot-sourced by sync-opencode.ps1 and model-catalog.ps1: reads the models each engine starts from the files that
# define them, so every list built from them agrees with what runs.
#   Get-RouterPresets <presets.ini>       ordered id -> @{ flag = value } per preset section ([*] left out)
#   Get-StrataConfigs <StrataDir>         one row per strata-<size>.json that has a run-<size>.bat (what
#                                         start-strata.ps1 -Model <size> accepts), in file-name order
#   Get-NinferProfiles <start-ninfer.ps1> one row per $Inkling profile; $NinferQwen is the run.bat Qwen profile

function Get-KeyValueSections([string]$Path) {
    $sections = [ordered]@{}
    $current = $null
    foreach ($line in Get-Content $Path) {
        if ($line -match '^\[(.+)\]\s*$') { $current = $sections[$Matches[1]] = @{} }
        elseif ($current -and $line -match '^([\w-]+)\s*=\s*(.*?)\s*$') { $current[$Matches[1]] = $Matches[2] }
    }
    $sections
}

function Get-RouterPresets([string]$Path) {
    $sections = Get-KeyValueSections $Path
    $sections.Remove('*')
    $sections
}

function Get-StrataConfigs([string]$StrataDir) {
    foreach ($file in Get-ChildItem (Join-Path $StrataDir 'strata-*.json') | Sort-Object Name) {
        $size = $file.BaseName -replace '^strata-', ''
        if (-not (Test-Path (Join-Path $StrataDir "run-$size.bat"))) { continue }
        $cfg = Get-Content $file -Raw | ConvertFrom-Json
        $args_ = @($cfg.args)
        $at = [array]::IndexOf($args_, '--max-context')
        $vision = [array]::IndexOf($args_, '--vision')
        [pscustomobject]@{
            Size      = $size
            ModelName = $cfg.model_name
            Context   = [int]$args_[$at + 1]
            # --vision takes an optional value: a path or "off".
            Vision    = $vision -ge 0 -and $args_[$vision + 1] -ne 'off'
            Gpu1      = $cfg.gpu -is [long] -and $cfg.gpu -eq 1
        }
    }
}

# ninfer-3090's run.bat Qwen3.8-27B profile; 188416 is its NINFER_SPEC=dflash2 maximum context.
$NinferQwen = [pscustomobject]@{ Profile = 'qwen38-27b'; Id = 'qwen3.8-27b'; Context = 188416; Gpu1 = $true; Vision = $false; Quant = $null }

function Get-NinferProfiles([string]$ScriptPath) {
    $text = Get-Content $ScriptPath -Raw
    $quants = @{}
    [regex]::Matches($text, '(?m)^\$(\w+)\s*=\s*''models\\[^\\]+\\Inkling-2-Mini-(UD-\w+?)-ninfer') |
        ForEach-Object { $quants[$_.Groups[1].Value] = $_.Groups[2].Value }
    $rows = foreach ($m in [regex]::Matches($text, "(?m)^\s*'(inkling2-[\w-]+)'\s*=\s*@\{ File = \`$(\w+); Context = (\d+); Gpus = ('')?")) {
        [pscustomobject]@{
            Profile = $m.Groups[1].Value
            # The model id is the profile name with dashes, as start-ninfer.ps1 passes it.
            Id      = $m.Groups[1].Value -replace '^inkling2', 'inkling-2'
            Context = [int]$m.Groups[3].Value
            Gpu1    = $m.Groups[4].Success
            Vision  = $true
            Quant   = $quants[$m.Groups[2].Value]
        }
    }
    if (-not $rows) { throw "No Inkling profiles found in $ScriptPath; its `$Inkling table changed shape." }
    $rows
}
