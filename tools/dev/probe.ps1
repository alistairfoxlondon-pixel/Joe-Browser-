# Reads the surfaces a fingerprint site reads, and checks them for the
# contradictions that are reported as "WebGL exception" and "Canvas tampering".
#
# The probe runs in the page of a profile that the application launched, and the
# same probe runs in a browser the application did not launch: the difference
# between the two answers is exactly what the application changed.
param(
    [Parameter(Mandatory = $true)][int]$Port,
    [string]$Url = ""
)
$ErrorActionPreference = "Stop"

$expr = @'
(function(){
  var out = {};
  var g = document.createElement('canvas');
  var ctx = g.getContext('2d');
  function hash(s){ var h=0; for (var i=0;i<s.length;i++){ h=(h*31+s.charCodeAt(i))|0; } return h; }
  function draw(c){
    c.textBaseline='top'; c.font='14px "Arial"';
    c.fillStyle='#f60'; c.fillRect(125,1,62,20);
    c.fillStyle='#069'; c.fillText('Cwm fjordbank glyphs vext quiz 😃',2,15);
    c.fillStyle='rgba(102,204,0,0.7)'; c.fillText('Cwm fjordbank glyphs vext quiz 😃',4,17);
    c.globalCompositeOperation='multiply';
    c.fillStyle='rgb(255,0,255)'; c.beginPath(); c.arc(50,50,50,0,Math.PI*2,true); c.closePath(); c.fill();
  }
  draw(ctx);
  out.canvasUrl = g.toDataURL();
  out.canvasHash1 = hash(g.toDataURL());
  var id1 = ctx.getImageData(0,0,300,150).data;
  out.canvasHash2 = hash(g.toDataURL());
  out.canvasStable = out.canvasHash1 === out.canvasHash2;
  /* A second canvas, drawn the same way: a real browser gives the same picture. */
  var g2 = document.createElement('canvas'); g2.width=300; g2.height=150;
  var c2 = g2.getContext('2d'); draw(c2);
  out.canvasHashOther = hash(g2.toDataURL());
  out.canvasMatchesSecondCanvas = out.canvasHash1 === out.canvasHashOther;
  /* getImageData against toDataURL: they must describe the same pixels. */
  var sum1 = 0; for (var i=0;i<id1.length;i+=997) sum1 += id1[i];
  var sum2 = 0; for (var i=0;i<id1.length;i+=997) sum2 += id1[i];
  out.imageDataSum = sum1;
  out.ua = navigator.userAgent;
  out.brands = JSON.stringify(navigator.userAgentData ? navigator.userAgentData.brands : null);
  out.platform = navigator.platform;
  out.hardwareConcurrency = navigator.hardwareConcurrency;
  out.deviceMemory = navigator.deviceMemory;
  out.screen = screen.width + 'x' + screen.height + '@' + screen.colorDepth;
  out.timezone = Intl.DateTimeFormat().resolvedOptions().timeZone;
  out.languages = JSON.stringify(navigator.languages);
  try {
    var gl = document.createElement('canvas').getContext('webgl2') || document.createElement('canvas').getContext('webgl');
    if (!gl) { out.webgl = 'NO CONTEXT'; }
    else {
      var ext = gl.getExtension('WEBGL_debug_renderer_info');
      var p = {};
      p.VENDOR = gl.getParameter(gl.VENDOR);
      p.RENDERER = gl.getParameter(gl.RENDERER);
      p.VERSION = gl.getParameter(gl.VERSION);
      p.SHADING_LANGUAGE_VERSION = gl.getParameter(gl.SHADING_LANGUAGE_VERSION);
      p.UNMASKED_VENDOR = ext ? gl.getParameter(ext.UNMASKED_VENDOR_WEBGL) : 'no-ext';
      p.UNMASKED_RENDERER = ext ? gl.getParameter(ext.UNMASKED_RENDERER_WEBGL) : 'no-ext';
      p.MAX_TEXTURE_SIZE = gl.getParameter(gl.MAX_TEXTURE_SIZE);
      p.MAX_VIEWPORT_DIMS = JSON.stringify(gl.getParameter(gl.MAX_VIEWPORT_DIMS));
      p.MAX_RENDERBUFFER_SIZE = gl.getParameter(gl.MAX_RENDERBUFFER_SIZE);
      p.MAX_TEXTURE_IMAGE_UNITS = gl.getParameter(gl.MAX_TEXTURE_IMAGE_UNITS);
      p.MAX_VERTEX_ATTRIBS = gl.getParameter(gl.MAX_VERTEX_ATTRIBS);
      p.MAX_VARYING_VECTORS = gl.getParameter(gl.MAX_VARYING_VECTORS);
      p.MAX_VERTEX_UNIFORM_VECTORS = gl.getParameter(gl.MAX_VERTEX_UNIFORM_VECTORS);
      p.MAX_FRAGMENT_UNIFORM_VECTORS = gl.getParameter(gl.MAX_FRAGMENT_UNIFORM_VECTORS);
      p.ALIASED_LINE_WIDTH_RANGE = JSON.stringify(gl.getParameter(gl.ALIASED_LINE_WIDTH_RANGE));
      p.ALIASED_POINT_SIZE_RANGE = JSON.stringify(gl.getParameter(gl.ALIASED_POINT_SIZE_RANGE));
      p.extensions = (gl.getSupportedExtensions() || []).length;
      p.extensionNames = (gl.getSupportedExtensions() || []).slice(0, 6).join(',');
      out.webgl = p;
    }
  } catch (e) { out.webgl = 'THREW: ' + e.message; }
  /* The plugins and the mime types are read by every scanner as well. */
  out.plugins = Array.prototype.map.call(navigator.plugins || [], function(p){ return p.name; }).join(' | ');
  out.mimeTypes = Array.prototype.map.call(navigator.mimeTypes || [], function(m){ return m.type; }).join(' | ');
  out.webdriver = navigator.webdriver;
  out.permissions = (navigator.permissions && navigator.permissions.query ? 'present' : 'missing');
  out.notificationPermission = (typeof Notification !== 'undefined') ? Notification.permission : 'n/a';
  out.chromeRuntime = !!(window.chrome && window.chrome.runtime);
  out.outer = window.outerWidth + 'x' + window.outerHeight;
  out.inner = window.innerWidth + 'x' + window.innerHeight;
  return JSON.stringify(out);
})()
'@

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
$msg = @{ id = 1; method = "Runtime.evaluate"; params = @{ expression = $expr; returnByValue = $true } } | ConvertTo-Json -Depth 8 -Compress
$bytes = [Text.Encoding]::UTF8.GetBytes($msg)
$ws.SendAsync((New-Object System.ArraySegment[byte] (,$bytes)), [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $ct.Token).Wait(10000) | Out-Null
$buf = New-Object byte[] 1048576
$sb = New-Object System.Text.StringBuilder
$deadline = (Get-Date).AddSeconds(30)
while ((Get-Date) -lt $deadline) {
    $r = $ws.ReceiveAsync((New-Object System.ArraySegment[byte] (,$buf)), $ct.Token).Result
    [void]$sb.Append([Text.Encoding]::UTF8.GetString($buf, 0, $r.Count))
    if ($r.EndOfMessage) {
        $text = $sb.ToString()
        if ($text -match '"id":1') {
            $j = $text | ConvertFrom-Json
            $v = $j.result.result.value
            if ($v) {
                $o = $v | ConvertFrom-Json
                $o | Format-List
                if ($o.webgl -is [string]) { Write-Host ("webgl: " + $o.webgl) }
                else { Write-Host "--- webgl"; $o.webgl | Format-List }
            }
            else { Write-Host ("RAW: " + $text.Substring(0, [Math]::Min(1500, $text.Length))) }
            break
        }
        [void]$sb.Clear()
    }
}
$ws.Dispose()
