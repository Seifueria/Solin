"""Model providers. Each takes a system prompt and a chat-style message list and returns one reply string."""
import base64
import json
import os
import subprocess
import tempfile

import requests

from .config import _key


def _image_b64(path):
    with open(path, "rb") as f:
        return base64.b64encode(f.read()).decode("ascii")


def _media_type(path):
    return "image/png" if path.lower().endswith(".png") else "image/jpeg"


class Provider:
    name = "base"
    model = None

    # image: a path to a screenshot the newest user message should be read with, or None.
    def complete(self, system, messages, max_tokens=400, image=None):
        raise NotImplementedError


class OpenAICompat(Provider):
    """OpenAI's chat completions shape, which Gemini and Ollama also serve."""

    def __init__(self, name, settings, default_url):
        self.name = name
        self.model = settings.get("model")
        self.base_url = (settings.get("base_url") or default_url).rstrip("/")
        self.key = _key(settings) or "ollama"

    def complete(self, system, messages, max_tokens=400, image=None):
        messages = [dict(m) for m in messages]
        if image:
            url = "data:%s;base64,%s" % (_media_type(image), _image_b64(image))
            messages[-1]["content"] = [{"type": "text", "text": messages[-1]["content"]}, {"type": "image_url", "image_url": {"url": url}}]
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

    def complete(self, system, messages, max_tokens=400, image=None):
        messages = [dict(m) for m in messages]
        if image:
            messages[-1]["images"] = [_image_b64(image)]
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

    def complete(self, system, messages, max_tokens=400, image=None):
        messages = [dict(m) for m in messages]
        if image:
            messages[-1]["content"] = [{"type": "image", "source": {"type": "base64", "media_type": _media_type(image), "data": _image_b64(image)}},
                                       {"type": "text", "text": messages[-1]["content"]}]
        body = {"model": self.model, "system": system, "messages": messages, "max_tokens": max_tokens, "temperature": 0.8}
        r = requests.post(self.base_url + "/v1/messages", json=body, timeout=120,
                          headers={"x-api-key": self.key, "anthropic-version": "2023-06-01", "Content-Type": "application/json"})
        r.raise_for_status()
        return "".join(part.get("text", "") for part in r.json().get("content", [])).strip()


class ClaudeCli(Provider):
    """Claude Code in print mode, which runs on a Claude subscription instead of an API key.

    Runs in an empty folder so no project instructions leak into Solin.
    """
    name = "claude-cli"

    def __init__(self, settings):
        self.path = settings.get("path", "claude")
        self.model = settings.get("model", "sonnet")

    def complete(self, system, messages, max_tokens=400, image=None):
        transcript = []
        for m in messages[:-1]:
            transcript.append(("User: " if m["role"] == "user" else "Solin: ") + m["content"])
        last = messages[-1]["content"]
        if image:
            last = f"[Their screenshot is at {image}. Read that image first, then answer.] " + last
        prompt = "\n".join(transcript + ["User: " + last, "Solin:"])
        # No tools, no saved session and no user settings: Solin's line in, Claude's line out, nothing else.
        # Not --bare: that mode skips the stored claude.ai login and answers "Not logged in".
        # With a screenshot, only the Read tool, and only for the folder the screenshot is in.
        tools = ["--tools", "Read", "--allowedTools", "Read", "--add-dir", os.path.dirname(image)] if image else ["--tools", ""]
        args = [self.path, "-p", "--output-format", "json", "--system-prompt", system, "--model", self.model,
                *tools, "--no-session-persistence", "--setting-sources", ""]
        # Only the call to the model leaves the Mac: Claude Code's own usage logs (sent to Datadog), error reports and
        # update checks are off. Firewalls like Intego otherwise ask about that traffic in Solin's name every launch.
        quiet = dict(os.environ, CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC="1")
        with tempfile.TemporaryDirectory() as empty:
            done = subprocess.run(args, input=prompt, capture_output=True, text=True, timeout=180, cwd=empty, env=quiet)
        if done.returncode != 0:
            raise RuntimeError("claude exited %d: %s" % (done.returncode, done.stderr.strip()[:300]))
        try:
            out = json.loads(done.stdout)
        except ValueError:
            return done.stdout.strip()
        # Claude Code reports a failed call, like an expired login, inside the JSON with exit code 0.
        if out.get("is_error"):
            raise RuntimeError("claude: " + str(out.get("result", "unknown error"))[:300])
        return str(out.get("result", "")).strip()


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
