"""Loopback API between Solin's Python backend and the desktop companion.

Solin-owned. Wraps the existing core_ai modules without changing them.

    GET  /health          -> {"ok": true, "brain": "...", "version": "..."}
    GET  /state           -> {"session_id", "mood", "trust", "brain", "speaking"}
    POST /chat            {"session_id", "text", "speak": true, "image": path}   speak defaults to config voice.speak;
                          image is a screenshot the companion just took, for "look at my screen"
                          -> {"visible", "internal", "mood", "trust", "audio", "error"}
                             audio is an MP3 path on this machine for the companion to play, or null
    POST /event           {"session_id", "kind"}   companion-side happenings, e.g. "poked"
                          -> {"mood", "trust"}
    GET  /events          server-sent events: mood, reply, speaking, brain
    GET  /brains          every configured brain, whether it can run, and the current one
    POST /hook            a trimmed Claude Code hook event from hooks/solin_hook.py
    POST /say             {"text", "mood", "speak"}  a line of hers without the brain: shown, mood set, spoken if asked
    GET  /history?limit=N the last N exchanges from her memory: [{"user", "visible", "internal"}]
    POST /greet           a one-line hello for the time of day, as a reply event (kind "greeting")
    GET  /reminders       pending reminders; "remind me in 20 min to ..." in /chat sets one, fired as a reply (kind "reminder")
    GET  /settings        {"brain", "brains", "voice": {...}, "companion": {...}} from data/config.json
    POST /settings        {"voice": {...}, "companion": {...}}  merged into data/config.json, returns the result
    POST /brain           {"name": "ollama"}  switch the live brain

Run from the repo root:  python3 -m server.companion_api
Port: SOLIN_COMPANION_PORT, default 47831. Binds 127.0.0.1 only and rejects
requests that carry an Origin header, so a web page cannot drive her.
"""
import json
import os
import queue
import sys
import threading
import time
from datetime import datetime

from flask import Flask, Response, jsonify, request

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
if ROOT not in sys.path:
    sys.path.insert(0, ROOT)

from dotenv import load_dotenv  # noqa: E402

load_dotenv(os.path.join(ROOT, "core_ai", ".env"))

from core_ai import memory  # noqa: E402

VERSION = "0.1.0"
PORT = int(os.environ.get("SOLIN_COMPANION_PORT", "47831"))


# What a companion-side happening does to her mood when no brain is involved.
EVENT_MOODS = {
    "poked": "curious",
    "petted": "warm",
    "dragged": "annoyed",
    "ignored": "tired",
    "greeted": "warm",
    "thrown": "annoyed",
    "pestered": "annoyed",
    # The Mac's own state, from the companion.
    "unplugged": "curious",
    "plugged": "calm",
    "battery_low": "tired",
    "battery_critical": "annoyed",
    "offline": "annoyed",
    "online": "warm",
    "hot": "annoyed",
    "cooled": "calm",
}

app = Flask(__name__)
lock = threading.Lock()
subscribers = []  # list[queue.Queue]
speaking = False
# What the coding agent is up to, from Claude Code hooks: idle, working, needs_you, done, error.
agent = {"state": "idle", "detail": "", "session": None, "report": None, "project": None}
# The task in progress: when it began, how many tool calls, which files were touched, for the report when it ends.
task = {"started": None, "actions": 0, "files": set()}
# The last hook events as received, for working out what Claude Code actually sends.
recent_hooks = []

# One persistent session, "solin", backed by core_ai/state/solin_state.json; others live only while the server runs.
DEFAULT_SESSION = "solin"
sessions = {}


def session(session_id):
    session_id = session_id or DEFAULT_SESSION
    with lock:
        if session_id not in sessions:
            if session_id == DEFAULT_SESSION:
                saved = memory.load_state()
                sessions[session_id] = {"mood": saved.get("mood", "neutral"), "trust": float(saved.get("trust", 0.5))}
            else:
                sessions[session_id] = {"mood": "neutral", "trust": 0.5}
        return session_id, sessions[session_id]


def set_mood(session_id, mood, trust=None):
    sid, s = session(session_id)
    with lock:
        s["mood"] = mood
        if trust is not None:
            s["trust"] = max(0.0, min(1.0, trust))
        if sid == DEFAULT_SESSION:
            memory.save_state({"mood": s["mood"], "trust": s["trust"], "last_interaction": datetime.now().isoformat()})
    broadcast("mood", {"session_id": sid, "mood": s["mood"], "trust": s["trust"]})
    return s


def broadcast(kind, data):
    message = f"event: {kind}\ndata: {json.dumps(data)}\n\n"
    with lock:
        dead = []
        for q in subscribers:
            try:
                q.put_nowait(message)
            except queue.Full:
                dead.append(q)
        for q in dead:
            subscribers.remove(q)


# --- brains -------------------------------------------------------------------

from core_ai import brains  # noqa: E402
from core_ai.brains import bond, reminders  # noqa: E402

brain = brains.make_brain()


def switch_brain(name):
    """Swap the live brain. Returns the new brain or raises ValueError for a name that cannot run."""
    global brain
    config = brains.load_config()
    options = brains.available(config)
    if name not in options:
        raise ValueError("unknown brain " + name)
    if not options[name]["ok"]:
        raise ValueError(f"{name} cannot run: {options[name]['why']}")
    brain = brains.make_brain(name, config)
    broadcast("brain", {"brain": brain.name})
    return brain


def voice_default():
    """Whether replies are spoken when the caller does not say: data/config.json {"voice": {"speak": true}}, else off."""
    from core_ai import speech
    return bool(speech.settings().get("speak", False))


# --- guards -------------------------------------------------------------------

@app.before_request
def refuse_browsers():
    if request.headers.get("Origin"):
        return jsonify({"error": "origin not allowed"}), 403
    if request.content_length and request.content_length > 64 * 1024:
        return jsonify({"error": "body too large"}), 413
    return None


# --- routes -------------------------------------------------------------------

@app.get("/health")
def health():
    return jsonify({"ok": True, "brain": brain.name, "version": VERSION})


@app.get("/brains")
def list_brains():
    return jsonify({"current": brain.name, "brains": brains.available()})


@app.post("/brain")
def set_brain():
    data = request.get_json(silent=True) or {}
    try:
        chosen = switch_brain((data.get("name") or "").strip())
    except ValueError as error:
        return jsonify({"error": str(error)}), 400
    return jsonify({"brain": chosen.name})


@app.get("/state")
def state():
    sid, s = session(request.args.get("session_id"))
    return jsonify({"session_id": sid, "mood": s["mood"], "trust": s["trust"], "brain": brain.name, "speaking": speaking, "agent": agent, "bond": bond.snapshot()})


@app.post("/chat")
def chat():
    global speaking
    data = request.get_json(silent=True) or {}
    text = (data.get("text") or "").strip()
    if not text:
        return jsonify({"error": "text required"}), 400
    sid, current = session(data.get("session_id"))
    error = None
    context = bond.prompt_lines()
    fresh = [line for at, line in recent_remarks if time.time() - at < 900]
    if fresh:
        context += "\nThings you said lately, glancing at their screen: " + " / ".join(fresh)
    image = data.get("image")
    if image and not (isinstance(image, str) and os.path.isfile(image) and image.lower().endswith((".jpg", ".jpeg", ".png"))):
        image = None
    if image:
        context += screens_line(data)
    reminder = reminders.parse(text)
    if reminder:
        due, what = reminder
        reminders.add(due, what)
        context += f"\nThey just asked you to remind them to \"{what}\" {reminders.when(due)}. You've set it. Confirm in one short line, in your voice."
    try:
        visible, internal, mood = brain.reply(text, context, image=image)
        bond.note("chat")
    except Exception as failure:  # noqa: BLE001  a dead key or an empty account must not take her down
        error = f"{type(failure).__name__}: {failure}"
        print(f"[companion_api] brain failed: {error}", file=sys.stderr)
        visible, internal, mood = "[offline] My brain just dropped the call. Check the key and the account.", "", current["mood"]
    s = set_mood(sid, mood, trust=current["trust"] + (0.0 if error else 0.01))
    audio = None
    speak = data.get("speak")
    if speak is None:
        speak = voice_default()
    if speak and not error:
        try:
            from core_ai import speech
            speaking = True
            broadcast("speaking", {"on": True})
            audio, _fresh = speech.speak(visible)
        except Exception as failure:  # noqa: BLE001
            error = f"voice: {failure}"
            print(f"[companion_api] {error}", file=sys.stderr)
        finally:
            speaking = False
            broadcast("speaking", {"on": False})
    broadcast("reply", {"session_id": sid, "visible": visible, "mood": s["mood"]})
    return jsonify({"visible": visible, "internal": internal, "mood": s["mood"], "trust": s["trust"], "audio": audio, "error": error})


CONFIG_PATH = os.path.join(ROOT, "data", "config.json")
COMPANION_DEFAULTS = {"size": "full", "sounds": 0.5, "wander": 0.35, "sleep_after_min": 8, "break_after_min": 120}


def read_config():
    try:
        with open(CONFIG_PATH, "r", encoding="utf-8") as f:
            text = f.read().strip()
        return json.loads(text) if text else {}
    except (OSError, ValueError):
        return {}


def write_config(config):
    os.makedirs(os.path.dirname(CONFIG_PATH), exist_ok=True)
    with open(CONFIG_PATH, "w", encoding="utf-8") as f:
        json.dump(config, f, indent=2)
        f.write("\n")


def settings_view():
    from core_ai import speech
    config = read_config()
    companion = dict(COMPANION_DEFAULTS)
    companion.update(config.get("companion") or {})
    return {"brain": brain.name, "brain_setting": config.get("brain", "auto"), "brains": brains.available(), "voice": speech.settings(), "companion": companion}


@app.get("/settings")
def get_settings():
    return jsonify(settings_view())


@app.post("/settings")
def post_settings():
    """Merges voice and companion settings, and a brain choice, into data/config.json. Other keys stay as they were."""
    data = request.get_json(silent=True) or {}
    config = read_config()
    for section in ("voice", "companion"):
        if isinstance(data.get(section), dict):
            config.setdefault(section, {}).update(data[section])
    if "brain" in data:
        config["brain"] = data["brain"]
    write_config(config)
    if "brain" in data and data["brain"] not in ("auto", brain.name.split(":")[0]):
        try:
            switch_brain(data["brain"])
        except ValueError as error:
            return jsonify({"error": str(error), "settings": settings_view()}), 400
    broadcast("settings", {"companion": settings_view()["companion"]})
    return jsonify(settings_view())


@app.post("/greet")
def greet():
    """A one-line hello for the time of day, in her voice, shown and spoken like a reply."""
    data = request.get_json(silent=True) or {}
    sid, current = session(data.get("session_id"))
    now = datetime.now()
    part = "morning" if now.hour < 12 else "afternoon" if now.hour < 18 else "evening" if now.hour < 23 else "late night"
    context = bond.prompt_lines() + f"\nIt's {now.strftime('%A')} {part}, {now.strftime('%-I:%M %p').lower()}. They just started you up. Greet them in one short line of under twelve words, in your voice; no questions about tasks, no emoji."
    try:
        visible, internal, mood = brain.reply("(you just came online)", context, sentences=1)
    except Exception as failure:  # noqa: BLE001
        print(f"[companion_api] greet failed: {failure}", file=sys.stderr)
        return jsonify({"visible": None})
    s = set_mood(sid, mood)
    audio = None
    if voice_default():
        try:
            from core_ai import speech
            audio, _fresh = speech.speak(visible)
        except Exception as failure:  # noqa: BLE001
            print(f"[companion_api] voice: {failure}", file=sys.stderr)
    broadcast("reply", {"session_id": sid, "visible": visible, "mood": s["mood"], "audio": audio, "kind": "greeting"})
    return jsonify({"visible": visible, "mood": s["mood"], "audio": audio})


@app.get("/reminders")
def list_reminders():
    return jsonify({"reminders": [{"due": r["due"], "when": reminders.when(r["due"]), "what": r["what"]} for r in reminders.pending()]})


def reminder_ticker():
    """Every ten seconds, fires reminders that have come due as reminder replies."""
    while True:
        time.sleep(10)
        try:
            for r in reminders.due_now():
                line = r["what"]
                audio = None
                if voice_default():
                    try:
                        from core_ai import speech
                        audio, _fresh = speech.speak("reminder: " + line)
                    except Exception as failure:  # noqa: BLE001
                        print(f"[companion_api] voice: {failure}", file=sys.stderr)
                broadcast("reply", {"session_id": DEFAULT_SESSION, "visible": line, "mood": "curious", "audio": audio, "kind": "reminder"})
        except Exception as failure:  # noqa: BLE001
            print(f"[companion_api] reminder ticker: {failure}", file=sys.stderr)


threading.Thread(target=reminder_ticker, daemon=True).start()


@app.get("/history")
def history():
    try:
        limit = max(1, min(200, int(request.args.get("limit", "30"))))
    except ValueError:
        limit = 30
    out = []
    for entry in memory.load_memory()[-limit:]:
        out.append({"user": entry.get("user", ""), "visible": entry.get("solin_visible", ""), "internal": entry.get("solin_internal", "")})
    return jsonify({"history": out})


@app.post("/say")
def say():
    """A scripted line: the companion shows it exactly as if she had thought of it. Demos, tests and other tools use this."""
    global speaking
    data = request.get_json(silent=True) or {}
    text = (data.get("text") or "").strip()
    if not text:
        return jsonify({"error": "text required"}), 400
    sid, current = session(data.get("session_id"))
    mood = data.get("mood") or current["mood"]
    s = set_mood(sid, mood)
    audio = None
    speak = data.get("speak")
    if speak is None:
        speak = voice_default()
    if speak:
        try:
            from core_ai import speech
            speaking = True
            broadcast("speaking", {"on": True})
            audio, _fresh = speech.speak(text)
        except Exception as failure:  # noqa: BLE001
            print(f"[companion_api] voice: {failure}", file=sys.stderr)
        finally:
            speaking = False
            broadcast("speaking", {"on": False})
    broadcast("reply", {"session_id": sid, "visible": text, "mood": s["mood"], "audio": audio})
    return jsonify({"visible": text, "mood": s["mood"], "audio": audio})


def screens_line(data):
    """A line for her brain when the screenshot holds several screens side by side."""
    try:
        screens = int(data.get("screens") or 1)
    except (TypeError, ValueError):
        screens = 1
    return f"\nThe screenshot shows all {screens} of their screens side by side, left to right." if screens > 1 else ""


# What she said glancing at the screen lately, so chat can follow on from it for a while. Not kept in her memory.
recent_remarks = []


@app.post("/remark")
def remark():
    """A glance at their screen: one short line about what they're doing. The screenshot path comes from the companion,
    which deletes it after; nothing is stored here."""
    data = request.get_json(silent=True) or {}
    image = data.get("image")
    if not (isinstance(image, str) and os.path.isfile(image) and image.lower().endswith((".jpg", ".jpeg", ".png"))):
        return jsonify({"error": "image required"}), 400
    sid, current = session(data.get("session_id"))
    context = bond.prompt_lines() + screens_line(data)
    try:
        visible, mood = brain.remark(image, context)
    except Exception as failure:  # noqa: BLE001
        print(f"[companion_api] remark failed: {failure}", file=sys.stderr)
        return jsonify({"visible": None})
    if not visible:
        return jsonify({"visible": None})
    recent_remarks.append((time.time(), visible))
    del recent_remarks[:-5]
    s = set_mood(sid, mood if mood != "neutral" else current["mood"])
    audio = None
    if voice_default():
        try:
            from core_ai import speech
            audio, _fresh = speech.speak(visible)
        except Exception as failure:  # noqa: BLE001
            print(f"[companion_api] voice: {failure}", file=sys.stderr)
    return jsonify({"visible": visible, "mood": s["mood"], "audio": audio})


@app.post("/event")
def event():
    data = request.get_json(silent=True) or {}
    kind = (data.get("kind") or "").strip()
    if kind not in EVENT_MOODS:
        return jsonify({"error": "unknown kind", "known": sorted(EVENT_MOODS)}), 400
    sid, s = session(data.get("session_id"))
    nudge = -0.02 if kind in ("dragged", "thrown", "pestered") else 0.005
    s = set_mood(sid, EVENT_MOODS[kind], trust=s["trust"] + nudge)
    snap = bond.note(kind)
    return jsonify({"mood": s["mood"], "trust": s["trust"], "bond": snap})


def first_line(text, limit=140):
    """The first sentence or line of a reply, markdown marks stripped, short enough for a bubble."""
    import re
    text = re.sub(r"[*_`#>]+", "", text or "").strip()
    text = re.split(r"(?<=[.!?])\s+|\n+", text, maxsplit=1)[0].strip()
    return text if len(text) <= limit else text[:limit].rstrip() + "…"


# Tools that mean Claude has stopped to wait for an answer from you.
ASKING_TOOLS = ("AskUserQuestion",)


def agent_state_of(event):
    """Map a Claude Code hook event to one of Solin's agent states, with a short line for her ticker."""
    name = event.get("hook_event_name", "")
    if name == "SessionStart":
        return "working", "claude is here"
    if name == "SessionEnd":
        return "idle", ""
    if name == "UserPromptSubmit":
        return "working", "reading what you asked"
    if name == "PreToolUse":
        tool = event.get("tool_name") or "a tool"
        if tool in ASKING_TOOLS:
            return "needs_you", "claude has a question for you"
        gist = (event.get("gist") or "").strip()
        return "working", (tool + (": " + gist if gist else "")).lower()[:80]
    if name == "PostToolUse":
        if event.get("error"):
            return "error", str(event.get("error"))[:80]
        return "working", "thinking"
    if name == "PermissionRequest":
        # The permission dialog itself; the desktop app shows these without a Notification event.
        tool = event.get("tool_name") or "a tool"
        gist = (event.get("gist") or "").strip()
        return "needs_you", ("claude wants to use " + tool + (": " + gist if gist else "")).lower()[:80]
    if name == "PermissionDenied":
        # Answered no: off the waiting card straight away, while Claude writes its reply.
        return "working", "declined, claude's rethinking"
    if name == "PostToolUseFailure":
        if event.get("is_interrupt"):
            return "idle", "stopped"
        return "working", ((event.get("tool_name") or "a tool") + " didn't go through").lower()[:80]
    if name == "Notification":
        kind = event.get("notification_type") or ""
        text = (event.get("message") or "").strip()[:80]
        if kind in ("permission_prompt", "elicitation_dialog", "agent_needs_input", "idle_prompt") or "permission" in text.lower() or "waiting" in text.lower():
            return "needs_you", text or "claude needs you"
        return "working", text or "thinking"
    if name == "Stop":
        if event.get("is_interrupt"):
            return "idle", "stopped"
        return "done", first_line(event.get("last_assistant_message")) or "done"
    return None, None


@app.post("/hook")
def hook():
    event = request.get_json(silent=True) or {}
    with lock:
        recent_hooks.append({"at": datetime.now().isoformat(timespec="seconds"), "event": event})
        del recent_hooks[:-30]
    state, detail = agent_state_of(event)
    if state is None:
        return jsonify({"ignored": event.get("hook_event_name")})
    name = event.get("hook_event_name", "")
    with lock:
        if name == "UserPromptSubmit":
            task.update({"started": time.time(), "actions": 0, "files": set()})
        if name == "PreToolUse":
            task["actions"] += 1
            if event.get("tool_name") in ("Edit", "Write", "MultiEdit", "NotebookEdit") and event.get("gist"):
                task["files"].add(os.path.basename(str(event["gist"])))
        report = None
        if state == "done":
            seconds = int(time.time() - task["started"]) if task["started"] else 0
            report = {"actions": task["actions"], "files": sorted(task["files"]), "seconds": seconds}
        project = os.path.basename(str(event.get("cwd") or "").rstrip("/")) or None
        changed = state != agent["state"] or detail != agent["detail"]
        agent.update({"state": state, "detail": detail, "session": event.get("session_id"), "report": report, "project": project or agent.get("project")})
    if changed:
        broadcast("agent", dict(agent))
        if state in ("working", "needs_you", "done", "error"):
            bond.note("agent_" + state)
    return jsonify(agent)


@app.get("/hook/recent")
def hook_recent():
    with lock:
        return jsonify({"agent": agent, "recent": list(recent_hooks)})


@app.get("/events")
def events():
    q = queue.Queue(maxsize=256)
    with lock:
        subscribers.append(q)
    sid, s = session(request.args.get("session_id"))
    first = f"event: mood\ndata: {json.dumps({'session_id': sid, 'mood': s['mood'], 'trust': s['trust']})}\n\n"

    def stream():
        yield first
        try:
            while True:
                try:
                    yield q.get(timeout=15)
                except queue.Empty:
                    yield ": keepalive\n\n"
        finally:
            with lock:
                if q in subscribers:
                    subscribers.remove(q)

    return Response(stream(), mimetype="text/event-stream", headers={"Cache-Control": "no-cache", "X-Accel-Buffering": "no"})


if __name__ == "__main__":
    print(f"[companion_api] brain={brain.name} on http://127.0.0.1:{PORT}")
    app.run(host="127.0.0.1", port=PORT, threaded=True, debug=False)
