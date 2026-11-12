# Changelog

## [0.7.0] - 2026-11-12

### Changed

- Native pooled SFX output now renders through the Steam Audio Unity spatializer instead of a managed `OnAudioFilterRead` driver, so no managed code runs on the audio thread and a stop-the-world GC no longer stalls the mixer (music included). Unity plays the authored clip; `SteamAudioSoundOutput` only writes spatializer parameters on the main thread.
- `SteamAudioSoundSetup.ApplyNativeOutput(config, acousticOutputs)` drops the decoded-PCM budget and the `PrepareDefinition` hook, and logs the detected spatializer support once.
- Mono clips get a √2 pathing gain because Unity feeds them into the spatializer at 1/√2 per channel; positional loudness matches the previous renderer.
- `SteamAudioSetupValidator` also warns when no spatializer plugin is selected.

### Added

- `Native~/steam-audio-4.8.1-unity-pathing-width.patch` and its build in `build_diagnostics.py`: a Bun3 `audioplugin_phonon.dll` with spatializer parameter 34, pathing stereo width (near-field mono). The stock plugin still works without near-field mono.
- `SteamAudioSpatializer.Detect`/`Classify` (`Missing`, `Stock`, `Patched`) and `SteamAudioSpatializerParams`.
- `SteamAudioSoundOutput.SetSimulationSource`, `SetPath`, `Block`, `IsActive`, `IsBlocked`, `HasSimulationSource`, `Generation`.
- `SteamAudioGcStallTests` (explicit; run alone in a fresh process).

### Removed

- `SteamAudioClipCache`, `SteamAudioSoundRuntime`, the managed SFX driver and retirement host, `SteamAudioSoundOutput.CurrentParameters`/`Fault`, and the `ClipNotPrepared`, `OutputFormatChanged`, `UnsupportedOutputChannels` and `ProcessingFailed` failures.

### Migration

1. Set **Project Settings > Audio > Spatializer Plugin** to **Steam Audio Spatializer**.
2. Build and install the patched `audioplugin_phonon.dll` (`Native~/README.md`) for near-field mono.
3. Call `ApplyNativeOutput(config, acousticOutputs)` without the byte budget; remove any decoded-cache setting.

## [0.6.1] - 2026-11-05

### Fixed

- `PlanarAcousticSessionScope` no longer emits the Unity 6000.5 `FindObjectsByType(FindObjectsSortMode)` deprecation warning when it looks for the active `AudioListener`; earlier editors keep the old call.

## [0.6.0] - 2026-11-02

### Added

- `SteamAudioSoundSetup.ApplyNativeOutput(config, maximumDecodedBytes, acousticOutputs)` assembles native pooled SFX output in one call before `SoundSystem.Initialize`: it disables core occlusion, gives every pooled voice a `SteamAudioSoundOutput` from one shared runtime and clip cache, decodes positional clips through `SoundSystemConfig.PrepareDefinition`, registers one `PlanarAcousticSfxBinding` per voice, and releases all of it through `SoundSystemConfig.OnDisposed` so the same config can initialize again. It is separate from `Apply`, which attaches the SDK's `SteamAudioSource`.
- Native SFX outputs handle audio device changes (`AudioSettings.OnAudioConfigurationChanged`, including `AudioSettings.Reset`) alongside the core recovery and rebuild only what was lost. When the shared driver clip lost its data, it is recreated and active positional requests end with `SteamAudioSoundFailure.OutputFormatChanged`; the HRTF and renderers stay. When the sample rate or DSP frame size changed, one new HRTF replaces the old one and each output renderer is swapped as soon as no audio reader holds it. Positional playback then works again without rebuilding the voice pool, re-decoding clips or reinitializing the `SoundSystem`, and the subscription is released with the `SoundSystem`.

### Changed

- Pooled native SFX outputs created from one internal runtime share a single Steam Audio context and HRTF, one looping driver clip and one hidden retirement host instead of creating them per output. Disposing an output leaves the shared resources to the runtime; the public `SteamAudioSoundOutput` constructor keeps its behavior with a private runtime released with the output.
- Require `com.bun3.unity.audio` 0.7.0.

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
