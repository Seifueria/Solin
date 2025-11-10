import os
import sys
import re
from dotenv import load_dotenv
import openai
from . import ai_logic, memory, voice
from .utility import credits
from . import load_prompt

# Load API keys
dotenv_path = os.path.join(os.path.dirname(__file__), ".env")
load_dotenv(dotenv_path)
openai.api_key = os.getenv("OPENAI_API_KEY")
print("OpenAI Key Loaded:", openai.api_key[:5] + "...")

# OpenAI client
client = openai.OpenAI()

# CONFIG 
SHOW_TTS_TAGS_IN_TERMINAL = False  # Toggle True = show tags, False = hide
OPENAI_TOTAL_QUOTA = 5.0  # total budget ($)
openai_spent = 1.55       # manually update if needed

# load_prompt is a function inside __init__.py
PREPROMPT = load_prompt()


def run_command_line():
    mem = memory.load_memory()
    global SHOW_TTS_TAGS_IN_TERMINAL  # Use global to toggle at module scope

    def handle_response(user_input, verbose=False):
        global SHOW_TTS_TAGS_IN_TERMINAL

        # Handle toggle command
        if user_input.strip().lower() == "[tags]":
            SHOW_TTS_TAGS_IN_TERMINAL = not SHOW_TTS_TAGS_IN_TERMINAL
            status = "shown" if SHOW_TTS_TAGS_IN_TERMINAL else "hidden"
            print(f"[TTS tags are now {status} in terminal display]")
            return

        # Generate AI response with internal + visible layers
        response_dict = ai_logic.generate_response(user_input, mem, client, PREPROMPT)
        internal_response = response_dict["internal"]
        visible_response = ai_logic.clean_forced_titles(response_dict["visible"])

        # Estimate OpenAI usage
        prompt_tokens = len(PREPROMPT + visible_response) // 4
        response_tokens = len(visible_response) // 4
        estimated_cost = credits.estimate_openai_usage(prompt_tokens, response_tokens)

        # Calculate remaining OpenAI quota
        openai_remaining = OPENAI_TOTAL_QUOTA - (openai_spent + estimated_cost)
        if openai_remaining < 0:
            openai_remaining = 0

        # Get ElevenLabs credits
        current_credits = credits.get_elevenlabs_credits()
        tts_cost = len(visible_response)

        # Prepare clean or raw display output based on toggle
        if SHOW_TTS_TAGS_IN_TERMINAL:
            display_response = visible_response
        else:
            display_response = re.sub(r'\[[^\]]+\]', '', visible_response).strip()

        # Append warning if TTS credits are low
        if current_credits < tts_cost:
            display_response += f" | ⚠️ Audio unavailable ({current_credits} credits)"

        # Print outputs in desired order
        print(display_response)
        print(f"[ElevenLabs credits remaining: {current_credits}]")
        print(f"[Estimated OpenAI usage for this request: ${estimated_cost:.5f}]")
        print(f"[Estimated OpenAI remaining balance: ${openai_remaining:.2f}]")

        # Play TTS if enough credits
        if current_credits >= tts_cost and "out of API credits" not in visible_response:
            audio_file = voice.speak_with_elevenlabs(visible_response, verbose=verbose)
            if audio_file:
                voice.play_audio(audio_file)
                credits.spend_elevenlabs_credits(tts_cost)

        # Save memory (store clean visible + internal)
        mem.append({
            "user": user_input,
            "solin_visible": visible_response,
            "solin_internal": internal_response
        })
        memory.save_memory(mem)

    # Handle command-line input
    if len(sys.argv) > 1:
        user_input = " ".join(sys.argv[1:])
        print(f"You: {user_input}")
        handle_response(user_input)

    # Interactive mode
    while True:
        user_input = input("You: ")
        if user_input.lower() in ["exit", "quit"]:
            print("じゃね")
            break
        handle_response(user_input)


if __name__ == "__main__":
    run_command_line()