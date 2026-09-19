# Dissonance output bridge

Requires a separately installed Dissonance SDK providing the `DissonanceVoip` assembly. Validated against Dissonance 9.0.9. The audio core remains independent; disable adapters before removing SDK assets as described below.

Place `DissonanceOutputBridge` beside `VoicePlayback`, `SamplePlaybackComponent` and `AudioSource` on the playback prefab, before the SDK caches subscribers in `Start`. Bind each instantiated bridge to an application-owned `ExternalAudioRegistry` and `ExternalAudioSettings`. Disable restores explicitly owned source controls; enable registers a fresh lifetime using the original settings. Call `Unbind` before disposing an application registry if playback objects will remain available for reuse. All control APIs are main-thread only.

Use `overrideMixerGroup: true` with your Voice bus. Apply user Voice/Master volume in the mixer once. Additional source gain and an existing low-pass filter are optional explicit ownership settings. The bridge does not call Play/Stop or change SDK-owned clip, pitch, loop, mute, spatialBlend, spatialize or transform.

`TryGetReadiness` checks actual components and registration; it does not use an SDK folder path and does not verify native codec availability or network/microphone readiness. `ReceivedSamples` reports mono PCM observed during the current registered lifetime. The subscriber does not modify PCM, allocate on the audio callback, or retain the SDK buffer. A new diagnostic state on each registration isolates in-flight callbacks from a previous lifetime.

This is a basic mixer/gain/filter bridge. It does not enable Steam Audio voice pathing. The SDK disables its spatializer and its subscriber exposes mono PCM before channel stretching; binaural/multichannel native rendering requires a separately validated output implementation.

## Microphone activity

`DissonanceVoiceActivityScope(comms)` subscribes explicitly to SDK voice activation. Construct, call `Poll()` and dispose on the main thread. The SDK may call its listener on a worker or synchronously during subscription changes; the scope publishes those callbacks atomically without Unity API calls or callback allocations. Disposal closes publication before unsubscribing, including late callbacks.

`Poll()` returns `IsSpeechActive` and cumulative `ActivationCount`. Compare counts across polls to detect speech that started and stopped between samples. Duplicate starts do not increment the count; polling does not consume it. The count saturates at 2^61 - 1 and remains available after disposal, when active is always false. It preserves occurrence, not transition timestamps or duration.

This is raw VAD information. It does not grant transmission permission, prove channels or packets are active, emit network reports, or derive intensity/radius. Local `VoicePlayerState.IsSpeaking` reflects open channels and is not a substitute. Mixer or listener playback muting does not suppress this activity. Subscribe to an existing SDK instance and dispose the scope with its owner; the scope creates no microphone or SDK objects.

## Room ownership

`DissonanceRoomScope(comms)` starts with no room. Call `SetRoom(roomName, transmit, positional)` to own one receiving membership and, when explicitly requested, one transmitting channel. `Clear()` releases both; `Dispose()` releases once and closes the scope. Room transitions close the old owned channel and leave the old owned membership before joining the new group. Same-room calls reuse resources, and positional changes update only the owned channel.

Other code's memberships and duplicate channels are preserved. The scope never closes channels by enumeration or room name. `IsTransmitting` means its channel is open, not that speech was detected or a packet delivered. Select names and transmission permission in the application; there is no default group, VAD gating or game policy in this scope.

Names must be nonblank and otherwise pass through exactly. Dissonance uses 16-bit room hashes and reports collisions in debug builds; choose noncolliding application group names. The scope defines no reserved names; individual transports may have special rooms, such as the offline integration's `Loopback`. Use the scope from one main-thread owner and do not reenter its methods from SDK room/channel notifications.

## Optional SDK activation

Adapters are disabled by default. Import the required SDKs and let Unity finish compiling, then run **Tools > Bun3 > Audio > Sync Installed Adapters**. The SDK-independent `Bun3.Unity.Audio.Editor.SoundSdkSetup.SyncInstalledAdapters` method is also available through Unity `-executeMethod` or Editor automation. It checks SDK assembly-definition assets and required loaded types before enabling adapters on Standalone, Android, iOS and WebGL, preserving unrelated scripting defines.

Dissonance requires `BUN3_DISSONANCE`; the official NGO binding additionally requires `BUN3_DISSONANCE_NFGO`. Steam Audio requires both `BUN3_STEAMAUDIO` and the SDK's `STEAMAUDIO_ENABLED`. Combined voice path playback requires both SDKs. A leftover `STEAMAUDIO_ENABLED` alone does not activate Bun3 adapters. Runtime, Editor and test assemblies share these gates.

Before removing SDK assets, remove or guard application references to adapter types, then run **Tools > Bun3 > Audio > Disable Adapters** (`Bun3.Unity.Audio.Editor.SoundSdkSetup.DisableAdapters`) and let compilation finish. This removes the three Bun3 symbols on the four supported targets and preserves the SDK-owned `STEAMAUDIO_ENABLED` flag; optional packages can remain installed. Synchronize again after installing or removing SDK components. Activation is explicit and does not run automatically on domain reload. Deleting SDK files outside the Editor while old activation symbols remain is not an automatically recoverable cold-start workflow; restore the SDK or clear the managed symbols before reopening.
