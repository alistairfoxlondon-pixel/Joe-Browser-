# Dumps every WebGL parameter and the canvas surfaces a site reads, so the
# values the application produces can be compared with the values a real
# browser produces on the same machine.
param(
    [Parameter(Mandatory = $true)][int]$Port,
    [switch]$Canvas
)
$ErrorActionPreference = "Stop"

$expr = @'
(async function(){
  var out = {};
  function glDump(){
    var c = document.createElement('canvas');
    var g = c.getContext('webgl2') || c.getContext('webgl');
    if (!g) return 'NO CONTEXT';
    var names = ['VENDOR','RENDERER','VERSION','SHADING_LANGUAGE_VERSION','VENDOR','RENDERER',
      'MAX_TEXTURE_SIZE','MAX_CUBE_MAP_TEXTURE_SIZE','MAX_RENDERBUFFER_SIZE','MAX_VIEWPORT_DIMS',
      'MAX_TEXTURE_IMAGE_UNITS','MAX_VERTEX_TEXTURE_IMAGE_UNITS','MAX_COMBINED_TEXTURE_IMAGE_UNITS',
      'MAX_VERTEX_ATTRIBS','MAX_VERTEX_UNIFORM_VECTORS','MAX_FRAGMENT_UNIFORM_VECTORS',
      'MAX_VARYING_VECTORS','ALIASED_LINE_WIDTH_RANGE','ALIASED_POINT_SIZE_RANGE',
      'MAX_TEXTURE_MAX_ANISOTROPY_EXT','ALIASED_POINT_SIZE_RANGE','IMPLEMENTATION_COLOR_READ_FORMAT',
      'IMPLEMENTATION_COLOR_READ_TYPE','LOW_FLOAT','MEDIUM_FLOAT','HIGH_FLOAT','LOW_INT','MEDIUM_INT',
      'HIGH_INT','MAX_3D_TEXTURE_SIZE','MAX_ARRAY_TEXTURE_LAYERS','MAX_ELEMENTS_INDICES',
      'MAX_ELEMENTS_VERTICES','MAX_DRAW_BUFFERS','MAX_COLOR_ATTACHMENTS','MAX_SAMPLES',
      'RED_BITS','GREEN_BITS','BLUE_BITS','ALPHA_BITS','DEPTH_BITS','STENCIL_BITS','SUBPIXEL_BITS',
      'MAX_TEXTURE_MAX_ANISOTROPY_EXT','MAX_DUAL_SOURCE_DRAW_BUFFERS','TIME_ELAPSED'];
    var res = {};
    for (var i=0;i<names.length;i++){
      var n = names[i];
      var v = g[n];
      if (v === undefined) continue;
      try { var p = g.getParameter(v); res[n] = (p && p.length !== undefined) ? Array.prototype.slice.call(p).join(',') : String(p); }
      catch(e){ res[n] = 'THREW:' + e.name; }
    }
    var ext = g.getExtension('WEBGL_debug_renderer_info');
    try { res.UNMASKED_VENDOR = String(g.getParameter(ext.UNMASKED_VENDOR_WEBGL)); } catch(e){ res.UNMASKED_VENDOR='THREW'; }
    try { res.UNMASKED_RENDERER = String(g.getParameter(ext.UNMASKED_RENDERER_WEBGL)); } catch(e){ res.UNMASKED_RENDERER='THREW'; }
    res.extCount = (g.getSupportedExtensions()||[]).length;
    res.extList = (g.getSupportedExtensions()||[]).join(',');
    /* The shape of the patched methods is itself a signal. */
    try { res.getParameterSrc = WebGLRenderingContext.prototype.getParameter.toString().slice(0,60); } catch(e){ res.getParameterSrc='?'; }
    try { res.getExtensionSrc = WebGLRenderingContext.prototype.getExtension.toString().slice(0,60); } catch(e){ res.getExtensionSrc='?'; }
    try { res.getSupportedSrc = WebGLRenderingContext.prototype.getSupportedExtensions.toString().slice(0,60); } catch(e){ res.getSupportedSrc='?'; }
    return res;
  }
  out.gl = glDump();
  if (arguments[0] === 'canvas') { }
  return JSON.stringify(out);
})()
'@

$targets = Invoke-RestMethod "http://127.0.0.1:$Port/json/list" -TimeoutSec 10
$pages = @($targets | Where-Object { $_.type -eq "page" })
$page = $pages | Where-Object { $_.title -and $_.title.Trim().Length -gt 0 } | Select-Object -First 1
if (-not $page) { $page = $pages | Select-Object -First 1 }
if (-not $page) { Write-Host "no page target"; exit 1 }

$ws = New-Object System.Net.WebSockets.ClientWebSocket
$ct = New-Object System.Threading.CancellationTokenSource
$ws.ConnectAsync([Uri]$page.webSocketDebuggerUrl, $ct.Token).Wait(10000) | Out-Null
$msg = @{ id = 1; method = "Runtime.evaluate"; params = @{ expression = $expr; returnByValue = $true; awaitPromise = $true } } | ConvertTo-Json -Depth 8 -Compress
$bytes = [Text.Encoding]::UTF8.GetBytes($msg)
$ws.SendAsync((New-Object System.ArraySegment[byte] (,$bytes)), [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $ct.Token).Wait(10000) | Out-Null
$buf = New-Object byte[] 4194304
$sb = New-Object System.Text.StringBuilder
$deadline = (Get-Date).AddSeconds(40)
while ((Get-Date) -lt $deadline) {
    $r = $ws.ReceiveAsync((New-Object System.ArraySegment[byte] (,$buf)), $ct.Token).Result
    [void]$sb.Append([Text.Encoding]::UTF8.GetString($buf, 0, $r.Count))
    if ($r.EndOfMessage) {
        $text = $sb.ToString()
        if ($text -match '"id":1') {
            $j = $text | ConvertFrom-Json
            if ($j.result.result.value) {
                $o = $j.result.result.value | ConvertFrom-Json
                $o.gl | Format-List
            }
            else { Write-Host ("RAW: " + $text.Substring(0, [Math]::Min(1200, $text.Length))) }
            break
        }
        [void]$sb.Clear()
    }
}
$ws.Dispose()
