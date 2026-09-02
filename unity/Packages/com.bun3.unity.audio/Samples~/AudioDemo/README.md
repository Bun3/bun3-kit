# Audio Demo

A self-contained demo of `SoundSystem` that needs no imported audio assets — every clip
(intro, loop, and SFX blip) is generated procedurally at runtime with `AudioClip.Create`.

## What it demos

- `SoundSystem` construction with the bundled default mixer (zero setup).
- Music: sample-accurate intro→loop handoff, crossfade to a second track, pause/resume.
- SFX: a one-shot with per-play pitch variation (`SoundDef.Pitch`).

## How to run

1. Import this sample via **Package Manager > Bun3 Unity Audio > Samples > Audio Demo**.
2. Drop `AudioDemo` (`Bun3.Unity.Audio.Samples.AudioDemo`) on any GameObject in an empty scene.
3. Enter Play mode. The Console logs the key bindings on start:
   - `1` — play the intro+loop music track.
   - `2` — crossfade to a second (loop-only) track.
   - `3` — play the SFX blip, with pitch varying each press.
   - `P` — pause/resume music.
   - `S` — stop music (1s fade-out).

## What to listen for

Pressing `1` is the ear-verification for the package's sample-accurate DSP seam: the
440Hz intro tone plays once, then hands off to the 330Hz loop tone. Listen at the
boundary for **no click and no gap** — the loop should pick up exactly where the intro
ends, scheduled on the audio DSP clock rather than a per-frame `Update` check.
