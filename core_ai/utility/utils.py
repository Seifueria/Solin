import threading
import queue
import time

class AudioManager:
    """Queue-based audio playback manager."""
    def __init__(self):
        self._queue = queue.Queue()
        self._lock = threading.Lock()
        self._is_playing = False
        self._thread = threading.Thread(target=self._process_queue, daemon=True)
        self._thread.start()

    def add_to_queue(self, tts_func, text):
        self._queue.put((tts_func, text))

    def _process_queue(self):
        while True:
            tts_func, text = self._queue.get()
            with self._lock:
                self._is_playing = True
                try:
                    tts_func(text)
                finally:
                    self._is_playing = False
            self._queue.task_done()
            time.sleep(0.1)

    def is_busy(self):
        with self._lock:
            return self._is_playing or not self._queue.empty()


audio_manager = AudioManager()


def get_model_client(client, prompt, preferred_model="gpt-4-turbo", fallback_model="gpt-3.5-turbo"):
    """
    Attempt to generate a response using the preferred model.
    If insufficient quota or rate limit error occurs, fallback to cheaper model.
    Returns: response object, model_used
    """
    try:
        response = client.chat.completions.create(
            model=preferred_model,
            messages=[{"role": "user", "content": prompt}],
            temperature=0.8
        )
        return response, preferred_model

    except Exception as e:
        err_str = str(e).lower()
        if "insufficient_quota" in err_str or "rate limit" in err_str:
            print(f" ⚠️ Solin: Switching to backup model ({fallback_model}) due to budget limits.")
            response = client.chat.completions.create(
                model=fallback_model,
                messages=[{"role": "user", "content": prompt}],
                temperature=0.8
            )
            return response, fallback_model
        else:
            raise e
        
