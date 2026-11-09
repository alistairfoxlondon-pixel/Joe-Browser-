"""Type a known string into the first field of the sign-in gate and report what landed."""
import os
import subprocess
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import flow

TEXT = "hunter22"
proc = subprocess.Popen([flow.EXE])
gate = flow.wait_for(proc.pid, "Sign in")
if not gate:
    print("no gate")
    proc.terminate()
    sys.exit(1)
flow.Pump = None  # not used by flow module
time.sleep(1.0)
if "--nofocus" not in sys.argv:
    flow.click(gate, 200, 122)
flow.type_text(TEXT)
time.sleep(0.8)
flow.shot(gate, "typing-check.png")
proc.terminate()
print("typed:", TEXT, "len", len(TEXT))
