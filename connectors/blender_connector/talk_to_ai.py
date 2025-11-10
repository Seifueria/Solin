# talk_to_ai.py
import os
import subprocess
import traceback

def _get_speak_func():
    try:
        from .starter_code import speak_with_elevenlabs
        print("[talk_to_ai] Using package-relative starter_code.speak_with_elevenlabs")
        return speak_with_elevenlabs
    except Exception as e:
        print("[talk_to_ai] package-relative import failed:", e)

    try:
        from starter_code import speak_with_elevenlabs
        print("[talk_to_ai] Using top-level starter_code.speak_with_elevenlabs")
        return speak_with_elevenlabs
    except Exception as e:
        print("[talk_to_ai] top-level import failed:", e)

    return None

def _call_server_fallback(message):
    try:
        import requests
    except Exception as e:
        print("[talk_to_ai] requests not available for server fallback:", e)
        return None

    try:
        resp = requests.post("http://127.0.0.1:5000/asuka", json={"message": message}, timeout=5)
        print(f"[talk_to_ai] server fallback status: {resp.status_code}")
        if resp.status_code == 200:
            try:
                data = resp.json()
                print("[talk_to_ai] server response text:", data.get("response"))
                return data.get("audio_path") or None
            except Exception as e:
                print("[talk_to_ai] server returned non-json or parsing failed:", e)
    except Exception as e:
        print("[talk_to_ai] server fallback request failed:", e)
    return None

def talk_to_ai(message, print_text=True):
    print("[talk_to_ai] called")
    if print_text:
        print("[AI TEXT]", message)

    speak = _get_speak_func()
    audio_path = None

    if speak:
        try:
            audio_path = speak(message)
            print("[talk_to_ai] speak_with_elevenlabs returned:", audio_path)
        except Exception as e:
            print("[talk_to_ai] speak_with_elevenlabs raised an exception:")
            traceback.print_exc()
            audio_path = None

    if not audio_path:
        print("[talk_to_ai] trying server fallback...")
        audio_path = _call_server_fallback(message)

    if audio_path:
        try:
            if os.path.exists(audio_path):
                print("[talk_to_ai] Playing audio at:", audio_path)
                subprocess.call(["afplay", audio_path])
                try:
                    os.remove(audio_path)
                    print("[talk_to_ai] Deleted audio file:", audio_path)
                except Exception as e:
                    print("[talk_to_ai] Failed deleting audio:", e)
            else:
                print("[talk_to_ai] audio_path is not a file on disk:", audio_path)
        except Exception as e:
            print("[talk_to_ai] playback error:", e)
    else:
        print("[talk_to_ai] No audio produced. Message left as text only.")
