"""Switchable brains for Solin.

A brain turns a user line into Solin's internal thought, mood and visible reply,
using whichever provider data/config.json names (or the first one that is
reachable when it says "auto"). Providers: anthropic, claude-cli, gemini,
openai, ollama, offline. Nothing here changes core_ai/ai_logic.py.
"""
from .config import load_config, available
from .solin import SolinBrain, OfflineBrain, make_brain

__all__ = ["load_config", "available", "SolinBrain", "OfflineBrain", "make_brain"]
