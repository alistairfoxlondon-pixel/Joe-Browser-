# Brings the window to the front and grabs the screen as fast as it can, so a
# repaint that arrives in pieces is caught in the middle of it.
param(
    [string]$Out = "C:\dev\joe-browser\dist\comeback",
    [int]$Frames = 6,
    [int]$Width = 1440,
    [int]$Height = 900
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

$sig = @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class Back {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  public static System.Collections.Generic.List<IntPtr> ForProcess(uint target) {
    var list = new System.Collections.Generic.List<IntPtr>();
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
if (-not ("Back" -as [type])) { Add-Type -TypeDefinition $sig }

if (Test-Path $Out) { Remove-Item $Out -Recurse -Force }
New-Item -ItemType Directory -Force $Out | Out-Null

$proc = @(Get-Process -Name JoeBrowser -ErrorAction SilentlyContinue)
$found = [IntPtr]::Zero
foreach ($p in $proc) {
    foreach ($h in [Back]::ForProcess([uint32]$p.Id)) {
        if ([Back]::Title($h) -eq "Joe Browser") { $found = $h; break }
    }
    if ($found -ne [IntPtr]::Zero) { break }
}
if ($found -eq [IntPtr]::Zero) { Write-Host "window not found"; exit 1 }

# Put another window on top of it, then bring it back: that is the moment the
# window is repainted, and it is the moment the user sees it.
[Back]::keybd_event(0x12, 0, 0, [IntPtr]::Zero)
[Back]::keybd_event(0x12, 0, 2, [IntPtr]::Zero)
[Back]::SetForegroundWindow($found) | Out-Null
[Back]::BringWindowToTop($found) | Out-Null

$bmp = New-Object System.Drawing.Bitmap($Width, $Height)
$g = [System.Drawing.Graphics]::FromImage($bmp)
for ($i = 0; $i -lt $Frames; $i++) {
    $g.CopyFromScreen(0, 0, 0, 0, (New-Object System.Drawing.Size($Width, $Height)))
    $bmp.Save((Join-Path $Out ("f{0:d2}.png" -f $i)), [System.Drawing.Imaging.ImageFormat]::Png)
}
$g.Dispose(); $bmp.Dispose()
Write-Host "grabbed $Frames frames into $Out"
