param(
    [int]$X = 0, [int]$Y = 0, [int]$W = 0, [int]$H = 0,
    [string]$Match = "JoeBrowser",
    [int]$Dx = -9999, [int]$Dy = -9999
)
# Clicks the child control of Joe Browser whose client rect matches (X,Y,W,H); handy when the
# window manager will not hit-test a painted control from a raw screen point.
Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class WinClick {
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr h, EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
  [DllImport("user32.dll")] public static extern IntPtr PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  public static IntPtr Root(uint pid) {
    IntPtr found = IntPtr.Zero;
    EnumWindows(delegate(IntPtr h, IntPtr l) {
      uint p; GetWindowThreadProcessId(h, out p);
      if (p == pid) { RECT r; GetWindowRect(h, out r); if (r.Right - r.Left >= 1000) found = h; }
      return true;
    }, IntPtr.Zero);
    return found;
  }
  public static IntPtr At(IntPtr root, int x, int y, int w, int h) {
    POINT origin; origin.X = 0; origin.Y = 0; ClientToScreen(root, ref origin);
    IntPtr hit = IntPtr.Zero;
    EnumChildWindows(root, delegate(IntPtr c, IntPtr l) {
      RECT r; GetWindowRect(c, out r);
      if (r.Left - origin.X == x && r.Top - origin.Y == y && r.Right - r.Left == w && r.Bottom - r.Top == h) hit = c;
      return true;
    }, IntPtr.Zero);
    return hit;
  }
  public static void Press(IntPtr h, int x, int y) {
    IntPtr lp = (IntPtr)((y << 16) | (x & 0xFFFF));
    PostMessage(h, 0x0200, IntPtr.Zero, lp);
    PostMessage(h, 0x0201, (IntPtr)1, lp);
    System.Threading.Thread.Sleep(60);
    PostMessage(h, 0x0202, IntPtr.Zero, lp);
  }
}
'@
$p = Get-Process -Name $Match -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { Write-Host "not running"; exit 1 }
$root = [WinClick]::Root([uint32]$p.Id)
$child = [WinClick]::At($root, $X, $Y, $W, $H)
if ($child -eq [IntPtr]::Zero) { Write-Host "no child at $X,$Y ${W}x${H}"; exit 1 }
$x = if ($Dx -ne -9999) { $Dx } else { [int]($W / 2) }
$y = if ($Dy -ne -9999) { $Dy } else { [int]($H / 2) }
[WinClick]::Press($child, $x, $y)
Write-Host "clicked child $child at $X,$Y ${W}x${H} local $x,$y"
