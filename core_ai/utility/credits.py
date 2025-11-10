import os
import json
import requests
from dotenv import load_dotenv

load_dotenv()
ELEVENLABS_API_KEY = os.getenv("ELEVENLABS_API_KEY")

# Paths
BASE_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), "../../"))
DATA_DIR = os.path.join(BASE_DIR, "data")
CREDITS_FILE = os.path.join(DATA_DIR, "elevenlabs_credits.json")


def get_elevenlabs_credits():
    """Return current ElevenLabs character credits, API first, fallback to JSON."""
    try:
        headers = {"xi-api-key": ELEVENLABS_API_KEY}
        resp = requests.get("https://api.elevenlabs.io/v1/user", headers=headers, timeout=5)
        if resp.status_code == 200:
            data = resp.json()
            remaining = data.get("subscription", {}).get("character_limit", 0) - data.get("subscription", {}).get("character_count", 0)
            # Update local JSON backup
            try:
                os.makedirs(DATA_DIR, exist_ok=True)
                with open(CREDITS_FILE, "w") as f:
                    json.dump({"credits": remaining}, f, indent=2)
            except Exception:
                pass
            return remaining
    except Exception:
        pass

    # Fallback to manual JSON
    try:
        with open(CREDITS_FILE, "r") as f:
            data = json.load(f)
            return data.get("credits", 0)
    except Exception:
        return 0


def spend_elevenlabs_credits(amount):
    """Deduct credits from manual JSON if API fails (API auto-handles usage)."""
    try:
        headers = {"xi-api-key": ELEVENLABS_API_KEY}
        resp = requests.get("https://api.elevenlabs.io/v1/user", headers=headers, timeout=5)
        if resp.status_code == 200:
            return
    except Exception:
        pass

    try:
        os.makedirs(DATA_DIR, exist_ok=True)
        if os.path.exists(CREDITS_FILE):
            with open(CREDITS_FILE, "r") as f:
                data = json.load(f)
        else:
            data = {"credits": 0}
        data["credits"] = max(0, data.get("credits", 0) - amount)
        with open(CREDITS_FILE, "w") as f:
            json.dump(data, f, indent=2)
    except Exception:
        pass


def estimate_openai_usage(prompt_tokens=0, response_tokens=0):
    """Estimate OpenAI usage in USD based on token counts."""
    cost_per_token = 0.0000096
    total_tokens = prompt_tokens + response_tokens
    approx_usage = total_tokens * cost_per_token
    return approx_usage


def display_elevenlabs_credits():
    credits_remaining = get_elevenlabs_credits()
    print(f"[ElevenLabs credits remaining: {credits_remaining}]")


def display_estimated_openai_cost(prompt_tokens=0, response_tokens=0):
    cost = estimate_openai_usage(prompt_tokens, response_tokens)
    print(f"[Estimated OpenAI usage for this request: ${cost:.5f}]")
