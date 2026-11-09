param([string]$Filter = 'JoeBrowser')
Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public class W {
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
  public delegate bool EnumProc(IntPtr h, IntPtr l);
}
"@
$procs = Get-Process -Name $Filter -EA 0 | Select-Object -ExpandProperty Id
$sb = New-Object Text.StringBuilder 300
$cb = [W+EnumProc] {
  param($h, $l)
  $wp = [uint32]0
  [void][W]::GetWindowThreadProcessId($h, [ref]$wp)
  if ($procs -contains [int]$wp -and [W]::IsWindowVisible($h)) {
    [void][W]::GetWindowText($h, $sb, 300)
    $t = $sb.ToString()
    if ($t.Length -gt 0) { Write-Output ("{0}  hwnd={1}  pid={2}" -f $t, $h, $wp) }
  }
  return $true
}
[void][W]::EnumWindows($cb, [IntPtr]::Zero)
