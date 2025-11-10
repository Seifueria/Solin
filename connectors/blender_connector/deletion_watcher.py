import bpy
from .talk_to_ai import talk_to_ai
from .utils import audio_manager
import threading

# --- Private state ---
_tracked_objects = set()
_deleted_count = 0
_DELETION_TIMER_HANDLE = None
_SPEAK_THRESHOLD = 1
_CHECK_INTERVAL = 1.0

# --- Callbacks ---
def _pre_delete_callback(self, context):
    global _tracked_objects
    selected = [obj.name for obj in context.selected_objects]
    _tracked_objects.update(selected)
    print(f"[Deletion Watcher] Pre-delete fired. Selected: {selected}")

def _depsgraph_post(scene, depsgraph):
    global _tracked_objects, _deleted_count
    existing_cubes = {obj.name for obj in bpy.data.objects if obj.name.lower().startswith("cube")}
    deleted_now = _tracked_objects - existing_cubes
    if deleted_now:
        _deleted_count += len(deleted_now)
        print(f"[Deletion Watcher] Deleted cubes detected: {deleted_now}. Total count: {_deleted_count}")
        _tracked_objects.clear()

def _talk_and_reset(message):
    global _deleted_count
    try:
        audio_manager.add_to_queue(talk_to_ai, message)
    except Exception as e:
        print("[Deletion Watcher] talk_to_ai call failed:", e)
    _deleted_count = 0

def _deletion_speaker_timer():
    global _deleted_count
    if _deleted_count >= _SPEAK_THRESHOLD:
        message = f"You just deleted {_deleted_count} cube(s)! Careful not to erase everything."
        print(f"[Deletion Watcher] {message}")
        threading.Thread(target=_talk_and_reset, args=(message,), daemon=True).start()
    return _CHECK_INTERVAL if _deleted_count > 0 else None

# --- Register / Unregister ---
def register():
    global _DELETION_TIMER_HANDLE
    if hasattr(bpy.types, "VIEW3D_MT_edit_mesh_delete"):
        bpy.types.VIEW3D_MT_edit_mesh_delete.prepend(_pre_delete_callback)
    bpy.app.handlers.depsgraph_update_post.append(_depsgraph_post)
    _DELETION_TIMER_HANDLE = bpy.app.timers.register(_deletion_speaker_timer)
    print("[Deletion Watcher] Registered.")

def unregister():
    global _DELETION_TIMER_HANDLE
    if hasattr(bpy.types, "VIEW3D_MT_edit_mesh_delete"):
        bpy.types.VIEW3D_MT_edit_mesh_delete.remove(_pre_delete_callback)
    bpy.app.handlers.depsgraph_update_post.remove(_depsgraph_post)
    if _DELETION_TIMER_HANDLE:
        try:
            bpy.app.timers.unregister(_DELETION_TIMER_HANDLE)
        except Exception:
            pass
        _DELETION_TIMER_HANDLE = None
    print("[Deletion Watcher] Unregistered.")
