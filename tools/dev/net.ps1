# Reads what a page sends to its server and what the server answers.
#
# The fingerprint sites send their measurements to an API and the API answers with
# the verdict, so the verdict is in the response and not on the page.
param(
    [Parameter(Mandatory = $true)][int]$Port,
    [Parameter(Mandatory = $true)][string]$Url,
    [string]$Match = "api",
    [int]$Wait = 35
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

Send-Cdp "Network.enable" @{} | Out-Null
Send-Cdp "Page.navigate" @{ url = $Url } | Out-Null
Start-Sleep -Seconds 12

# Click the scan button.
$expr = "(function(){ var els = document.querySelectorAll('button,a,div,span'); for (var i=0;i<els.length;i++){ var t=(els[i].innerText||'').trim(); if (t === 'Scan My Browser Now' && els[i].children.length === 0){ els[i].click(); return 'clicked'; } } return 'none'; })()"
Send-Cdp "Runtime.evaluate" @{ expression = $expr; returnByValue = $true } | Out-Null

$buf = New-Object byte[] 8388608
$sb = New-Object System.Text.StringBuilder
$deadline = (Get-Date).AddSeconds($Wait + 30)
$seen = @{}
while ((Get-Date) -lt $deadline) {
    try {
        $r = $ws.ReceiveAsync((New-Object System.ArraySegment[byte] (,$buf)), $ct.Token).Result
    } catch { break }
    [void]$sb.Append([Text.Encoding]::UTF8.GetString($buf, 0, $r.Count))
    if (-not $r.EndOfMessage) { continue }
    $text = $sb.ToString()
    [void]$sb.Clear()
    if ($text -match 'Network\.responseReceived' -and $text -match $Match) {
        try {
            $j = $text | ConvertFrom-Json
            $u = $j.params.response.url
            if ($u -and -not $seen.ContainsKey($u)) {
                $seen[$u] = $true
                Write-Host ("--- " + $u)
                $id = Send-Cdp "Network.getResponseBody" @{ requestId = $j.params.requestId }
                # The body comes in a later message; read it here.
                $deadline2 = (Get-Date).AddSeconds(15)
                while ((Get-Date) -lt $deadline2) {
                    $r2 = $ws.ReceiveAsync((New-Object System.ArraySegment[byte] (,$buf)), $ct.Token).Result
                    [void]$sb.Append([Text.Encoding]::UTF8.GetString($buf, 0, $r2.Count))
                    if (-not $r2.EndOfMessage) { continue }
                    $t2 = $sb.ToString()
                    [void]$sb.Clear()
                    if ($t2 -match ('"id":' + $id + '[,\}]')) {
                        $b = ($t2 | ConvertFrom-Json)
                        if ($b.result -and $b.result.body) { Write-Host $b.result.body.Substring(0, [Math]::Min(3000, $b.result.body.Length)) }
                        break
                    }
                }
            }
        } catch { }
    }
}
$ws.Dispose()
