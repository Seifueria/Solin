"""Reads data/config.json and works out which brains can run right now."""
import copy
import json
import os
import shutil

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
CONFIG_PATH = os.path.join(ROOT, "data", "config.json")
EXAMPLE_PATH = os.path.join(os.path.dirname(__file__), "config.example.json")


def load_config():
    """The example config with data/config.json laid over it. An empty or missing file means the example as is."""
    with open(EXAMPLE_PATH, "r", encoding="utf-8") as f:
        config = json.load(f)
    try:
        with open(CONFIG_PATH, "r", encoding="utf-8") as f:
            text = f.read().strip()
        if text:
            user = json.loads(text)
            if "brain" in user:
                config["brain"] = user["brain"]
            if "auto_order" in user:
                config["auto_order"] = user["auto_order"]
            for name, settings in (user.get("brains") or {}).items():
                config["brains"].setdefault(name, {}).update(settings or {})
    except (OSError, ValueError):
        pass
    return config


def _key(settings):
    env = settings.get("key_env")
    return settings.get("key") or (os.environ.get(env) if env else None)


def reachable(name, settings):
    """Whether a provider has what it needs, without calling a model. Returns (ok, reason)."""
    if name in ("anthropic", "gemini", "openai"):
        return (True, "key set") if _key(settings) else (False, f"no {settings.get('key_env', 'key')}")
    if name == "claude-cli":
        path = settings.get("path", "claude")
        found = shutil.which(path) or (os.path.isfile(path) and os.access(path, os.X_OK))
        return (True, "cli found") if found else (False, "claude command not found")
    if name == "ollama":
        try:
            import requests
            r = requests.get(settings.get("base_url", "http://127.0.0.1:11434") + "/api/tags", timeout=1.5)
            names = [m.get("name", "") for m in r.json().get("models", [])]
            model = settings.get("model", "")
            if any(n == model or n.split(":")[0] == model.split(":")[0] for n in names):
                return True, "running"
            return False, f"model {model} not pulled"
        except Exception as error:  # noqa: BLE001
            return False, f"not running ({type(error).__name__})"
    if name == "offline":
        return True, "always"
    return False, "unknown provider"


def available(config=None):
    """Every configured brain with whether it can run and why."""
    config = config or load_config()
    out = {}
    for name, settings in config["brains"].items():
        ok, why = reachable(name, settings)
        out[name] = {"ok": ok, "why": why, "model": settings.get("model")}
    out["offline"] = {"ok": True, "why": "always", "model": None}
    return out


def pick(config=None):
    """The brain name to use: the configured one if it can run, else the first in auto_order that can, else offline."""
    config = config or load_config()
    want = config.get("brain", "auto")
    if want != "auto":
        ok, _ = reachable(want, config["brains"].get(want, {}))
        if ok:
            return want
    for name in config.get("auto_order", []):
        ok, _ = reachable(name, config["brains"].get(name, {}))
        if ok:
            return name
    return "offline"
