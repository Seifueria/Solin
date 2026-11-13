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
poses are separate files with a `_flipped` suffix. Placeholder strips from the
base follow this already, so Solin's own art just has to match it.

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
| GET /events | server-sent events: mood, reply, speaking |

Mood changes arriving on `/events` shape her idle: breath and tilt pace, and
how often she blinks (see `src/Solin/MoodState.cs`).

## Build and run on macOS

Requires the .NET 10 SDK.

```
cd companion
dotnet build
dotnet run
```

The first time she perches on another app's window macOS will ask for
Accessibility and Automation permission.
