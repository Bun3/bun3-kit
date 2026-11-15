# Changelog

## [0.5.0] - 2026-11-16

### Fixed

- A main-thread stall longer than the written lead no longer shifts the pitch for seconds afterwards. Dissonance paces speech by wall time since its first read and sped playback up to 15% to recover the speech the stall skipped; the ring now consumes that speech instead (`VoiceClipRing.SkipFrames`), so a stall costs a silent gap and playback stays at its normal speed.
- The written lead no longer slows playback down. Dissonance's clock starts with an empty read when the speech is created and the first write waits out the lead, so the clock and the speech written ahead stay level. A start or device-reset resume more than 30 ms late skips the speech it owes.

### Changed

- `LeadSeconds` defaults to 0.1 (was 0.06): stalls up to about 100 ms play without a gap. Each speech starts after the lead, so voice latency is the lead plus the SDK's jitter buffer. 0.4 took the lead out of the jitter buffer instead, keeping latency but shrinking the network-jitter margin by the lead; expect speech about 0.1 s later than 0.4.
- `VoiceClipRing.SpeechFramesPlayed` includes skipped speech. Adds `VoiceClipRing.Start(playFrame, lateFrames)`.

### Added

- Diagnostics on `DissonanceSteamAudioPlayback`: `TotalUnderruns`, `SkippedSeconds`, `PlaybackRate` (the SDK's synchronisation speed) and `LogUnderruns` (a warning per underrun).

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

