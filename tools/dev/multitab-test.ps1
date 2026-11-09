param([int]$Port = 0)
# Opens a second tab in a running Joe Browser session and reads the fingerprint from that tab,
# which is what the per-target auto-attach path has to keep covered.
Add-Type -AssemblyName System.Net.Http
$ErrorActionPreference = "Stop"

if ($Port -le 0) {
  $cmd = Get-CimInstance Win32_Process -Filter "Name='chrome.exe'" |
    Where-Object { $_.CommandLine -like '*remote-debugging-port*' } |
    Select-Object -First 1 -ExpandProperty CommandLine
  $Port = [int]([regex]::Match($cmd, '--remote-debugging-port=(\d+)').Groups[1].Value)
}

$ver = Invoke-RestMethod "http://127.0.0.1:$Port/json/version"
$ws = New-Object System.Net.WebSockets.ClientWebSocket
$ct = [System.Threading.CancellationToken]::None
$ws.ConnectAsync([Uri]$ver.webSocketDebuggerUrl, $ct).Wait()

function Send($id, $method, $params, $sessionId) {
  $o = @{ id = $id; method = $method }
  if ($params) { $o.params = $params }
  if ($sessionId) { $o.sessionId = $sessionId }
  $json = $o | ConvertTo-Json -Depth 12 -Compress
  $bytes = [Text.Encoding]::UTF8.GetBytes($json)
  $seg = New-Object System.ArraySegment[byte] -ArgumentList @(,$bytes)
  $ws.SendAsync($seg, [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $ct).Wait()
}

function Recv() {
  $buf = New-Object byte[] 262144
  $seg = New-Object System.ArraySegment[byte] -ArgumentList @(,$buf)
  $ms = New-Object IO.MemoryStream
  while ($true) {
    $r = $ws.ReceiveAsync($seg, $ct).Result
    $ms.Write($buf, 0, $r.Count)
    if ($r.EndOfMessage) { break }
  }
  return [Text.Encoding]::UTF8.GetString($ms.ToArray()) | ConvertFrom-Json
}

function Wait-For($id) {
  while ($true) {
    $m = Recv
    if ($m.id -eq $id) { return $m }
  }
}

Send 1 "Target.createTarget" @{ url = "about:blank" } $null
$created = Wait-For 1
$targetId = $created.result.targetId
Write-Host "created target $targetId"

Start-Sleep -Seconds 6

Send 2 "Target.attachToTarget" @{ targetId = $targetId; flatten = $true } $null
$attached = Wait-For 2
$sid = $attached.result.sessionId
Write-Host "attached session $sid"

$expr = @'
JSON.stringify({
  webdriver: navigator.webdriver,
  ua: navigator.userAgent.slice(0, 40),
  langs: navigator.languages.join(','),
  tz: Intl.DateTimeFormat().resolvedOptions().timeZone,
  gl: (function(){ var c=document.createElement('canvas'); var g=c.getContext('webgl');
      if(!g) return 'NULL';
      var d=g.getExtension('WEBGL_debug_renderer_info');
      return d ? g.getParameter(d.UNMASKED_RENDERER_WEBGL) : 'no-ext'; })(),
  marker: (function(){ var k=Object.getOwnPropertyNames(window).filter(function(n){return /^__jb|^jbFP|^__playwright|^cdc_/.test(n);});
      return k.length ? k.join(',') : 'clean'; })(),
  plugIns: navigator.plugins.length + '/' + Object.prototype.toString.call(navigator.plugins),
  mem: navigator.deviceMemory,
  hints: navigator.userAgentData ? navigator.userAgentData.brands.map(function(b){return b.brand+'@'+b.version;}).join('|') : 'none'
})
'@

Send 3 "Runtime.evaluate" @{ expression = $expr; returnByValue = $true } $sid
$res = Wait-For 3
Write-Host "NEW TAB FINGERPRINT:"
$res.result.result.value
