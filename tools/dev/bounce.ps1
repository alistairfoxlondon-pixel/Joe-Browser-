# Bounces the application's window behind the desktop and back to the front,
# grabbing frames through the transition, so a repaint that arrives in pieces
# is caught in the act.
param(
    [string]$Out = "C:\dev\joe-browser\dist\bounce",
    [int]$Width = 1440,
    [int]$Height = 900,
    [int]$Frames = 10
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

$sig = @"
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class Bounce {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint d, uint e, uint o, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  public static List<IntPtr> ForProcess(uint target) {
    var list = new List<IntPtr>();
    EnumWindows(delegate(IntPtr h, IntPtr l) {
      if (!IsWindowVisible(h)) return true;
      uint pid; GetWindowThreadProcessId(h, out pid);
      if (pid == target) {
        var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
        if (sb.Length > 0) list.Add(h);
      }
      return true;
    }, IntPtr.Zero);
    return list;
  }
  public static string Title(IntPtr h) { var sb = new StringBuilder(256); GetWindowText(h, sb, 256); return sb.ToString(); }
}
"@
if (-not ("Bounce" -as [type])) { Add-Type -TypeDefinition $sig }

if (Test-Path $Out) { Remove-Item $Out -Recurse -Force }
New-Item -ItemType Directory -Force $Out | Out-Null

$proc = @(Get-Process -Name JoeBrowser -ErrorAction SilentlyContinue)
$jb = [IntPtr]::Zero
foreach ($p in $proc) {
    foreach ($h in [Bounce]::ForProcess([uint32]$p.Id)) {
        if ([Bounce]::Title($h) -eq "Joe Browser") { $jb = $h; break }
    }
    if ($jb -ne [IntPtr]::Zero) { break }
}
if ($jb -eq [IntPtr]::Zero) { Write-Host "Joe Browser window not found"; exit 1 }

# Put the desktop in front of the app: a click on the empty desktop area does it.
[Bounce]::mouse_event(0x8000, 1490, 480, 0, [IntPtr]::Zero)  # MOUSEEVENTF_MOVE (absolute coords, normalized below)
[Bounce]::keybd_event(0x12, 0, 0, [IntPtr]::Zero)
[Bounce]::keybd_event(0x12, 0, 2, [IntPtr]::Zero)

# Absolute move needs 0..65535 normalization
[Bounce]::mouse_event(0x8001, 1490 * 65535 / 1920, 480 * 65535 / 1080, 0, [IntPtr]::Zero)  # MOVE | ABSOLUTE
[Bounce]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)   # down
[Bounce]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)   # up
Start-Sleep -Milliseconds 700

# Now the app comes back to the front, the way a user brings it back.
[Bounce]::keybd_event(0x12, 0, 0, [IntPtr]::Zero)
[Bounce]::keybd_event(0x12, 0, 2, [IntPtr]::Zero)
[Bounce]::SetForegroundWindow($jb) | Out-Null
[Bounce]::BringWindowToTop($jb) | Out-Null

$bmp = New-Object System.Drawing.Bitmap($Width, $Height)
$g = [System.Drawing.Graphics]::FromImage($bmp)
for ($i = 0; $i -lt $Frames; $i++) {
    $g.CopyFromScreen(0, 0, 0, 0, (New-Object System.Drawing.Size($Width, $Height)))
    $bmp.Save((Join-Path $Out ("f{0:d2}.png" -f $i)), [System.Drawing.Imaging.ImageFormat]::Png)
    [System.Threading.Thread]::Sleep(8)
}
$g.Dispose(); $bmp.Dispose()
Write-Host "grabbed $Frames frames into $Out"
