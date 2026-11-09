param(
    [int]$Port = 0,
    [string]$Expr = ""
)
# Reads the live fingerprint from a Joe Browser session over the DevTools protocol so the
# anti-detect overrides can be verified without opening a test page by hand.
if ($Port -le 0) {
    $cmd = Get-CimInstance Win32_Process -Filter "Name='chrome.exe'" |
        Where-Object { $_.CommandLine -like '*remote-debugging-port*' } |
        Select-Object -First 1 -ExpandProperty CommandLine
    if (-not $cmd) { Write-Host "no joe browser chrome running"; exit 1 }
    $m = [regex]::Match($cmd, '--remote-debugging-port=(\d+)')
    if (-not $m.Success) { Write-Host "no debug port"; exit 1 }
    $Port = [int]$m.Groups[1].Value
}

$targets = Invoke-RestMethod "http://127.0.0.1:$Port/json/list"
$page = $targets | Where-Object { $_.type -eq 'page' } | Select-Object -First 1
if (-not $page) { Write-Host "no page target"; exit 1 }

if ($Expr -eq "") {
    $Expr = @'
((() => {
  const gl = (() => { try { const c = document.createElement('canvas'); const g = c.getContext('webgl');
    if (!g) return null; const d = g.getExtension('WEBGL_debug_renderer_info');
    return d ? g.getParameter(d.UNMASKED_RENDERER_WEBGL) + ' | ' + g.getParameter(d.UNMASKED_VENDOR_WEBGL) : g.getParameter(g.RENDERER); } catch (e) { return 'err ' + e; } })();
  return JSON.stringify({
    url: location.href,
    ua: navigator.userAgent,
    appVersion: navigator.appVersion,
    platform: navigator.platform,
    languages: navigator.languages,
    language: navigator.language,
    timezone: Intl.DateTimeFormat().resolvedOptions().timeZone,
    offset: new Date().getTimezoneOffset(),
    cores: navigator.hardwareConcurrency,
    memory: navigator.deviceMemory,
    screen: screen.width + 'x' + screen.height + '@' + screen.colorDepth,
    avail: screen.availWidth + 'x' + screen.availHeight,
    dpr: devicePixelRatio,
    touch: navigator.maxTouchPoints,
    webdriver: navigator.webdriver,
    webgl: gl,
    canvasNoise: (() => { try { const c = document.createElement('canvas'); c.width = 120; c.height = 30;
      const x = c.getContext('2d'); x.textBaseline = 'top'; x.font = '12px Arial'; x.fillStyle = '#f60';
      x.fillText('Joe Browser', 2, 2); return c.toDataURL().slice(-24); } catch (e) { return 'err'; } })(),
    doNotTrack: navigator.doNotTrack,
    uaCH: navigator.userAgentData ? { platform: navigator.userAgentData.platform, mobile: navigator.userAgentData.mobile } : null
  }, null, 2);
})())
'@
}

$ws = New-Object System.Net.WebSockets.ClientWebSocket
$ct = [System.Threading.CancellationToken]::None
$ws.ConnectAsync([Uri]$page.webSocketDebuggerUrl, $ct).Wait(5000) | Out-Null

function Send-Cdp([string]$json) {
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
    $seg = New-Object System.ArraySegment[byte] -ArgumentList @(,$bytes)
    $ws.SendAsync($seg, [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $ct).Wait(5000) | Out-Null
}

function Receive-Cdp() {
    $buf = New-Object byte[] 262144
    $seg = New-Object System.ArraySegment[byte] -ArgumentList @(,$buf)
    $sb = New-Object System.Text.StringBuilder
    do {
        $res = $ws.ReceiveAsync($seg, $ct).Result
        $sb.Append([System.Text.Encoding]::UTF8.GetString($buf, 0, $res.Count)) | Out-Null
    } while (-not $res.EndOfMessage)
    return $sb.ToString()
}

$payload = @{ id = 1; method = 'Runtime.evaluate'; params = @{ expression = $Expr; returnByValue = $true; awaitPromise = $true } } | ConvertTo-Json -Depth 8 -Compress
Send-Cdp $payload
for ($i = 0; $i -lt 20; $i++) {
    $msg = Receive-Cdp
    $obj = $msg | ConvertFrom-Json
    if ($obj.id -eq 1) {
        if ($obj.result.result.value) { Write-Host $obj.result.result.value }
        else { Write-Host ($obj | ConvertTo-Json -Depth 8) }
        break
    }
}
$ws.Dispose()
