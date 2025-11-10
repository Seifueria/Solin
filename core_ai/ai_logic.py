import re
from .memory import load_memory, save_memory
from .utility.utils import get_model_client  
from .utility import credits      
import random         

def generate_response(user_input, mem, client, PREPROMPT):
    # Build conversation context from memory
    context = ""
    for entry in mem[-50:]:
        user_msg = entry.get("user", "")
        solin_msg = entry.get("solin_visible", "")
        context += f"User: {user_msg}\nSolin: {solin_msg}\n"

    # Internal thought
    internal_prompt = f"""
You are Solin D'Arc. Write 1-2 sentences of private internal reflection.
Conversation so far:
{context}
User: {user_input}
Internal thought:
"""
    internal_output = client.responses.create(model="gpt-4o-mini", input=internal_prompt).output_text.strip()

    # Determine mood
    mood_keywords = ["warm", "curious", "annoyed", "calm", "tired", "teasing"]
    mood = "neutral"
    for word in mood_keywords:
        if word in internal_output.lower():
            mood = word
            break

    # Visible response prompt
    visible_prompt = f"""
{PREPROMPT}
[Internal context: {internal_output}]
Solin currently feels: {mood}
Conversation so far:
{context}
User: {user_input}

Write Solin's visible reply aloud:
- Vary sentence lengths naturally (short, slangy lines + occasional longer reflections)
- Include occasional line breaks (\n), but not after every sentence
- Use casual acronyms/shorthand naturally
- Include audio tags like [pause], [soft sigh], [teasing]
- Keep continuity with past memory
- Randomness should feel natural to Solin’s personality (teasing, calm, sarcastic)
"""
    visible_output = client.responses.create(model="gpt-4o-mini", input=visible_prompt).output_text.strip()

    # Personality-based internal fragment bleed
    bleed_prob = 0.1
    if mood in ["teasing", "annoyed"]:
        bleed_prob = 0.2
    elif mood in ["calm", "warm"]:
        bleed_prob = 0.05

    if random.random() < bleed_prob:
        fragment = internal_output.split(".")[0]
        visible_output += f" [soft sigh] ({fragment.strip()})"

    return {"internal": internal_output, "visible": visible_output}

def detect_emotion(text):
    text_lower = text.lower()
    if any(word in text_lower for word in ["shut up", "idiot", "dumbass", "fuck", "angry", "*slams", "the hell", "goddamn"]):
        return "angry"
    elif any(word in text_lower for word in ["good job", "impressed", "finally", "not bad", "wow"]):
        return "surprised"
    elif any(word in text_lower for word in ["ugh", "whatever", "fine", "tch", "sigh", "seriously"]):
        return "annoyed"
    elif any(word in text_lower for word in ["congrats", "nice work", "proud"]):
        return "soft"
    else:
        return "neutral"


def clean_forced_titles(response):
    """Remove short forced quotes and excessive whitespace."""
    response = re.sub(r'“[^”]{1,30}”', '', response)  # smart quotes
    response = re.sub(r'"[^"]{1,30}"', '', response)  # straight quotes
    return re.sub(r'\s{2,}', ' ', response).strip()

