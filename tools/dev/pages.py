"""Open Joe Browser, sign in if needed, then capture every page for visual review.

This is the "look at it in the real app" step: it drives the same window a user sees and
saves one PNG per navigation item so layout problems are visible instead of guessed at.
"""
import os
import subprocess
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import flow

OUT = r"C:\Users\Administrator\AppData\Local\Temp"
# (page name, y of the sidebar row centre at 100% DPI) - mirrors MainForm.LayoutAll:
# navY = 130, step = 44, and the System group starts one extra 12 px gap lower.
NAV = [
    ("profiles", 150),
    ("proxies", 194),
    ("extensions", 238),
    ("browsers", 282),
    ("settings", 338),
    ("tools", 382),
    ("about", 426),
    ("plan", 480),
]


def ensure_session(gate_or_main):
    """If the sign-in gate is up, register a throwaway account so we land in the workspace."""
    proc_pid = None
    return proc_pid


def main():
    email = "review" + str(int(time.time()))[-6:] + "@joebrowser.app"
    # --portable: the app refuses to run uninstalled, and this is the harness escape hatch.
    proc = subprocess.Popen([flow.EXE, "--portable"])
    gate = flow.wait_for(proc.pid, "Sign in", 20)

    if gate:
        flow.click(gate, 120, 333)          # switch to register
        for offset in (0, -8, 8, -14, 14):
            if "create account" in flow.window_title(gate).lower():
                break
            flow.click(gate, 120, 333 + offset)
        flow.click(gate, 200, 122)
        flow.type_text(email)
        flow.tap(flow.VK_TAB)
        flow.type_text("Review")
        flow.tap(flow.VK_TAB)
        flow.type_text("hunter22")
        flow.tap(0x0D)                       # Enter submits
        time.sleep(2.5)

    main_win = flow.wait_for(proc.pid, "Joe Browser", 25, exclude=("sign in", "create account"))
    if not main_win:
        print("FAIL no main window")
        proc.terminate()
        return 1

    time.sleep(2.0)
    for i, (name, y) in enumerate(NAV):
        flow.click(main_win, 110, y)   # sidebar item
        time.sleep(1.2)
        flow.shot(main_win, "page-%d-%s.png" % (i, name))

    proc.terminate()
    try:
        os.remove(os.path.join(os.environ["APPDATA"], "JoeBrowser", "session.bin"))
    except OSError:
        pass
    return 0


if __name__ == "__main__":
    sys.exit(main())
