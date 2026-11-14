# Capri desktop-companion audit for Solin

Date: 2026-11-13
Scope: Phase 1, audit and plan only. No porting has been done. Nothing in
"/Users/kaelen/Capri References" was modified.

## Decisions taken so far

- Base to derive from: lachydotmcg/capri-desktop (folder `Lachy/`), version 0.9.0,
  licence PolyForm Noncommercial 1.0.0 with two Required Notice lines.
- The companion front end lives in `companion/` and is the only derivative work.
  Everything outside `companion/` stays Solin's own.
- The Python backend owns Solin: brain selection, personality, memory, session
  identities, mood and trust, ElevenLabs voice. The companion is a thin body that
  talks to the backend over a loopback HTTP API.
- Lachy's sprite strips may be used as temporary placeholders. This is allowed
  because the project is non-commercial and the base's art is under the same
  PolyForm licence with the same credit requirement. They stay under `companion/`
  only, are credited in NOTICE.md, and are replaced with Solin's own strips in the
  same format later. Art from Mochii (frames from the show) and Edgerunners
  (xilia_world00, excluded from its MIT licence) is never used.
- Brain priority: Claude and Gemini first, OpenAI kept as an option, Ollama for
  local. Switching is a backend config matter, not a companion matter.
- Notes file: none exists yet. Goals below come from the Phase 1 brief. When a
  notes JSON is added, the ranking in Step 3 should be re-checked against it.

## Step 1: goals and Solin's current state

Goals from the brief:

1. Mood- and state-driven animation.
2. Switchable brains: local Ollama versus Gemini (OpenAI-compatible config), with
   Claude and OpenAI as further options.
3. Session identities.
4. ElevenLabs voice.
5. Persistent personality.

Solin today, from the code (no notes file available):

- `core_ai/main_code.py`: terminal chat loop. Loads the OpenAI key from
  `core_ai/.env`, checks ElevenLabs credits, appends each exchange to memory.
- `core_ai/ai_logic.py`: brain is OpenAI gpt-4o-mini via the Responses API, called
  twice per turn. Pass one produces a private internal thought. A keyword scan of
  it picks a mood word from warm, curious, annoyed, calm, tired, teasing. Pass two
  produces the visible reply with mandatory tone tags. A separate keyword detector
  maps replies to angry, surprised, annoyed, soft, neutral.
- `core_ai/memory.py`: memory is a JSON list in `data/solin_memory.json`
  (about 108 KB). State is `core_ai/state/solin_state.json` holding mood, trust
  and last interaction. Legacy "asuka" keys are normalised on load.
- `core_ai/voice.py`: ElevenLabs eleven_v3 streaming to WAV, md5 cache under
  `data/audio_file/`, simpleaudio playback.
- `server/flask_app.py`: one POST route still named after the old Asuka
  character. README calls connectors "for show".
- `core_ai/prompt.txt`: Solin persona, 22, tone tags per sentence.
- `data/config.json` and `data/solin_personality.json` are empty files.
- No Ollama, Gemini or Claude path exists yet. No session identity concept.
- `.gitignore` excludes `data/`, `tests/`, datasets and the venv.

## Step 2: reference inventory

Structural note: the folder "/Users/kaelen/Capri References" is itself the
lachydotmcg git clone (its `.git` remote is lachydotmcg/capri-desktop) but the
working files were moved into `Lachy/`, so the top-level repo shows about 930
deletions. Harmless for reading; left untouched.

| Folder | Upstream | Language / framework | Licence file | Last commit | Commits |
|---|---|---|---|---|---|
| Lachy | lachydotmcg/capri-desktop | C#, .NET 10. Windows: WinForms (`app/`). Mac and Linux: Avalonia 11.3 (`cross/`) with sandboxed Python worker children | `LICENSE.md`: PolyForm Noncommercial 1.0.0 plus two Required Notice lines. `THIRD-PARTY-NOTICES.md` | 2026-11-13 | 1663 |
| Mochii | mochiiwuff/capri-desktop | JavaScript, Electron 44, WebGL renderer | `LICENSE`: PolyForm Noncommercial 1.0.0 from 0.4.4. MIT only up to 0.4.3. Art excluded | 2026-11-09 (v0.5.2) | 109 |
| Edgerunners | edgerunners-2/capri-ai-desktop | Python 3, PySide6 (Qt), llama.cpp and LM Studio backends | `LICENSE`: MIT, code only. Art excluded, credited to xilia_world00. Fonts OFL | 2026-11-13 | 21 |

Licence flags:

- Mochii is not MIT any more. Only code up to tag 0.4.3 is MIT. Current code is
  PolyForm Noncommercial under a different licensor than the base, so copying it
  would need a second Required Notice. Describing its features is fine.
- Edgerunners has unclear provenance. Code credits name yume_mud and a different
  repo (Capri-cmd/Capri-AI). Its README is largely lachydotmcg's copied. Its
  `assets/readme` images and CapriMainframe-Bold.ttf are lachydotmcg's files. It
  bundles a VB-Cable driver zip and two odd files under `metadata/`. Treat as
  ideas only; copy no code from it.
- The base's Required Notice demands credit to the Capri project for any use of
  its code, art or animations. With placeholder sprites in use, the credit covers
  both code and art until the art is replaced.
- "Capri" and her look are CD PROJEKT RED and Studio Trigger property. Nothing
  Capri-branded belongs in Solin's UI, names or shipped assets long term.

Assets that are not freely reusable:

- Lachy: `assets/sprites`, `assets/emotes` (Qewiw's zips), capri.png, chip*.png,
  perch.png, cursor.png, `assets/readme`, `site/` art, `assets/voice` wavs,
  CapriMainframe-Bold.ttf, the Vocyn starter mods, VB-Cable zip (donationware,
  redistributable only unmodified). Barlow and Share Tech Mono are OFL and
  reusable with their OFL notices. Sprites: placeholder use only, see Decisions.
- Mochii: `assets/capri` (frames cut from the show), `docs/*.png`, icons. Never.
- Edgerunners: `assets/desk` (xilia_world00), `assets/readme` (lachydotmcg's),
  `docs/images`, voice wavs, VB-Cable zip, `metadata/`. Never.

## Step 3: feature audit

### How the base works

(a) Brain. One interface, `IBrainProvider` in `app/Brain.cs`, with a single
`Complete` method taking an OpenAI-style message list, a tools JSON string, a
streaming callback and a purpose tag. The `Brain` class implements it with raw
HTTP calls. Local Ollama uses Ollama's native `/api/chat`. A user-run server
(llama-server, LM Studio, vLLM, TGI) uses `/v1/chat/completions`. OpenAI, Google
Gemini, DeepSeek, NVIDIA, OpenRouter and Kimi all use the OpenAI-compatible chat
endpoint, with Gemini at Google's `v1beta/openai` address. Anthropic uses its
native `/v1/messages`. Provider kind is a string and keys are detected by prefix.
A second implementation, `PlanBrain` in `cross/PlanBrain.cs`, drives ChatGPT or
Claude Code subscriptions through a sandboxed Python child. Personality is a
user-editable `personality.md` with fixed safety lines appended in code; notes
about the user live in `me.md`. No few-shot layout, no mood variable in the prompt.

(b) Animation. No state enum. A `Clip` class in `cross/Pet.cs` names one
horizontal PNG strip (embedded resource) with frame count, fps, drawn height
multiplier and x/y anchor shift. `Pet` holds about 28 Clip fields on Mac (idle,
idle_blink, idle_blink_glitch, idle_mouth, blink, glitch, dangle, walk, dance,
arrive, leave, grin, sleep, wake, perch family, laydown family) and 32 on Windows.
State is booleans (lying, riding, falling, hanging, dancing, seated) plus the
clip currently playing, switched by a Play call. Mirrored poses are separate
`_flipped` PNGs. Talking swaps in a mouth strip. Windows-only emotes are zips of
30 fps frames. Mood drives nothing; reactions are event-driven.

(c) macOS overlay. `Pet` is an Avalonia `Window` subclass with no system
decorations, transparent background and transparency hint, Topmost, hidden from
the taskbar, not resizable. The app runs with `MacOSPlatformOptions.ShowInDock`
false and `LSUIElement` true in `cross/mac/Info.plist`, so it is an agent with no
Dock icon. Other windows' geometry comes from CoreGraphics via P/Invoke in
`MacDesk` (`cross/Desk.cs`). Moving other apps' windows goes through osascript and
System Events, needing Accessibility and Automation permissions. Speech and plan
workers are sandboxed Python children. Minimum macOS 15. CI publishes osx-arm64
and osx-x64 with ad-hoc codesigning.

Mac toolchain state on this machine: macOS 26.6.2 arm64, .NET SDK 7.0.315 only
(the base needs .NET 10), node 22.17.1, python3 3.13.3, Ollama with gemma4:e4b.

### Features the base lacks

"Mac" means testable on the M4 Pro. Effort is for reimplementing in C#/Avalonia.

From Mochii (Electron):

| Feature | Files | What it does | Win-only | Mac | Effort |
|---|---|---|---|---|---|
| Feelings and bond into the prompt | `src/renderer/js/bond.js`, `chat.js` | Events set a short-lived feeling sentence prepended to the prompt; a five-stage attachment score grows daily from capped sources | No | Yes | Low |
| Mood-picked portrait with glitch | `panel.js`, `chat.js` | A mood scorer reads the reply, picks one of eight faces, panel glitches between them | No | Yes | Low–Med |
| Procedural single-cut-out animation | `sprite.js`, `acting.js` | Fragment shader (tilt, sway, squash, smears, glitch slices) plus exposure sheets holding poses N frames; one PNG gives a full acting range | No | Yes | High |
| Local HTTP event bridge | `src/main/bridge.js`, `hooks/capri-hook.js` | 127.0.0.1 server, POST /event JSON, rejects Origin headers, caps body | No | Yes | Low |
| Scripted demo replay | `scripts/demo.js`, `demo-sequence.js` | Replays a fake event sequence to exercise every animation without a brain | No | Yes | Low |
| Synthesised sound cues | `voice.js` | Web Audio blips and chimes, no files; dropped-in files override | No | Yes | Med |
| Health breaks | `breaks.js` | Nudge after two hours without a five-minute idle gap | No | Yes | Low |
| PC-feel reactions | `pc.js`, `main/system.js` | CPU hot, charger, battery, network, device plug | Partly | Partly | Med |
| Throw physics | `capri.js` | Velocity from drag, edge bounce, ledge catch, anger after three throws | No | Yes | Med |
| Window possession, window tracking, PC care, mission report, guide pointing | various | Windows-only or low value for Solin | Yes | No | Skip |

From Edgerunners (PySide6):

| Feature | Files | What it does | Win-only | Mac | Effort |
|---|---|---|---|---|---|
| Small-model persona layout and avoid list | `capri/brain/persona.py`, `llm.py` | Character-sheet turn, in-voice acknowledgement, few example exchanges, real history, short reminder glued to the newest message; overused openers pushed down with logit bias | No | Yes | Low |
| Live-portrait call bubble | `capri/desk/bubble.py` | Chat box shows the current animation frame as a portrait, with signal meter and call timer, reply types in | No | Yes | Med |
| Mini form | `capri/desk/mini.py` | Small sprite that blinks, talks, gets annoyed when poked, looks at the cursor, lies on windows | No | Yes | Med |
| Procedural entrance | `capri/desk/portal.py` | Brackets lock, CRT flicks on, sticker stamps, collapses to a dot, she pours out; all drawn | No | Yes | Med |
| Hold-to-talk local STT | `capri/voice/stt.py`, `hotkey.py` | faster-whisper warmed at boot, decode starts on key release | No | Yes (whisper.cpp on Metal) | High |
| Privacy and focus phrases | `capri/brain/daily.py` | "Leave me alone" or "never look at Discord" set no-look rules without the model | No | Yes | Low–Med |
| Vector memory with JSON fallback | `capri/abilities/memory.py` | Qdrant-backed memory | No | Yes | Backend job, not companion |
| MCP hub, Spotify, weather, clips, launcher, Linux stand-ins, llama-server supervisor | various | Low value or platform-specific | Mixed | Mixed | Skip |

### Combined ranking, value against goals divided by effort

1. Feelings and bond state into the prompt (Mochii). Maps onto Solin's mood/trust.
2. Small-model persona layout with avoid list (Edgerunners). Holds Solin's voice on Ollama.
3. Local HTTP event bridge (Mochii). The transport between backend and companion.
4. Scripted demo replay (Mochii). Tests mood-driven animation before the brain works.
5. Mood-picked portrait (Mochii) merged with the live-portrait bubble (Edgerunners).
6. Health breaks and idle awareness (Mochii).
7. Synthesised sound cues (Mochii).
8. Throw physics (Mochii).
9. Procedural single-cut-out animation (Mochii). Biggest payoff if art is scarce, biggest port.
10. Mini form (Edgerunners).
11. Procedural entrance (Edgerunners).
12. PC-feel reactions, portable subset (Mochii).
13. Privacy and focus phrases (Edgerunners).
14. Local speech-to-text (Edgerunners).

Goals with no reference feature: brain switching already exists in the base's
`Brain.cs` but moves to the backend under this plan. Session identities exist in
no reference and must be designed in Solin's backend.

## Step 4: approved layout

```
Solin/
  core_ai/  micro_model/  connectors/  data/     (untouched)
  server/
    flask_app.py                                  (untouched for now)
    companion_api.py        NEW, later phase, 127.0.0.1 only
                              POST /chat   {session_id, text} -> {visible, internal, mood, audio}
                              GET  /state  current mood, trust, session, brain name
                              GET  /events server-sent events: mood changes, speaking
                                           start/stop, amplitude ticks for the mouth
  companion/                CAPRI-DERIVED FRONT END, PolyForm Noncommercial 1.0.0
    LICENSE.md              verbatim PolyForm NC text plus lachydotmcg's Required Notice lines
    NOTICE.md               provenance: which files descend from lachydotmcg/capri-desktop
                            and at what commit; which placeholder sprites are theirs;
                            ideas described from mochiiwuff and edgerunners-2, no code copied
    README.md               build and run on macOS, permissions, backend protocol
    Solin.Companion.csproj  shaped after cross/Capri.Cross.csproj (net10, Avalonia)
    src/
      Upstream/             files derived from the base, kept close to upstream for diffing
      Solin/                Solin-only C#: SolinBrain (IBrainProvider over HTTP), MoodState,
                            EventClient (SSE), BackendLauncher (spawns the venv's Python)
    assets/
      sprites/              placeholder strips from the base at first, Solin's own later,
                            same strip convention documented in README
      portraits/            one per mood
      fonts/                OFL fonts only, with their OFL notices
  docs/
    capri-audit.md          this file
```

Licence boundaries:

- `companion/` is the derivative work. Its LICENSE.md and NOTICE.md travel with
  it. Nothing under it may be used commercially. Each file derived from the base
  gets a one-line header naming the upstream file.
- Everything outside `companion/` is Solin's own. Nothing from any reference goes
  there. The HTTP API is the clean boundary.
- Placeholder sprites stay under `companion/assets/sprites/` and are listed in
  NOTICE.md. Solin's replacements use the same strip format so the Clip loader
  works unchanged.
- Mochii and Edgerunners contribute descriptions only. If code is ever lifted
  from Mochii it must come from a tag at or before 0.4.3 with the MIT notice, or
  carry mochiiwuff's PolyForm notice. Edgerunners code is not copied at all.
- Mood becomes first-class: a `MoodState` in `src/Solin/` driven by the backend
  over SSE, with a mood-to-clip-family table. Entirely Solin-owned.

## Prerequisites before porting

- Install the .NET 10 SDK (only 7.0.315 is present).
- Expect Accessibility and Automation prompts the first time perching is tried.
- Decide the backend brain config shape in `data/config.json` (currently empty):
  provider kind, model, base URL, key env-var name, with Claude, Gemini, OpenAI
  and Ollama entries.

## Phase 2 working rules (from the brief)

- One feature per branch.
- Before any edit, state the file and the section being changed.
- References folder stays read-only.
