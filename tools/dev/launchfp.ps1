param([int]$X = -1, [int]$Y = -1, [string]$Out = 'shots/v-run.png', [int]$Settle = 5000)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
& (Join-Path $PSScriptRoot 'killjb.ps1') | Out-Null
& (Join-Path $PSScriptRoot 'shot.ps1') -Launch -Wait 3200 -Top -Out 'shots/v-list.png' | Out-Null
if ($X -ge 0) {
  & (Join-Path $PSScriptRoot 'shot.ps1') -ClickX $X -ClickY $Y -Wait $Settle -Top -Out $Out | Out-Null
}
$ps = Get-CimInstance Win32_Process -Filter "Name='msedge.exe' OR Name='chrome.exe' OR Name='brave.exe'"
foreach ($p in $ps) {
  if ($p.CommandLine -notmatch 'JoeBrowser[\\/]data') { continue }
  if ($p.CommandLine -match '--type=') { continue }
  $port = if ($p.CommandLine -match 'remote-debugging-port=(\d+)') { $Matches[1] } else { '?' }
  $size = if ($p.CommandLine -match '--window-size=([\d,]+)') { $Matches[1] } else { '?' }
  $ua   = if ($p.CommandLine -match '--user-agent="([^"]*)"') { $Matches[1] } else { '' }
  $prof = if ($p.CommandLine -match '--user-data-dir="?([^" ]+)' ) { ($Matches[1] -split '[\\/]')[-1] } else { '?' }
  Write-Output ("PORT=$port SIZE=$size ID=$prof")
  Write-Output ("UA=$ua")
}
