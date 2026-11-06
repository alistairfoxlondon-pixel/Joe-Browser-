param(
    [int]$Port = 9222,
    [string]$Expr = "navigator.userAgent",
    [string]$ExprFile = "",
    [string]$Shot = "",
    [string]$Match = "about:blank"
)
if ($ExprFile.Length -gt 0) { $Expr = Get-Content -Raw -LiteralPath $ExprFile }
[void][Reflection.Assembly]::Load('System.Net.WebSockets.Client, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a')
[void][Reflection.Assembly]::Load('System.Net.WebSockets, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a')
$targets = Invoke-RestMethod -Uri "http://127.0.0.1:$Port/json" -TimeoutSec 10
$page = $targets | Where-Object { $_.type -eq 'page' -and $_.url -like "*$Match*" } | Select-Object -First 1
if (-not $page) { $page = $targets | Where-Object { $_.webSocketDebuggerUrl } | Select-Object -First 1 }
if (-not $page) { Write-Host "no target"; exit 1 }

$ws = New-Object System.Net.WebSockets.ClientWebSocket
$ct = [System.Threading.CancellationToken]::None
$ws.ConnectAsync([Uri]$page.webSocketDebuggerUrl, $ct).Wait()

function Send-Text($socket, $text) {
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($text)
    $seg = New-Object "System.ArraySegment[byte]" -ArgumentList $bytes, 0, $bytes.Length
    $socket.SendAsync($seg, [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $ct).Wait()
}

$id = 1
if ($Shot.Length -gt 0) {
    $payload = @{ id = $id; method = "Page.captureScreenshot"; params = @{ format = "png" } } | ConvertTo-Json -Compress -Depth 6
} else {
    $payload = @{ id = $id; method = "Runtime.evaluate"; params = @{ expression = $Expr; returnByValue = $true } } | ConvertTo-Json -Compress -Depth 6
}
Send-Text $ws $payload

$buf = New-Object byte[] 2000000
$sb = New-Object System.Text.StringBuilder
for ($i = 0; $i -lt 40; $i++) {
    $seg = New-Object "System.ArraySegment[byte]" -ArgumentList $buf, 0, $buf.Length
    $task = $ws.ReceiveAsync($seg, $ct)
    if (-not $task.Wait(15000)) { break }
    [void]$sb.Append([System.Text.Encoding]::UTF8.GetString($buf, 0, $task.Result.Count))
    if ($task.Result.EndOfMessage) { break }
}
$resp = $sb.ToString() | ConvertFrom-Json
if ($resp.error) { Write-Host ("ERROR " + ($resp.error | ConvertTo-Json -Compress)) }
elseif ($Shot.Length -gt 0) {
    [IO.File]::WriteAllBytes($Shot, [Convert]::FromBase64String($resp.result.data))
    Write-Host ("shot saved " + $Shot)
}
else { ($resp.result.result.value | ConvertTo-Json -Depth 8) }
$ws.Dispose()
