#!/usr/bin/env python3
"""Claude Code hook -> Solin.

Reads one hook event as JSON on stdin and forwards a trimmed copy to Solin's
companion API. Rule one: never get in Claude's way. No output, exit 0 always,
give up fast. Install with hooks/install_hook.py.
"""
import json
import os
import sys
import urllib.request

PORT = int(os.environ.get("SOLIN_COMPANION_PORT", "47831"))
# Fields Solin uses. File contents and tool output stay on your machine.
KEEP = ("hook_event_name", "session_id", "cwd", "tool_name", "notification_type", "message", "title",
        "prompt", "error", "is_interrupt", "stop_hook_active", "last_assistant_message")


def trim(value, depth=0):
    if isinstance(value, str):
        return value if len(value) <= 200 else value[:200] + "…"
    if isinstance(value, dict):
        return None if depth > 1 else {k: trim(v, depth + 1) for k, v in list(value.items())[:12]}
    if isinstance(value, list):
        return None if depth > 1 else [trim(v, depth + 1) for v in value[:6]]
    return value


def main():
    try:
        raw = sys.stdin.read()
        event = json.loads(raw) if raw.strip() else {}
        slim = {k: trim(event[k]) for k in KEEP if k in event}
        if "tool_input" in event and isinstance(event["tool_input"], dict):
            # Only the one-line gist of what the tool is doing, never file contents.
            gist = event["tool_input"].get("description") or event["tool_input"].get("command") or event["tool_input"].get("file_path") or ""
            slim["gist"] = trim(str(gist))
        body = json.dumps(slim).encode("utf-8")
        req = urllib.request.Request(f"http://127.0.0.1:{PORT}/hook", data=body, headers={"Content-Type": "application/json"}, method="POST")
        urllib.request.urlopen(req, timeout=0.6).read()
    except Exception:  # noqa: BLE001
        pass
    sys.exit(0)


if __name__ == "__main__":
    main()
