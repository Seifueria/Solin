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

## Backend protocol (planned)

The companion talks to `server/companion_api.py` on 127.0.0.1 only.

| call | purpose |
|---|---|
| POST /chat `{session_id, text}` | returns `{visible, internal, mood, audio}` |
| GET /state | current mood, trust, session, brain name |
| GET /events | server-sent events: mood changes, speaking start/stop, mouth amplitude |

## Build and run on macOS

Requires the .NET 10 SDK.

```
cd companion
dotnet build
dotnet run
```

The first time she perches on another app's window macOS will ask for
Accessibility and Automation permission.
