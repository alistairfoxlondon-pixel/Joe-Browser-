$ErrorActionPreference = 'SilentlyContinue'
Get-Process JoeBrowser | Stop-Process -Force
$ps = Get-CimInstance Win32_Process -Filter "Name='msedge.exe' OR Name='chrome.exe' OR Name='brave.exe' OR Name='firefox.exe'"
foreach ($p in $ps) {
  if ($p.CommandLine -match 'JoeBrowser[\\/]data') { Stop-Process -Id $p.ProcessId -Force }
}
Start-Sleep -Milliseconds 500
$apps = Get-CimInstance Win32_Process -Filter "Name='msedge.exe' OR Name='chrome.exe' OR Name='brave.exe' OR Name='firefox.exe'"
$left = @()
foreach ($a in $apps) { $left += ($a.ProcessId.ToString() + ':' + $a.CommandLine.Substring(0, [Math]::Min(90, $a.CommandLine.Length))) }
Write-Output ("browsers left: " + $left.Count)
$left | Select-Object -First 6 | ForEach-Object { Write-Output $_ }
