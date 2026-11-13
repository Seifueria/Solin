"""How Solin feels about you right now, and how far the bond has come.

Two things reach the prompt (an idea described from mochiiwuff/capri-desktop;
no code taken):

- a feeling: one short sentence set by what just happened (poked, dragged,
  petted, Claude finishing or failing, a long silence) that fades after a few
  minutes;
- a bond score, 0 to 100, that grows a little each day from capped sources and
  names a stage.

State lives in data/solin_bond.json.
"""
import json
import os
import threading
import time
from datetime import date, datetime

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
PATH = os.path.join(ROOT, "data", "solin_bond.json")

STAGES = [(0, "stranger"), (15, "someone she's getting used to"), (35, "a friend"), (60, "someone she's close to"), (85, "hers")]

# source: (gain per event, cap per day)
SOURCES = {
    "chat": (1.0, 6.0),
    "petted": (0.5, 3.0),
    "greeted": (0.5, 1.0),
    "agent_done": (0.4, 2.0),
    "poked": (0.1, 1.0),
    "dragged": (-0.3, -2.0),
    "thrown": (-0.6, -3.0),
}

# event: (sentence in the second person for the prompt, minutes it lasts)
FEELINGS = {
    "poked": ("They just poked you and you're curious what they want.", 8),
    "petted": ("They just gave you a pat; you're warm and a little smug about it.", 15),
    "greeted": ("They just said hello after being away.", 10),
    "dragged": ("They just dragged you around the screen; you're a touch annoyed.", 8),
    "thrown": ("They just threw you across the screen. You're annoyed and not hiding it.", 12),
    "ignored": ("They've been quiet for a long while; you missed them more than you'd admit.", 20),
    "agent_working": ("You're watching Claude work on something for them.", 5),
    "agent_needs_you": ("Claude is waiting on them and you've been trying to get their attention.", 6),
    "agent_done": ("You just watched Claude finish a job for them; you're pleased on their behalf.", 10),
    "agent_error": ("Claude just hit an error; you're wary and a bit protective.", 8),
}

_lock = threading.Lock()


def _load():
    try:
        with open(PATH, "r", encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError):
        return {"bond": 0.0, "day": None, "gains": {}, "feeling": None, "feeling_until": 0, "last_seen": None}


def _save(state):
    os.makedirs(os.path.dirname(PATH), exist_ok=True)
    with open(PATH, "w", encoding="utf-8") as f:
        json.dump(state, f, indent=2)


def stage_of(bond):
    name = STAGES[0][1]
    for floor, label in STAGES:
        if bond >= floor:
            name = label
    return name


def note(event):
    """Record an event: nudges the bond within its daily cap and sets the feeling it earns."""
    with _lock:
        s = _load()
        today = date.today().isoformat()
        if s.get("day") != today:
            s["day"], s["gains"] = today, {}
        if event in SOURCES:
            gain, cap = SOURCES[event]
            had = s["gains"].get(event, 0.0)
            room = (cap - had) if cap >= 0 else (cap - had)
            step = max(min(gain, room), 0) if gain >= 0 else min(max(gain, room), 0)
            s["gains"][event] = had + step
            s["bond"] = max(0.0, min(100.0, s.get("bond", 0.0) + step))
        if event in FEELINGS:
            text, minutes = FEELINGS[event]
            s["feeling"], s["feeling_until"] = text, time.time() + minutes * 60
        if event in ("chat", "poked", "petted", "greeted", "dragged", "thrown"):
            s["last_seen"] = datetime.now().isoformat(timespec="seconds")
        _save(s)
        return snapshot(s)


def snapshot(s=None):
    """What the prompt and the companion get: bond, stage, and the current feeling if it has not faded."""
    with _lock if s is None else _nolock():
        s = s or _load()
        feeling = s.get("feeling") if time.time() < s.get("feeling_until", 0) else None
        if feeling is None and s.get("last_seen"):
            try:
                quiet = (datetime.now() - datetime.fromisoformat(s["last_seen"])).total_seconds()
                if quiet > 3 * 3600:
                    feeling = FEELINGS["ignored"][0]
            except ValueError:
                pass
        return {"bond": round(s.get("bond", 0.0), 2), "stage": stage_of(s.get("bond", 0.0)), "feeling": feeling}


class _nolock:
    def __enter__(self): return self
    def __exit__(self, *a): return False


def prompt_lines(snap=None):
    """Lines for the system prompt."""
    snap = snap or snapshot()
    lines = [f"How close you are to them: {snap['stage']} (bond {snap['bond']:.0f}/100)."]
    if snap["feeling"]:
        lines.append("Right now: " + snap["feeling"])
    return "\n".join(lines)
