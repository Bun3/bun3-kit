# Dissonance Steam Audio playback

Optional integration requiring Dissonance 9.0.9, Steam Audio 4.8.1 and the Bun3 adapters named in `package.json`. SDK assets are installed separately. Runtime and test assemblies require `BUN3_DISSONANCE`, `BUN3_STEAMAUDIO` and `STEAMAUDIO_ENABLED`.

## Installation and dependencies

The package requires Unity **6000.3** or later. Its manifest declares `com.bun3.unity.audio` **0.4.0**, `com.bun3.unity.audio.dissonance` **0.2.0**, and `com.bun3.unity.audio.steamaudio` **0.5.0**. The Steam Audio adapter also needs the acoustics package. Merge these Git entries into the project's existing `Packages/manifest.json` dependencies:

```json
{
  "com.bun3.unity.audio": "https://github.com/Bun3/bun3-kit.git?path=unity/Packages/com.bun3.unity.audio#Bun3/jp-sound-integration",
  "com.bun3.unity.acoustics": "https://github.com/Bun3/bun3-kit.git?path=unity/Packages/com.bun3.unity.acoustics#Bun3/jp-sound-integration",
  "com.bun3.unity.audio.dissonance": "https://github.com/Bun3/bun3-kit.git?path=unity/Packages/com.bun3.unity.audio.dissonance#Bun3/jp-sound-integration",
  "com.bun3.unity.audio.steamaudio": "https://github.com/Bun3/bun3-kit.git?path=unity/Packages/com.bun3.unity.audio.steamaudio#Bun3/jp-sound-integration",
  "com.bun3.unity.audio.dissonance.steamaudio": "https://github.com/Bun3/bun3-kit.git?path=unity/Packages/com.bun3.unity.audio.dissonance.steamaudio#Bun3/jp-sound-integration"
}
```

This is a dependency fragment, not a complete manifest. The fragment after `#` selects the moving integration branch. Numeric Bun3 package dependencies do not automatically resolve sibling Git paths: install all Bun3 transitive dependencies directly or configure a registry serving them. In particular, follow the [audio README](../com.bun3.unity.audio/README.md) for core/common, UniTask, and SerializeReferenceExtensions setup. For local development, use corresponding embedded or project-manifest `file:` dependencies. Import Dissonance, Steam Audio's managed `SteamAudioUnity` assembly, and target-platform native libraries separately. For planar simulation, also satisfy the [Steam Audio adapter's native simulation prerequisites](../com.bun3.unity.audio.steamaudio/README.md); a path-rendering library alone is not a complete planar simulation setup.

The runtime assembly `Bun3.Unity.Audio.Dissonance.SteamAudio` references `Bun3.Unity.Audio.SteamAudio`, `Bun3.Unity.Audio`, `DissonanceVoip`, and `SteamAudioUnity`. Application assembly definitions must reference the assemblies of the types they use and share all three SDK define constraints: `BUN3_DISSONANCE`, `BUN3_STEAMAUDIO`, `STEAMAUDIO_ENABLED`. The Netcode adapter is optional; this output implementation is independent of the chosen voice transport.

## Playback boundary and lifecycle

This is a replacement SDK playback implementation, not a component to add beside the default `VoicePlayback`/`SamplePlaybackComponent` path. Choose one output path per voice. The basic `DissonanceOutputBridge` requires those default components and is not the ownership wrapper for this custom playback. Route the custom output's `AudioSource` to the desired mixer bus directly. Do not add a second spatializer or Unity distance rolloff to the already-rendered stereo signal.

`DissonanceSteamAudioPlayback` implements the SDK playback-pool interface with a DSP output generator, a silent driver clip and a separate decoder host. Configure every pooled instance with a main-thread renderer factory, path coefficients and listener coordinates before accepting voice. Application prefab/bootstrap wiring is separate.

The generation-bound output filter pulls decoded samples only at DSP playback time. Streaming-clip prefetch must not consume live speech. The driver starts on the following Update, giving the application's LateUpdate one opportunity to publish initial acoustic parameters before the first read. Binding a monitor does not admit decoder reads: callbacks remain silent until the source start is explicitly admitted. Blocked output still consumes live speech after startup.

A directly constructed `DissonancePathPlayback` takes ownership of its renderer only after successful construction. Each pump represents one immutable session generation. An audio callback reads interleaved stereo through `ReadStereo`. Control code calls `RequestRetirement` before stopping the source or changing decoder ownership, and polls `TryDisposeRetired` before resetting/reusing the SDK decoder or native effect. The facade uses the same retirement barrier but retains its native resources after natural completion. Reclamation waits for both PCM and metadata readers and does not require another callback after the source stops. Concurrent audio consumers are unsupported and receive silence when they cannot claim the pump.

Channel queries copy a retained snapshot instead of claiming PCM processing. The audio owner refreshes that snapshot before an SDK read can recycle its decoder. Snapshot contention skips metadata refresh, never PCM processing. Channel-list capacity is reserved on the control thread from received packet metadata; the audio callback skips refresh until sufficient capacity is available.

The active `Parameters` mailbox accepts copied SH, listener coordinates, three-band EQ, EQ normalization and additional path gain as one coherent snapshot. No SDK-native coefficient pointer is retained. Set `StartBlocked=true` before activation when coverage/simulation must be validated first, then publish and explicitly unblock the current generation. A final source-filter gate also silences already queued PCM. Path gain must exclude SDK user/bus volume and distance attenuation already represented by SH coefficients.

The facade polls the output sample rate and DSP frame size, retiring an incompatible generation before constructing a matching renderer and clip. The output source bypasses Unity spatialization and distance attenuation; SDK volume/priority processing remains upstream. Packet-loss reporting is currently unavailable.

Reset, disable, destruction and identity/codec changes retire readers before SDK reset. The detached host survives an outstanding reader and reclaims on the main thread even without another audio callback. Commands received during retirement are dropped; a fresh `StartPlayback` is required afterward. Natural completion preserves queued SDK sessions and waits for the final native tail to pass the source filter before stopping. It retains the renderer, native context/HRTF/effect, PCM buffers, silent driver clip and monitor for the next speech session. After reader quiescence, the monitor is unbound and may be bound to a new immutable pump. Each callback captures one generation, so an old callback cannot consume reused storage or update the new generation's delivery counters. Destructive resets dispose the retained resources and replace the monitor. Small pump and mailbox objects still allocate per speech generation; speech startup is not allocation-free.

Validation uses generated Identity-codec packets through the actual SDK decoder and native path effect. PlayMode tests exercise DSP callbacks, short speech with initial path publication, paced continuous packets, directional stereo at the downstream source filter, native tails, held-reader reset/destruction, resource reuse across speech sessions, metadata/PCM independence, pre-start admission, coherent concurrent publication, live native gain changes and warm managed allocation. This does not establish microphone capture, Opus decoding, network transport, hardware audibility or end-to-end latency. Producing valid simulation snapshots and device-change regression coverage remain separate work.

## Minimal planar voice integration

The high-level `DissonancePlanarAcousticOutput` binds one playback instance to the shared planar simulation. Before constructing it, configure the SDK playback prefab/pool to instantiate `DissonanceSteamAudioPlayback` and deliver its `IVoicePlaybackInternal` lifecycle (`Setup`, codec/identity assignment, packet input, and `StartPlayback`). The adapter does not replace that application/pool wiring. Configure each pooled instance before it accepts voice; do not reconstruct the acoustic output every frame or every speech burst.

The following reusable owner registers a single voice in an existing `PlanarAcousticOutputs`. Create one owner per instantiated playback object. The application must already own a valid `PlanarAcousticSessionScope` with authored map/native simulation resources and an enabled listener, then call its `Tick` from `LateUpdate`, for example `session.Tick(Time.unscaledTime, Time.unscaledTimeAsDouble, 0.05f)`. The interval is an application choice. This ordering allows initial parameters to reach a new generation before DSP output begins.

```csharp
using System;
using Bun3.Unity.Audio;
using Bun3.Unity.Audio.Dissonance.SteamAudio;
using Bun3.Unity.Audio.SteamAudio;

public sealed class PlanarVoiceOwner : IResolvedSoundAcousticSettings, IDisposable
{
    private readonly PlanarAcousticOutputs outputs;
    private readonly DissonancePlanarAcousticOutput output;

    public bool IsAvailable { get; set; } = true;
    public SoundAcousticSettings Acoustics { get; set; }

    public PlanarVoiceOwner(PlanarAcousticOutputs outputs,
        DissonanceSteamAudioPlayback playback,
        SoundAcousticSettings acoustics)
    {
        this.outputs = outputs;
        Acoustics = acoustics;
        output = DissonancePlanarAcousticOutput.FromAcoustics(playback, this);
        outputs.Register(output);
    }

    public void Dispose()
    {
        outputs.Unregister(output);
        output.Dispose();
    }
}
```

Dispose/unregister this owner on the main thread before destroying its playback object. Recreate/register it when reusing a pooled instance after this owner has been disposed. Setting `IsAvailable` false gates output on the next session tick. Disposing this owner blocks and detaches simulation ownership; the playback component continues to own its decoder/DSP lifetime and retires that lifetime when disabled/destroyed. Disposing the shared session detaches outputs and releases the world; clearing registrations is a separate operation on `PlanarAcousticOutputs`.

## Distance and stereo-width profiles

Create a shared acoustic asset through **Assets > Create > Bun3 > Audio > Sound Acoustic Profile**. Its settings may reference **Distance Attenuation Profile** and **Spatial Blend Profile** assets. These types live in the audio core; this package consumes their resolved value through `IResolvedSoundAcousticSettings`.

| Setting | Behavior |
|---|---|
| `IResolvedSoundAcousticSettings.IsAvailable` | False keeps output blocked when tuning/context is unavailable. |
| `Acoustics.DistanceAttenuation` | Enables/disables the native distance gain. It does not bypass coverage gating or path occlusion. |
| `Acoustics.AttenuationProfile` | Live distance-in-metres/volume curve using native path distance. The last key is the hard silence distance; put it at zero gain for a smooth end. Empty curves are silent. |
| Null attenuation profile | Uses `Acoustics.MinDistance` and `MaxDistance` with edge fade fraction 0.2. |
| `Acoustics.InheritSpatialBlend` | True inherits the world's profile/range. False uses the selected profile or inline mono/full distances. |
| `SpatialBlendProfile.MonoDistance` | At/below this native path distance, output is processed dual mono. Default asset value: 1 m. |
| `SpatialBlendProfile.FullSpatialDistance` | At/above this distance, output retains full native binaural width. Default asset value: 3 m. A value at/below `MonoDistance` disables collapse. |

Width interpolates smoothly between the two distances. It changes processed stereo width while retaining native occlusion, EQ, and attenuation; it is distinct from `AudioSource.spatialBlend`, which remains zero for the custom output. Authored distance-curve edits create a new immutable snapshot on the control thread. Avoid recreating curves/profiles each frame. SDK user volume, mixer volume, native attenuation, and additional `PathPlaybackSettings.Gain` each need a single owner to avoid multiplying the same gain twice.

Create outputs with `FromAcoustics`, passing the application-owned `IResolvedSoundAcousticSettings` implementation.

## Manual parameter publication

For an application supplying its own simulation, call `Configure(factory, initialCoefficients, initialListener)` once before accepting voice and set `StartBlocked = true`. The factory runs on the main thread and must return a compatible `SteamAudioPathRenderer` for the requested sample rate and DSP frame size. `SteamAudioPathRenderer.CreateDefault(rate, frame)` creates the default native renderer when the required SDK/native resources are available. Coefficient count must match the renderer's SH order; the planar binding uses order one (four coefficients). `Configure` copies the initial coefficient array and retires any previous output, so it is not a per-frame update API.

Publish against the current mailbox in `LateUpdate`. The helper below assumes a preallocated coefficient array containing a valid simulation result and an orthonormal listener coordinate frame already converted to Steam Audio space. A missing mailbox simply means no active generation. Do not invent coefficients from a Unity direction vector or retain SDK native coefficient pointers.

```csharp
using Bun3.Unity.Audio.Dissonance.SteamAudio;
using SA = SteamAudio;

public static class VoicePathPublisher
{
    public static bool Publish(DissonanceSteamAudioPlayback playback,
        float[] copiedSh, SA.CoordinateSpace3 listener)
    {
        var mailbox = playback.Parameters;
        if (mailbox == null) return false;
        long generation = mailbox.Generation;
        bool published = mailbox.TryPublish(generation, copiedSh,
            new PathPlaybackSettings(listener, gain: 1f, spatialBlend: 1f));
        mailbox.TrySetBlocked(generation, !published);
        return published;
    }
}
```

When coverage or simulation data is invalid, set the current mailbox blocked without publishing stale data. Generation tokens reject stale control writes; reacquire `Parameters` after each speech session/reset. A successful publication does not itself unblock output. One control writer and one audio reader are supported. Direct `DissonancePathPlayback` users must observe the retirement barrier described above rather than disposing a renderer under an active callback.

## Threading and allocation contract

Configure playback, change SDK identity/codec, receive SDK commands, register planar outputs, edit profiles, publish parameters, and reclaim native objects on the main thread. The DSP callback uses retained buffers and copied parameters, without accessing application Unity objects or allocating per processed frame on the warmed path. Do not log, use LINQ, resize lists, or allocate in custom callback code. Native resources, renderer construction, clip creation, initial buffers, subscriptions, and profile edits are cold work. Speech generations still allocate small managed pump/mailbox objects even when native resources are retained. Listener discovery and capacity growth are also outside the steady-state allocation guarantee.

`ReadStereo` expects interleaved stereo and exclusive audio-reader ownership. Mailbox reads/publications may fail under contention instead of blocking. Warm managed allocation tests cover specific adapter paths; they do not establish that every SDK codec, network stack, device backend, or Unity callback is allocation-free. Reuse destination lists for `GetRemoteChannels` and reserve sufficient capacity on the control thread.

## Diagnostics and troubleshooting

| Symptom | Check |
|---|---|
| No adapter types | Verify SDK assemblies, all three defines, application asmdef references, and activation below. |
| Silent with no callbacks | Ensure SDK `Setup`, valid codec, configured renderer factory, live packet input, and a fresh `StartPlayback` after retirement. |
| `Fault` is non-null | Inspect renderer/native-library loading, coefficient count, sample rate, and frame-size compatibility. |
| Decoded amplitude is nonzero but output silent | Check current `Parameters.IsBlocked`, simulation coverage, profile endpoint, SDK mute/priority/volume, mixer route, and listener. |
| Planar source handle is invalid | Check active speech/settings, source capacity, map coverage, and session initialization error. Capacity failures retry on scheduled ticks. |
| Left and right are identical | Inspect `SpatialBlendProfile`, `PathPlaybackSettings.SpatialBlend`, positional playback permission, and path direction before assuming a native failure. |
| `DroppedCommands` grows after reset | Wait for `IsRetiring` to clear, then start a new SDK playback session; retirement does not queue new commands. |
| Speech starts allocate | Per-generation managed allocations remain by design; natural completion reuses heavier native resources. |

`CallbackCount` counts entries including silent calls; `PeakDecodedAmplitude` measures decoder output, and `PeakStereoDifference` measures native channel difference. None proves hardware audibility. `PacketLoss` returns null. After device changes, confirm the session rebuilt its world and that new voice sessions use the new output format.

## Running the package tests

Install Unity Test Framework, add `com.bun3.unity.audio.dissonance.steamaudio` to the project's top-level `testables` array, and satisfy `UNITY_INCLUDE_TESTS` plus the three SDK constraints. Run the `Bun3.Unity.Audio.Dissonance.SteamAudio.Tests` assembly in PlayMode with native libraries available and an audio environment capable of driving DSP callbacks. A no-audio run cannot establish playback behavior. Test fixtures include `DissonancePathMailboxTests`, `PathParameterLeaseTests`, `DissonancePathPlaybackTests`, and `DissonanceSteamAudioPlaybackTests`.

Use these tests after SDK upgrades and changes to generation lifetime, parameter publication, or native rendering. Their Identity-codec fixtures and synthetic packets isolate adapter behavior; add separate application tests for the configured codec, transport, microphone, real simulation maps, and output devices. The validation scope described above is not a report that tests were run during installation.

## Optional SDK activation

Adapters are disabled by default. Import the required SDKs and let Unity finish compiling, then run **Tools > Bun3 > Audio > Sync Installed Adapters**. The SDK-independent `Bun3.Unity.Audio.Editor.SoundSdkSetup.SyncInstalledAdapters` method is also available through Unity `-executeMethod` or Editor automation. It checks SDK assembly-definition assets and required loaded types before enabling adapters on Standalone, Android, iOS and WebGL, preserving unrelated scripting defines.

Dissonance requires `BUN3_DISSONANCE`; the official NGO binding additionally requires `BUN3_DISSONANCE_NFGO`. Steam Audio requires both `BUN3_STEAMAUDIO` and the SDK's `STEAMAUDIO_ENABLED`. Combined voice path playback requires both SDKs. A leftover `STEAMAUDIO_ENABLED` alone does not activate Bun3 adapters. Runtime, Editor and test assemblies share these gates.

Before removing SDK assets, remove or guard application references to adapter types, then run **Tools > Bun3 > Audio > Disable Adapters** (`Bun3.Unity.Audio.Editor.SoundSdkSetup.DisableAdapters`) and let compilation finish. This removes the three Bun3 symbols on the four supported targets and preserves the SDK-owned `STEAMAUDIO_ENABLED` flag; optional packages can remain installed. Synchronize again after installing or removing SDK components. Activation is explicit and does not run automatically on domain reload. Deleting SDK files outside the Editor while old activation symbols remain is not an automatically recoverable cold-start workflow; restore the SDK or clear the managed symbols before reopening.
