# Clicks one absolute point on the screen with a real mouse.
#
# The click helpers in ui.ps1 are relative to the window it found, and a modal
# dialog is a window of its own - so a click meant for a button on the dialog
# lands somewhere else entirely. This one takes screen coordinates and does not
# care which window is on top.
param(
    [Parameter(Mandatory = $true)][int]$X,
    [Parameter(Mandatory = $true)][int]$Y,
    [int]$Wait = 900,
    [string]$Out = ""
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

$sig = @"
using System;
using System.Runtime.InteropServices;
public static class ClickAt {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint d, uint e, uint o, IntPtr l);
}
"@
if (-not ("ClickAt" -as [type])) { Add-Type -TypeDefinition $sig }

[ClickAt]::SetCursorPos($X, $Y) | Out-Null
Start-Sleep -Milliseconds 150
[ClickAt]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 80
[ClickAt]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds $Wait
Write-Host "clicked $X,$Y"

if ($Out) {
    $b = [System.Windows.Forms.SystemInformation]::VirtualScreen
    $bmp = New-Object System.Drawing.Bitmap($b.Width, $b.Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($b.X, $b.Y, 0, 0, $bmp.Size)
    $g.Dispose()
    $bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "saved $Out"
}
