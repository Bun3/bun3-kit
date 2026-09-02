# Changelog

## [0.1.0] - Unreleased

### Added

- Initial core: `SoundSystem` with a prewarmed `AudioSource` pool driven by a
  single player-loop tick (no MonoBehaviours, no coroutines).
- Generation-validated `SoundHandle` — stale handles no-op instead of throwing.
- Coroutine-free fade-in/fade-out.
- Per-`SoundDef` voice limits (oldest steals when exceeded) and cooldowns.
- Pitch/volume variation via rolled `FloatRange`s.
- 2D, fixed-position, and transform-following 3D playback.
- Logical channel volumes (`Master`/`Music`/`Sfx`/`Voice`) via `AudioMixer`.
- UniTask-based playback awaiting (`PlayAsync`, `SoundHandle.WaitAsync`/`StopAsync`).
- Zero-allocation hot play/tick path (asserted by a PlayMode test).
- Isolated variation RNG (private `System.Random`, optional `RandomSeed`) — never
  consumes `UnityEngine.Random` state, so seeded gameplay stays deterministic.
- Music subsystem: sample-accurate intro+loop via `PlayScheduled`, crossfade with
  newest-wins channel stealing, pause/resume with loop-schedule recomputation,
  and awaitable transitions (`PlayMusicAsync`/`StopMusicAsync`).
- Occlusion: pluggable `IOcclusionProvider` (built-in raycast default),
  round-robin per-frame evaluation, smoothed volume attenuation and low-pass
  filtering via `SoundDef.Occlusion` and `SoundSystemConfig` tuning.
- `PitchWithTimescale` (SFX pitch scaled by `Time.timeScale`) and
  `SoundSystem.TransitionTo` (thin `AudioMixerSnapshot.TransitionTo` wrapper).
- Bundled default `AudioMixer` (`Bun3DefaultAudioMixer`, groups `Music`/`SFX`/
  `Voice`, params `MasterVolume`/`MusicVolume`/`SfxVolume`/`VoiceVolume`,
  snapshots `Normal`/`Paused`) as a zero-setup fallback.
