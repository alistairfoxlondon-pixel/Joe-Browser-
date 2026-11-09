param([int]$X = 0, [int]$Y = 0)
# Reports which top-level and child window sits under a screen point, plus its process.
Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class HitProbe {
  [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
  [DllImport("user32.dll")] public static extern IntPtr ChildWindowFromPointEx(IntPtr h, POINT p, uint flags);
  [DllImport("user32.dll")] public static extern bool ScreenToClient(IntPtr h, ref POINT p);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  public static string Describe(IntPtr h) {
    if (h == IntPtr.Zero) return "none";
    uint pid; GetWindowThreadProcessId(h, out pid);
    var sb = new StringBuilder(200); GetClassName(h, sb, 200);
    return sb + " pid=" + pid + " hwnd=" + h;
  }
}
'@
$pt = New-Object HitProbe+POINT
$pt.X = $X; $pt.Y = $Y
$h = [HitProbe]::WindowFromPoint($pt)
Write-Host ("at {0},{1}: {2}" -f $X, $Y, [HitProbe]::Describe($h))
Write-Host ("root: " + [HitProbe]::Describe([HitProbe]::GetAncestor($h, 2)))
$names = (Get-Process -Id (Get-Process JoeBrowser -ErrorAction SilentlyContinue | Select-Object -First 1).Id).ProcessName
Write-Host "joe pid: $((Get-Process JoeBrowser -ErrorAction SilentlyContinue | Select-Object -First 1).Id) ($names)"
