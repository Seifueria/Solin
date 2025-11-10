import bpy
import time
import random
from .utils import audio_manager, is_ai_paused
from .talk_to_ai import talk_to_ai

# --- Config ---
_MIN_IDLE = 300       # Minimum AFK time before AI speaks (5 min)
_MAX_IDLE = 600       # Maximum AFK interval (10 min)
_MAX_IDLE_FIRES = 10  # Max AFK messages per session

# --- Messages ---
_FALLBACK_GREETING = [
    "Welcome back, digital maestro. Ready to conjure something spectacular? [soft chuckle]",
    "Ah, opening Blender again. Let’s see what wonders—or messes—you’ll create today. [soft chuckle]"
]

_FALLBACK_IDLE = [
    "Looks like you’ve wandered off again. The scene will wait… unlike your coffee. [teasing]",
    "Oh, AFK already? I’ll keep an eye on your viewport… silently judging. [soft chuckle]",
    "You’re gone… I can wait. But if you leave me to untangle that mesh, don’t expect help. [quiet laugh]"
]

# --- State ---
_idle_timer_handle = None
_last_activity = time.time()
_greeting_played = False
_idle_counter = 0
_idle_scheduled = False

# --- Helpers ---
def _generate_message(messages, exclude=[]):
    """Randomly pick a message avoiding recent duplicates."""
    choices = [m for m in messages if m not in exclude]
    if not choices:
        choices = messages
    return random.choice(choices)

def _queue_message(message):
    """Queue a message to audio_manager with callback on completion."""
    def on_complete():
        global _idle_scheduled
        _idle_scheduled = False  # Mark queue free so next message can schedule
    audio_manager.add_to_queue(talk_to_ai, message, callback=on_complete)

# --- Greeting ---
def play_greeting():
    global _greeting_played, _last_activity
    if _greeting_played:
        return
    _greeting_played = True
    _last_activity = time.time()
    message = _generate_message(_FALLBACK_GREETING)
    print(f"[Idle Watcher] Greeting:\n{message}")
    _queue_message(message)

# --- Idle Timer ---
def idle_timer():
    global _idle_counter, _last_activity, _idle_scheduled

    if _idle_scheduled or is_ai_paused():
        return 5.0  # Retry shortly

    inactivity = time.time() - _last_activity
    if inactivity < _MIN_IDLE:
        return 5.0  # Not idle enough

    if _idle_counter >= _MAX_IDLE_FIRES:
        print("[Idle Watcher] Max idle messages reached.")
        return None

    # Schedule the AFK message
    message = _generate_message(_FALLBACK_IDLE)
    print(f"[Idle Watcher] AFK message queued:\n{message}")
    _queue_message(message)
    _idle_counter += 1
    _idle_scheduled = True

    # Randomize next check
    return random.uniform(_MIN_IDLE, _MAX_IDLE)

# --- Activity Tracking ---
def _track_activity(scene, depsgraph):
    reset_idle()

# --- Pause / Resume / Reset ---
def reset_idle():
    global _last_activity, _idle_scheduled
    _last_activity = time.time()
    _idle_scheduled = False  # Allow AFK message to be rescheduled

def pause_idle():
    if _track_activity in bpy.app.handlers.depsgraph_update_post:
        bpy.app.handlers.depsgraph_update_post.remove(_track_activity)
    print("[Idle Watcher] Paused.")

def resume_idle():
    if _track_activity not in bpy.app.handlers.depsgraph_update_post:
        bpy.app.handlers.depsgraph_update_post.append(_track_activity)
    print("[Idle Watcher] Resumed.")

# --- Register / Unregister ---
def register_idle():
    global _idle_timer_handle
    play_greeting()
    if _track_activity not in bpy.app.handlers.depsgraph_update_post:
        bpy.app.handlers.depsgraph_update_post.append(_track_activity)
    _idle_timer_handle = bpy.app.timers.register(idle_timer, first_interval=5.0)

def unregister_idle():
    global _idle_timer_handle
    if _idle_timer_handle:
        try:
            bpy.app.timers.unregister(_idle_timer_handle)
        except Exception:
            pass
        _idle_timer_handle = None
    if _track_activity in bpy.app.handlers.depsgraph_update_post:
        bpy.app.handlers.depsgraph_update_post.remove(_track_activity)
