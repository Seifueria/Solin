# Provenance notice for companion/

Everything under `companion/` is licensed under the PolyForm Noncommercial
License 1.0.0 (see LICENSE.md) and carries the Required Notice lines of the
Capri project. Nothing in this folder may be used for a commercial purpose.
Everything outside `companion/` in this repository is Solin's own work and is
not covered by this notice.

## Upstream

Derived from lachydotmcg/capri-desktop
<https://github.com/lachydotmcg/capri-desktop>, version 0.9.0,
commit 26224db16484b51a894f96682d366c4a7a89db26 (2026-11-13).

## Files derived from the upstream base

Each file under `src/Upstream/` begins with a one-line header naming the
upstream file it descends from. The list below is kept current as files
are added.

| companion file | upstream file | notes |
|---|---|---|
| src/Upstream/Clip.cs | cross/Pet.cs, class Clip | legacy cells, hologram and threaded flip loading removed |
| src/Upstream/Body.cs | cross/Pet.cs, class Body | hologram and sleeping z's removed |
| src/Upstream/SpriteWindow.cs | cross/Pet.cs, class Pet | window setup, Play, idle/blink part of Advance, drag handlers only |

## Placeholder art

Capri's sprite strips may be used under `assets/sprites/` as temporary
placeholders while Solin's own art is produced. They are the work of Capri's
artist and belong to the Capri project. Any build that contains them must keep
this notice. The list below is kept current.

| file | source | status |
|---|---|---|
| assets/sprites/idle.png | lachydotmcg/capri-desktop assets/sprites | placeholder, replace with Solin art |
| assets/sprites/idle_blink.png | same | placeholder |
| assets/sprites/idle_blink_glitch.png | same | placeholder |
| assets/sprites/idle_mouth.png | same | placeholder |
| assets/sprites/glitch.png | same | placeholder, used for teleports |
| assets/sprites/dangle.png | same | placeholder, held and flying |

## Ideas described, no code copied

Feature ideas were taken from reading these projects. No code or art from
them is included.

- mochiiwuff/capri-desktop (PolyForm Noncommercial 1.0.0 from 0.4.4; MIT up to 0.4.3)
- edgerunners-2/capri-ai-desktop (MIT, code only)

## Solin additions

Files under `src/Solin/` are written for Solin by Kaelen (Seifueria). Because
they live inside the derivative work they are distributed under the same
PolyForm Noncommercial terms.
