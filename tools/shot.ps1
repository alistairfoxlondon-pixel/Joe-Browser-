param(
    [string]$Exe = "C:\Users\Administrator\Documents\New folder\JoeBrowser\dist\JoeBrowser.exe",
    [string]$Out = "C:\temp\jb2\shot.png",
    [string]$Match = "Joe Browser",
    [int]$Wait = 2500,
    [int]$ClickX = -1,
    [int]$ClickY = -1,
    [int]$ClickW = -1,
    [int]$ClickH = -1,
    [int]$MoveX = -1,
    [int]$MoveY = -1,
    [int]$Key = -1,
    [int]$ResizeW = 0,
    [int]$ResizeH = 0,
    [int]$Click2X = -1,
    [int]$Click2Y = -1,
    [int]$WheelX = -1,
    [int]$WheelY = -1,
    [int]$Wheel = 0,
    [string]$Type = "",
    [switch]$Ctrl,
    [switch]$Launch,
    [switch]$Screen,
    [switch]$Top,
    [switch]$Real
)
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$sig = @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class Win {
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int ht, bool repaint);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int ht, uint flags);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint d, uint e, uint o, IntPtr l);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
  [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
  [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
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
Add-Type -TypeDefinition $sig

function Click-At([int]$sx, [int]$sy, [uint32]$OwnerPid) {
  if ($Real) {
    [Win]::SetCursorPos($sx, $sy) | Out-Null
    Start-Sleep -Milliseconds 120
    [Win]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 70
    [Win]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
    Write-Host "real click at $sx,$sy"
    return
  }
  $pt = New-Object Win+POINT; $pt.X = $sx; $pt.Y = $sy
  $target = [Win]::WindowFromPoint($pt)
  if ($target -eq [IntPtr]::Zero) { Write-Host "no window at $sx,$sy"; return }
  $tpid = [uint32]0; [Win]::GetWindowThreadProcessId($target, [ref]$tpid) | Out-Null
  if ($tpid -ne $OwnerPid) { Write-Host "point is covered by pid $tpid (want $OwnerPid)"; return }
  $lp = New-Object Win+POINT; $lp.X = 0; $lp.Y = 0
  [Win]::ClientToScreen($target, [ref]$lp) | Out-Null
  $lx = $sx - $lp.X; $ly = $sy - $lp.Y
  $lparam = [IntPtr]([int](($lx -band 0xFFFF) -bor ($ly -shl 16)))
  if ($Ctrl) { [Win]::SetForegroundWindow($found) | Out-Null; Start-Sleep -Milliseconds 120; [Win]::keybd_event(0x11, 0, 0, [IntPtr]::Zero); Start-Sleep -Milliseconds 80 }
  [Win]::PostMessage($target, 0x200, [IntPtr]::Zero, $lparam) | Out-Null
  [Win]::PostMessage($target, 0x201, [IntPtr]1, $lparam) | Out-Null
  [Win]::PostMessage($target, 0x202, [IntPtr]::Zero, $lparam) | Out-Null
  if ($Ctrl) { Start-Sleep -Milliseconds 80; [Win]::keybd_event(0x11, 0, 0x02, [IntPtr]::Zero) }
  Write-Host ("clicked hwnd $target local $lx,$ly")
}

function Capture([IntPtr]$h, [string]$path) {
  $r = New-Object Win+RECT
  [Win]::GetWindowRect($h, [ref]$r) | Out-Null
  $w = $r.Right - $r.Left; $ht = $r.Bottom - $r.Top
  if ($w -le 0 -or $ht -le 0) { Write-Host "bad size"; return }
  if ($Screen) {
    $b = [System.Windows.Forms.SystemInformation]::VirtualScreen
    $bmp = New-Object System.Drawing.Bitmap($b.Width, $b.Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($b.X, $b.Y, 0, 0, $bmp.Size)
    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host ("saved screen {0} {1}x{2}" -f $path, $b.Width, $b.Height)
    return
  }
  $bmp = New-Object System.Drawing.Bitmap($w, $ht)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $dc = $g.GetHdc()
  [Win]::PrintWindow($h, $dc, 2) | Out-Null
  $g.ReleaseHdc($dc); $g.Dispose()
  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose()
  Write-Host ("saved {0} {1}x{2}" -f $path, $w, $ht)
}

$name = [System.IO.Path]::GetFileNameWithoutExtension($Exe)
$proc = @(Get-Process -Name $name -ErrorAction SilentlyContinue)
if ($Launch -or $proc.Count -eq 0) {
  Start-Process $Exe
  Start-Sleep -Milliseconds $Wait
  $proc = @(Get-Process -Name $name -ErrorAction SilentlyContinue)
} else { Start-Sleep -Milliseconds 400 }
if ($proc.Count -eq 0) { Write-Host "process not running"; exit 1 }

$found = [IntPtr]::Zero
foreach ($p in $proc) {
  foreach ($h in [Win]::ForProcess([uint32]$p.Id)) {
    if (([Win]::Title($h)) -eq $Match) { $found = $h; break }
  }
  if ($found -ne [IntPtr]::Zero) { break }
}
if ($found -eq [IntPtr]::Zero) {
  foreach ($p in $proc) {
    foreach ($h in [Win]::ForProcess([uint32]$p.Id)) {
      if (([Win]::Title($h)) -like "*$Match*") { $found = $h; break }
    }
    if ($found -ne [IntPtr]::Zero) { break }
  }
}
if ($found -eq [IntPtr]::Zero) {
  Write-Host "window not found: $Match"
  foreach ($p in $proc) { foreach ($h in [Win]::ForProcess([uint32]$p.Id)) { Write-Host ("  visible: " + [Win]::Title($h)) } }
  exit 1
}

[Win]::SetForegroundWindow($found) | Out-Null
$foundPid = [uint32]0; [Win]::GetWindowThreadProcessId($found, [ref]$foundPid) | Out-Null
Start-Sleep -Milliseconds 300

if ($Top) {
  [Win]::ShowWindow($found, 9) | Out-Null
  [Win]::SetWindowPos($found, [IntPtr](-1), 40, 40, 0, 0, 0x0001 -bor 0x0040) | Out-Null
  Start-Sleep -Milliseconds 500
}

if ($ResizeW -gt 0 -or $ResizeH -gt 0) {
  $r = New-Object Win+RECT; [Win]::GetWindowRect($found, [ref]$r) | Out-Null
  $w = if ($ResizeW -gt 0) { $ResizeW } else { $r.Right - $r.Left }
  $ht = if ($ResizeH -gt 0) { $ResizeH } else { $r.Bottom - $r.Top }
  [Win]::MoveWindow($found, $r.Left, $r.Top, $w, $ht, $true) | Out-Null
  Start-Sleep -Milliseconds 500
}

if ($ClickX -ge 0 -and $ClickY -ge 0) {
  $r = New-Object Win+RECT; [Win]::GetWindowRect($found, [ref]$r) | Out-Null
  if ($ClickW -gt 0) { $x = $r.Left + $ClickX + [int]($ClickW / 2) } else { $x = $r.Left + $ClickX }
  if ($ClickH -gt 0) { $y = $r.Top + $ClickY + [int]($ClickH / 2) } else { $y = $r.Top + $ClickY }
  Click-At $x $y $foundPid
  Start-Sleep -Milliseconds 800
} elseif ($MoveX -ge 0 -and $MoveY -ge 0) {
  $r = New-Object Win+RECT; [Win]::GetWindowRect($found, [ref]$r) | Out-Null
  [Win]::SetCursorPos($r.Left + $MoveX, $r.Top + $MoveY) | Out-Null
  Start-Sleep -Milliseconds 500
}

if ($Click2X -ge 0 -and $Click2Y -ge 0) {
  $r = New-Object Win+RECT; [Win]::GetWindowRect($found, [ref]$r) | Out-Null
  Click-At ($r.Left + $Click2X) ($r.Top + $Click2Y) $foundPid
  Start-Sleep -Milliseconds 800
}

if ($Type.Length -gt 0) {
  [System.Windows.Forms.SendKeys]::SendWait($Type)
  Start-Sleep -Milliseconds 500
}

if ($Key -ge 0) {
  $vk = [IntPtr]$Key
  [Win]::PostMessage($found, 0x0100, $vk, [IntPtr]0) | Out-Null
  [Win]::PostMessage($found, 0x0101, $vk, [IntPtr]0) | Out-Null
  Start-Sleep -Milliseconds 700
}

if ($Wheel -ne 0 -and $WheelX -ge 0) {
  $r = New-Object Win+RECT; [Win]::GetWindowRect($found, [ref]$r) | Out-Null
  $sx = $r.Left + $WheelX; $sy = $r.Top + $WheelY
  $target = [Win]::WindowFromPoint((New-Object Win+POINT -Property @{X=$sx; Y=$sy}))
  $lp = [IntPtr]([int]((($sy -band 0xFFFF) -shl 16) -bor ($sx -band 0xFFFF)))
  $wp = [IntPtr](($Wheel -shl 16) -bor 1)
  [Win]::PostMessage($target, 0x020A, $wp, $lp) | Out-Null
  Write-Host ("wheeled $Wheel at $sx,$sy on hwnd $target")
  Start-Sleep -Milliseconds 600
}

Capture $found $Out
