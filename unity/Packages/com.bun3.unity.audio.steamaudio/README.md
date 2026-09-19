# Bun3 Unity Audio - Steam Audio Adapter

Optional [Steam Audio](https://valvesoftware.github.io/steam-audio/) integration for
[`com.bun3.unity.audio`](../com.bun3.unity.audio). It supplies a legacy Unity
spatializer binder, explicit native path rendering/simulation, and pooled native
SFX output. Native output consumes copied coefficients and produces stereo directly;
the core continues owning source gain, fades and mixer routing.

## Install

Steam Audio ships as a legacy `.unitypackage`, not a UPM package, so it is
installed once per game project (not referenced from `manifest.json`):

1. Download the Unity integration zip for Steam Audio **v4.8.1** from
   [github.com/ValveSoftware/steam-audio/releases](https://github.com/ValveSoftware/steam-audio/releases) —
   grab `steamaudio_unity_<version>.zip` and extract `SteamAudio.unitypackage`
   from it (the zip also has FMOD/Wwise variants — not needed for plain Unity
   audio).
2. In the Unity Editor: **Assets > Import Package > Custom Package...**,
   select `SteamAudio.unitypackage`, keep everything selected, **Import**.
   This adds `Assets/Plugins/SteamAudio/` and the SDK's own installer sets
   `STEAMAUDIO_ENABLED`. After SDK compilation completes, run **Tools > Bun3 >
   Audio > Sync Installed Adapters** to opt into the Bun3 adapter. Its assemblies
   require both `BUN3_STEAMAUDIO` and `STEAMAUDIO_ENABLED` and reference
   `SteamAudioUnity`. See the removal workflow below before deleting SDK assets.
3. For the legacy `SteamAudioSoundSetup` source-binder route, open **Edit > Project Settings > Audio** and set **Spatializer Plugin** to
   **Steam Audio Spatializer**. The adapter's editor validator
   (`SteamAudioSetupValidator`) logs a warning on domain load if this isn't
   set, since the binder's occlusion/spatialization mapping is a no-op
   without it.

This package itself is a normal embedded UPM package
(`unity/Packages/com.bun3.unity.audio.steamaudio`) depending on
`com.bun3.unity.audio`; it does not declare a Steam Audio UPM dependency
because none exists. (This repo's own dev-project fetch of the
`.unitypackage` — used to build/test the adapter itself — is documented
separately in `unity/Vendor/README.md`.)

## Usage

```csharp
using Bun3.Unity.Audio;
using Bun3.Unity.Audio.SteamAudio;

var config = SteamAudioSoundSetup.Apply(new SoundSystemConfig
{
    SfxVoices = 24,
});
var sound = new SoundSystem(config);
```

`Apply`:

- Sets `SoundSystemConfig.OcclusionChecksPerFrame = 0` — the framework's off
  switch for the core occlusion/low-pass pipeline: no raycasts run and no
  `LowPassFilter` components are attached, since Steam Audio's spatializer
  owns spatialization and occlusion instead. `SoundDef.Occlusion` and
  `SoundDef.Spatial` remain the only game-facing knobs either way.
- Chains a per-voice binder onto `SoundSystemConfig.OnVoiceConfigured`
  (running after any hook already set on the config). Per SFX play, the
  binder:
  - Adds a `SteamAudio.SteamAudioSource` to the voice's `AudioSource`
    GameObject the first time it's used (pooled voices keep it afterward).
  - Sets `AudioSource.spatialize` from `SoundDef.Spatial` (`!= None`).
  - For 3D sounds (`Positional`/`Follow`): enables the `SteamAudioSource` and
    sets its `occlusion` field from `SoundDef.Occlusion`.
  - For 2D sounds (`None`): disables the `SteamAudioSource`.
  - Leaves `occlusionType`, `occlusionInput`, and the `transmission*` fields
    at Steam Audio's own defaults — this adapter maps spatialization/occlusion
    on/off only, not per-def transmission tuning.
- Is idempotent: calling `Apply` again on the same config does not
  double-register the binder.

`Apply` returns the same `config` it was given, for chaining into
`SoundSystem`'s constructor.

## Native path PCM rendering

`SteamAudioPathRenderer` consumes an existing Steam Audio `Context` and `HRTF`, a fixed sample rate/frame size, and Ambisonics order zero through three. It retains its own native context/HRTF references. The HRTF must have been created with the same audio settings. This path requires the standard three-band Steam Audio **4.8.1** native ABI; a different SDK build requires an ABI review.

```csharp
var renderer = new SteamAudioPathRenderer(context, hrtf, 48000, 256, order: 1);
var mono = new float[renderer.FrameSize];
var stereo = new float[renderer.FrameSize * 2];
var coefficients = new float[renderer.CoefficientCount];

// Supply valid PCM, native world-space SH coefficients, and an orthonormal
// listener coordinate space before processing each frame.
renderer.Render(mono, stereo, coefficients, eqLow: 1, eqMid: 1, eqHigh: 1,
    listener: listenerSpace, gain: 1, normalizeEq: false);
renderer.Reset();
renderer.Dispose();
```

Output is interleaved left/right stereo. Arrays must exactly match the configured counts. `Render` copies inputs into preallocated native storage and performs no managed allocations after warmup. Create the renderer and arrays outside the audio callback. All processing and reset calls belong to a single owner; stop and join that owner's work before disposal. No finalizer or concurrent-disposal synchronization is provided.

EQ and final output gain are separate. EQ normalization is disabled by default so its attenuation is preserved. Explicit zero output gain writes exact silence while still advancing the effect. `Reset` clears native processing history; native floating-point filter noise can remain below the tested silence tolerance. Keep user mixer volume ownership outside the renderer to avoid applying it twice.

The renderer only processes caller-supplied coefficients. It does not retrieve simulator outputs, bake probes, construct geometry, bridge an SDK playback callback, or schedule simulation. Its native PCM tests establish DSP behavior with known coefficient distributions. They do not establish game-level door authoring, speech delivery, or device voice quality.

The installed Unity wrapper omits the trailing `normalizeEQ` field from native `IPLPathEffectParams`. This package binds the complete 4.8.1 structure directly and does not pass that incomplete wrapper to the path effect. Native export availability and processing were tested on Windows x64; other platform native binaries still require validation.

## Copied native simulation results

`SteamAudioPathSimulationScope` retains an existing default native scene and baked probe batch and owns a fixed-capacity simulator/source pool. Register sources, set an orthonormal listener coordinate space, and call `Simulate` on the creating control thread. `TryCopyResult` copies SH into an exact-size caller array and returns direct distance/air/occlusion factors plus path EQ and normalization. No native pointers escape in results, and warm input updates, simulation and copying allocate no managed memory in the native tests.

`SetSourceMinimumDistance(handle, minimum)` configures unity gain inside the authored minimum and `minimum / distance` outside it. Steam Audio applies the callback to its native direct and indirect path calculations, so the copied SH already carries the attenuation; do not add a second distance gain. This uses a rooted static callback with preallocated per-source state because Steam Audio's built-in inverse model instead evaluates `1 / max(distance, minimum)`. A new/recycled registration defaults to 1. Finite negative values clamp to zero (unity gain only at the coincident endpoint, silence at positive distance); NaN/infinity throw. Changes invalidate the copied result, while unchanged values preserve it. Callback roots outlive all native sources and the simulator.

After geometry changes, commit the caller-owned scene and call `InvalidateGeometry` with an increased revision before simulating. Pending and failed results contain zero signal but are not classified as physically blocked. `Valid` means a finite native computation completed; `HasPathSignal` does not establish probe coverage or geometric reachability. Callers must validate endpoint coverage before using results. No indirect path-distance value is invented.

Sources use generation handles, nonthrowing capacity failure and explicit removal. `SetOcclusion` supports raycast and fractional volumetric direct occlusion with a constructor-bounded sample count. Transmission and source directivity are not enabled. Dispose on the creating thread; the native scene, probes and context remain retained until disposal. This scope requires the 64-bit Steam Audio 4.8.1 ABI. Ten native simulation tests passed, including door-close/reopen simulation, partial occlusion, authored minimum-distance near/far and indirect SH, zero/invalid values and warm callback updates; game geometry authoring is a separate layer.

## Baked acoustic assets

`SteamAudioAcousticBaker.Bake` accepts native-coordinate mesh arrays, materials, exact probe spheres, `SteamAudioPathBakeSettings` and a nonblank geometry fingerprint. It performs a cold synchronous pathing bake and returns a new `SteamAudioAcousticAsset`; callers own saving or destroying that ScriptableObject. Mesh upload buffers and native serialization objects have explicit cleanup. No asset-database mutation or map-specific geometry generation occurs inside the baker.

The asset copies scene/probe blobs, settings and probe spheres. `ProbeCount` and `GetProbe` expose exact immutable probe values for coverage validation. SDK/format metadata and checksums over both blobs and metadata are validated before loading; these checks detect accidental corruption and do not authenticate arbitrary native data. Bake and load also require a nonempty native pathing data layer.

`SteamAudioAcousticSceneScope(context, asset)` retains a context and owns loaded `Scene` and `Probes`. Its `Context`, `Scene`, and `Probes` properties return borrowed wrappers for same-control-thread simulation and dynamic mesh edits. Commit edits before invalidating the simulation revision. Dispose simulations before the load scope normally; independently retained native simulation handles still survive loader disposal.

Create/load/dispose on the Unity main/control thread. Supported SDK deserialization constructors are used without reflection or source modifications. If native loading itself throws, an SDK-internal serialized temporary may be reclaimed by its finalizer; successfully returned scope wrappers and temporary Unity objects are cleaned deterministically. Five asset tests passed against the real native library, including persisted asset reload and continued simulation after loader/context release.

`SteamAudioMeshScope` retains a scene and a copied native-coordinate mesh for cold-created dynamic obstacles. `Add`/`Remove` change membership; callers commit the scene and invalidate simulation snapshots afterward. Disposal removes and releases the mesh on its creating thread without implicitly committing the scene.

## Native pooled SFX output

`SteamAudioSoundOutput` implements the audio core's optional `ISoundVoiceOutput` seam. Create one output per prewarmed source through `SoundSystemConfig.CreateVoiceOutput`, retain the output/source pairs for application simulation updates, and prepare every positional clip before playback:

```csharp
var cache = new SteamAudioClipCache(maximumDecodedBytes: 64L * 1024 * 1024);
cache.PrepareClips(positionalClips);
var config = new SoundSystemConfig
{
    SfxVoices = 24,
    CreateVoiceOutput = source => new SteamAudioSoundOutput(source, cache)
};
var sounds = new SoundSystem(config);
```

The cache budget counts managed float PCM in addition to Unity's own asset memory. Preparation accepts loaded mono/stereo Decompress On Load clips and commits a batch only after its complete validation/copy succeeds. Runtime-created streaming clips can report Decompress On Load while still rejecting `GetData`; Unity emits a readability diagnostic and the cache rejects them. No loading/decompression/cache growth occurs inside Play or an audio callback. Disposing the cache drops its references; outputs retain their current immutable PCM through retirement and release it during reuse/disposal.

Each output prewarms a renderer, buffers, reusable parameter storage and a looping stereo flatline clip. Its source filter reads cached PCM, applies forward pitch through a linear interpolating cursor, downmixes stereo positional input to a point source, and renders native stereo. It multiplies that output into the driver envelope, preserving Unity source/fade/mixer gain once. Unity spatialization, rolloff and Doppler are bypassed for handled positional voices. Nonpositional/UI requests keep the original clip and pass through untouched. Missing prepared data or unsupported output format returns Unavailable, never dry positional fallback.

Logical pitch clamps to 0..3; zero freezes input and effect progress while writing silence. This implements forward pitch/rate semantics, not Unity's proprietary resampler. Natural completion drains the native terminal input overlap and convolution tail, then observes one subsequent source-filter block before releasing the core voice. Stop/fade/steal/dispose retire immediately. A held reader delays reset/reuse and native release; an independent precreated main-thread host survives source destruction until that reader releases. No new clip, source, PCM array or producer object is needed per play. Output sample-rate/DSP-frame changes fail closed and require a cold replacement output pool.

`CurrentParameters` is a valid immutable `PathParameterLease` only for an active handled request; `MaxDistance` exposes that request's authored range and `MinDistance` its nonnegative minimum. Forward `MinDistance` to the corresponding simulation source before its next run; user/bus gain belongs to the Unity source. Invalid nonfinite minimum distances return Unavailable. `StartBlocked` defaults true. Publish a current copied native result through `lease.TryPublish(sh, new PathRenderSettings(...))`, then explicitly call `lease.TrySetBlocked(false)` once coverage and application policy permit output. Keep the token itself; old tokens cannot acquire a replacement generation. Path gain excludes user/bus volume and distance attenuation already encoded in SH. Probe coverage, geometry revisions and room permissions remain application-owned.

`SteamAudioPathRenderer.CreateDefault(rate, frame)` creates a retained default context/HRTF pair without requiring map simulation to exist first. A custom renderer factory can instead share properly retained native resources across the pool. `PathParameterMailbox` supplies one fixed three-slot implementation to SFX and voice consumers; one control writer and one processing reader are supported. Reusing its storage requires retirement and reader quiescence, with exclusive admission during the generation transition.

The 185-test Unity PlayMode regression passed, including native source-filter output from prepared stereo PCM, source volume 0/.25/1 and mixer gain measured through `AudioListener.GetOutputData`, a short SoundSystem voice, pitch frequency/completion rate, pause/tail/reuse, source destruction under a held processing claim, warm processing/per-play allocations, and deterministic delayed-reader admission. The default renderer factory also passed its dedicated native EditMode test. This establishes engine PCM behavior, not hardware audibility or every platform's native/audio backend.

## Optional SDK activation

Adapters are disabled by default. Import the required SDKs and let Unity finish compiling, then run **Tools > Bun3 > Audio > Sync Installed Adapters**. The SDK-independent `Bun3.Unity.Audio.Editor.SoundSdkSetup.SyncInstalledAdapters` method is also available through Unity `-executeMethod` or Editor automation. It checks SDK assembly-definition assets and required loaded types before enabling adapters on Standalone, Android, iOS and WebGL, preserving unrelated scripting defines.

Dissonance requires `BUN3_DISSONANCE`; the official NGO binding additionally requires `BUN3_DISSONANCE_NFGO`. Steam Audio requires both `BUN3_STEAMAUDIO` and the SDK's `STEAMAUDIO_ENABLED`. Combined voice path playback requires both SDKs. A leftover `STEAMAUDIO_ENABLED` alone does not activate Bun3 adapters. Runtime, Editor and test assemblies share these gates.

Before removing SDK assets, remove or guard application references to adapter types, then run **Tools > Bun3 > Audio > Disable Adapters** (`Bun3.Unity.Audio.Editor.SoundSdkSetup.DisableAdapters`) and let compilation finish. This removes the three Bun3 symbols on the four supported targets and preserves the SDK-owned `STEAMAUDIO_ENABLED` flag; optional packages can remain installed. Synchronize again after installing or removing SDK components. Activation is explicit and does not run automatically on domain reload. Deleting SDK files outside the Editor while old activation symbols remain is not an automatically recoverable cold-start workflow; restore the SDK or clear the managed symbols before reopening.

## Distance profiles and final-path diagnostics

`SoundDef.DistanceAttenuation` and `SoundDef.AttenuationProfile` are exposed by the active `SteamAudioSoundOutput`. Applications apply these settings to the corresponding simulation handle with `SetSourceDistanceAttenuationEnabled`, `SetSourceMinimumDistance` and `SetSourceDistanceRange`. Read profile scalars on the control thread, not the audio callback. A null profile can fall back to the sound's existing minimum/maximum distances and a 0.2 edge fade. The shared `DistanceAttenuationProfile.Evaluate` curve runs inside each native path evaluation, before weighted SH accumulation. Do not apply the curve again to the rendered output. Disabling distance attenuation preserves occlusion and direction.

`TryGetPathDistanceRange` works with the stock SDK. Optional `SupportsPathDiagnostics`, `SetPathDiagnosticsEnabled`, `CopyPathDiagnostics` and `CopyPathDiagnosticPoints` additionally expose each actual native SH contribution, its virtual source and ordered probe vertices. The official library does not have this extension: capability is false, enabling returns false, and audio continues normally. See [native build and capture contract](Native~/README.md) for the versioned extension, supported Windows CPU configuration, buffer limits and rollback instructions. Validation-ray segments remain a distinct diagnostic API; they are not final routes.

### Editable Unity distance curves

Profiles now expose `VolumeByDistance` (AnimationCurve): X is the native evaluated distance in metres, Y is gain clamped to [0,1]. Apply `profile.GetSnapshot()` with `SetSourceDistanceCurve(handle, snapshot)` and independently set attenuation enabled. Snapshots copy keys and weighted tangents only after edits; warm access/evaluation allocates no managed memory. At and beyond the last key's time output is zero regardless of post-wrap mode. Empty curves are silent. Put the final key at zero for a continuous end. Passing null to SetSourceDistanceCurve restores legacy scalar settings. Existing serialized profiles migrate to editable Hermite keys; hidden scalar fields only preserve migration compatibility. Do not apply both scalar attenuation and the authored curve to a path.


## Planar acoustic runtime

`PlanarAcousticMap` stores the baked asset and planar grid geometry. `PlanarAcousticWorld` owns native simulation, source generations, dynamic obstacles and path diagnostics. Planar Unity XY coordinates map to native XZ; this is an explicit top-down runtime, not a general 3D scene solver.

Create `PlanarAcousticOutputs`, register `PlanarAcousticSfxBinding` instances, then create `PlanarAcousticSessionScope` with a world factory and optional geometry-update/release callbacks. Call `Tick` on the control thread with unscaled time and your simulation interval. The scope finds the active listener, gates outputs, prepares sources, simulates and publishes. It retains output registrations across device reconstruction and detaches them before native world disposal. `TryReinitialize` retries a failed world; `LastInitializationError` reports factory failure. Dispose the scope when the owning scene/session ends. The caller still owns SFX output disposal. Geometry callbacks must not mutate output registration.

World settings are supplied through `PlanarAcousticSettings`; applications may override its getters for live tuning. Dynamic obstacles use stable integer IDs and BoxCollider2D geometry. Game-specific door discovery, map authoring and balance policy remain application responsibilities.

The optional combined Dissonance adapter provides `DissonancePlanarAcousticOutput` and `IPlanarVoiceSettings` for the same registry. In the Editor, `PlanarAcousticDiagnostics.Capture` reads typed `IPlanarAcousticDiagnostics` sources without reflection, and `PlanarAcousticDiagnosticView` draws snapshots. Snapshot capture allocates and is intended for opt-in editor diagnostics, never audio callbacks. Applications own remote-editor transport and capture toggles.


## Near-field mono output

Override `PlanarAcousticSettings.MonoDistance` and `FullSpatialDistance` to collapse nearby output to identical channels. Package defaults (0/0) disable this behavior. Valid ranges require finite nonnegative near distance and a strictly greater far distance. Width is zero at/below near, one at/above far, and smoothstep between them. Invalid ranges or missing native path measurements retain full width.

`PlanarAcousticWorld.GetSpatialBlend` uses the shortest native evaluated path distance, never an inferred grid or straight-line distance. Both planar SFX and Dissonance bindings publish the same width through their parameter mailboxes. Multiple native contributions share this final output width. This is not per-path width processing.

`SteamAudioPathRenderer.RenderSpatial` processes native audio first, then scales the side component of its stereo output. Width zero duplicates the processed mid signal; width one preserves the original samples. Path EQ, gain, attenuation and independent output gates remain effective, with no dry-audio bypass. The first frame takes the requested width immediately; later changes slew at a full-range rate of 20 ms. Tails retain the target and reset clears width history. This operation needs no per-frame allocation. Summing HRTF output may change timbre or perceived loudness; it is intentionally not unprocessed microphone PCM.


### Shared and per-output width profiles

`PlanarAcousticSettings.SpatialBlendProfile` optionally supplies the world default; its inline distances remain the null-profile fallback. SFX bindings honor the effective SoundDef spatial group: inherit world defaults, or select a shared blend profile/inline range. Dissonance settings may additionally implement `IPlanarVoiceSpatialSettings` to supply a voice-specific profile; absent/null means world inheritance. Existing `IPlanarVoiceSettings` implementations remain compatible. Diagnostics read the output's effective width rather than assuming the world default.

### Definition preparation

For a direct `SoundDef`, call `cache.PrepareDefinition(definition)` and `soundSystem.Prepare(definition)` during loading. The cache uses effective playback clips, including Addressables overrides; nonpositional definitions bypass native PCM preparation. Null clip entries are ignored. `IsDefinitionPrepared` and `IsPrepared` are allocation-free readiness checks. Replacing clips requires another preparation call before positional playback. Budget or format failures leave the previously committed PCM unchanged, allowing the caller to reject one definition while keeping other sounds available.
