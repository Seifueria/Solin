#!/usr/bin/env python3
"""Replays a scripted day with Solin through the companion API, so every reaction can be watched without a brain.

    python3 hooks/demo.py            # the full sequence, about a minute
    python3 hooks/demo.py --fast     # same beats, short gaps
    python3 hooks/demo.py --speak    # also speak her lines (spends ElevenLabs characters)

Run the companion first; the API comes up with her.
"""
import json
import os
import sys
import time
import urllib.request

PORT = int(os.environ.get("SOLIN_COMPANION_PORT", "47831"))
FAST = "--fast" in sys.argv
SPEAK = "--speak" in sys.argv


def post(path, body):
    req = urllib.request.Request(f"http://127.0.0.1:{PORT}{path}", data=json.dumps(body).encode("utf-8"),
                                 headers={"Content-Type": "application/json"}, method="POST")
    with urllib.request.urlopen(req, timeout=120) as r:
        return json.loads(r.read().decode("utf-8"))


def wait(seconds):
    time.sleep(0.6 if FAST else seconds)


def event(kind):
    print(f"  event  {kind}")
    post("/event", {"kind": kind})


def hook(name, **fields):
    print(f"  hook   {name} {fields if fields else ''}")
    post("/hook", {"hook_event_name": name, **fields})


def say(text, mood="neutral"):
    print(f"  say    [{mood}] {text}")
    post("/say", {"text": text, "mood": mood, "speak": SPEAK})


BEATS = [
    ("she notices you",            lambda: (event("greeted"), say("[soft smile] oh, you're back. [calm] i was starting to think you'd forgotten me.", "warm")), 7),
    ("a couple of pokes",          lambda: (event("poked"), wait(1.2), event("poked")), 4),
    ("a pat",                      lambda: (event("petted"), say("[quiet laugh] okay, that's nice. don't tell anyone.", "warm")), 7),
    ("claude starts working",      lambda: (hook("UserPromptSubmit"), wait(1.5), hook("PreToolUse", tool_name="Bash", gist="run the tests")), 5),
    ("claude needs you",           lambda: hook("Notification", notification_type="permission_prompt", message="Claude needs your permission to edit main.py"), 7),
    ("claude finishes",            lambda: hook("Stop", last_assistant_message="Fixed the bug and the tests pass."), 8),
    ("dragged around",             lambda: (event("dragged"), say("[flat] you could just ask me to move, you know.", "annoyed")), 7),
    ("thrown",                     lambda: (event("thrown"), say("[sharp exhale] okay. that one i'm not letting go.", "annoyed")), 7),
    ("claude hits an error",       lambda: hook("PostToolUse", tool_name="Bash", error="exit code 1"), 5),
    ("winding down",               lambda: say("[yawn] i think that's enough excitement for one demo. [soft sigh] night.", "tired"), 8),
    ("quiet again",                lambda: hook("SessionEnd"), 1),
]


def main():
    try:
        urllib.request.urlopen(f"http://127.0.0.1:{PORT}/health", timeout=2).read()
    except Exception as error:  # noqa: BLE001
        print(f"companion API not reachable on port {PORT}: {error}")
        sys.exit(1)
    for title, beat, gap in BEATS:
        print(title)
        beat()
        wait(gap)
    print("done")


if __name__ == "__main__":
    main()
