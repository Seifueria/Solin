# ui.py
import bpy
from .utils import set_ai_paused, is_ai_paused, speak_text

class AIWatcherPanel(bpy.types.Panel):
    bl_label = "AI Watcher Controls"
    bl_idname = "VIEW3D_PT_ai_watcher"
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = "AI Watcher"

    def draw(self, context):
        layout = self.layout
        pause_text = "Resume AI" if is_ai_paused() else "Pause AI"
        layout.operator("ai_watcher.pause", text=pause_text)
        layout.operator("ai_watcher.talk", text="Talk to AI")


class AIWatcherPause(bpy.types.Operator):
    bl_idname = "ai_watcher.pause"
    bl_label = "Pause/Resume AI Watcher"

    def execute(self, context):
        set_ai_paused(not is_ai_paused())
        return {'FINISHED'}


class AIWatcherTalk(bpy.types.Operator):
    bl_idname = "ai_watcher.talk"
    bl_label = "Talk to AI"

    def execute(self, context):
        speak_text("Hello from Blender!")  # Now queued properly
        return {'FINISHED'}


def register():
    bpy.utils.register_class(AIWatcherPanel)
    bpy.utils.register_class(AIWatcherPause)
    bpy.utils.register_class(AIWatcherTalk)


def unregister():
    bpy.utils.unregister_class(AIWatcherTalk)
    bpy.utils.unregister_class(AIWatcherPause)
    bpy.utils.unregister_class(AIWatcherPanel)
