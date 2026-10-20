# Closes background apps that hold RAM Strata could use, and reopens them afterwards.
#   -Mode List    JSON of every candidate with its RAM: known apps, other apps that are safe to close,
#                 and (as information only) VS Code extensions and services. The tray's picker shows it.
#   -Mode On       closes the apps named in memory-saver.choices.json and records them in logs\memory-saver.json;
#                  run again while on, it closes the ones reopened since and adds them to the record
#   -Mode Reapply  the second half of On only: refuses when the saver is off (the tray's reapply item)
#   -Mode Off      reopens exactly the apps -Mode On closed
#   -Mode Status   what is on and what each known app holds
# What each known app costs while it is closed is in README.md § Memory saver.
param([Parameter(Mandatory)][ValidateSet('List', 'On', 'Reapply', 'Off', 'Status')][string]$Mode)
$ErrorActionPreference = 'Stop'

$StateFile   = Join-Path $PSScriptRoot 'logs\memory-saver.json'
$ChoicesFile = Join-Path $PSScriptRoot 'memory-saver.choices.json'
$DockerExe   = 'C:\Program Files\Docker\Docker\resources\bin\docker.exe'

# An "other" app smaller than this is not worth a row.
$MinOtherMB = 100
# A service smaller than this is not worth a row.
$MinServiceMB = 20

# Apps with a tested way to close and reopen them. Close outputs what Reopen needs later (for Docker, the
# containers that were running); it is saved to the state file.
# NVIDIA App and NVIDIA Overlay are not here: NVIDIA's services restart both within a minute of being killed.
$KnownApps = @(
    @{
        Name      = 'Discord'
        Processes = @('Discord')
        Cost      = 'No messages, calls or notifications until reopened (it reopens minimized).'
        Close     = { Stop-Process -Name 'Discord' -Force }
        Reopen    = {
            param($saved)
            & "$env:LOCALAPPDATA\Discord\Update.exe" --processStart Discord.exe --process-start-args --start-minimized | Out-Null
        }
    },
    @{
        Name      = 'OneDrive'
        Processes = @('OneDrive', 'OneDrive.Sync.Service')
        Cost      = 'Files stop syncing; changes sync when it reopens.'
        Close     = { & "$env:LOCALAPPDATA\Microsoft\OneDrive\OneDrive.exe" /shutdown }
        Reopen    = { param($saved) Start-Process "$env:LOCALAPPDATA\Microsoft\OneDrive\OneDrive.exe" -ArgumentList '/background' }
    },
    @{
        Name      = 'Docker Desktop'
        # Its containers run in the WSL VM, whose memory is in the WSL row.
        Processes = @('Docker Desktop', 'com.docker.backend', 'com.docker.build', 'docker-agent')
        Cost      = 'All containers stop (the local SonarQube scan fails). Also frees the WSL row when no other distro runs. Reopening takes about a minute and restarts the containers that were running.'
        Close     = {
            $containers = @(& $DockerExe ps --format '{{.Names}}')
            & $DockerExe desktop stop | Out-Null
            $containers
        }
        Reopen    = {
            param($saved)
            & $DockerExe desktop start | Out-Null
            # Containers without a restart policy (boxel-sonarqube) stay stopped unless started again.
            if ($saved) { & $DockerExe start @saved | Out-Null }
        }
    },
    @{
        Name      = 'WSL'
        # One VM holds every distro, Docker Desktop's included. Listed after Docker so that, when both are
        # picked, Docker has already stopped cleanly.
        Processes = @('vmmemWSL')
        Cost      = 'Shuts down every WSL distro, ending whatever runs in them; a distro starts again when you open it. If Docker Desktop is running, it is stopped first and reopened afterwards.'
        Close     = {
            $docker = $KnownApps | Where-Object Name -eq 'Docker Desktop'
            if (Get-Process -Name $docker.Processes[0] -ErrorAction SilentlyContinue) {
                [pscustomobject]@{ DockerContainers = @(& $docker.Close) }
            }
            wsl.exe --shutdown | Out-Null
        }
        Reopen    = {
            param($saved)
            $dockerState = $saved | Where-Object { $_.PSObject.Properties.Name -contains 'DockerContainers' }
            if ($dockerState) {
                $docker = $KnownApps | Where-Object Name -eq 'Docker Desktop'
                & $docker.Reopen @($dockerState.DockerContainers)
            }
        }
    }
)

# Never offered as "other": the shell, terminals and the interpreters other tools run on (closing one breaks its
# parent tool), the editors and agents doing work, and the servers this repo runs.
$NeverCloseNames = @(
    'explorer', 'LlamaTray', 'llama-server', 'strata', 'strata-vision',
    'pwsh', 'powershell', 'cmd', 'conhost', 'OpenConsole', 'WindowsTerminal', 'bash', 'sh', 'git', 'ssh',
    'python', 'pythonw', 'node', 'java', 'javaw', 'uv',
    'Code', 'claude', 'msedgewebview2', 'Taskmgr'
)
# Paths whose processes belong to drivers, security software or Store apps (which cannot be relaunched by path).
$NeverClosePathPatterns = @(
    "$env:windir\*", '*\NVIDIA Corporation\*', '*\Realtek\*', '*\Intel\*', '*\WindowsApps\*',
    '*\Windows Defender\*', '*\Microsoft\EdgeWebView\*'
)

function Get-FreeMB {
    [math]::Round((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1KB)
}

function Get-ProcessTable {
    Get-CimInstance Win32_Process | Select-Object ProcessId, ParentProcessId, SessionId, Name, ExecutablePath, CommandLine,
        @{ n = 'BaseName'; e = { [IO.Path]::GetFileNameWithoutExtension($_.Name) } },
        @{ n = 'PrivateMB'; e = { $_.PrivatePageCount / 1MB } }
}

function Get-KnownAppRows($processes) {
    foreach ($app in $KnownApps) {
        $members = @($processes | Where-Object { $_.BaseName -in $app.Processes })
        if (-not $members) { continue }
        [pscustomobject]@{
            Id = "known:$($app.Name)"; Kind = 'known'; Name = $app.Name; CanClose = $true; Cost = $app.Cost
            PrivateMB = [math]::Round(($members | Measure-Object PrivateMB -Sum).Sum)
        }
    }
}

function Test-OfferedAsOther($process, [string[]]$knownNames) {
    if ($process.SessionId -ne (Get-Process -Id $PID).SessionId) { return $false }   # services and other users
    if (-not $process.ExecutablePath) { return $false }
    if ($process.BaseName -in $NeverCloseNames -or $process.BaseName -in $knownNames) { return $false }
    foreach ($pattern in $NeverClosePathPatterns) {
        if ($process.ExecutablePath -like $pattern) { return $false }
    }
    return $true
}

# The folder an app is installed in: the first folder under Program Files or AppData, else the exe's own folder.
# Grouping by it makes one row of an app that runs several executables (PowerToys runs one per module).
function Get-InstallFolder([string]$path) {
    $installRoots = @($env:ProgramFiles, ${env:ProgramFiles(x86)}, "$env:LOCALAPPDATA\Programs", $env:LOCALAPPDATA, $env:APPDATA)
    foreach ($installRoot in $installRoots) {
        $prefix = $installRoot.TrimEnd('\') + '\'
        if (-not $path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { continue }
        $relative = $path.Substring($prefix.Length)
        if ($relative.Contains('\')) { return $prefix + $relative.Split('\')[0] }
    }
    return Split-Path $path
}

# The processes of a group that its app (or the user) launched, rather than ones it spawned itself.
function Get-RootProcesses($members) {
    $memberIds = $members.ProcessId
    $members | Where-Object { $_.ParentProcessId -notin $memberIds }
}

function Get-OtherAppGroups($processes) {
    $knownNames = $KnownApps | ForEach-Object { $_.Processes }
    $processes | Where-Object { Test-OfferedAsOther $_ $knownNames } |
        Group-Object { Get-InstallFolder $_.ExecutablePath } |
        Where-Object { ($_.Group | Measure-Object PrivateMB -Sum).Sum -ge $MinOtherMB }
}

function Get-OtherAppRows($processes) {
    foreach ($group in Get-OtherAppGroups $processes) {
        $roots = @(Get-RootProcesses $group.Group)
        $description = (Get-Item $roots[0].ExecutablePath).VersionInfo.FileDescription
        [pscustomobject]@{
            Id = "other:$($group.Name)"; Kind = 'other'; CanClose = $true
            Name = if ("$description".Trim()) { "$description".Trim() } else { Split-Path $group.Name -Leaf }
            PrivateMB = [math]::Round(($group.Group | Measure-Object PrivateMB -Sum).Sum)
            Cost = "Asked to close, then ended like Task Manager's End task if still running after 10 s, so save work in it first. Reopened afterwards as it was started."
        }
    }
}

# VS Code runs each extension's language server as its own process, with the extension's folder on its command line.
function Get-ExtensionRows($processes) {
    $byExtension = @{}
    foreach ($process in $processes) {
        if ($process.CommandLine -notmatch '\\\.vscode\\extensions\\([a-z0-9-]+\.[a-z0-9-]+?)-\d') { continue }
        $byExtension[$Matches[1]] += $process.PrivateMB
    }
    foreach ($extension in $byExtension.Keys) {
        if ($byExtension[$extension] -lt $MinOtherMB) { continue }
        [pscustomobject]@{
            Id = "extension:$extension"; Kind = 'extension'; Name = $extension; CanClose = $false
            PrivateMB = [math]::Round($byExtension[$extension])
            Cost = 'Runs inside VS Code, summed over every window. Close the sessions or windows using it, or disable it in the Extensions view; a window opened before you disabled it keeps it running until Developer: Reload Window.'
        }
    }
}

# Third-party services in their own process. Stopping a service needs admin, so these are information only.
function Get-ServiceRows($processes) {
    $byId = @{}
    foreach ($process in $processes) { $byId[$process.ProcessId] = $process }
    foreach ($service in Get-CimInstance Win32_Service -Filter "State = 'Running'") {
        # PathName comes from the registry; a process's own ExecutablePath is unreadable for services without admin.
        $binary = "$($service.PathName)".Trim('"')
        if ($binary -like "$env:windir\*" -or $binary -like '*\Windows Defender\*') { continue }
        $process = $byId[$service.ProcessId]
        if (-not $process -or $process.PrivateMB -lt $MinServiceMB) { continue }
        [pscustomobject]@{
            Id = "service:$($service.Name)"; Kind = 'service'; Name = $service.DisplayName; CanClose = $false
            PrivateMB = [math]::Round($process.PrivateMB)
            Cost = 'Stopping a service needs admin. Set it to Manual in services.msc if you never use it.'
        }
    }
}

function Get-Candidates {
    $processes = Get-ProcessTable
    @(Get-KnownAppRows $processes) + @(Get-OtherAppRows $processes) + @(Get-ExtensionRows $processes) + @(Get-ServiceRows $processes)
}

function Read-JsonFile([string]$path) {
    if (Test-Path $path) { Get-Content $path -Raw | ConvertFrom-Json } else { $null }
}

# A state file last written before this boot is stale: Windows' startup has relaunched those apps, so the saver is
# off and Off must not start second copies. The tray applies the same rule (MemorySaver.IsOn).
function Read-State {
    if (-not (Test-Path $StateFile)) { return $null }
    if ((Get-Item $StateFile).LastWriteTime -lt (Get-CimInstance Win32_OperatingSystem).LastBootUpTime) {
        Remove-Item $StateFile
        return $null
    }
    Read-JsonFile $StateFile
}

function Read-Choices {
    $choices = Read-JsonFile $ChoicesFile
    if ($choices) { return @($choices.Close) }
    # Before the first pick, the known apps.
    return @($KnownApps | ForEach-Object { "known:$($_.Name)" })
}

# The executable's arguments from its command line: `"C:\path\app.exe" --hidden`, `app.exe --hidden`, or an
# unquoted path with spaces such as `C:\Program Files\App\app.exe --hidden` (Logi Options+ starts that way).
function Get-Arguments([string]$commandLine, [string]$path) {
    $line = $commandLine.TrimStart()
    if ($path -and $line.StartsWith($path, [StringComparison]::OrdinalIgnoreCase)) { return $line.Substring($path.Length).Trim() }
    if ($line -match '^("[^"]*"|\S+)\s*(.*)$') { return $Matches[2].Trim() }
    return ''
}

# Whether a root process was started by the user or at logon, as opposed to by another app that is still running
# and owns it (Command Palette starts its PowerToys extension); only the first kind is relaunched.
function Test-UserLaunched($root, $processes) {
    # COM servers are started on demand by Windows (through svchost), never by hand.
    if ($root.CommandLine -match '\s-Embedding\b') { return $false }
    $parent = $processes | Where-Object ProcessId -eq $root.ParentProcessId
    # A launcher that has exited, such as Discord's Update.exe, leaves no parent behind.
    if (-not $parent) { return $true }
    return $parent.BaseName -in @('explorer', 'svchost', 'sihost', 'userinit')
}

# Outputs how to start the app again: one entry per process the user or logon launched.
function Close-OtherApp($group, $processes) {
    $members = @($group.Group)
    $memberIds = $members.ProcessId
    foreach ($root in Get-RootProcesses $members) {
        if (-not (Test-UserLaunched $root $processes)) { continue }
        [pscustomobject]@{ Path = $root.ExecutablePath; Arguments = Get-Arguments $root.CommandLine $root.ExecutablePath }
    }

    foreach ($member in $members) {
        $running = Get-Process -Id $member.ProcessId -ErrorAction SilentlyContinue
        if ($running -and $running.MainWindowHandle -ne 0) { $running.CloseMainWindow() | Out-Null }
    }
    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $deadline -and (Get-Process -Id $memberIds -ErrorAction SilentlyContinue)) {
        Start-Sleep -Milliseconds 500
    }
    Get-Process -Id $memberIds -ErrorAction SilentlyContinue | Stop-Process -Force
}

# The launches an app needs, from both passes that closed it, without repeats. A launch is one per executable
# (the first pass's arguments win), so an app reopened with other arguments still starts once.
function Join-Saved($earlier, $now) {
    $seen = @{}
    foreach ($item in @($earlier) + @($now)) {
        if ($null -eq $item) { continue }
        $key = if ($item.PSObject.Properties.Name -contains 'Path') { "$($item.Path)".ToLowerInvariant() }
               else { ConvertTo-Json -InputObject $item -Compress -Depth 6 }
        if ($seen.ContainsKey($key)) { continue }
        $seen[$key] = $true
        $item
    }
}

# One entry per app across every pass, so Off reopens each app once.
function Merge-Closed($earlier, $now) {
    $merged = [ordered]@{}
    foreach ($entry in @($earlier) + @($now)) {
        if (-not $entry) { continue }
        if ($merged.Contains($entry.Id)) { $merged[$entry.Id].Saved = @(Join-Saved $merged[$entry.Id].Saved $entry.Saved) }
        else { $merged[$entry.Id] = $entry }
    }
    @($merged.Values)
}

# Run again while on, it closes the chosen apps that were opened since and adds them to what Off reopens.
function Enable-Saver {
    $state = Read-State
    $choices = Read-Choices
    $processes = Get-ProcessTable
    $freeBefore = Get-FreeMB
    $otherGroups = @{}
    foreach ($group in Get-OtherAppGroups $processes) { $otherGroups["other:$($group.Name)"] = $group }
    $closed = @()
    try {
        foreach ($row in @(Get-KnownAppRows $processes) + @(Get-OtherAppRows $processes)) {
            if ($row.Id -notin $choices) { continue }
            if ($row.Kind -eq 'known') {
                $app = $KnownApps | Where-Object { "known:$($_.Name)" -eq $row.Id }
                $saved = @(& $app.Close)
            }
            else {
                $saved = @(Close-OtherApp $otherGroups[$row.Id] $processes)
            }
            $closed += [pscustomobject]@{ Id = $row.Id; Name = $row.Name; PrivateMB = $row.PrivateMB; Saved = $saved }
            Write-Output "Closed $($row.Name) ($($row.PrivateMB) MB private)"
        }
    }
    finally {
        # Even when a close throws, Off must still reopen the apps this pass already closed.
        New-Item -ItemType Directory -Force (Split-Path $StateFile) | Out-Null
        $since = if ($state) { $state.Since } else { (Get-Date).ToString('s') }
        [pscustomobject]@{ Since = $since; Closed = @(Merge-Closed $state.Closed $closed) } | ConvertTo-Json -Depth 8 | Set-Content $StateFile
    }

    if ($state -and -not $closed) { Write-Output 'Memory saver is on; none of the chosen apps is running again.'; return }
    # Windows takes a few seconds to return a closed app's pages to the free list.
    Start-Sleep -Seconds 5
    $names = if ($closed) { $closed.Name -join ', ' } else { 'nothing (none of the chosen apps was running)' }
    $verb = if ($state) { 'reapplied: closed' } else { 'on: closed' }
    Write-Output ("Memory saver {0} {1}. Free RAM {2:N1} -> {3:N1} GB." -f $verb, $names, ($freeBefore / 1KB), ((Get-FreeMB) / 1KB))
}

function Open-ClosedApp($entry) {
    if ($entry.Id -like 'known:*') {
        $app = $KnownApps | Where-Object { "known:$($_.Name)" -eq $entry.Id }
        & $app.Reopen @($entry.Saved)
        return
    }
    foreach ($launch in $entry.Saved) {
        $start = @{ FilePath = $launch.Path; WorkingDirectory = (Split-Path $launch.Path) }
        if ($launch.Arguments) { $start.ArgumentList = $launch.Arguments }
        Start-Process @start
    }
}

function Disable-Saver {
    $state = Read-State
    if (-not $state) { Write-Output 'Memory saver is already off.'; return }
    $failed = @()
    foreach ($entry in $state.Closed) {
        try {
            Open-ClosedApp $entry
        }
        catch {
            $failed += "$($entry.Name) ($($_.Exception.Message))"
        }
    }
    Remove-Item $StateFile
    if ($failed) { throw "Memory saver off, but these did not reopen: $($failed -join '; ')" }
    $names = if ($state.Closed) { $state.Closed.Name -join ', ' } else { 'nothing to reopen' }
    Write-Output "Memory saver off: reopened $names."
}

function Show-Status {
    $state = Read-State
    if ($state) { Write-Output "Memory saver on since $($state.Since): closed $($state.Closed.Name -join ', ')." }
    else { Write-Output 'Memory saver off.' }
    $choices = Read-Choices
    foreach ($row in Get-Candidates | Sort-Object Kind, @{ e = 'PrivateMB'; Descending = $true }) {
        $mark = if ($row.Id -in $choices) { '[x]' } elseif ($row.CanClose) { '[ ]' } else { '   ' }
        Write-Output ("  {0} {1,-9} {2,6} MB  {3}" -f $mark, $row.Kind, $row.PrivateMB, $row.Name)
    }
    Write-Output ("Free RAM {0:N1} GB." -f ((Get-FreeMB) / 1KB))
}

switch ($Mode) {
    'List' {
        $choices = Read-Choices
        $rows = Get-Candidates | ForEach-Object { $_ | Add-Member -PassThru Chosen ($_.Id -in $choices) }
        ConvertTo-Json -InputObject @($rows) -Depth 3 -Compress
    }
    'On' { Enable-Saver }
    'Reapply' {
        if (-not (Read-State)) { Write-Output 'Memory saver is off; nothing to reapply.'; return }
        Enable-Saver
    }
    'Off' { Disable-Saver }
    'Status' { Show-Status }
}
