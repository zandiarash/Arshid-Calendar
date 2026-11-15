#Requires -Version 7.0
<#
.SYNOPSIS
    Runs the Windows App Certification Kit (WACK) on the Store bundle. Partner Center runs the same checks.

.DESCRIPTION
    Build the bundle first with ./scripts/Publish.ps1 -Store. WACK needs administrator rights, so the
    script asks for elevation (UAC) when it isn't already elevated. The report is saved next to the bundle
    as wack-report.xml and a pass/fail summary is printed.

.EXAMPLE
    ./scripts/Test-StorePackage.ps1
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props') -Raw
$version = @($props.Project.PropertyGroup | ForEach-Object { $_.AppVersion } | Where-Object { $_ })[0]
$bundle = Join-Path $root "artifacts/$version/Store/ArshidCalendar_$version.msixbundle"
if (-not (Test-Path $bundle)) { throw "No Store bundle for v$version. Run ./scripts/Publish.ps1 -Store first." }

$appcert = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/App Certification Kit/appcert.exe'
if (-not (Test-Path $appcert)) { throw 'appcert.exe not found. Install the Windows SDK (App Certification Kit component).' }

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host 'WACK needs administrator rights; restarting elevated…' -ForegroundColor Yellow
    Start-Process pwsh -Verb RunAs -Wait -ArgumentList '-NoProfile', '-NoExit', '-File', $PSCommandPath
    exit 0
}

$report = Join-Path (Split-Path $bundle) 'wack-report.xml'
Remove-Item $report -ErrorAction SilentlyContinue
Write-Host "Running WACK on $bundle (this takes several minutes)…" -ForegroundColor Cyan
& $appcert reset | Out-Null
& $appcert test -appxpackagepath $bundle -reportoutputpath $report
if (-not (Test-Path $report)) { throw 'WACK produced no report.' }

[xml]$xml = Get-Content $report -Raw
$overall = $xml.REPORT.OVERALL_RESULT
$failed = $xml.SelectNodes('//TEST[RESULT="FAIL"]')
foreach ($t in $failed) {
    Write-Host "✘ $($t.NAME)" -ForegroundColor Red
    foreach ($m in $t.MESSAGES.MESSAGE) { Write-Host "    $($m.TEXT)" -ForegroundColor Red }
}
$color = if ($overall -eq 'PASS') { 'Green' } else { 'Red' }
Write-Host "`nWACK overall result: $overall   (report: $report)" -ForegroundColor $color
