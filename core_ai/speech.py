"""Solin's spoken voice for the companion.

Like core_ai/voice.py (which stays as it is for the terminal loop) but with the
voice and model chosen in data/config.json, MP3 kept as MP3 so no audio
libraries are needed, and a clear reason when ElevenLabs says no.

    "voice": {"speak": true, "voice_id": "...", "model_id": "eleven_v3"}

The default voice is a premade one, because the cloned voice in voice.py needs
a paid ElevenLabs plan. Put that clone's id here once the plan allows it.
"""
import hashlib
import json
import os
import sys

import requests

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
CACHE_DIR = os.path.join(ROOT, "data", "audio_file")
CONFIG_PATH = os.path.join(ROOT, "data", "config.json")

DEFAULT_VOICE = "cgSgspJ2msm6clMCkdW9"  # ElevenLabs premade "Jessica": young, playful, warm
DEFAULT_MODEL = "eleven_v3"
FALLBACK_MODEL = "eleven_flash_v2_5"


class SpeechError(Exception):
    pass


def settings():
    out = {"speak": False, "voice_id": DEFAULT_VOICE, "model_id": DEFAULT_MODEL}
    try:
        with open(CONFIG_PATH, "r", encoding="utf-8") as f:
            text = f.read().strip()
        out.update((json.loads(text) if text else {}).get("voice") or {})
    except (OSError, ValueError):
        pass
    return out


def speak(text, voice_id=None, model_id=None):
    """MP3 path for the line, from the cache when it has been said before. Raises SpeechError with ElevenLabs' reason."""
    key = os.environ.get("ELEVENLABS_API_KEY")
    if not key:
        raise SpeechError("ELEVENLABS_API_KEY is not set")
    conf = settings()
    voice_id = voice_id or conf["voice_id"]
    model_id = model_id or conf["model_id"]
    os.makedirs(CACHE_DIR, exist_ok=True)
    digest = hashlib.md5(f"{voice_id}|{model_id}|{text}".encode("utf-8")).hexdigest()
    path = os.path.join(CACHE_DIR, f"{digest}.mp3")
    if os.path.exists(path):
        return path, False
    body = {"text": text, "model_id": model_id, "voice_settings": {"stability": 0.4, "similarity_boost": 0.8, "use_speaker_boost": True}}
    r = requests.post(f"https://api.elevenlabs.io/v1/text-to-speech/{voice_id}/stream", json=body, timeout=60,
                      headers={"xi-api-key": key, "Content-Type": "application/json", "Accept": "audio/mpeg"})
    if r.status_code != 200 and model_id != FALLBACK_MODEL:
        body["model_id"] = FALLBACK_MODEL
        r = requests.post(f"https://api.elevenlabs.io/v1/text-to-speech/{voice_id}/stream", json=body, timeout=60,
                          headers={"xi-api-key": key, "Content-Type": "application/json", "Accept": "audio/mpeg"})
    if r.status_code != 200:
        try:
            reason = r.json().get("detail", {}).get("message") or r.text[:200]
        except ValueError:
            reason = r.text[:200]
        raise SpeechError(f"ElevenLabs {r.status_code}: {reason}")
    with open(path, "wb") as f:
        f.write(r.content)
    try:
        from core_ai.utility import credits
        credits.spend_elevenlabs_credits(len(text))
    except Exception as error:  # noqa: BLE001
        print(f"[speech] credit note failed: {error}", file=sys.stderr)
    return path, True
