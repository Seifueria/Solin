# utils.py
import threading
import queue
import time
from .talk_to_ai import talk_to_ai

# --- AI Pause State ---
_ai_paused = False

def set_ai_paused(value: bool):
    global _ai_paused
    _ai_paused = value
    print(f"[AI Utils] AI paused set to: {value}")

def is_ai_paused() -> bool:
    return _ai_paused


# --- Audio Queue Manager ---
class AudioManager:
    def __init__(self):
        self._queue = queue.Queue()
        self._is_playing = False
        self._lock = threading.Lock()
        self._thread = threading.Thread(target=self._process_queue, daemon=True)
        self._thread.start()

    def add_to_queue(self, tts_func, message, callback=None):
        """Queue a TTS message for playback."""
        if is_ai_paused():
            print("[AudioManager] AI paused - message skipped")
            return
        self._queue.put((tts_func, message, callback))

    def clear_queue(self):
        """Clear all pending audio."""
        with self._lock:
            while not self._queue.empty():
                self._queue.get_nowait()
                self._queue.task_done()
            self._is_playing = False
        print("[AudioManager] Audio queue cleared.")

    def _process_queue(self):
        while True:
            tts_func, message, callback = self._queue.get()
            with self._lock:
                self._is_playing = True
                try:
                    tts_func(message)  # Must be blocking
                except Exception as e:
                    print(f"[AudioManager] Error playing audio: {e}")
                finally:
                    self._is_playing = False
                    if callback:
                        try:
                            callback()
                        except Exception as e:
                            print(f"[AudioManager] Error in callback: {e}")
            self._queue.task_done()
            time.sleep(0.1)

    def is_busy(self):
        with self._lock:
            return self._is_playing or not self._queue.empty()


# --- Singleton instance ---
audio_manager = AudioManager()


# --- Speak text using the queue ---
def speak_text(text: str):
    """Add a message to the audio queue using talk_to_ai."""
    audio_manager.add_to_queue(talk_to_ai, text)
