param([string]$Name = "JoeBrowser")
# Dumps every top-level and child window of the app process, with class and rect.
Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class WinDump {
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr h, EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  public static List<string> Top(uint pid) {
    var o = new List<string>();
    EnumWindows(delegate(IntPtr h, IntPtr l) {
      uint p; GetWindowThreadProcessId(h, out p);
      if (p == pid && IsWindowVisible(h)) {
        RECT r; GetWindowRect(h, out r);
        var sb = new StringBuilder(200); GetClassName(h, sb, 200);
        o.Add("TOP " + sb + " | " + r.Left + "," + r.Top + " " + (r.Right - r.Left) + "x" + (r.Bottom - r.Top));
      }
      return true;
    }, IntPtr.Zero);
    return o;
  }
  public static List<string> Kids(IntPtr root) {
    var o = new List<string>();
    EnumChildWindows(root, delegate(IntPtr h, IntPtr l) {
      RECT r; GetWindowRect(h, out r);
      var sb = new StringBuilder(200); GetClassName(h, sb, 200);
      o.Add("CHILD " + sb + " | " + r.Left + "," + r.Top + " " + (r.Right - r.Left) + "x" + (r.Bottom - r.Top));
      return true;
    }, IntPtr.Zero);
    return o;
  }
  public static IntPtr Find(IntPtr from, uint pid) {
    IntPtr found = IntPtr.Zero;
    EnumWindows(delegate(IntPtr h, IntPtr l) {
      uint p; GetWindowThreadProcessId(h, out p);
      if (p == pid) { RECT r; GetWindowRect(h, out r); if (r.Right - r.Left >= 1000) found = h; }
      return true;
    }, IntPtr.Zero);
    return found;
  }
}
'@
$p = Get-Process -Name $Name -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { Write-Host "not running"; exit 1 }
$main = [WinDump]::Find([IntPtr]::Zero, [uint32]$p.Id)
[WinDump]::Top([uint32]$p.Id) | ForEach-Object { Write-Host $_ }
Write-Host "--- children of $main"
[WinDump]::Kids($main) | ForEach-Object { Write-Host $_ }
