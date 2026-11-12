# Clicks the theme switch with a real mouse and grabs the screen as fast as it
# can, so a flash, a half-painted frame or a stale palette is visible instead
# of being described.
param(
    [string]$Exe = "C:\dev\joe-browser\dist\JoeBrowser.exe",
    [string]$Dir = "C:\dev\joe-browser\dist\flicker",
    [int]$Frames = 16,
    [int]$SwitchX = 70,
    [int]$SwitchY = 862,
    [int]$Width = 1440,
    [int]$Height = 900
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$sig = @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class F {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint d, uint e, uint o, IntPtr l);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
  [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int ht, bool repaint);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int ht, uint flags);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  public static System.Collections.Generic.List<IntPtr> ForProcess(uint target) {
    var list = new System.Collections.Generic.List<IntPtr>();
    EnumWindows(delegate(IntPtr h, IntPtr l) {
      if (!IsWindowVisible(h)) return true;
      uint pid; GetWindowThreadProcessId(h, out pid);
      if (pid == target) { var sb = new StringBuilder(256); GetWindowText(h, sb, 256); if (sb.Length > 0) list.Add(h); }
      return true;
    }, IntPtr.Zero);
    return list;
  }
  public static string Title(IntPtr h) { var sb = new StringBuilder(256); GetWindowText(h, sb, 256); return sb.ToString(); }
}
"@
if (-not ("F" -as [type])) { Add-Type -TypeDefinition $sig }

if (Test-Path $Dir) { Remove-Item $Dir -Recurse -Force }
New-Item -ItemType Directory -Force $Dir | Out-Null

$name = [System.IO.Path]::GetFileNameWithoutExtension($Exe)
$proc = @(Get-Process -Name $name -ErrorAction SilentlyContinue)
if ($proc.Count -eq 0) { Write-Host "not running"; exit 1 }
$found = [IntPtr]::Zero
foreach ($p in $proc) {
    foreach ($h in [F]::ForProcess([uint32]$p.Id)) {
        if (([F]::Title($h)) -like "*Joe Browser*") { $found = $h; break }
    }
    if ($found -ne [IntPtr]::Zero) { break }
}
if ($found -eq [IntPtr]::Zero) { Write-Host "window not found"; exit 1 }
[F]::SetForegroundWindow($found) | Out-Null
# The foreground lock belongs to whatever else is running, and a click that lands on
# another application does nothing at all. The window is made topmost for the run and
# put back afterwards.
[F]::SetWindowPos($found, [IntPtr](-1), 0, 0, 0, 0, 0x0001 -bor 0x0040) | Out-Null
$r = New-Object F+RECT; [F]::GetWindowRect($found, [ref]$r) | Out-Null
[F]::MoveWindow($found, $r.Left, $r.Top, $Width, $Height, $true) | Out-Null
Start-Sleep -Milliseconds 700
[F]::SetForegroundWindow($found) | Out-Null
Start-Sleep -Milliseconds 300

$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$bmp = New-Object System.Drawing.Bitmap($Width, $Height)
$g = [System.Drawing.Graphics]::FromImage($bmp)

function Grab([string]$path) {
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size($Width, $Height)))
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
}

# Real mouse click on the theme switch. Another application's window may hold the
# foreground, and a click that lands on it goes there instead - so the ALT key is
# pressed first, which is the documented way for a process to take the foreground.
[F]::keybd_event(0x12, 0, 0, [IntPtr]::Zero)
[F]::keybd_event(0x12, 0, 2, [IntPtr]::Zero)
Start-Sleep -Milliseconds 120
$sx = $r.Left + $SwitchX; $sy = $r.Top + $SwitchY
[F]::SetCursorPos($sx, $sy) | Out-Null
Start-Sleep -Milliseconds 150
[F]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 30
[F]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)

for ($i = 0; $i -lt $Frames; $i++) {
    Grab (Join-Path $Dir ("f{0:d2}.png" -f $i))
}
$g.Dispose(); $bmp.Dispose()
[F]::SetWindowPos($found, [IntPtr](-2), 0, 0, 0, 0, 0x0001 -bor 0x0040) | Out-Null
Write-Host "grabbed $Frames frames into $Dir"
