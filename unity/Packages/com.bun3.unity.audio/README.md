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
- Music subsystem: sample-accurate intro+loop handoff, crossfade with
  newest-wins channel stealing, pause/resume, and awaitable transitions
  (`PlayMusicAsync`, `StopMusicAsync`).

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
sound.Play(mySoundDef, fadeIn: 0.3f);       // ramps volume from silence

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

### Music

```csharp
// Intro + loop: the intro plays once, then hands off to the loop sample-accurately.
sound.PlayMusic(introLoopDef);              // fade = -1 uses def.DefaultFade
sound.PlayMusic(loopOnlyDef, fade: 0f);     // no intro clip, no fade: starts instantly

// Crossfade: while a track is playing, PlayMusic fades the old one out while the
// new one fades in on the other channel. A third call mid-crossfade steals the
// fading-out channel (newest wins).
sound.PlayMusic(nextTrackDef, fade: 1.5f);

// Awaitable transitions — completes on fade-in end (or immediately if fade is 0).
// Cancelling stops the music and throws OperationCanceledException.
await sound.PlayMusicAsync(introLoopDef, fade: 1.5f);
await sound.StopMusicAsync(fadeOut: 1f);

sound.PauseMusic();
sound.ResumeMusic();   // reschedules a cancelled loop from the intro's remaining time
```

Sound definitions are authored as `SoundDef` assets
(`Assets > Create > Bun3 > Audio > Sound Def`), which hold clips, volume/pitch
ranges, loop/spatial settings, max instances, and cooldown. Music tracks are
authored as `MusicDef` assets (`Assets > Create > Bun3 > Audio > Music Def`),
which hold the optional intro clip, the required loop clip, volume, and the
default fade duration.
