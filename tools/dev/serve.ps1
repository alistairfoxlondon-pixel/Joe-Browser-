# Serves one page and keeps running until it is stopped.
#
# A page on a real origin is the only place an extension's content script runs:
# a data: URL and a file: URL are both excluded from it.
param(
    [int]$Port = 8899,
    [string]$Html = @"
<!doctype html>
<html><body>
<textarea id="t" style="width:400px;height:80px"></textarea>
<div id="out"></div>
<script>
function run(){
  var t = document.getElementById('t');
  t.focus();
  var dt = new DataTransfer();
  dt.setData('text/plain', 'Hello human');
  var ev = new ClipboardEvent('paste', { clipboardData: dt, bubbles: true, cancelable: true });
  var reached = t.dispatchEvent(ev);
  setTimeout(function(){
    document.getElementById('out').textContent = 'reached=' + reached + ' typed=[' + t.value + ']';
    document.title = 'reached=' + reached + ' typed=[' + t.value + ']';
  }, 8000);
}
run();
</script>
</body></html>
"@
)
$ErrorActionPreference = "Stop"
$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add("http://127.0.0.1:$Port/")
$listener.Start()
$bytes = [Text.Encoding]::UTF8.GetBytes($Html)
while ($listener.IsListening) {
    try {
        $ctx = $listener.GetContext()
        $ctx.Response.ContentType = "text/html; charset=utf-8"
        $ctx.Response.ContentLength64 = $bytes.Length
        $ctx.Response.OutputStream.Write($bytes, 0, $bytes.Length)
        $ctx.Response.OutputStream.Close()
    } catch { break }
}
