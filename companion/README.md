# Solin companion

Desktop body for Solin: a transparent, always-on-top sprite window for macOS
built with .NET 10 and Avalonia, derived from the Capri desktop companion.
The brain, personality, memory and voice live in Solin's Python backend; this
app only draws her and relays what the backend says.

Licence: PolyForm Noncommercial 1.0.0, see LICENSE.md and NOTICE.md.

## Layout

```
companion/
  Solin.Companion.csproj   project file
  src/Upstream/            code derived from lachydotmcg/capri-desktop
  src/Solin/               Solin-only code (backend client, mood state, entry point)
  assets/sprites/          horizontal PNG strips, one clip per file
  assets/portraits/        one face per mood
  assets/fonts/            OFL fonts and their notices
```

## Sprite strip convention

A clip is one PNG holding its frames side by side at a fixed height. Frame
count and frames per second are declared in code, not in the file. Mirrored
poses are separate files with a `_flipped` suffix. Cells are cut to the
figure's outline, not square, and a per-clip shift in code says where the
cell's middle sits relative to the body's square.

Current clips, with the exact sizes Solin's own art must match to drop in:

| file | cells | fps | cell size (px) | strip size (px) | drawn over |
|---|---|---|---|---|---|
| idle.png | 60 | 24 | 116 x 384 | 6960 x 384 | the standing loop |
| idle_blink.png | 8 | 24 | 116 x 384 | 928 x 384 | cells 25 to 32 of idle |
| idle_blink_glitch.png | 8 | 24 | 116 x 384 | 928 x 384 | same cells, glitchy |
| idle_mouth.png | 5 | 12 | 116 x 384 | 580 x 384 | the idle face, while talking |

All four are transparent PNGs. The blink and mouth strips hold only the eye
or mouth region on an otherwise clear cell, placed where it sits in the idle
cell, so they lay over the idle frame. Alpha above 24 counts as solid for
click hit testing. Height can change as long as every strip in a set agrees;
the drawn size is set in code, not by the pixels.

## Backend protocol

The companion talks to `server/companion_api.py` on 127.0.0.1 only
(port 47831, or `SOLIN_COMPANION_PORT`). If nothing answers, the companion
starts it itself from the repo root with `python3 -m server.companion_api`
(or `SOLIN_PYTHON`, or `venv/bin/python` when present).

| call | purpose |
|---|---|
| GET /health | `{ok, brain, version}` |
| GET /state | current mood, trust, session, brain name |
| POST /chat `{session_id, text, speak}` | `{visible, internal, mood, trust, audio, error}` |
| POST /event `{session_id, kind}` | companion happenings: poked, petted, dragged, ignored, greeted |
| GET /events | server-sent events: mood, reply, speaking, brain |
| GET /brains | configured brains, which can run, the current one |
| POST /brain `{name}` | switch the live brain |

Mood changes arriving on `/events` shape her idle: breath and tilt pace, and
how often she blinks (see `src/Solin/MoodState.cs`).

## Using her

| do | she |
|---|---|
| click | dips, tells the backend she was poked |
| drag | hangs from the cursor; the backend hears she was dragged |
| throw (let go mid-swing) | flies, bounces off the screen edges, lands; the third throw in a row she takes personally |
| double-click | opens a typing box under her feet. Enter sends, Escape closes |
| reply arrives | types into a bubble above her head, mouth moving, then fades |

## Her voice

Replies are spoken through ElevenLabs when `data/config.json` says so:

```json
{ "voice": { "speak": true, "voice_id": "cgSgspJ2msm6clMCkdW9", "model_id": "eleven_v3" } }
```

The backend makes the MP3 (`core_ai/speech.py`, cached under
`data/audio_file/`) and the companion plays it with afplay, moving her mouth
meanwhile. The default voice is a premade one; the cloned voice in
`core_ai/voice.py` needs a paid ElevenLabs plan, so put its id here once the
plan allows it.

## Watching Claude Code

With the hook installed she acts out what Claude Code is doing. A short pill
over her head shows the current tool. When Claude needs you she teleports
next to your cursor and bristles until you answer. When a turn finishes she
teleports to the middle of the screen, bows, and reads out the last line.
An error makes her shake it off.

Install the hook once (it only adds Solin's lines to ~/.claude/settings.json):

```
python3 hooks/install_hook.py          # or --remove
```

The hook forwards event names, tool names and a one-line gist to the local
API. File contents and tool output never leave the machine.

## Build and run on macOS

Requires the .NET 10 SDK.

```
cd companion
dotnet build
dotnet run
```

The first time she perches on another app's window macOS will ask for
Accessibility and Automation permission.
