# Changelog

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
