# Prints the string table of one of the fingerprint site's bundles, decoded.
#
# The bundle is obfuscated: the strings live in an array and every use of them
# goes through a small lookup. Decoding the table is what turns "x(331)" into
# the name of the check.
param(
    [string]$Url = "https://www.browserscan.net/dist/Cokn8RlA.js",
    [int[]]$Index = @(252, 262, 263, 269, 271, 272, 275, 282, 285, 286, 291, 292, 296, 297, 300, 310, 312, 317, 318, 320, 321, 322, 324, 326, 331, 333, 336, 337, 338, 341, 342, 349)
)
$ErrorActionPreference = "Stop"
$c = (Invoke-WebRequest $Url -UseBasicParsing -TimeoutSec 60).Content
$i = $c.IndexOf("function X()")
if ($i -lt 0) { Write-Host "no table"; exit 1 }
$seg = $c.Substring($i, [Math]::Min(12000, $c.Length - $i))
$open = $seg.IndexOf("[")
$close = $seg.IndexOf("];")
if ($open -lt 0 -or $close -lt 0) { Write-Host "no array"; exit 1 }
$raw = $seg.Substring($open + 1, $close - $open - 1)
$items = @()
foreach ($m in [regex]::Matches($raw, '"((?:[^"\\]|\\.)*)"')) { $items += $m.Groups[1].Value }
Write-Host ("items: " + $items.Count)
# The table is rotated before it is used, so the index of a string is not the
# position it was written at. Two strings that are known from the code around
# the checks give the shift.
$known = @{ 317 = 'indexOf'; 312 = '/png;base64' }
$shift = $null
foreach ($k in $known.Keys) {
    $want = $known[$k]
    $pos = [array]::IndexOf($items, $want)
    if ($pos -ge 0) { $s = $pos - ($k - 252); if ($null -eq $shift) { $shift = $s } elseif ($shift -ne $s) { $shift = -1 } }
}
if ($null -eq $shift -or $shift -lt 0) { Write-Host "could not work out the shift"; $shift = 0 }
Write-Host ("shift: " + $shift)
foreach ($n in $Index) {
    $idx = $n - 252 + $shift
    if ($idx -ge 0 -and $idx -lt $items.Count) { Write-Host ("{0} = '{1}'" -f $n, $items[$idx]) }
}
