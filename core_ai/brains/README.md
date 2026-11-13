# Solin's brains

`core_ai/brains` picks a model provider and runs Solin's two passes through it:
a private internal thought, a mood word scanned from that thought, then the
visible reply in her voice. `core_ai/ai_logic.py` is untouched; this package
replaces its hard-wired OpenAI call for the companion API.

## Choosing a brain

`data/config.json` (currently empty, so the defaults apply). Copy
`config.example.json` there and edit. `"brain": "auto"` tries `auto_order`
and takes the first that can run; name one to force it.

| name | needs | notes |
|---|---|---|
| anthropic | `ANTHROPIC_API_KEY` in `core_ai/.env` | Claude via the Messages API. Needs API credits in the Anthropic Console, which a Claude Pro subscription does not include. |
| claude-cli | a `claude` command on PATH | Claude Code in print mode, which does run on a Claude Pro subscription. Install Claude Code, run `claude` once to log in. Untested so far. |
| gemini | `GEMINI_API_KEY` in `core_ai/.env` | A free key from Google AI Studio (aistudio.google.com). Separate from a Gemini app subscription. |
| openai | `OPENAI_API_KEY` | Needs credits on the OpenAI account. |
| ollama | Ollama running with the model pulled | Local. Uses the small-model layout. |
| offline | nothing | Canned line so the companion still works. |

Switch live from the companion API: `GET /brains` lists them, `POST /brain {"name": "ollama"}` swaps.

## Small-model layout

For `"small": true` providers the persona goes in as a character sheet plus
one in-character acknowledgement, the last dozen exchanges, and a one-line
reminder glued to the newest message. Replies that open with filler
("Ah,", "Oh,", "Well,") lose the opener. Big hosted models get the plain
system prompt.
