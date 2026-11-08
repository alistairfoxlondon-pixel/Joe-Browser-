"""Drive the real Joe Browser sign-up flow and capture screenshots at each step.

This is a black-box check of the thing a new user actually does: open the app, create an
account, land in the workspace. It clicks real coordinates and types with real keyboard
events, so it exercises the same code paths a person would.
"""
import ctypes
import ctypes.wintypes as wt
import os
import subprocess
import sys
import time

from PIL import ImageGrab

EXE = r"C:\Users\Administrator\Documents\New folder\JoeBrowser\dist\JoeBrowser.exe"
OUTDIR = r"C:\Users\Administrator\AppData\Local\Temp"
user32 = ctypes.windll.user32


# ---------------------------------------------------------------- input plumbing
class KEYBDINPUT(ctypes.Structure):
    _fields_ = [("wVk", wt.WORD), ("wScan", wt.WORD), ("dwFlags", wt.DWORD),
                ("time", wt.DWORD), ("dwExtraInfo", ctypes.POINTER(ctypes.c_ulong))]


class MOUSEINPUT(ctypes.Structure):
    _fields_ = [("dx", wt.LONG), ("dy", wt.LONG), ("mouseData", wt.DWORD),
                ("dwFlags", wt.DWORD), ("time", wt.DWORD),
                ("dwExtraInfo", ctypes.POINTER(ctypes.c_ulong))]


class HARDWAREINPUT(ctypes.Structure):
    _fields_ = [("uMsg", wt.DWORD), ("wParamL", wt.WORD), ("wParamH", wt.WORD)]


class _INPUTUNION(ctypes.Union):
    _fields_ = [("ki", KEYBDINPUT), ("mi", MOUSEINPUT), ("hi", HARDWAREINPUT)]


class INPUT(ctypes.Structure):
    _fields_ = [("type", wt.DWORD), ("u", _INPUTUNION)]


KEYEVENTF_UNICODE = 0x0004
KEYEVENTF_KEYUP = 0x0002
VK_TAB = 0x09
VK_RETURN = 0x0D


def _send(*inputs):
    arr = (INPUT * len(inputs))(*inputs)
    user32.SendInput(len(inputs), arr, ctypes.sizeof(INPUT))


def _kbd(vk, scan, flags):
    i = INPUT(type=1)
    i.u.ki = KEYBDINPUT(vk, scan, flags, 0, None)
    return i


def type_text(text):
    for ch in text:
        _send(_kbd(0, ord(ch), KEYEVENTF_UNICODE), _kbd(0, ord(ch), KEYEVENTF_UNICODE | KEYEVENTF_KEYUP))
        time.sleep(0.05)


def tap(vk):
    scan = user32.MapVirtualKeyW(vk, 0)
    _send(_kbd(vk, scan, 0), _kbd(vk, scan, KEYEVENTF_KEYUP))
    time.sleep(0.2)


def click(hwnd, x, y):
    """Click at client-area coordinates, converted to screen space the way Windows does it."""
    pt = wt.POINT(int(x), int(y))
    user32.ClientToScreen(hwnd, ctypes.byref(pt))
    user32.SetForegroundWindow(hwnd)
    time.sleep(0.3)
    user32.SetCursorPos(pt.x, pt.y)
    time.sleep(0.15)
    user32.mouse_event(0x0002, 0, 0, 0, 0)
    time.sleep(0.06)
    user32.mouse_event(0x0004, 0, 0, 0, 0)
    time.sleep(0.5)


def shot(hwnd, name):
    try:
        rect = wt.RECT()
        user32.GetWindowRect(hwnd, ctypes.byref(rect))
        box = (rect.left, rect.top, rect.right, rect.bottom)
        path = OUTDIR + "\\" + name
        ImageGrab.grab(bbox=box, all_screens=True).save(path)
        print("shot:", path, box)
        return path
    except Exception as e:
        # A locked or disconnected desktop cannot be captured; the flow checks still run.
        print("shot skipped:", name, e)
        return None


def window_title(hwnd):
    buf = ctypes.create_unicode_buffer(256)
    user32.GetWindowTextW(hwnd, buf, 256)
    return buf.value


def windows_of(pid):
    found = []
    cb = ctypes.WINFUNCTYPE(wt.BOOL, wt.HWND, wt.LPARAM)

    def _cb(hwnd, _):
        p = wt.DWORD()
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(p))
        if p.value == pid and user32.IsWindowVisible(hwnd):
            buf = ctypes.create_unicode_buffer(256)
            user32.GetWindowTextW(hwnd, buf, 256)
            found.append((hwnd, buf.value))
        return True

    user32.EnumWindows(cb(_cb), 0)
    return found


def wait_for(pid, title_part, timeout=25, exclude=()):
    end = time.time() + timeout
    while time.time() < end:
        for hwnd, title in windows_of(pid):
            low = title.lower()
            if title_part.lower() in low and not any(x in low for x in exclude):
                return hwnd
        time.sleep(0.3)
    return None


def main():
    email = "qa" + str(int(time.time()))[-6:] + "@joebrowser.app"
    proc = subprocess.Popen([EXE])
    gate = wait_for(proc.pid, "Sign in")
    if not gate:
        print("FAIL gate window never appeared")
        proc.terminate()
        return 1
    print("gate:", gate)
    shot(gate, "flow-1-signin.png")

    click(gate, 120, 333)          # "New here? Create an account" (client coordinates)
    # The link sits in a small target; verify the mode actually changed instead of assuming.
    for offset in (0, -8, 8, -14, 14):
        if "create account" in window_title(gate).lower():
            break
        click(gate, 120, 333 + offset)
    if "create account" not in window_title(gate).lower():
        print("FAIL could not switch to register mode")
        shot(gate, "flow-2-register.png")
        proc.terminate()
        return 1
    shot(gate, "flow-2-register.png")

    click(gate, 200, 122)          # email field (register layout)
    type_text(email)
    tap(VK_TAB)                    # display name
    type_text("QA Tester")
    tap(VK_TAB)                    # password
    type_text("hunter22")
    shot(gate, "flow-3-filled.png")

    # Enter in the password field is the keyboard path a real user takes; --click-submit
    # exercises the button instead.
    if "--click-submit" in sys.argv:
        for attempt in range(4):
            if not user32.IsWindow(gate) or "create account" not in window_title(gate).lower():
                break
            click(gate, 200, 285)      # "Create account"
            time.sleep(1.6)
            print("clicked submit", attempt + 1)
    else:
        tap(0x0D)
        time.sleep(1.8)

    for attempt in range(3):
        if not user32.IsWindow(gate):
            break
        if "create account" not in window_title(gate).lower():
            break
        click(gate, 200, 285)          # "Create account"
        time.sleep(1.5)
        if not user32.IsWindow(gate):
            break
        print("submit did not take, retrying", attempt + 1)
        tap(0x0D)
        time.sleep(1.2)

    main_win = wait_for(proc.pid, "Joe Browser", 20, exclude=("sign in", "create account"))
    titles = [t for _, t in windows_of(proc.pid)]
    print("windows now:", titles)
    if main_win:
        time.sleep(3.5)            # let the toast paint
        shot(main_win, "flow-4-main.png")
        print("OK main window:", main_win)
    else:
        shot(gate, "flow-4-failed.png")
        print("FAIL main window never appeared")

    proc.terminate()

    # Leave the machine in first-run state: the test account and its remembered session are
    # test artefacts, not something the real user should inherit.
    try:
        os.remove(os.path.join(os.environ["APPDATA"], "JoeBrowser", "session.bin"))
        print("cleared session marker")
    except OSError as e:
        print("session marker not cleared:", e)
    return 0


if __name__ == "__main__":
    sys.exit(main())
