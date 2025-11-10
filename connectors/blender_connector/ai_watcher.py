import bpy
import sys
import os

# Attach your real talk_to_ai function
from talk_to_ai import talk_to_ai
talk_to_ai = talk_to_ai  # ensure reference in module

# Ensure addon folder is in sys.path
addon_path = os.path.dirname(__file__)
if addon_path not in sys.path:
    sys.path.append(addon_path)

# Attempt to import watcher modules
deletion_watcher = None
idle_watcher = None

try:
    from . import deletion_watcher as dw
    deletion_watcher = dw
except ImportError:
    print("[AI Watcher] Could not import deletion_watcher.")

try:
    from . import idle_watcher as iw
    idle_watcher = iw
except ImportError:
    print("[AI Watcher] Could not import idle_watcher.")

# Track Blender UI classes to unregister safely
ui_classes = []

try:
    from .ui_panel import AIWatcherPanel
    ui_classes.append(AIWatcherPanel)
except ImportError:
    print("[AI Watcher] AIWatcherPanel not found.")

# --- REGISTER / UNREGISTER ---
def register():
    # Register UI classes
    for cls in ui_classes:
        try:
            bpy.utils.register_class(cls)
        except Exception as e:
            print(f"[AI Watcher] Failed to register {cls.__name__}: {e}")

    # Register watcher modules safely
    for module, name in [(deletion_watcher, "deletion_watcher"), (idle_watcher, "idle_watcher")]:
        if module and hasattr(module, "register"):
            try:
                module.register()
                print(f"[AI Watcher] {name} registered.")
            except Exception as e:
                print(f"[AI Watcher] Failed to register {name}: {e}")

    print("[AI Watcher] All modules registered.")

def unregister():
    # Unregister watcher modules safely
    for module, name in [(deletion_watcher, "deletion_watcher"), (idle_watcher, "idle_watcher")]:
        if module and hasattr(module, "unregister"):
            try:
                module.unregister()
                print(f"[AI Watcher] {name} unregistered.")
            except Exception as e:
                print(f"[AI Watcher] Failed to unregister {name}: {e}")

    # Unregister UI classes in reverse order
    for cls in reversed(ui_classes):
        try:
            bpy.utils.unregister_class(cls)
        except Exception as e:
            print(f"[AI Watcher] Failed to unregister {cls.__name__}: {e}")

    print("[AI Watcher] All modules unregistered.")

__all__ = ["talk_to_ai"]
