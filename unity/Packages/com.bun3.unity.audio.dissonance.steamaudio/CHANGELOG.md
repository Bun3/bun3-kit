# Changelog

## [0.4.2] - 2026-11-13

### Fixed

- Closing a playback gate (`Parameters.TrySetBlocked(..., true)`) silences speech already written ahead in the ring clip beyond one DSP buffer, instead of letting the written lead (about 60 ms plus a frame) play out. Rejected rooms stop within about a frame plus one DSP buffer, as with the 0.3 audio-thread gate. Adds `VoiceClipRing.SilenceAhead`.

## [0.4.1] - 2026-11-13

### Fixed

- An audio device reset (`AudioSettings.Reset`, output device change) no longer silences the rest of an utterance in progress. When the ring clip's source stops or the clip is emptied, `DissonanceSteamAudioPlayback` resumes the same speech generation on a fresh ring, losing at most the written lead. A disabled source or a changed output sample rate/DSP buffer size still retires the speech until the next utterance.

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

