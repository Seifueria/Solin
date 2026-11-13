"""How Solin's character reaches a model.

Big hosted models take the persona as a plain system prompt. Small local models
hold a character better with a sheet up front, one in-voice acknowledgement,
the real history, and a short reminder glued to the newest line (an idea
described from edgerunners-2/capri-ai-desktop; no code taken).
"""
import re

MOODS = ["warm", "curious", "annoyed", "calm", "tired", "teasing"]

# Openers small models lean on. A reply that starts with one loses it.
AVOID_OPENERS = ("ah,", "oh,", "well,", "hmm,", "ahh,", "ooh,", "ah ", "oh ", "*")

STYLE = """Write Solin's visible reply aloud:
- Vary sentence lengths naturally (short, slangy lines + occasional longer reflections)
- Include occasional line breaks, but not after every sentence
- Use casual acronyms/shorthand naturally
- Include audio tags like [pause], [soft sigh], [teasing]
- Keep continuity with past memory
- Randomness should feel natural to Solin's personality (teasing, calm, sarcastic)"""


def mood_of(text):
    low = (text or "").lower()
    for word in MOODS:
        if word in low:
            return word
    return "neutral"


def history(mem, limit=50):
    out = []
    for entry in mem[-limit:]:
        user = entry.get("user", "")
        solin = entry.get("solin_visible", "")
        if user:
            out.append({"role": "user", "content": user})
        if solin:
            out.append({"role": "assistant", "content": solin})
    return out


def internal_messages(mem, user_input):
    system = "You are Solin D'Arc. Write 1-2 sentences of private internal reflection on what the user just said. Only the reflection."
    return system, history(mem) + [{"role": "user", "content": user_input}]


def visible_messages(preprompt, mem, user_input, internal, mood, small=False):
    if not small:
        system = f"{preprompt}\n[Internal context: {internal}]\nSolin currently feels: {mood}\n\n{STYLE}"
        return system, history(mem) + [{"role": "user", "content": user_input}]
    # Small-model layout.
    system = f"CHARACTER SHEET\n{preprompt}\n\nSTYLE\n{STYLE}"
    ack = {"role": "assistant", "content": "[calm] Got it. I'm Solin. I'll stay in character and keep it short."}
    reminder = f"\n\n(Stay as Solin. You feel {mood}. Private thought: {internal} Reply in one to three short lines.)"
    msgs = [{"role": "user", "content": "Read your character sheet and acknowledge in character."}, ack]
    msgs += history(mem, limit=12)
    msgs.append({"role": "user", "content": user_input + reminder})
    return system, msgs


def tidy(reply):
    """Strip leaked labels and overused openers."""
    reply = re.sub(r"^\s*(Solin|Assistant)\s*:\s*", "", reply, flags=re.IGNORECASE).strip()
    low = reply.lower()
    for opener in AVOID_OPENERS:
        if low.startswith(opener):
            cut = reply[len(opener):].lstrip()
            if cut:
                reply = cut[0].upper() + cut[1:]
            break
    return re.sub(r"\s{2,}", " ", reply).strip()
