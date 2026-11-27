# Builds tray\Switchboard.cs with tray\tests\SwitchboardTests.cs and runs the tests. They use fake engines on
# loopback ports, so they never touch the router, Strata, ninfer or the GPUs. Exit code 1 means a test failed.
$ErrorActionPreference = 'Stop'

$Root = $PSScriptRoot
$FrameworkDir = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$Out = Join-Path ([IO.Path]::GetTempPath()) 'switchboard-tests'
New-Item -ItemType Directory -Force $Out | Out-Null
$Exe = Join-Path $Out 'SwitchboardTests.exe'

$References = 'System.dll', 'System.Core.dll', 'System.Net.Http.dll', 'System.Web.Extensions.dll', 'System.Windows.Forms.dll' |
    ForEach-Object { "/reference:$(Join-Path $FrameworkDir $_)" }

$output = & (Join-Path $FrameworkDir 'csc.exe') /nologo /target:exe /debug+ "/out:$Exe" @References `
    (Join-Path $Root 'tray\Switchboard.cs') (Join-Path $Root 'tray\RequestLoop.cs') (Join-Path $Root 'tray\EngineClients.cs') `
    (Join-Path $Root 'tray\tests\SwitchboardTests.cs')
if ($LASTEXITCODE -ne 0) { $output; throw "csc failed with exit code $LASTEXITCODE" }

& $Exe
exit $LASTEXITCODE
