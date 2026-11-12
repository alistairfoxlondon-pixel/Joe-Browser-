# Clicks an element in a running browser through the DevTools protocol.
#
# The pixelscan and iphey checks only answer a trusted mouse event, so a
# scripted click is not enough - the click has to go through the input pipeline.
param(
    [Parameter(Mandatory = $true)][int]$Port,
    [Parameter(Mandatory = $true)][string]$Text,
    [string]$Out = "",
    [int]$Wait = 15
)
$ErrorActionPreference = "Stop"

$targets = Invoke-RestMethod "http://127.0.0.1:$Port/json/list" -TimeoutSec 10
$page = @($targets | Where-Object { $_.type -eq "page" } | Where-Object { $_.title } | Select-Object -First 1)
if (-not $page) { $page = @($targets | Where-Object { $_.type -eq "page" } | Select-Object -First 1) }
if (-not $page) { Write-Host "no page target"; exit 1 }

$ws = New-Object System.Net.WebSockets.ClientWebSocket
$ct = New-Object System.Threading.CancellationTokenSource
$ws.ConnectAsync([Uri]$page.webSocketDebuggerUrl, $ct.Token).Wait(10000) | Out-Null
$script:id = 0
function Send-Cdp([string]$method, $params) {
    $script:id++
    $msg = @{ id = $script:id; method = $method; params = $params } | ConvertTo-Json -Depth 8 -Compress
    $bytes = [Text.Encoding]::UTF8.GetBytes($msg)
    $ws.SendAsync((New-Object System.ArraySegment[byte] (,$bytes)), [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $ct.Token).Wait(15000) | Out-Null
    return $script:id
}
function Read-Result($wantId) {
    $buf = New-Object byte[] 8388608
    $sb = New-Object System.Text.StringBuilder
    $deadline = (Get-Date).AddSeconds(60)
    while ((Get-Date) -lt $deadline) {
        $r = $ws.ReceiveAsync((New-Object System.ArraySegment[byte] (,$buf)), $ct.Token).Result
        [void]$sb.Append([Text.Encoding]::UTF8.GetString($buf, 0, $r.Count))
        if ($r.EndOfMessage) {
            $text = $sb.ToString()
            [void]$sb.Clear()
            if ($text -match ('"id":' + $wantId + '[,\}]')) {
                try { return ($text | ConvertFrom-Json) } catch { return $null }
            }
        }
    }
    return $null
}

$expr = "(function(){ var els = document.querySelectorAll('button,a,div,span'); for (var i=0;i<els.length;i++){ var t=(els[i].innerText||'').trim(); if (t === " + (($Text | ConvertTo-Json -Compress)) + " && els[i].children.length === 0){ var r = els[i].getBoundingClientRect(); return JSON.stringify({x: Math.round(r.left + r.width/2), y: Math.round(r.top + r.height/2)}); } } return ''; })()"
$id = Send-Cdp "Runtime.evaluate" @{ expression = $expr; returnByValue = $true }
$r = Read-Result $id
$rect = $null
if ($r -and $r.result -and $r.result.result -and $r.result.result.value) { $rect = $r.result.result.value | ConvertFrom-Json }
if (-not $rect) { Write-Host "element not found"; $ws.Dispose(); exit 1 }
Write-Host ("clicking at " + $rect.x + "," + $rect.y)

$id = Send-Cdp "Input.dispatchMouseEvent" @{ type = "mouseMoved"; x = $rect.x; y = $rect.y }
Read-Result $id | Out-Null
$id = Send-Cdp "Input.dispatchMouseEvent" @{ type = "mousePressed"; x = $rect.x; y = $rect.y; button = "left"; clickCount = 1 }
Read-Result $id | Out-Null
$id = Send-Cdp "Input.dispatchMouseEvent" @{ type = "mouseReleased"; x = $rect.x; y = $rect.y; button = "left"; clickCount = 1 }
Read-Result $id | Out-Null

Start-Sleep -Seconds $Wait
if ($Out -ne "") {
    $id = Send-Cdp "Page.captureScreenshot" @{ format = "png" }
    $s = Read-Result $id
    if ($s -and $s.result) {
        [System.IO.File]::WriteAllBytes($Out, [Convert]::FromBase64String($s.result.data))
        Write-Host ("saved " + $Out)
    }
}
$ws.Dispose()
