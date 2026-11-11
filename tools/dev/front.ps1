# Brings the Joe Browser window to the front and grabs the screen.
#
# A screen grab is the only way to see what is really on screen: PrintWindow
# hands back the pixels the window last painted, which for a page that has just
# been hidden is the page it used to show.
param(
    [string]$Out = "C:\dev\joe-browser\dist\front.png",
    [string]$Match = "Joe Browser",
    [int]$Wait = 0
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

$sig = @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class Front {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
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
if (-not ("Front" -as [type])) { Add-Type -TypeDefinition $sig }

if ($Wait -gt 0) { Start-Sleep -Milliseconds $Wait }

$proc = @(Get-Process -Name JoeBrowser -ErrorAction SilentlyContinue)
$found = [IntPtr]::Zero
foreach ($p in $proc) {
    foreach ($h in [Front]::ForProcess([uint32]$p.Id)) {
        if ([Front]::Title($h) -eq $Match) { $found = $h; break }
    }
    if ($found -ne [IntPtr]::Zero) { break }
}
if ($found -eq [IntPtr]::Zero) { Write-Host "window not found"; exit 1 }
[Front]::ShowWindow($found, 9) | Out-Null
[Front]::BringWindowToTop($found) | Out-Null
[Front]::SetForegroundWindow($found) | Out-Null
Start-Sleep -Milliseconds 700

$b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$bmp = New-Object System.Drawing.Bitmap($b.Width, $b.Height)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($b.X, $b.Y, 0, 0, $bmp.Size)
$g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Host ("saved " + $Out + " " + $b.Width + "x" + $b.Height)
