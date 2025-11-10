import os, hashlib
from io import BytesIO
from pydub import AudioSegment
from core_ai.ai_logic import detect_emotion
from core_ai.utility import credits
from core_ai.utility.utils import audio_manager
import requests
from dotenv import load_dotenv

load_dotenv()
ELEVENLABS_API_KEY = os.getenv("ELEVENLABS_API_KEY")
voice_id = "ufJvdzcEEGUXbDllpdMJ"

CACHE_DIR = os.path.join(os.path.abspath(os.path.join(os.path.dirname(__file__), "..")), "data/audio_file")
os.makedirs(CACHE_DIR, exist_ok=True)

def generate_audio(text, verbose=False):
    """Generate WAV bytes with caching."""
    
    text_hash = hashlib.md5(text.encode("utf-8")).hexdigest()
    cached_file = os.path.join(CACHE_DIR, f"{text_hash}.wav")
    if os.path.exists(cached_file):
        if verbose:
            print(f"[DEBUG] Using cached audio: {cached_file}")
        return cached_file

    # Generate via ElevenLabs
    emotion = detect_emotion(text)
    style, stability = (0.8, 0) if emotion=="surprised" else (0,0)
    url = f"https://api.elevenlabs.io/v1/text-to-speech/{voice_id}/stream"
    headers = {"xi-api-key": ELEVENLABS_API_KEY, "Content-Type":"application/json"}
    data = {
        "text": text,
        "model_id": "eleven_v3",
        "voice_settings": {"stability": stability, "similarity_boost": 1, "use_speaker_boost": True}
    }

    resp = requests.post(url, headers=headers, json=data, timeout=15)
    if resp.status_code != 200:
        if verbose: print(f"[ERROR] ElevenLabs returned {resp.status_code}")
        return None

    # Convert to WAV
    mp3_audio = AudioSegment.from_file(BytesIO(resp.content), format="mp3")
    mp3_audio.export(cached_file, format="wav")
    if verbose: print(f"[DEBUG] Generated audio: {cached_file}")
    return cached_file

def speak_async(text, verbose=False):
    """Queue TTS for async playback"""
    def play_func(tts_text):
        audio_file = generate_audio(tts_text, verbose=verbose)
        if audio_file:
            from simpleaudio import WaveObject
            wave_obj = WaveObject.from_wave_file(audio_file)
            play_obj = wave_obj.play()
            play_obj.wait_done()
            credits.spend_elevenlabs_credits(len(tts_text))
    audio_manager.add_to_queue(play_func, text)

def speak_with_elevenlabs(text, verbose=False):
    """Generate audio and return WaveObject for synchronous playback."""
    audio_file = generate_audio(text, verbose=verbose)
    if audio_file:
        from simpleaudio import WaveObject
        wave_obj = WaveObject.from_wave_file(audio_file)
        credits.spend_elevenlabs_credits(len(text))
        return wave_obj
    return None

def play_audio(wave_obj):
    """Play the given WaveObject synchronously."""
    if wave_obj:
        play_obj = wave_obj.play()
        play_obj.wait_done()