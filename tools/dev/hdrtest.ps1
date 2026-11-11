# Checks that the header band of the window is clean after the page changes.
#
# The search field, the sort button and the New profile button belong to the
# Profiles page. Hiding a child control does not ask the parent to paint over
# it, so their pixels can stay on the window after the page is left. The check
# grabs the window's own rectangle off the screen - no PrintWindow, which
# re-renders the window and would hide the fault - and reports the pixels that
# are still there.
param(
    [string]$Exe = "C:\dev\joe-browser\dist\JoeBrowser.exe",
    [string]$Dir = "C:\dev\joe-browser\dist\hdr",
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
public static class Hdr {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint d, uint e, uint o, IntPtr l);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
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
if (-not ("Hdr" -as [type])) { Add-Type -TypeDefinition $sig }

if (Test-Path $Dir) { Remove-Item $Dir -Recurse -Force }
New-Item -ItemType Directory -Force $Dir | Out-Null

Get-Process -Name JoeBrowser -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 900
Start-Process $Exe -ArgumentList "--portable"
Start-Sleep -Seconds 8

$proc = @(Get-Process -Name JoeBrowser -ErrorAction SilentlyContinue)
$found = [IntPtr]::Zero
foreach ($p in $proc) {
    foreach ($h in [Hdr]::ForProcess([uint32]$p.Id)) {
        if ([Hdr]::Title($h) -eq "Joe Browser") { $found = $h; break }
    }
    if ($found -ne [IntPtr]::Zero) { break }
}
if ($found -eq [IntPtr]::Zero) { Write-Host "window not found"; exit 1 }

$r = New-Object Hdr+RECT
[Hdr]::GetWindowRect($found, [ref]$r) | Out-Null
[Hdr]::BringWindowToTop($found) | Out-Null
[Hdr]::SetForegroundWindow($found) | Out-Null
Start-Sleep -Milliseconds 600

function Grab([string]$name) {
    $rect = New-Object Hdr+RECT
    [Hdr]::GetWindowRect($found, [ref]$rect) | Out-Null
    $w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size($w, $h)))
    $g.Dispose()
    $path = Join-Path $Dir $name
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "saved $path"
}

function Click([int]$x, [int]$y) {
    $rect = New-Object Hdr+RECT
    [Hdr]::GetWindowRect($found, [ref]$rect) | Out-Null
    [Hdr]::SetCursorPos($rect.Left + $x, $rect.Top + $y) | Out-Null
    Start-Sleep -Milliseconds 150
    [Hdr]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 80
    [Hdr]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 900
}

Grab "01-profiles.png"
Click 98 404          # Browsers
Grab "02-browsers.png"
Click 103 358         # Extensions
Grab "03-extensions.png"
Click 96 266          # Account
Grab "04-account.png"
Click 94 174          # Profiles
Grab "05-profiles-again.png"
Click 98 404          # Browsers
Grab "06-browsers-again.png"
Write-Host "done"
