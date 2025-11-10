import os
import sys
import subprocess
from flask import Flask, request, jsonify

# Path setup
current_dir = os.path.dirname(os.path.realpath(__file__))  # folder containing flask_app.py
sys.path.insert(0, os.path.abspath(os.path.join(current_dir, "..")))  # Add parent dir to sys.path

from core_ai import ai_logic, memory, voice, load_prompt
import openai
from dotenv import load_dotenv

# Load API keys
load_dotenv()
openai.api_key = os.getenv("OPENAI_API_KEY")
print("[Flask] OpenAI Key Loaded:", openai.api_key[:5] + "...")

# OpenAI client
client = openai.OpenAI()

# Load prompt 
PREPROMPT = load_prompt()

# Load memory 
mem = memory.load_memory()

# Flask setup 
app = Flask(__name__)

@app.route('/asuka', methods=['POST'])
def asuka():
    data = request.get_json()
    message = data.get('message', '')
    if not message:
        return jsonify({'error': 'No message provided'}), 400

    # Generate AI response
    text_response = ai_logic.generate_response(message, mem, client, PREPROMPT)
    print(f"[AI TEXT] {text_response}")

    # Speak with ElevenLabs TTS
    audio_path = voice.speak_with_elevenlabs(text_response)
    if audio_path and os.path.exists(audio_path):
        try:
            subprocess.call(["afplay", audio_path])  # macOS audio
            os.remove(audio_path)
        except Exception as e:
            return jsonify({'error': str(e)}), 500

    # Update memory
    mem.append({"user": message, "asuka": text_response})
    memory.save_memory(mem)

    return jsonify({'response': text_response, 'status': 'played'})


if __name__ == '__main__':
    print("[Flask] Server starting...")
    app.run(debug=True)