"""Capture the Joe Browser window, optionally clicking a point first.

Usage: python shot.py <out.png> [wait] [clickX,clickY] [wait2]
Coordinates for the click are relative to the window's top-left corner.
"""
import ctypes
import ctypes.wintypes as wt
import subprocess
import sys
import time

from PIL import ImageGrab

EXE = r"C:\Users\Administrator\Documents\New folder\JoeBrowser\dist\JoeBrowser.exe"
OUT = sys.argv[1] if len(sys.argv) > 1 else r"C:\Users\Administrator\AppData\Local\Temp\jb-shot.png"
WAIT = float(sys.argv[2]) if len(sys.argv) > 2 else 5.0
CLICK = sys.argv[3] if len(sys.argv) > 3 else ""
WAIT2 = float(sys.argv[4]) if len(sys.argv) > 4 else 1.5

user32 = ctypes.windll.user32
proc = subprocess.Popen([EXE])
time.sleep(WAIT)

hwnds = []
EnumProc = ctypes.WINFUNCTYPE(wt.BOOL, wt.HWND, wt.LPARAM)


def _cb(hwnd, _):
    pid = wt.DWORD()
    user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
    if pid.value == proc.pid and user32.IsWindowVisible(hwnd):
        hwnds.append(hwnd)
    return True


user32.EnumWindows(EnumProc(_cb), 0)
if not hwnds:
    print("no visible window for pid", proc.pid)
    proc.terminate()
    sys.exit(1)

hwnd = hwnds[0]
rect = wt.RECT()
user32.GetWindowRect(hwnd, ctypes.byref(rect))

if CLICK:
    cx, cy = [int(v) for v in CLICK.split(",")]
    user32.SetForegroundWindow(hwnd)
    time.sleep(0.4)
    user32.SetCursorPos(rect.left + cx, rect.top + cy)
    time.sleep(0.2)
    user32.mouse_event(0x0002, 0, 0, 0, 0)  # left down
    time.sleep(0.08)
    user32.mouse_event(0x0004, 0, 0, 0, 0)  # left up
    time.sleep(WAIT2)

user32.GetWindowRect(hwnd, ctypes.byref(rect))
box = (rect.left, rect.top, rect.right, rect.bottom)
ImageGrab.grab(bbox=box, all_screens=True).save(OUT)
print("saved", OUT, box)
proc.terminate()
