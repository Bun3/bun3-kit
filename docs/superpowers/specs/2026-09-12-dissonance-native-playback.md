# Dissonance native stereo playback ownership

Status: reusable playback seam implemented and validated; application wiring is tracked separately.

The optional combined Dissonance/Steam Audio package supplies a fixed-frame stereo pump first. It consumes actual public `SpeechSession.Read` output and owns a `SteamAudioPathRenderer`. It does not simulate paths, capture a microphone, transport packets, or establish end-to-end latency.

## SDK constraints

Inspected installed Dissonance 9.0.9 `BaseVoicePlayback`, `SpeechSession`, `SpeechSessionStream`, `DecoderPipeline`, and `PlaybackPool` in the isolated validation project.

- `SpeechSession.Read` returns true on completion, contrary to its XML comment; `SamplePlaybackComponent.Filter` and `DecoderPipeline.Read` establish the actual contract. A completed read returns the decoder to its pool. Never read or query that session again afterward.
- `BaseVoicePlayback.TryDequeueSession(rate)` performs SDK activation-delay checks and prepares its decoder on the main thread. Preserve this machinery.
- The explicit SDK `ForceReset` implementation resets the decoder before invoking the protected override. Decoder reset is not synchronized with reading. A direct subclass reading on the audio thread cannot insert a pre-reset retirement barrier.
- `PlaybackPool` accepts `IVoicePlaybackInternal`, so a pool-facing facade can forward to a separate `BaseVoicePlayback` decoder host. It gates reset, ownership reassignment and codec changes until retirement acknowledgement. Pending-control packet delivery uses an explicit drop contract; lossless delivery is not assumed.

## Pump contract

Each pump is one immutable session generation. Construction receives a prepared session, sample-rate-matched renderer, copied SH coefficients and listener coordinates. Path EQ and additional gain are initially unity. Main-thread creation preallocates mono/stereo buffers. One audio reader adapts arbitrary even stereo callback lengths to the renderer frame size. Decoder completion switches to native tail drain, then silence. Disabled or retired generations cannot restart.

An atomic state combines a retired bit with a reader claim. Retirement closes entry atomically, including readers delayed before their claim. The main thread may reclaim when retired and unclaimed; no future audio callback is needed. Disposal never waits on the audio thread, and never frees native state while a reader holds it. An in-flight callback clears its output if it observes retirement before returning. Source stop/disable must request retirement first; source/clip replacement and SDK reset occur only after acknowledgement.

Sample-rate or renderer-format changes require a new generation after retirement. No rate mutation occurs on an active decoder. The later component will own its stereo clip/source and bypass Unity spatialization/distance attenuation for already rendered stereo. Native path coefficients must be copied from a simulation owner with proven synchronization, never retained SDK native pointers.

## Pool-facing facade stage

`DissonanceSteamAudioPlayback` implements the SDK pool interface and owns one AudioSource and a stereo streaming clip. `Configure` receives a renderer factory `(sampleRate, frameSize)`, copied initial coefficients and listener coordinates. The active generation exposes a live parameter mailbox. The application must configure every pooled instance before accepting voice; this is not automatic game bootstrap.

A separate hidden `DissonanceDecoderHost` derives from `BaseVoicePlayback`, lives outside the facade hierarchy and survives scene unload until its owner releases it and any reader acknowledges retirement. Its main-thread Update can reclaim an idle retired pump even after the source stops and the facade is disabled or destroyed. Then it may invoke the SDK reset and destroy itself. No blocking waits or native disposal occur inside an audio callback.

Disable/reset/reassignment stops accepting packets, retires the pump before source stop, and defers decoder reset until reclamation. Start/Stop/packets arriving during this transition are dropped, not buffered. A subsequent fresh StartPlayback is required; lossless handoff is not promised. Normal input completion reclaims only the finished pump and preserves queued SDK sessions. Device-rate changes retire the old generation and replace the clip; old-rate clips are never reused at a new rate.

The factory creates matching native resources on the control thread. Audio callbacks use only their already published generation. Existing SDK decoder gain/priority processing remains upstream of native rendering; the output source uses no additional distance attenuation or spatializer. End-to-end microphone transport and moving simulation parameters remain separate integration stages.

Natural completion freezes the submitted PCM frame index after the final native tail. A bind-once source-filter monitor counts downstream frames into the same immutable managed generation. The facade retains speaking/priority state until delivery passes that index plus one measured DSP output block, then stops and releases the clip. The block accounts for the source-filter boundary; it is not a network latency guarantee. Cancellation still retires immediately. Old monitor callbacks cannot advance a replacement generation's completion count.

## Live publication

The lower native package owns one three-slot `PathParameterMailbox` implementation. Its immutable `PathParameterLease` captures a generation token, allowing pooled SFX storage to be reused without granting stale producers ownership. The Dissonance mailbox preserves its original public API by forwarding to this shared implementation. One control writer copies SH, EQ, listener, gain and normalization into a slot that is neither published nor reader-pinned. The single audio owner pins and rechecks publication once, then copies into fixed audio-owned storage at a native frame boundary. A racing publication may keep the previous coherent snapshot rather than blocking. Concurrent readers are rejected.

Retirement permanently closes the generation's output gate; storage reuse requires reader quiescence and a strictly newer token. Immediate blocking is independent of snapshot publication and is checked after queued PCM in the final source monitor. `StartBlocked` is applied before creating the streaming clip. Gate changes do not pause decoder progress or create a new speech session. Coverage validation remains application-owned; finite SH values do not prove valid endpoint coverage. Path gain excludes existing SDK/bus volume and SH distance attenuation.

## Validation sequence

The last-sample impulse test exposed terminal input overlap in the exact native implementation: immediate `GetTail` returned only rounding-level energy (2.7e-15). [Valve's v4.8.1 convolution source](https://github.com/ValveSoftware/steam-audio/blob/v4.8.1/core/src/core/overlap_add_convolution_effect.cpp) retains one quarter-frame of dry input behind a Tukey window; `tail` drains only the already convolved overlap. The bridge therefore submits one final zero-input frame using the last path parameters, then invokes native `GetTail` until complete. This preserves final-window samples without weakening the impulse-energy assertion. This explicit flush is part of reported remaining processing samples.

1. Missing-pump assertion RED in the actual SDK host.
2. Real SDK Identity-codec packets through `IVoicePlaybackInternal` and `TryDequeueSession`, then decoded PCM through the native stereo pump; finite nonzero stereo and completed silence.
3. Retirement before a first callback reclaims immediately and delayed callbacks write silence.
4. Native tails and fixed-frame adaptation; reader/retirement overlap tests before facade integration.
5. Facade tests passed for actual Unity callback processing, held-reader reset/reassignment, and destruction while reading. An independent downstream source-filter short-burst assertion first failed with zero signal despite nonzero prepared PCM; waiting for submitted frames to drain made that assertion pass. Final regression adds late-monitor generation isolation and automatic release after natural completion.
6. The facade/pump regression passed 9/9; mailbox and live integration then passed 15/15. The final 185-test PlayMode regression passed with absolute-amplitude gate assertions, shared lower-storage reuse and deterministic delayed-reader admission coverage. A paused real reader first reproduced entry into a replacement generation; exclusive transition admission and a post-claim owner check made that regression pass.
