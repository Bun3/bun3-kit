# Changelog

## [0.4.0] - 2026-09-13

- Add explicit SDK activation and removal support for optional audio adapters across runtime, Editor and test assemblies.
- Align audio package dependencies with this release; SDK assets remain separately installed.


## [0.3.0] - 2026-09-12

- Preserve authored SFX minimum distance through the native simulation callback model, with normalized near/far gain, default-one slot reuse and no second attenuation applied to path SH.

- Add pooled native SFX output through the audio 0.2.0 output-owner interface, with cold budgeted PCM preparation, forward pitch, source/mixer envelope preservation and explicit native tail completion.
- Add reusable three-slot path parameter storage, immutable producer leases, immediate output blocking and exclusive generation transition admission.
- Keep reset/disposal behind processing-reader retirement, including source destruction without further callbacks; verify warm processing and per-play ownership reuse allocations.
- Add a default retained context/HRTF renderer factory and explicit dynamic-mesh scene membership ownership.
- Verify native Unity source/listener output, source and mixer gain, pitch frequency/duration, short pooled playback, UI fallback and delayed-reader generation reuse.

## [0.2.2] - 2026-09-12

- Add a bounded control-thread path simulation scope with retained scene/probe references, generation-safe sources and copied native results.
- Bind complete 4.8.1 simulation inputs/outputs and support raycast or fractional volumetric direct occlusion.
- Verify native geometry invalidation/reopening, ownership, x64 layout and zero warm managed allocations.
- Add copied, checksummed native acoustic assets, cold path baking and retained scene/probe loading with exact probe metadata.
- Verify Unity asset persistence, native reload, corruption rejection and simulation lifetime independence.

## [0.2.1] - 2026-09-12

- Expose fixed sample rate and explicit native path-effect tail draining for streaming consumers.

## [0.2.0] - 2026-09-11

### Added

- `SteamAudioPathRenderer`: fixed-frame mono-to-binaural-stereo PCM processing using supplied world-space path coefficients and three-band EQ.
- Exact Steam Audio 4.8.1 path-effect ABI bindings, including `normalizeEQ`, native buffer ownership, retained context/HRTF lifetimes, reset, and deterministic disposal.
- Native PCM tests for direction differences, reset, gain, ownership, layout, validation, and steady-state managed allocations.

## [0.1.0] - 2026-09-04

### Added

- `SteamAudioSoundSetup.Apply(config)`: disables the core occlusion/low-pass
  pipeline (`OcclusionChecksPerFrame = 0`) and chains a per-voice binder onto
  `SoundSystemConfig.OnVoiceConfigured`, preserving any existing hook.
  Idempotent — safe to call more than once on the same config.
- Voice binder: attaches (once per source) and configures a Steam Audio
  `SteamAudioSource` per SFX voice — `AudioSource.spatialize` from
  `SoundDef.Spatial`, and for 3D voices the component's `occlusion` field
  from `SoundDef.Occlusion`.
- Editor spatializer validator (`SteamAudioSetupValidator`): logs a one-time
  warning on domain load if the project's configured Audio spatializer
  plugin isn't "Steam Audio Spatializer". Log-only, never blocks batchmode/CI.
- All runtime and editor assemblies gate on the `STEAMAUDIO_ENABLED`
  scripting define (auto-added by Steam Audio's own `.unitypackage`
  installer), so this package compiles to nothing until Steam Audio is
  imported — safe to keep installed either way.
