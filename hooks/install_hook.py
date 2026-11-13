#!/usr/bin/env python3
"""Adds (or removes, with --remove) Solin's hook to ~/.claude/settings.json without touching anything else in it."""
import json
import os
import sys

EVENTS = ["UserPromptSubmit", "PreToolUse", "PostToolUse", "Notification", "Stop", "SessionStart", "SessionEnd"]
HOOK = os.path.abspath(os.path.join(os.path.dirname(__file__), "solin_hook.py"))
COMMAND = f"python3 {HOOK}"
SETTINGS = os.path.expanduser("~/.claude/settings.json")


def main():
    remove = "--remove" in sys.argv
    settings = {}
    if os.path.exists(SETTINGS):
        with open(SETTINGS, "r", encoding="utf-8") as f:
            text = f.read().strip()
            settings = json.loads(text) if text else {}
    hooks = settings.setdefault("hooks", {})
    for event in EVENTS:
        groups = hooks.setdefault(event, [])
        groups[:] = [g for g in groups if not any(h.get("command") == COMMAND for h in g.get("hooks", []))]
        if not remove:
            groups.append({"hooks": [{"type": "command", "command": COMMAND, "timeout": 2}]})
        if not groups:
            del hooks[event]
    if not hooks:
        del settings["hooks"]
    os.makedirs(os.path.dirname(SETTINGS), exist_ok=True)
    with open(SETTINGS, "w", encoding="utf-8") as f:
        json.dump(settings, f, indent=2)
        f.write("\n")
    print(("removed from " if remove else "installed in ") + SETTINGS)


if __name__ == "__main__":
    main()
