#Requires -Version 7.0
# Removes the Arshid Calendar package (desktop widget + Widgets Board provider) for the current user.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props') -Raw
$packageName = @($props.Project.PropertyGroup | ForEach-Object { $_.PackageIdentityName } | Where-Object { $_ })[0]

Get-Process ArshidCalendar, ArshidCalendar.WidgetProvider -ErrorAction SilentlyContinue | Stop-Process -Force
# Also removes the development package from before the rename (PersianCalendar.WidgetProvider).
Get-AppxPackage | Where-Object Name -in $packageName, 'PersianCalendar.WidgetProvider' | Remove-AppxPackage
Remove-Item (Join-Path $env:LOCALAPPDATA 'ArshidCalendar\WidgetProvider') -Recurse -Force -ErrorAction SilentlyContinue
Write-Host '✔ Arshid Calendar package removed.' -ForegroundColor Green
