# Runs one JavaScript expression in a running browser through the DevTools
# protocol and prints the result.
#
# The tools the application injects - paste as human, the proxy watch, the
# right-click guard - live in the page, so the only honest way to test them is
# to ask the page.
param(
    [Parameter(Mandatory = $true)][int]$Port,
    [Parameter(Mandatory = $true)][string]$Expr,
    [string]$Url = ""
)
$ErrorActionPreference = "Stop"

$targets = Invoke-RestMethod "http://127.0.0.1:$Port/json/list" -TimeoutSec 10
$pages = @($targets | Where-Object { $_.type -eq "page" })
$page = $null
if ($Url -ne "") { $page = $pages | Where-Object { $_.url -like "*$Url*" } | Select-Object -First 1 }
if (-not $page -and $pages.Count -gt 0) {
    # The first page target is often the empty one the browser opened with; the one with a
    # title is the one a person is looking at.
    $titled = $pages | Where-Object { $_.title -and $_.title.Trim().Length -gt 0 } | Select-Object -First 1
    $page = if ($titled) { $titled } else { $pages[0] }
}
if (-not $page) { Write-Host "no page target"; exit 1 }

$ws = New-Object System.Net.WebSockets.ClientWebSocket
$ct = New-Object System.Threading.CancellationTokenSource
$ws.ConnectAsync([Uri]$page.webSocketDebuggerUrl, $ct.Token).Wait(10000) | Out-Null

$msg = @{ id = 1; method = "Runtime.evaluate"; params = @{ expression = $Expr; awaitPromise = $true; returnByValue = $true } } | ConvertTo-Json -Depth 8 -Compress
$bytes = [Text.Encoding]::UTF8.GetBytes($msg)
$seg = New-Object System.ArraySegment[byte] (,$bytes)
$ws.SendAsync($seg, [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $ct.Token).Wait(10000) | Out-Null

$buf = New-Object byte[] 1048576
$sb = New-Object System.Text.StringBuilder
$deadline = (Get-Date).AddSeconds(40)
while ((Get-Date) -lt $deadline) {
    $r = $ws.ReceiveAsync((New-Object System.ArraySegment[byte] (,$buf)), $ct.Token).Result
    $chunk = [Text.Encoding]::UTF8.GetString($buf, 0, $r.Count)
    [void]$sb.Append($chunk)
    if ($r.EndOfMessage) {
        $text = $sb.ToString()
        if ($text -match '"id":1') {
            try {
                $j = $text | ConvertFrom-Json
                if ($j.result -and $j.result.result) {
                    if ($j.result.result.value -ne $null) {
                        Write-Host ("VALUE: " + ($j.result.result.value | ConvertTo-Json -Compress -Depth 4))
                    } else {
                        Write-Host ("RAW: " + ($j.result.result | ConvertTo-Json -Compress -Depth 6))
                    }
                } else {
                    Write-Host ("RAW: " + ($j | ConvertTo-Json -Compress -Depth 6))
                }
            } catch { Write-Host $text }
            break
        }
        [void]$sb.Clear()
    }
}
$ws.Dispose()
