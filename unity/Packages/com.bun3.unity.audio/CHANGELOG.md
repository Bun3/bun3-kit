# Changelog

## [0.1.0] - Unreleased

### Added

- Initial core: `SoundSystem` with a prewarmed `AudioSource` pool driven by a
  single player-loop tick (no MonoBehaviours, no coroutines).
- Generation-validated `SoundHandle` — stale handles no-op instead of throwing.
  `SoundHandle` exposes `IsValid` only; a speculative `IsPlaying` alias was
  removed before this package's first release.
- `SoundSystem.SetCompletionCallback`: registers a callback invoked once when
  a voice ends (natural end, fade-out, steal, Stop, or dispose), with the
  original (now-stale) handle.
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
  `OcclusionChecksPerFrame = 0` disables the pipeline entirely (no raycasts,
  no filters) — the off switch for external spatializer adapters.
- `SoundSystemConfig.OnVoiceConfigured`: per-play hook invoked after an SFX
  source is fully configured, just before `Play` — lets adapters (e.g. the
  Steam Audio package) attach and configure components on the pooled
  `AudioSource`.
- `PitchWithTimescale` (SFX pitch scaled by `Time.timeScale`) and
  `SoundSystem.TransitionTo` (thin `AudioMixerSnapshot.TransitionTo` wrapper).
- Bundled default `AudioMixer` (`Bun3DefaultAudioMixer`, groups `Music`/`SFX`/
  `Voice`, params `MasterVolume`/`MusicVolume`/`SfxVolume`/`VoiceVolume`,
  snapshots `Normal`/`Paused`) as a zero-setup fallback.
- `SoundDef.AddressableClips` (requires `com.unity.addressables`, otherwise
  compiles out entirely): `SoundSystem.PreloadAsync`/`IsPreloaded`/
  `ReleasePreloaded` load and release Addressable clips ahead of `Play`;
  unpreloaded defs play nothing, and load failures silent-skip with a
  development-build warning.
- `SoundDef` inspector Preview/Stop buttons (new `Editor` assembly), reached via
  reflection over `UnityEditor.AudioUtil` with a graceful no-throw fallback when
  the expected methods are missing.
- `Samples~/AudioDemo`: an asset-free demo (procedurally-generated intro/loop/SFX
  clips) that ear-verifies the intro-loop DSP seam and exercises crossfade,
  pause/resume, and pitch-varied SFX.
