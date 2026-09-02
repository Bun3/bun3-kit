# Changelog

## [0.1.0] - Unreleased

### Added

- `SteamAudioSoundSetup.Apply(config)`: disables the core occlusion/low-pass
  pipeline (`OcclusionChecksPerFrame = 0`) and chains a per-voice binder onto
  `SoundSystemConfig.OnVoiceConfigured`, preserving any existing hook.
  Idempotent — safe to call more than once on the same config.
- Voice binder: attaches (once per source) and configures a Steam Audio
  `SteamAudioSource` per SFX voice — `AudioSource.spatialize` from
  `SoundDef.Spatial`, and for 3D voices the component's `occlusion` field
  from `SoundDef.Occlusion`.
