"""Loopback API between Solin's Python backend and the desktop companion.

Solin-owned. Wraps the existing core_ai modules without changing them.

    GET  /health          -> {"ok": true, "brain": "...", "version": "..."}
    GET  /state           -> {"session_id", "mood", "trust", "brain", "speaking"}
    POST /chat            {"session_id", "text", "speak": false}
                          -> {"visible", "internal", "mood", "trust", "audio"}
    POST /event           {"session_id", "kind"}   companion-side happenings, e.g. "poked"
                          -> {"mood", "trust"}
    GET  /events          server-sent events: mood, reply, speaking

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

from core_ai import load_prompt, memory  # noqa: E402

VERSION = "0.1.0"
PORT = int(os.environ.get("SOLIN_COMPANION_PORT", "47831"))

# Same vocabulary generate_response() scans for; it keeps the result private, so the API re-derives it here.
MOODS = ["warm", "curious", "annoyed", "calm", "tired", "teasing"]

# What a companion-side happening does to her mood when no brain is involved.
EVENT_MOODS = {
    "poked": "curious",
    "petted": "warm",
    "dragged": "annoyed",
    "ignored": "tired",
    "greeted": "warm",
}

app = Flask(__name__)
lock = threading.Lock()
subscribers = []  # list[queue.Queue]
speaking = False

# One persistent session, "solin", backed by core_ai/state/solin_state.json; others live only while the server runs.
DEFAULT_SESSION = "solin"
sessions = {}


def mood_of(text):
    low = (text or "").lower()
    for word in MOODS:
        if word in low:
            return word
    return "neutral"


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

class OpenAIBrain:
    name = "openai:gpt-4o-mini"

    def __init__(self):
        import openai
        from core_ai import ai_logic
        self.ai_logic = ai_logic
        self.client = openai.OpenAI()
        self.prompt = load_prompt()
        self.mem = memory.load_memory()

    def reply(self, text):
        out = self.ai_logic.generate_response(text, self.mem, self.client, self.prompt)
        self.mem.append({"user": text, "solin_visible": out["visible"], "solin_internal": out["internal"]})
        memory.save_memory(self.mem)
        return out["visible"], out["internal"], mood_of(out["internal"])


class OfflineBrain:
    """No key, no model: enough to exercise the companion end to end."""
    name = "offline"

    def reply(self, text):
        mood = mood_of(text)
        visible = "[offline] My brain's not plugged in yet. Set a key in core_ai/.env and start me again."
        return visible, "", mood


def make_brain():
    if os.environ.get("OPENAI_API_KEY"):
        try:
            return OpenAIBrain()
        except Exception as error:  # noqa: BLE001
            print(f"[companion_api] OpenAI brain unavailable: {error}", file=sys.stderr)
    return OfflineBrain()


brain = make_brain()


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


@app.get("/state")
def state():
    sid, s = session(request.args.get("session_id"))
    return jsonify({"session_id": sid, "mood": s["mood"], "trust": s["trust"], "brain": brain.name, "speaking": speaking})


@app.post("/chat")
def chat():
    global speaking
    data = request.get_json(silent=True) or {}
    text = (data.get("text") or "").strip()
    if not text:
        return jsonify({"error": "text required"}), 400
    sid, current = session(data.get("session_id"))
    error = None
    try:
        visible, internal, mood = brain.reply(text)
    except Exception as failure:  # noqa: BLE001  a dead key or an empty account must not take her down
        error = f"{type(failure).__name__}: {failure}"
        print(f"[companion_api] brain failed: {error}", file=sys.stderr)
        visible, internal, mood = "[offline] My brain just dropped the call. Check the key and the account.", "", current["mood"]
    s = set_mood(sid, mood, trust=current["trust"] + (0.0 if error else 0.01))
    audio = None
    if data.get("speak") and os.environ.get("ELEVENLABS_API_KEY"):
        try:
            from core_ai import voice
            speaking = True
            broadcast("speaking", {"on": True})
            audio = voice.generate_audio(visible)
        except Exception as error:  # noqa: BLE001
            print(f"[companion_api] voice failed: {error}", file=sys.stderr)
        finally:
            speaking = False
            broadcast("speaking", {"on": False})
    broadcast("reply", {"session_id": sid, "visible": visible, "mood": s["mood"]})
    return jsonify({"visible": visible, "internal": internal, "mood": s["mood"], "trust": s["trust"], "audio": audio, "error": error})


@app.post("/event")
def event():
    data = request.get_json(silent=True) or {}
    kind = (data.get("kind") or "").strip()
    if kind not in EVENT_MOODS:
        return jsonify({"error": "unknown kind", "known": sorted(EVENT_MOODS)}), 400
    sid, s = session(data.get("session_id"))
    nudge = -0.02 if kind == "dragged" else 0.005
    s = set_mood(sid, EVENT_MOODS[kind], trust=s["trust"] + nudge)
    return jsonify({"mood": s["mood"], "trust": s["trust"]})


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
