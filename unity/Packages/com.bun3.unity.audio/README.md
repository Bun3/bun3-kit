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
- Occlusion: round-robin per-frame evaluation with a pluggable
  `IOcclusionProvider` (built-in single-linecast default), smoothed
  volume attenuation and low-pass filtering.
- Optional timescale-scaled SFX pitch (`PitchWithTimescale`) and a thin
  `TransitionTo` wrapper over `AudioMixerSnapshot` transitions.
- Bundled default `AudioMixer` (`Bun3DefaultAudioMixer`) so channel volumes
  and routing work with zero mixer setup.

## Install

This package lives at `unity/Packages/com.bun3.unity.audio` and is consumed as
an embedded UPM package (referenced by path in `manifest.json`). It depends on
`com.bun3.unity.core` and `com.cysharp.unitask`.

Using [Steam Audio](https://valvesoftware.github.io/steam-audio/) for
spatialization/occlusion? See the optional
[`com.bun3.unity.audio.steamaudio`](../com.bun3.unity.audio.steamaudio) adapter.

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

### Occlusion

```csharp
// Enable per SoundDef; only 3D sounds (Positional/Follow) are evaluated.
mySoundDef.Occlusion = true;

var sound = new SoundSystem(new SoundSystemConfig
{
    SfxVoices = 24,
    Listener = playerHead,                 // null finds the scene AudioListener
    OcclusionMask = wallsLayerMask,         // layers the built-in raycast provider tests against
    OcclusionChecksPerFrame = 4,            // round-robin budget; not every voice re-checked each frame
    OcclusionMuffledCutoffHz = 1200f,       // low-pass cutoff at full occlusion (22000 = open)
    OcclusionVolumeAtFull = 0.35f,          // volume multiplier at full occlusion
    OcclusionSmoothingSeconds = 0.15f,      // seconds for the occlusion factor to travel 0->1
});
```

The default provider (`RaycastOcclusionProvider`) does a single
`Physics.Linecast` from listener to source (binary blocked/clear). Supply a
custom strategy via `SoundSystemConfig.OcclusionProvider` — implement
`IOcclusionProvider.Evaluate(in Vector3 listenerPos, in Vector3 sourcePos)`
returning 0 (open) .. 1 (fully occluded); it is called from the tick on the
round-robin budget above, so implementations must not allocate.

### Timescale and mixer snapshots

```csharp
// Scales SFX voice pitch by Time.timeScale (slow-motion); music is unaffected.
var sound = new SoundSystem(new SoundSystemConfig { SfxVoices = 24, PitchWithTimescale = true });

// Thin wrapper over AudioMixerSnapshot.TransitionTo; no-op on a null snapshot.
sound.TransitionTo(pausedSnapshot, seconds: 0.3f);
```

Non-loop voice completion follows playback progress (pitch × timescale), not
real time, so a one-shot played at a low `Time.timeScale` plays out its full
audio instead of being cut short. At `Time.timeScale = 0` SFX freeze in
place — audio and lifetime both stop advancing — and resume intact once
timeScale recovers. `Time.timeScale = 0` is still **not** the recommended
pause path — use `AudioListener.pause` or the bundled `Paused` snapshot
instead, since a sound started while timeScale is 0 begins silent/frozen
rather than paused.

### Bundled default mixer

Games that never assign `SoundSystemConfig.Mixer` fall back to the package's
own `Bun3DefaultAudioMixer` (loaded from `Resources`), so channel volumes and
group routing work out of the box:

- Groups: `Music`, `SFX`, `Voice` (matched by `AudioMixer.FindMatchingGroups`;
  `SfxGroup`/`MusicGroup` are populated in place on the config when left null).
- Exposed parameters: `MasterVolume`, `MusicVolume`, `SfxVolume`, `VoiceVolume`
  (used by `SetChannelVolume`/`GetChannelVolume`).
- Snapshots: `Normal`, `Paused`.
- The bundled mixer does **not** include ducking or low-pass effects — add
  those in the Editor's Audio Mixer window if your game needs them.
- Snapshot ducking routes through an unexposed `Mix` stage (`Master` → `Mix`
  → `Music`/`SFX`/`Voice`): the bundled `Paused` snapshot lowers `Mix`, never
  an exposed channel parameter, so `SetChannelVolume` and
  `TransitionTo(Paused)` are independent by design — turning a volume slider
  can never disarm pause ducking. The general Unity caveat still applies to
  custom mixers you build yourself: `AudioMixer.SetFloat` on an *exposed*
  parameter permanently takes it out of snapshot control until
  `AudioMixer.ClearFloat` is called.

Sound definitions are authored as `SoundDef` assets
(`Assets > Create > Bun3 > Audio > Sound Def`), which hold clips, volume/pitch
ranges, loop/spatial settings, max instances, and cooldown. Music tracks are
authored as `MusicDef` assets (`Assets > Create > Bun3 > Audio > Music Def`),
which hold the optional intro clip, the required loop clip, volume, and the
default fade duration.
