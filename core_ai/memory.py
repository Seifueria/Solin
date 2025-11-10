# core_ai/memory.py
import json
import os
from datetime import datetime

BASE_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
DATA_DIR = os.path.join(BASE_DIR, "data")
STATE_DIR = os.path.join(BASE_DIR, "core_ai/state")
MEMORY_PATH = os.path.join(DATA_DIR, "solin_memory.json")
STATE_PATH = os.path.join(STATE_DIR, "solin_state.json")
MAX_MEMORY = 50000

# -----------------------
# Memory handling
# -----------------------
def load_memory():
    """Load memory from file. Initialize topic structure if needed."""
    if os.path.exists(MEMORY_PATH):
        with open(MEMORY_PATH, "r", encoding="utf-8") as f:
            memory = json.load(f)
    else:
        memory = []

    # Normalize keys
    updated = False
    for m in memory:
        if "asuka" in m:
            m["solin_visible"] = m.pop("asuka")
            updated = True
        if "solin_visible" not in m:
            m["solin_visible"] = ""
            updated = True
        if "topic" not in m:
            m["topic"] = "general"
            updated = True
        if "timestamp" not in m:
            m["timestamp"] = datetime.now().isoformat()
            updated = True

    if updated:
        save_memory(memory)
    return memory

def save_memory(memory):
    os.makedirs(DATA_DIR, exist_ok=True)
    trimmed = memory[-MAX_MEMORY:] if len(memory) > MAX_MEMORY else memory
    with open(MEMORY_PATH, "w", encoding="utf-8") as f:
        json.dump(trimmed, f, ensure_ascii=False, indent=2)

# -----------------------
# Mood / Trust State
# -----------------------
def load_state():
    """Load AI state: mood, trust, last interaction timestamp"""
    os.makedirs(STATE_DIR, exist_ok=True)
    if os.path.exists(STATE_PATH):
        with open(STATE_PATH, "r", encoding="utf-8") as f:
            state = json.load(f)
    else:
        state = {"mood": "neutral", "trust": 0.5, "last_interaction": None}
        save_state(state)
    return state

def save_state(state):
    os.makedirs(STATE_DIR, exist_ok=True)
    with open(STATE_PATH, "w", encoding="utf-8") as f:
        json.dump(state, f, ensure_ascii=False, indent=2)









#{
#  "user": user_input,
#  "solin_visible": visible_response,
#  "solin_internal": internal_response,
#  "context_topic": None,              # topic or subject of the exchange
#  "unfinished": False,                # if Solin was interrupted mid-thought
#  "interaction_type": "normal"        # could be "interrupted", "emotional", etc.
#}

# Ignore this stuff