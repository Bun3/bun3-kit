# Dissonance output bridge

Requires a separately installed Dissonance SDK providing the `DissonanceVoip` assembly. Validated against Dissonance 9.0.9. The audio core remains independent; disable adapters before removing SDK assets as described below.

## Installation and assembly setup

The package declares Unity **6000.3** as its minimum. Import the third-party SDK assets separately; Git installation does not install licensed SDK files or native binaries. For a Git-based project, merge these entries into `Packages/manifest.json`'s existing `dependencies` object:

```json
{
  "com.bun3.unity.audio": "https://github.com/Bun3/bun3-kit.git?path=unity/Packages/com.bun3.unity.audio#Bun3/jp-sound-integration",
  "com.bun3.unity.audio.dissonance": "https://github.com/Bun3/bun3-kit.git?path=unity/Packages/com.bun3.unity.audio.dissonance#Bun3/jp-sound-integration"
}
```

These are dependency entries, not a complete project manifest. The fragment `Bun3/jp-sound-integration` selects the integration branch; it is a moving branch, not a release tag. Package dependencies containing Bun3 version numbers do not resolve sibling packages from the same Git checkout automatically. Add the Bun3 transitive packages explicitly, including `com.bun3.unity.core` and its `com.bun3.common` dependency, or provide a registry that actually serves the declared versions. Follow the [audio installation guide](../com.bun3.unity.audio/README.md) for that dependency chain, UniTask, and SerializeReferenceExtensions. Do not assume a single Git URL is a self-contained install.

For a local checkout, use the same package set as embedded packages or project-manifest `file:` dependencies pointing to each package directory. Do not copy the third-party SDK into these packages.

The runtime assembly is `Bun3.Unity.Audio.Dissonance`. An application with its own assembly definition must reference the assemblies whose types it uses: `Bun3.Unity.Audio.Dissonance`, `Bun3.Unity.Audio`, `DissonanceVoip`. Match the adapter's define constraints (`BUN3_DISSONANCE`) on application adapter assemblies, so SDK removal can disable those assemblies cleanly. See **Optional SDK activation** below before adding symbols manually.

## Scope and playback setup

This package supplies output ownership, raw microphone activity, room ownership, and an optional transmission hold gate. It does not select a transport, authenticate speakers, configure microphones, or decide who may speak. Use the Netcode adapter for NGO identity/position binding and the combined Steam Audio adapter for native voice path rendering.

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

## Minimal output ownership example

Attach this owner to each SDK playback prefab instance alongside the bridge and the required SDK components. Assign the Voice mixer group in the Inspector. The bridge must already be on the prefab when `SamplePlaybackComponent.Start` caches subscribers. The example owns a registry for one source; a shared registry may instead be owned by the application audio service.

```csharp
using Bun3.Unity.Audio;
using Bun3.Unity.Audio.Dissonance;
using UnityEngine;
using UnityEngine.Audio;

public sealed class VoiceOutputOwner : MonoBehaviour
{
    [SerializeField] private DissonanceOutputBridge bridge = null;
    [SerializeField] private AudioMixerGroup voiceBus = null;
    private ExternalAudioRegistry registry;

    private void Awake()
    {
        registry = new ExternalAudioRegistry(1);
        bridge.Bind(registry, new ExternalAudioSettings(
            overrideMixerGroup: true, mixerGroup: voiceBus));
    }

    private void OnDestroy()
    {
        if (bridge != null) bridge.Unbind();
        registry?.Dispose();
    }
}
```

Disabling the playback object releases controls; enabling it registers again from the original settings. `SetGain` and `SetLowPassCutoff` affect only a currently valid registration and only controls explicitly selected at bind time. If gain is omitted, the SDK keeps volume ownership. If a filter is selected, it must already exist on the same GameObject. Registration, rebind, capacity growth, and diagnostics-state creation are control-thread work and may allocate. Readiness and sample counters are diagnostic observations, not proof of audible output.

## VAD, transmission permission, and rooms

This application-owned example starts receive-only, detects short VAD activations between ticks, and bridges 150 ms gaps. Construct it against an initialized, existing comms object on the main thread. Call `Tick` from the owner's `Update`, passing authoritative application permission and `Time.unscaledTimeAsDouble`; dispose it before destroying comms. Keep `roomName` stable for this scope. To change routing, dispose/recreate the scope or reset the gate and activation baseline before selecting the new room.

```csharp
using System;
using Bun3.Unity.Audio.Dissonance;
using Dissonance;

public sealed class VoiceRoomOwner : IDisposable
{
    private readonly DissonanceVoiceActivityScope activity;
    private readonly DissonanceRoomScope room;
    private readonly DissonanceTransmissionGate gate = new();
    private readonly string roomName;
    private long previousCount;

    public VoiceRoomOwner(DissonanceComms comms, string roomName)
    {
        this.roomName = roomName;
        room = new DissonanceRoomScope(comms);
        room.SetRoom(roomName, transmit: false, positional: true);
        activity = new DissonanceVoiceActivityScope(comms);
    }

    public void Tick(bool mayTransmit, double unscaledNow)
    {
        var sample = activity.Poll();
        bool speech = sample.IsSpeechActive || sample.ActivationCount != previousCount;
        previousCount = sample.ActivationCount;
        bool transmit = gate.Evaluate(speech, mayTransmit, unscaledNow, 0.15);
        room.SetRoom(roomName, transmit, positional: true);
    }

    public void Dispose()
    {
        gate.Reset();
        room.Dispose();
        activity.Dispose();
    }
}
```

`DissonanceTransmissionGate.Evaluate` requires finite monotonic time and a finite nonnegative release duration. `allowed: false` clears the hold immediately; `Reset()` clears routing history. This gate does not buffer pre-roll audio: noticing a brief activation after it ends does not recover already discarded microphone samples. Avoid competing transmit triggers if this scope is intended to own transmission permission.

## Threading and allocation contract

All component, registry, room, and scope control belongs to the main thread. Only SDK VAD callbacks and the PCM subscriber publication are designed to cross threads. The subscriber counts samples without copying or retaining the buffer. Warm VAD callbacks and polling do not allocate managed memory. Scope construction/subscription and output registration are cold paths; SDK room/channel operations have their own allocation behavior. The transmission gate is stateful and requires one owner thread. No package-wide claim of zero allocation includes SDK networking, capture, codecs, or Unity internals.

There are no dedicated profile assets in this package. Mixer routing, optional source gain/filter ownership, room names, positional flags, and release duration are explicit caller settings. Acoustic distance and stereo-width profiles apply through the combined Steam Audio adapter.

## Troubleshooting and tests

| Symptom | Check |
|---|---|
| Adapter type is unavailable | Verify `DissonanceVoip`, synchronize adapters, and check the application assembly's references/constraints. |
| `Bind` throws | Verify all three required components share the bridge GameObject and the registry is live. |
| Gain or cutoff does not change | Opt into that control when constructing `ExternalAudioSettings`; enable/rebind starts from the original settings. |
| `ReceivedSamples` remains zero | Verify the bridge existed before SDK `Start`, is registered/enabled, and the SDK is producing playback PCM. |
| VAD is active but nobody hears speech | Check permission, an open channel, matching receiving room, transport, codec, and output routing independently. |
| Volume is unexpectedly low | Apply user Voice/Master volume once in the mixer; avoid duplicate source/SDK attenuation. |

To expose package tests, add `com.bun3.unity.audio.dissonance` to the project's top-level `testables` array, install Unity Test Framework, and activate the SDK adapter. Run `Bun3.Unity.Audio.Dissonance.Tests` in EditMode and `Bun3.Unity.Audio.Dissonance.PlayMode.Tests` in PlayMode. Existing tests cover control restoration and PCM immutability, room ownership/transitions, VAD concurrency and warm callback allocations, plus disable/reuse and actual SDK subscription lifetimes. They do not establish microphone/device quality or network delivery. Run the relevant suites again after changing SDK versions or playback prefabs.

## Optional SDK activation

Adapters are disabled by default. Import the required SDKs and let Unity finish compiling, then run **Tools > Bun3 > Audio > Sync Installed Adapters**. The SDK-independent `Bun3.Unity.Audio.Editor.SoundSdkSetup.SyncInstalledAdapters` method is also available through Unity `-executeMethod` or Editor automation. It checks SDK assembly-definition assets and required loaded types before enabling adapters on Standalone, Android, iOS and WebGL, preserving unrelated scripting defines.

Dissonance requires `BUN3_DISSONANCE`; the official NGO binding additionally requires `BUN3_DISSONANCE_NFGO`. Steam Audio requires both `BUN3_STEAMAUDIO` and the SDK's `STEAMAUDIO_ENABLED`. Combined voice path playback requires both SDKs. A leftover `STEAMAUDIO_ENABLED` alone does not activate Bun3 adapters. Runtime, Editor and test assemblies share these gates.

Before removing SDK assets, remove or guard application references to adapter types, then run **Tools > Bun3 > Audio > Disable Adapters** (`Bun3.Unity.Audio.Editor.SoundSdkSetup.DisableAdapters`) and let compilation finish. This removes the three Bun3 symbols on the four supported targets and preserves the SDK-owned `STEAMAUDIO_ENABLED` flag; optional packages can remain installed. Synchronize again after installing or removing SDK components. Activation is explicit and does not run automatically on domain reload. Deleting SDK files outside the Editor while old activation symbols remain is not an automatically recoverable cold-start workflow; restore the SDK or clear the managed symbols before reopening.
