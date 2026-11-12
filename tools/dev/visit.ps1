# Opens a page in a running browser and reads what it says.
#
# The fingerprint sites are the only honest judge of a fingerprint: they read
# the surfaces a real browser reads and compare them with what a real device
# reports. This navigates there, waits for the verdict and prints the text.
param(
    [Parameter(Mandatory = $true)][int]$Port,
    [Parameter(Mandatory = $true)][string]$Url,
    [int]$Wait = 30,
    [string]$Needle = ""
)
$ErrorActionPreference = "Stop"

$targets = Invoke-RestMethod "http://127.0.0.1:$Port/json/list" -TimeoutSec 10
$page = @($targets | Where-Object { $_.type -eq "page" } | Where-Object { $_.url -notlike "about:blank" } | Select-Object -First 1)
if (-not $page) { $page = @($targets | Where-Object { $_.type -eq "page" } | Select-Object -First 1) }
if (-not $page) { Write-Host "no page target"; exit 1 }

$ws = New-Object System.Net.WebSockets.ClientWebSocket
$ct = New-Object System.Threading.CancellationTokenSource
$ws.ConnectAsync([Uri]$page.webSocketDebuggerUrl, $ct.Token).Wait(10000) | Out-Null
$id = 0
function Send-Cdp([string]$method, $params) {
    $script:id++
    $msg = @{ id = $script:id; method = $method; params = $params } | ConvertTo-Json -Depth 8 -Compress
    $bytes = [Text.Encoding]::UTF8.GetBytes($msg)
    $ws.SendAsync((New-Object System.ArraySegment[byte] (,$bytes)), [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $ct.Token).Wait(10000) | Out-Null
}
function Read-Result($wantId) {
    $buf = New-Object byte[] 4194304
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

Send-Cdp "Page.navigate" @{ url = $Url }
Write-Host "navigating to $Url, waiting $Wait s"
Start-Sleep -Seconds $Wait

$expr = "(function(){ var t = document.body ? document.body.innerText : ''; return t.substring(0, 6000); })()"
Send-Cdp "Runtime.evaluate" @{ expression = $expr; returnByValue = $true }
$r = Read-Result $script:id
$text = $null
if ($r -and $r.result -and $r.result.result) { $text = $r.result.result.value }
if (-not $text) { Write-Host "no text"; $ws.Dispose(); exit 1 }
if ($Needle -ne "") {
    $lines = $text -split "`r?`n"
    $hit = $false
    foreach ($l in $lines) {
        if ($l -match $Needle) { Write-Host ("  > " + $l.Trim()); $hit = $true }
    }
    if (-not $hit) { Write-Host "  (nothing matched the needle)" }
}
Write-Host "---- page text ----"
Write-Host $text
$ws.Dispose()
