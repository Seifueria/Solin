# __init__.py
bl_info = {
    "name": "AI Watcher",
    "blender": (4, 5, 0),
    "category": "3D View",
}

import bpy
import sys
import os

# Add addon folder to sys.path if not already present
addon_path = os.path.dirname(__file__)
if addon_path not in sys.path:
    sys.path.append(addon_path)

# --- IMPORTS WITH CORRECT ORDER ---
from . import utils  # Must load first for set_ai_paused & speak_text

deletion_watcher = None
idle_watcher = None
ui_module = None

try:
    from . import deletion_watcher
    print("[AI Watcher] Deletion watcher loaded.")
except ImportError as e:
    print(f"[AI Watcher] Could not import deletion_watcher: {e}")

try:
    from . import idle_watcher
    print("[AI Watcher] Idle watcher loaded.")
except ImportError as e:
    print(f"[AI Watcher] Could not import idle_watcher: {e}")

try:
    from . import ui as ui_module
    print("[AI Watcher] UI module loaded.")
except ImportError as e:
    print(f"[AI Watcher] Could not import UI module: {e}")

# --- REGISTER / UNREGISTER ---
def register():
    print("[AI Watcher] Registering addon...")

    if deletion_watcher:
        try:
            deletion_watcher.register()
            print("[Deletion Watcher] Registered.")
        except Exception as e:
            print(f"[Deletion Watcher] Register error: {e}")

    if idle_watcher:
        try:
            idle_watcher.register()
            print("[Idle Watcher] Registered.")
        except Exception as e:
            print(f"[Idle Watcher] Register error: {e}")

    if ui_module:
        try:
            ui_module.register()
            print("[UI] Registered.")
        except Exception as e:
            print(f"[UI] Register error: {e}")

    print("[AI Watcher] Addon fully registered.")

def unregister():
    print("[AI Watcher] Unregistering addon...")

    if deletion_watcher:
        try:
            deletion_watcher.unregister()
            print("[Deletion Watcher] Unregistered.")
        except Exception as e:
            print(f"[Deletion Watcher] Unregister error: {e}")

    if idle_watcher:
        try:
            idle_watcher.unregister()
            print("[Idle Watcher] Unregistered.")
        except Exception as e:
            print(f"[Idle Watcher] Unregister error: {e}")

    if ui_module:
        try:
            ui_module.unregister()
            print("[UI] Unregistered.")
        except Exception as e:
            print(f"[UI] Unregister error: {e}")

    print("[AI Watcher] Addon fully unregistered.")
