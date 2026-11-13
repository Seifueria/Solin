"""Solin's two-pass brain over any provider: internal thought, mood, visible reply."""
import random
import sys

from core_ai import load_prompt, memory

from . import persona
from .config import load_config, pick
from .providers import make_provider


class OfflineBrain:
    """No model reachable. Enough to exercise the companion end to end."""
    name = "offline"
    model = None

    def reply(self, text, context=""):
        return "[offline] My brain's not plugged in yet. Set a key in core_ai/.env or start Ollama, then switch me.", "", persona.mood_of(text)


class SolinBrain:
    def __init__(self, provider_name, settings, mem=None, preprompt=None):
        self.provider = make_provider(provider_name, settings)
        self.name = f"{provider_name}:{self.provider.model}"
        self.model = self.provider.model
        self.small = bool(settings.get("small"))
        self.preprompt = preprompt if preprompt is not None else load_prompt()
        self.mem = mem if mem is not None else memory.load_memory()

    def reply(self, text, context=""):
        system, msgs = persona.internal_messages(self.mem, text)
        if context:
            system += "\n" + context
        internal = self.provider.complete(system, msgs, max_tokens=120)
        mood = persona.mood_of(internal)
        system, msgs = persona.visible_messages(self.preprompt, self.mem, text, internal, mood, small=self.small, context=context)
        visible = persona.tidy(self.provider.complete(system, msgs, max_tokens=400))
        # Personality-based internal fragment bleed, as in core_ai/ai_logic.py.
        bleed = 0.2 if mood in ("teasing", "annoyed") else 0.05 if mood in ("calm", "warm") else 0.1
        if internal and random.random() < bleed:
            visible += f" [soft sigh] ({internal.split('.')[0].strip()})"
        self.mem.append({"user": text, "solin_visible": visible, "solin_internal": internal})
        memory.save_memory(self.mem)
        return visible, internal, mood


def make_brain(name=None, config=None, mem=None):
    """A brain by name, or the configured/auto one. Falls back to offline and says why on stderr."""
    config = config or load_config()
    name = name or pick(config)
    if name == "offline":
        return OfflineBrain()
    try:
        return SolinBrain(name, config["brains"].get(name, {}), mem=mem)
    except Exception as error:  # noqa: BLE001
        print(f"[brains] {name} unavailable: {error}", file=sys.stderr)
        return OfflineBrain()
