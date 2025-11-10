# Solin Core

Solin Core is the main personality engine and AI logic for Solin D’Arc.  
It handles dialogue generation, memory, mood tracking, and micro-model training.

## Structure
- **core_ai/** – main AI logic (memory, dialogue, voice, emotion)
- **micro_model/** – dataset + training logic for fine-tuning
- **connectors/** – integrations for Blender, After Effects, etc.
- **server/** – optional Flask server for remote use
- Python is 3.11.9

## Setup
```bash
python3 -m venv solin3.11py
source solin3.11py/bin/activate
pip install -r requirements.txt


## Progress

Currently the micro-personality feature doesen't work. Howerver just slowly adding features also alot of python files
are pretty much for show such as flask and connectors.