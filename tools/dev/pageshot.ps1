# Takes a screenshot of a page in a running browser through the DevTools
# protocol, so what the page shows can be seen even when the window is behind
# something else.
param(
    [Parameter(Mandatory = $true)][int]$Port,
    [Parameter(Mandatory = $true)][string]$Out,
    [string]$Url = "",
    [int]$Wait = 0,
    [switch]$Full
)
$ErrorActionPreference = "Stop"

$targets = Invoke-RestMethod "http://127.0.0.1:$Port/json/list" -TimeoutSec 10
$pages = @($targets | Where-Object { $_.type -eq "page" })
$page = $null
if ($Url -ne "") { $page = $pages | Where-Object { $_.url -like "*$Url*" } | Select-Object -First 1 }
if (-not $page -and $pages.Count -gt 0) {
    $titled = $pages | Where-Object { $_.title -and $_.title.Trim().Length -gt 0 } | Select-Object -First 1
    $page = if ($titled) { $titled } else { $pages[0] }
}
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
    $buf = New-Object byte[] 33554432
    $sb = New-Object System.Text.StringBuilder
    $deadline = (Get-Date).AddSeconds(90)
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

if ($Wait -gt 0) { Start-Sleep -Seconds $Wait }

$id = Send-Cdp "Page.captureScreenshot" @{ format = "png"; captureBeyondViewport = [bool]$Full }
$r = Read-Result $id
if (-not $r -or -not $r.result) { Write-Host "no screenshot"; $ws.Dispose(); exit 1 }
$data = $r.result.data
$bytes = [Convert]::FromBase64String($data)
[System.IO.File]::WriteAllBytes($Out, $bytes)
Write-Host ("saved " + $Out + " " + $bytes.Length + " bytes")
$ws.Dispose()
