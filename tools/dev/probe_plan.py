"""Sign in (or register) and capture the Plan page, with diagnostics at every step.

pages.py is the general capture tool; this one exists because the gate is the flaky part and
a silent failure there is hard to tell apart from a crash.
"""
import os
import subprocess
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import flow


def main():
    proc = subprocess.Popen([flow.EXE])
    gate = flow.wait_for(proc.pid, "Sign in", 30)
    print("gate:", gate, flow.window_title(gate) if gate else None)
    if gate:
        flow.shot(gate, "gate-before.png")
        email = "plan" + str(int(time.time()))[-6:] + "@joebrowser.app"

        # The gate swaps between sign-in and create-account with a link near the bottom.
        for y in (333, 325, 341, 317, 349):
            flow.click(gate, 150, y)
            time.sleep(0.6)
            t = flow.window_title(gate).lower()
            print("  after click y=%d -> %s" % (y, t))
            if "create" in t:
                break

        flow.shot(gate, "gate-register.png")
        flow.click(gate, 200, 122)
        time.sleep(0.4)
        flow.type_text(email)
        flow.tap(flow.VK_TAB)
        flow.type_text("Plan")
        flow.tap(flow.VK_TAB)
        flow.type_text("hunter22")
        flow.tap(0x0D)
        time.sleep(3.0)

    win = flow.wait_for(proc.pid, "Joe Browser", 30, exclude=("sign in", "create account"))
    print("main:", win, flow.window_title(win) if win else None)
    if not win:
        flow.shot(gate, "gate-stuck.png") if gate else None
        proc.terminate()
        return 1

    time.sleep(2.0)
    flow.click(win, 110, 480)      # Plan in the sidebar
    time.sleep(1.5)
    flow.shot(win, "plan-pro.png")
    print("captured plan-pro.png")
    proc.terminate()
    try:
        os.remove(os.path.join(os.environ["APPDATA"], "JoeBrowser", "session.bin"))
    except OSError:
        pass
    return 0


if __name__ == "__main__":
    sys.exit(main())
