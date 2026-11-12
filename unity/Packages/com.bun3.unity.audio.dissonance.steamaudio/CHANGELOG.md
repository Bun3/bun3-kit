# Changelog

## [0.4.0] - 2026-11-12

### Changed

- `DissonanceSteamAudioPlayback` renders speech on the main thread and writes it ahead of the play position into a looping ring clip (`VoiceClipRing`, `LeadSeconds` default 0.06), instead of reading it in a managed `OnAudioFilterRead` monitor. No managed code runs on the audio thread, so a GC no longer stalls the mixer. Speech plays about 60 ms later, gate changes take effect after the written lead, and a stalled main thread plays silence for voice only.

### Removed

- `DissonanceOutputMonitor`.

## [0.3.1] - 2026-09-23

- Wait for native stereo output as well as decoded amplitude in playback integration tests, avoiding assertions during an unfinished audio callback. Runtime behavior is unchanged.

## [0.2.0] - 2026-09-13

- Add explicit SDK activation and removal support for optional audio adapters across runtime, Editor and test assemblies.
- Align audio package dependencies with this release; SDK assets remain separately installed.

