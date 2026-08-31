# Bun3 Unity Audio

A lightweight sound manager for Unity: a prewarmed `AudioSource` pool driven by a
single player-loop tick. No MonoBehaviours, no coroutines, no per-play GC
allocation on the hot play/tick path.

Features:

- Generation-validated `SoundHandle` — safe to hold after a voice ends or its
  slot is stolen; every member no-ops on a stale handle instead of throwing.
- Coroutine-free fades (fade-in on play, fade-out on stop).
- Per-`SoundDef` voice limits (oldest steals when exceeded), cooldowns, and
  pitch/volume variation.
- 2D, fixed-position, and transform-following 3D playback.
- Logical channel volumes (`Master` / `Music` / `Sfx` / `Voice`) via an
  `AudioMixer`.
- Optional UniTask-based awaiting (`PlayAsync`, `SoundHandle.WaitAsync`).

## Install

This package lives at `unity/Packages/com.bun3.unity.audio` and is consumed as
an embedded UPM package (referenced by path in `manifest.json`). It depends on
`com.bun3.unity.core` and `com.cysharp.unitask`.

## Usage

```csharp
using Bun3.Unity.Audio;
using UnityEngine;

// Create once (e.g. in a bootstrap script) and keep it alive for the app lifetime.
var sound = new SoundSystem(new SoundSystemConfig
{
    Mixer = myMixer,
    SfxGroup = mySfxGroup,
    SfxVoices = 24,
});

// Fire-and-forget playback.
SoundHandle handle = sound.Play(mySoundDef);
sound.Play(mySoundDef, worldPosition);      // fixed 3D position
sound.Play(mySoundDef, followTransform);    // tracks a Transform every frame

// Stop, optionally fading out.
handle.Stop(fadeOut: 0.2f);

// Channel volumes (linear 0..1), persisted by the game.
sound.SetChannelVolume(SoundChannel.Music, 0.5f);
var current = sound.GetChannelVolume(SoundChannel.Music);

// UniTask awaiting — completes on natural end, steal, or Stop.
await sound.PlayAsync(mySoundDef);
await handle.WaitAsync();

// Tear down (stops all voices, unregisters the tick).
sound.Dispose();
```

Sound definitions are authored as `SoundDef` assets
(`Assets > Create > Bun3 > Audio > Sound Def`), which hold clips, volume/pitch
ranges, loop/spatial settings, max instances, and cooldown.
