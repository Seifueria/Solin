"""Model providers. Each takes a system prompt and a chat-style message list and returns one reply string."""
import json
import os
import subprocess
import tempfile

import requests

from .config import _key


class Provider:
    name = "base"
    model = None

    def complete(self, system, messages, max_tokens=400):
        raise NotImplementedError


class OpenAICompat(Provider):
    """OpenAI's chat completions shape, which Gemini and Ollama also serve."""

    def __init__(self, name, settings, default_url):
        self.name = name
        self.model = settings.get("model")
        self.base_url = (settings.get("base_url") or default_url).rstrip("/")
        self.key = _key(settings) or "ollama"

    def complete(self, system, messages, max_tokens=400):
        body = {"model": self.model, "messages": [{"role": "system", "content": system}] + messages, "max_tokens": max_tokens, "temperature": 0.8}
        r = requests.post(self.base_url + "/chat/completions", json=body, timeout=120,
                          headers={"Authorization": "Bearer " + self.key, "Content-Type": "application/json"})
        r.raise_for_status()
        return r.json()["choices"][0]["message"]["content"].strip()


class Ollama(Provider):
    """Ollama's own chat endpoint, with thinking off: a thinking model otherwise spends the whole budget reasoning and
    hands back empty content."""
    name = "ollama"

    def __init__(self, settings):
        self.model = settings.get("model", "gemma4:e4b")
        self.base_url = (settings.get("base_url") or "http://127.0.0.1:11434").rstrip("/")

    def complete(self, system, messages, max_tokens=400):
        body = {"model": self.model, "stream": False, "think": False,
                "messages": [{"role": "system", "content": system}] + messages,
                "options": {"num_predict": max_tokens, "temperature": 0.8}}
        r = requests.post(self.base_url + "/api/chat", json=body, timeout=180)
        r.raise_for_status()
        return (r.json().get("message") or {}).get("content", "").strip()


class Anthropic(Provider):
    name = "anthropic"

    def __init__(self, settings):
        self.model = settings.get("model", "claude-sonnet-5-5")
        self.key = _key(settings)
        self.base_url = (settings.get("base_url") or os.environ.get("ANTHROPIC_BASE_URL") or "https://api.anthropic.com").rstrip("/")

    def complete(self, system, messages, max_tokens=400):
        body = {"model": self.model, "system": system, "messages": messages, "max_tokens": max_tokens, "temperature": 0.8}
        r = requests.post(self.base_url + "/v1/messages", json=body, timeout=120,
                          headers={"x-api-key": self.key, "anthropic-version": "2023-06-01", "Content-Type": "application/json"})
        r.raise_for_status()
        return "".join(part.get("text", "") for part in r.json().get("content", [])).strip()


class ClaudeCli(Provider):
    """Claude Code in print mode, which runs on a Claude subscription instead of an API key.

    Untested until a `claude` command is installed. Runs in an empty folder so no project instructions leak into Solin.
    """
    name = "claude-cli"

    def __init__(self, settings):
        self.path = settings.get("path", "claude")
        self.model = settings.get("model", "sonnet")

    def complete(self, system, messages, max_tokens=400):
        transcript = []
        for m in messages[:-1]:
            transcript.append(("User: " if m["role"] == "user" else "Solin: ") + m["content"])
        prompt = "\n".join(transcript + ["User: " + messages[-1]["content"], "Solin:"])
        args = [self.path, "-p", "--output-format", "json", "--system-prompt", system, "--model", self.model, "--max-turns", "1"]
        with tempfile.TemporaryDirectory() as empty:
            done = subprocess.run(args, input=prompt, capture_output=True, text=True, timeout=180, cwd=empty)
        if done.returncode != 0:
            raise RuntimeError("claude exited %d: %s" % (done.returncode, done.stderr.strip()[:300]))
        try:
            return json.loads(done.stdout).get("result", "").strip()
        except ValueError:
            return done.stdout.strip()


def make_provider(name, settings):
    if name == "anthropic":
        return Anthropic(settings)
    if name == "claude-cli":
        return ClaudeCli(settings)
    if name == "gemini":
        return OpenAICompat("gemini", settings, "https://generativelanguage.googleapis.com/v1beta/openai")
    if name == "openai":
        return OpenAICompat("openai", settings, "https://api.openai.com/v1")
    if name == "ollama":
        return Ollama(settings)
    raise ValueError("unknown provider " + name)
