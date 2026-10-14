# Changelog

## [0.5.0] - 2026-10-14

### Added

- `ChargeSoundDef` and `SoundSystem.PlayCharge` (2D, fixed position, transform
  follow) returning `ChargeSoundHandle`: a looping layer whose volume and pitch
  follow `SetProgress(0..1)` through `VolumeByProgress`/`PitchByProgress` curves,
  with optional start, complete and cancel one-shots. `Complete`/`Cancel` fade the
  loop out once and return the one-shot's handle; stolen, ended or disposed charges
  do nothing. Progress updates do not allocate.

## [0.4.0] - 2026-09-20

Covers 0.3.1 through 0.4.0.

### Added

- `SoundAcousticSettings`, the shareable `SoundAcousticProfile` asset and
  `SoundAcousticSelection` (shared profile or local settings) as
  `SoundDef.Acoustics` and `SoundSpatialProfile.Acoustics`;
  `SoundDef.EffectiveAcoustics` resolves the active settings. Inspector drawers for
  acoustic selections. `IResolvedSoundAcousticSettings` exposes live resolved
  settings to native playback adapters.
- `SoundSystem.Prepare(SoundDef)` / `Prepare(SoundCatalog)` prewarm cooldown
  bookkeeping so the first cooldown playback does not allocate.
- `SoundDef.PlaybackClips` exposes the clips playback uses (preloaded clips first).

### Changed

- `DistanceAttenuationProfile.CreateLegacyCurve` is now `CreateInverseDistanceCurve`.
- Basic AudioSource playback honors the resolved distance-attenuation setting
  (flat rolloff when disabled).

### Removed

- **Breaking:** per-field acoustic members on `SoundDef` and `SoundSpatialProfile`
  (`MinDistance`, `MaxDistance`, `DistanceAttenuation`, `AttenuationProfile`,
  `InheritSpatialBlend`, `SpatialBlendProfile`, `MonoDistance`,
  `FullSpatialDistance`) and their `Effective*` accessors. Use `Acoustics` /
  `EffectiveAcoustics`.
- **Breaking:** `DistanceAttenuationProfile.MinimumDistance`, `MaximumDistance`
  and `FadeFraction`.

## [0.3.0] - 2026-09-13

### Added

- `SoundCatalog`: ordinal string keys mapped to `SoundDef` assets, with validation
  and allocation-free warm lookup.
- Shared definition profiles referenced from `SoundDef`: `SoundPlaybackProfile`,
  `SoundRoutingProfile`, `SoundConcurrencyProfile` and `SoundSpatialProfile`, plus
  `DistanceAttenuationProfile` and `SpatialBlendProfile` for native adapters.
- Request-local `Play(def, position, spatial, volumeScale)` and
  `ISpatialSoundVoiceOutput` for owners that receive the effective request mode.
- `SoundDef.VolumeGroup` with live `SoundSystemConfig.GroupGain`.
- Add SDK-independent Editor setup commands to explicitly synchronize installed optional adapters or disable them before SDK removal on Standalone, Android, iOS and WebGL.


## [0.2.0] - 2026-09-12

### Added

- Optional prewarmed `ISoundVoiceOutput` owners through `SoundSystemConfig.CreateVoiceOutput`.
  Owners can replace SFX processing and driver configuration while the core retains gain,
  fades and mixer routing. Native output completion can include processing tails.
- Explicit Unsupported/Started/Unavailable start results prevent handled failures from
  falling back to dry clips. Retirement precedes source reuse, stop and disposal; logical
  pitch updates reach the owner without modifying its driver pitch.
- Owned spatial processing bypasses the core's occlusion queries, low-pass and occlusion
  gain. Null/unsupported owners preserve ordinary playback and music behavior.

## [0.1.2] - 2026-09-12

### Added

- `SoundSystemConfig.OnSourceCreated` prepares each pooled SFX source once at
  construction. Callback failure destroys the partial pool before registration.

## [0.1.1] - 2026-09-11

### Added

- Fixed-capacity external playback registrations with generation-safe handles,
  explicit gain, mixer routing and existing low-pass filter ownership, and
  restoration on release or disposal without changing playback state.

### Fixed

- A reentrant stolen-voice completion callback can no longer make an outer
  `Play` call return a handle belonging to the callback's replacement voice.
- A reentrant source-configuration callback can no longer make the outer `Play`
  restart a replacement voice occupying the same pool slot.

## [0.1.0] - 2026-09-04

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
