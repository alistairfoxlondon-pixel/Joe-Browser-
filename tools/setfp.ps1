param(
  [Parameter(Mandatory=$true)][string]$Id,
  [string]$Name,
  [string]$Os,
  [string]$DeviceType,
  [int]$W = 0,
  [int]$H = 0,
  [double]$Dpr = 0,
  [string]$Vendor,
  [string]$Renderer,
  [int]$Cores = 0,
  [int]$Mem = 0,
  [int]$Touch = -1
)
$ErrorActionPreference = 'Stop'
$path = Join-Path $env:APPDATA ("JoeBrowser\profiles\" + $Id + ".json")
$bak  = Join-Path $PSScriptRoot ("profilebak\" + $Id + ".json")
if (-not (Test-Path $bak)) {
  New-Item -ItemType Directory -Force -Path (Join-Path $PSScriptRoot 'profilebak') | Out-Null
  Copy-Item $path $bak
}
$s = [IO.File]::ReadAllText($path)
function Sub([string]$pat, [string]$val, [string]$text) {
  [regex]::Replace($text, $pat, { param($m) $m.Groups[1].Value + $val + $m.Groups[3].Value })
}
if ($Name)    { $s = Sub '("Name":")([^"]*)(")' $Name $s }
if ($Os)      { $s = Sub '("Os":")([^"]*)(")' $Os $s }
if ($DeviceType) { $s = Sub '("DeviceType":")([^"]*)(")' $DeviceType $s }
if ($W -gt 0) {
  $s = [regex]::Replace($s, '"ScreenW":\d+', ('"ScreenW":' + $W))
  $s = [regex]::Replace($s, '"AvailW":\d+', ('"AvailW":' + $W))
}
if ($H -gt 0) {
  $s = [regex]::Replace($s, '"ScreenH":\d+', ('"ScreenH":' + $H))
  $s = [regex]::Replace($s, '"AvailH":\d+', ('"AvailH":' + $H))
}
if ($Dpr -gt 0)   { $s = [regex]::Replace($s, '"PixelRatio":[\d.]+', ('"PixelRatio":' + $Dpr)) }
if ($Vendor)      { $s = [regex]::Replace($s, '"WebGlVendor":"[^"]*"', ('"WebGlVendor":"' + $Vendor + '"')) }
if ($Renderer)    { $s = [regex]::Replace($s, '"WebGlRenderer":"[^"]*"', ('"WebGlRenderer":"' + $Renderer + '"')) }
if ($Cores -gt 0) { $s = [regex]::Replace($s, '"Cores":\d+', ('"Cores":' + $Cores)) }
if ($Mem -gt 0)   { $s = [regex]::Replace($s, '"Memory":\d+', ('"Memory":' + $Mem)) }
if ($Touch -ge 0) { $s = [regex]::Replace($s, '"TouchPoints":\d+', ('"TouchPoints":' + $Touch)) }
[IO.File]::WriteAllText($path, $s)
Write-Output ("patched " + $Id + " -> " + $Os + " " + $W + "x" + $H + " dpr " + $Dpr)
