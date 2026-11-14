"""Reminders she keeps without the model: "remind me in 20 minutes to stretch", "remind me at 5pm to call mum".

Parsed from what you type, stored in data/solin_reminders.json, fired by the companion API's ticker.
(Idea described from the base's and the Mochii build's reminders; no code taken.)
"""
import json
import os
import re
import threading
import time
from datetime import datetime, timedelta

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
PATH = os.path.join(ROOT, "data", "solin_reminders.json")
_lock = threading.Lock()

UNITS = {"s": 1, "sec": 1, "secs": 1, "second": 1, "seconds": 1, "m": 60, "min": 60, "mins": 60, "minute": 60, "minutes": 60,
         "h": 3600, "hr": 3600, "hrs": 3600, "hour": 3600, "hours": 3600}

IN = re.compile(r"\bremind me\b(?: to (?P<what1>.+?))? in (?P<n>\d+(?:\.\d+)?)\s*(?P<unit>s|secs?|seconds?|m|mins?|minutes?|h|hrs?|hours?)\b(?: to (?P<what2>.+))?", re.I)
AT = re.compile(r"\bremind me\b(?: to (?P<what1>.+?))? at (?P<h>\d{1,2})(?::(?P<mi>\d{2}))?\s*(?P<ap>am|pm)?\b(?: to (?P<what2>.+))?", re.I)
LEAD = re.compile(r"^in (?P<n>\d+(?:\.\d+)?)\s*(?P<unit>s|secs?|seconds?|m|mins?|minutes?|h|hrs?|hours?)\b,?\s*remind me(?: to)? (?P<what>.+)", re.I)


def _load():
    try:
        with open(PATH, "r", encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError):
        return []


def _save(items):
    os.makedirs(os.path.dirname(PATH), exist_ok=True)
    with open(PATH, "w", encoding="utf-8") as f:
        json.dump(items, f, indent=2)


def parse(text):
    """-> (due_epoch, what) or None."""
    m = LEAD.search(text) or IN.search(text)
    if m and m.groupdict().get("n"):
        seconds = float(m.group("n")) * UNITS[m.group("unit").lower()]
        what = (m.groupdict().get("what") or m.groupdict().get("what2") or m.groupdict().get("what1") or "").strip(" .!")
        return time.time() + seconds, what or "that thing"
    m = AT.search(text)
    if m:
        hour, minute, ap = int(m.group("h")), int(m.group("mi") or 0), (m.group("ap") or "").lower()
        if ap == "pm" and hour < 12:
            hour += 12
        if ap == "am" and hour == 12:
            hour = 0
        now = datetime.now()
        due = now.replace(hour=hour % 24, minute=minute, second=0, microsecond=0)
        if due <= now:
            due += timedelta(days=1)
        what = (m.group("what2") or m.group("what1") or "").strip(" .!")
        return due.timestamp(), what or "that thing"
    return None


def add(due, what):
    with _lock:
        items = _load()
        items.append({"due": due, "what": what, "set": time.time()})
        _save(items)
    return {"due": due, "what": what}


def due_now():
    """Pops and returns every reminder whose time has come."""
    with _lock:
        items = _load()
        now = time.time()
        ready = [r for r in items if r["due"] <= now]
        if ready:
            _save([r for r in items if r["due"] > now])
        return ready


def pending():
    with _lock:
        return sorted(_load(), key=lambda r: r["due"])


def when(due):
    """A short human phrase for the due time."""
    delta = due - time.time()
    if delta < 90:
        return f"in {int(delta)} seconds"
    if delta < 3600:
        return f"in {int(round(delta / 60))} minutes"
    d = datetime.fromtimestamp(due)
    return "at " + d.strftime("%-I:%M %p").lower()
