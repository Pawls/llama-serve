# Runs the model finder's tests: the page's filter and sort rules (node), the catalog builder on fixtures and on the
# real launch files (tray\tests\model-catalog.tests.ps1), and the tray's listener (tray\ModelFinder.cs, built with
# tray\tests\ModelFinderTests.cs on a free loopback port). Reads files only: no engine, no GPU. Exit 1 means a failure.
$ErrorActionPreference = 'Stop'

$Root = $PSScriptRoot
$FrameworkDir = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$Out = Join-Path ([IO.Path]::GetTempPath()) 'model-finder-tests'
New-Item -ItemType Directory -Force $Out | Out-Null
$Exe = Join-Path $Out 'ModelFinderTests.exe'
$failed = @()

Write-Host '== page rules (node --test web/model-finder.test.mjs)'
& node --test (Join-Path $Root 'web\model-finder.test.mjs')
if ($LASTEXITCODE -ne 0) { $failed += 'page rules' }

Write-Host '== catalog builder'
& pwsh -NoProfile -File (Join-Path $Root 'tray\tests\model-catalog.tests.ps1')
if ($LASTEXITCODE -ne 0) { $failed += 'catalog builder' }

Write-Host '== listener'
$References = 'System.dll', 'System.Core.dll', 'System.Net.Http.dll', 'System.Web.Extensions.dll', 'System.Windows.Forms.dll' |
    ForEach-Object { "/reference:$(Join-Path $FrameworkDir $_)" }
$output = & (Join-Path $FrameworkDir 'csc.exe') /nologo /target:exe /debug+ "/out:$Exe" @References `
    (Join-Path $Root 'tray\ModelFinder.cs') (Join-Path $Root 'tray\RequestLoop.cs') `
    (Join-Path $Root 'tray\tests\ModelFinderTests.cs')
if ($LASTEXITCODE -ne 0) { $output; $failed += 'listener (build)' }
else {
    & $Exe
    if ($LASTEXITCODE -ne 0) { $failed += 'listener' }
}

if ($failed) { Write-Host "FAILED: $($failed -join ', ')"; exit 1 }
Write-Host 'All model finder tests passed.'
