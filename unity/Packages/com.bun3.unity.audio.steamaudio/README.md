# Bun3 Unity Audio - Steam Audio Adapter

Optional [Steam Audio](https://valvesoftware.github.io/steam-audio/) integration for
[`com.bun3.unity.audio`](../com.bun3.unity.audio). It supplies a legacy Unity
spatializer binder, explicit native path rendering/simulation, and pooled native
SFX output. Native output consumes copied coefficients and produces stereo directly;
the core continues owning source gain, fades and mixer routing.

## Choose an integration route

| Route | Entry point | Responsibility |
| --- | --- | --- |
| SDK spatializer binding | `SteamAudioSoundSetup.Apply` | Configures pooled Unity sources; the installed SDK owns spatialization and scene simulation. |
| Native planar SFX | `SteamAudioSoundOutput` + `PlanarAcousticSessionScope` | Renders prepared PCM using baked paths, grid coverage, dynamic obstacles and shared profiles. |
| Custom native integration | `SteamAudioPathSimulationScope` + `SteamAudioPathRenderer` | Supplies explicit simulation and DSP primitives; the caller supplies geometry, scheduling, coverage policy and output transport. |

Use one spatialization owner per positional output. Native PCM output bypasses the Unity spatializer and does not need `SteamAudioSoundSetup.Apply`. Neither route supplies game-specific map discovery, networking, voice capture, room permissions or authoring UI. Music remains owned by the audio core.

## Install

The package declares Unity **6000.3.14f1**. Runtime and Editor assemblies directly reference `Bun3.Unity.Audio`, `Bun3.Unity.Acoustics` and the SDK's `SteamAudioUnity`; the two Bun3 packages are its declared UPM dependencies. Resolve the audio core's dependency chain as well, including core/common, UniTask and SerializeReference Extensions. See the [audio installation instructions](../com.bun3.unity.audio/README.md) for that chain.

For a Git installation, add these entries to the consuming project's `Packages/manifest.json` dependencies, alongside the audio core's prerequisites:

```json
{
  "com.bun3.unity.audio": "https://github.com/Bun3/bun3-kit.git?path=unity/Packages/com.bun3.unity.audio#Bun3/jp-sound-integration",
  "com.bun3.unity.acoustics": "https://github.com/Bun3/bun3-kit.git?path=unity/Packages/com.bun3.unity.acoustics#Bun3/jp-sound-integration",
  "com.bun3.unity.audio.steamaudio": "https://github.com/Bun3/bun3-kit.git?path=unity/Packages/com.bun3.unity.audio.steamaudio#Bun3/jp-sound-integration"
}
```

This branch is a moving development reference, not a release tag. UPM does not infer sibling Git paths from a package's numeric dependency declarations, and Git dependencies must be resolved at the project manifest level; installing only the adapter URL is insufficient without a registry or explicit project entries for its dependency chain. Embedded packages or Package Manager's **Add package from disk** using each package's `package.json` are alternatives. SDK import is separate from all UPM routes.

Steam Audio ships as a legacy `.unitypackage`, not a UPM package, so it is
installed once per game project (not referenced from `manifest.json`):

1. Download the Unity integration zip for Steam Audio **v4.8.1** from
   [github.com/ValveSoftware/steam-audio/releases](https://github.com/ValveSoftware/steam-audio/releases) —
   grab `steamaudio_unity_<version>.zip` and extract `SteamAudio.unitypackage`
   from it (the zip also has FMOD/Wwise variants — not needed for plain Unity
   audio).
2. In the Unity Editor: **Assets > Import Package > Custom Package...**,
   select `SteamAudio.unitypackage`, keep everything selected, **Import**.
   This adds `Assets/Plugins/SteamAudio/`; the imported SDK integration supplies
   `STEAMAUDIO_ENABLED`. After SDK compilation completes, run **Tools > Bun3 >
   Audio > Sync Installed Adapters** to opt into the Bun3 adapter. Its assemblies
   require both `BUN3_STEAMAUDIO` and `STEAMAUDIO_ENABLED` and reference
   `SteamAudioUnity`. See the removal workflow below before deleting SDK assets.
3. For the legacy `SteamAudioSoundSetup` source-binder route, open **Edit > Project Settings > Audio** and set **Spatializer Plugin** to
   **Steam Audio Spatializer**. The adapter's editor validator
   (`SteamAudioSetupValidator`) warns on domain load when a different nonempty spatializer is selected. An empty selection does not produce a warning; explicitly verify the setting for this route.

This package itself is a normal embedded UPM package
(`unity/Packages/com.bun3.unity.audio.steamaudio`) depending on
`com.bun3.unity.audio` and `com.bun3.unity.acoustics`; it does not declare a Steam Audio UPM dependency
because none exists. (This repo's own dev-project fetch of the
`.unitypackage` — used to build/test the adapter itself — is documented
separately in `unity/Vendor/README.md`.)

## Legacy spatializer setup

```csharp
using Bun3.Unity.Audio;
using Bun3.Unity.Audio.SteamAudio;

var config = SteamAudioSoundSetup.Apply(new SoundSystemConfig
{
    SfxVoices = 24,
});
var sound = new SoundSystem(config);
```

This fragment configures the binder only. Provide an active AudioListener, the SDK scene/geometry setup required for occlusion, a prepared SoundDef and a positional `SoundSystem.Play` call to hear it. Dispose the SoundSystem when its owner ends.

`Apply`:

- Sets `SoundSystemConfig.OcclusionChecksPerFrame = 0` — the framework's off
  switch for the core occlusion/low-pass pipeline: no raycasts run and no
  `LowPassFilter` components are attached, since Steam Audio's spatializer
  owns spatialization and occlusion instead. The binder uses effective definition occlusion and the already-configured source spatial blend, including profile and per-play spatial choices.
- Chains a per-voice binder onto `SoundSystemConfig.OnVoiceConfigured`
  (running after any hook already set on the config). Per SFX play, the
  binder:
  - Adds a `SteamAudio.SteamAudioSource` to the voice's `AudioSource` GameObject on its first spatial play (pooled voices keep it afterward). Initial 2D playback does not add one.
  - Sets `AudioSource.spatialize` from the configured `AudioSource.spatialBlend` (`> 0`).
  - For 3D sounds (`Positional`/`Follow`): enables the `SteamAudioSource` and
    sets its `occlusion` field from `SoundDef.EffectiveOcclusion`.
  - For 2D sounds: disables any existing `SteamAudioSource`.
  - Leaves `occlusionType`, `occlusionInput`, and the `transmission*` fields
    at Steam Audio's own defaults — this adapter maps spatialization/occlusion
    on/off only, not per-def transmission tuning.
- Is idempotent: calling `Apply` again on the same config does not
  double-register the binder.

`Apply` returns the same `config` it was given, for chaining into
`SoundSystem`'s constructor.

## Minimal planar SFX component

Prerequisites:

- Import and activate the SDK as described above. In a custom application asmdef, reference `Bun3.Unity.Audio` and `Bun3.Unity.Audio.SteamAudio`, and gate it with `BUN3_STEAMAUDIO` and `STEAMAUDIO_ENABLED` if it is optional.
- Supply a valid `PlanarAcousticMap` whose bake, occupancy mask and native coordinate frame describe the same geometry. Creating a map is a loading/editor task described below; this component does not bake an empty map automatically.
- Assign a SoundDef with effective spatial mode **Positional**, a loaded mono/stereo **Decompress On Load** clip, and an audible volume/distance profile. Resolve Addressables before preparation if used. Place this component and the active AudioListener inside connected walkable cells and the baked probe coverage.
- Use a working stereo Unity audio output and unmuted mixer. The example targets a fixed device configuration; replacing the SFX pool after an audio-format change is an application lifetime decision.

Save as `PlanarSoundExample.cs`, attach it to a scene object, and assign both assets:

```csharp
using System;
using Bun3.Unity.Audio;
using Bun3.Unity.Audio.SteamAudio;
using UnityEngine;

public sealed class PlanarSoundExample : MonoBehaviour
{
    [SerializeField] private PlanarAcousticMap map = null;
    [SerializeField] private SoundDef sound = null;

    private SteamAudioClipCache cache;
    private SoundSystem sounds;
    private PlanarAcousticOutputs outputs;
    private PlanarAcousticSessionScope session;

    private void Start()
    {
        try
        {
            if (map == null || sound == null)
                throw new InvalidOperationException("Assign a map and positional sound.");

            outputs = new PlanarAcousticOutputs();
            cache = new SteamAudioClipCache(64L * 1024 * 1024);
            cache.PrepareDefinition(sound);
            sounds = new SoundSystem(new SoundSystemConfig
            {
                SfxVoices = 8,
                OcclusionChecksPerFrame = 0,
                CreateVoiceOutput = CreateOutput
            });
            sounds.Prepare(sound);
            session = new PlanarAcousticSessionScope(
                outputs,
                () => new PlanarAcousticWorld(map, new PlanarAcousticSettings()),
                updateGeometry: null,
                releaseGeometry: null);

            if (session.World == null)
                throw new InvalidOperationException(
                    "Could not initialize acoustics.", session.LastInitializationError);

            sounds.Play(sound, transform.position);
        }
        catch
        {
            Release();
            throw;
        }
    }

    private ISoundVoiceOutput CreateOutput(AudioSource source)
    {
        var output = new SteamAudioSoundOutput(source, cache);
        outputs.Register(new PlanarAcousticSfxBinding(source, output));
        return output;
    }

    private void Update()
    {
        session?.Tick(Time.unscaledTime, Time.unscaledTimeAsDouble, 0.05f);
    }

    private void OnDestroy() => Release();

    private void Release()
    {
        session?.Dispose();
        session = null;
        outputs?.Clear();
        sounds?.Dispose();
        sounds = null;
        cache?.Dispose();
        cache = null;
    }
}
```

`SoundSystem` drives its own core update through the player loop; only the acoustic session needs the explicit `Tick`. Playback starts blocked until a covered native result is published. `SoundSystem` owns and disposes the outputs returned by `CreateVoiceOutput`; the registry only owns registrations. The closure and buffers are created during initialization, outside steady-state playback. This component intentionally plays once from its starting position; use the core's follow overload with an effective Follow definition for moving sounds.

The session retries its world factory on audio-device changes, preserving registrations. It does **not** reconstruct the SoundSystem/output pool. If sample rate or DSP frame size changes, dispose the session, SoundSystem and cache in that order and build a fresh prepared pool. `TryReinitialize` alone cannot repair an old output format.

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

After geometry changes, commit the caller-owned scene and call `InvalidateGeometry` with an increased revision before simulating. Pending and failed results contain zero signal but are not classified as physically blocked. `Valid` means a finite native computation completed; `HasPathSignal` does not establish probe coverage or geometric reachability. Callers must validate endpoint coverage before using results. Use `TryGetPathDistanceRange` for measured native attenuation-callback distances; these are not inferred grid distances.

Sources use generation handles, nonthrowing capacity failure and explicit removal. `SetOcclusion` supports raycast and fractional volumetric direct occlusion with a constructor-bounded sample count. Transmission and source directivity are not enabled. Dispose on the creating thread; the native scene, probes and context remain retained until disposal. This scope requires the 64-bit Steam Audio 4.8.1 ABI. Native simulation tests cover door-close/reopen simulation, partial occlusion, authored minimum-distance near/far and indirect SH, zero/invalid values and warm callback updates; game geometry authoring is a separate layer.

## Baked acoustic assets

`SteamAudioAcousticBaker.Bake` accepts native-coordinate mesh arrays, materials, exact probe spheres, `SteamAudioPathBakeSettings` and a nonblank geometry fingerprint. It performs a cold synchronous pathing bake and returns a new `SteamAudioAcousticAsset`; callers own saving or destroying that ScriptableObject. Mesh upload buffers and native serialization objects have explicit cleanup. No asset-database mutation or map-specific geometry generation occurs inside the baker.

The asset copies scene/probe blobs, settings and probe spheres. `ProbeCount` and `GetProbe` expose exact immutable probe values for coverage validation. SDK/format metadata and checksums over both blobs and metadata are validated before loading; these checks detect accidental corruption and do not authenticate arbitrary native data. Bake and load also require a nonempty native pathing data layer.

`SteamAudioAcousticSceneScope(context, asset)` retains a context and owns loaded `Scene` and `Probes`. Its `Context`, `Scene`, and `Probes` properties return borrowed wrappers for same-control-thread simulation and dynamic mesh edits. Commit edits before invalidating the simulation revision. Dispose simulations before the load scope normally; independently retained native simulation handles still survive loader disposal.

Create/load/dispose on the Unity main/control thread. Supported SDK deserialization constructors are used without reflection or source modifications. If native loading itself throws, an SDK-internal serialized temporary may be reclaimed by its finalizer; successfully returned scope wrappers and temporary Unity objects are cleaned deterministically. Asset tests exercise the real native library, including persisted asset reload and continued simulation after loader/context release.

`SteamAudioMeshScope` retains a scene and a copied native-coordinate mesh for cold-created dynamic obstacles. `Add`/`Remove` change membership; callers commit the scene and invalidate simulation snapshots afterward. Disposal removes and releases the mesh on its creating thread without implicitly committing the scene.

## Native pooled SFX output

`SteamAudioSoundOutput` implements the audio core's optional `ISoundVoiceOutput` seam. Create one output per prewarmed source through `SoundSystemConfig.CreateVoiceOutput`, retain the output/source pairs for application simulation updates, and prepare every positional clip before playback:

```csharp
var cache = new SteamAudioClipCache(maximumDecodedBytes: 64L * 1024 * 1024);
cache.PrepareClips(positionalClips);
var config = new SoundSystemConfig
{
    SfxVoices = 24,
    OcclusionChecksPerFrame = 0,
    CreateVoiceOutput = source => new SteamAudioSoundOutput(source, cache)
};
var sounds = new SoundSystem(config);
```

This construction fragment does not yet publish simulation parameters or prepare the core definition. Use the complete planar example above, or implement the lower-level publication/coverage contract below, before positional playback.

The cache budget counts managed float PCM in addition to Unity's own asset memory. Preparation accepts loaded mono/stereo Decompress On Load clips and commits a batch only after its complete validation/copy succeeds. Runtime-created streaming clips can report Decompress On Load while still rejecting `GetData`; Unity emits a readability diagnostic and the cache rejects them. No loading/decompression/cache growth occurs inside Play or an audio callback. Disposing the cache drops its references; outputs retain their current immutable PCM through retirement and release it during reuse/disposal.

Each output prewarms a renderer, buffers, reusable parameter storage and a looping stereo flatline clip. Its source filter reads cached PCM, applies forward pitch through a linear interpolating cursor, downmixes stereo positional input to a point source, and renders native stereo. It multiplies that output into the driver envelope, preserving Unity source/fade/mixer gain once. Unity spatialization, rolloff and Doppler are bypassed for handled positional voices. Nonpositional/UI requests keep the original clip and pass through untouched. Missing prepared data or unsupported output format returns Unavailable, never dry positional fallback.

Logical pitch clamps to 0..3; zero freezes input and effect progress while writing silence. This implements forward pitch/rate semantics, not Unity's proprietary resampler. Natural completion drains the native terminal input overlap and convolution tail, then observes one subsequent source-filter block before releasing the core voice. Stop/fade/steal/dispose retire immediately. A held reader delays reset/reuse and native release; an independent precreated main-thread host survives source destruction until that reader releases. No new clip, source, PCM array or producer object is needed per play. Output sample-rate/DSP-frame changes fail closed and require a cold replacement output pool.

`CurrentParameters` is a valid immutable `PathParameterLease` only for an active handled request; `MaxDistance` exposes that request's authored range and `MinDistance` its nonnegative minimum. Forward `MinDistance` to the corresponding simulation source before its next run; user/bus gain belongs to the Unity source. Invalid nonfinite minimum distances return Unavailable. `StartBlocked` defaults true. Publish a current copied native result through `lease.TryPublish(sh, new PathRenderSettings(...))`, then explicitly call `lease.TrySetBlocked(false)` once coverage and application policy permit output. Keep the token itself; old tokens cannot acquire a replacement generation. Path gain excludes user/bus volume and distance attenuation already encoded in SH. Probe coverage, geometry revisions and room permissions remain application-owned.

`SteamAudioPathRenderer.CreateDefault(rate, frame)` creates a retained default context/HRTF pair without requiring map simulation to exist first. A custom renderer factory can instead share properly retained native resources across the pool. `PathParameterMailbox` supplies one fixed three-slot implementation to SFX and voice consumers; one control writer and one processing reader are supported. Reusing its storage requires retirement and reader quiescence, with exclusive admission during the generation transition.

The PlayMode regression tests cover native source-filter output from prepared stereo PCM, source and mixer gain measured through `AudioListener.GetOutputData`, pitch/completion, pause/tail/reuse, source destruction under a held processing claim, warm allocations and delayed-reader admission. A dedicated EditMode test exercises the default renderer factory. These test contracts concern engine PCM behavior; they do not establish hardware audibility or every platform's backend. See the test section below for running the current suites.

## Optional SDK activation

Adapters are disabled by default. Import the required SDKs and let Unity finish compiling, then run **Tools > Bun3 > Audio > Sync Installed Adapters**. The SDK-independent `Bun3.Unity.Audio.Editor.SoundSdkSetup.SyncInstalledAdapters` method is also available through Unity `-executeMethod` or Editor automation. It checks SDK assembly-definition assets and required loaded types before enabling adapters on Standalone, Android, iOS and WebGL, preserving unrelated scripting defines.

Dissonance requires `BUN3_DISSONANCE`; the official NGO binding additionally requires `BUN3_DISSONANCE_NFGO`. Steam Audio requires both `BUN3_STEAMAUDIO` and the SDK's `STEAMAUDIO_ENABLED`. Combined voice path playback requires both SDKs. A leftover `STEAMAUDIO_ENABLED` alone does not activate Bun3 adapters. Runtime, Editor and test assemblies share these gates.

Before removing SDK assets, remove or guard application references to adapter types, then run **Tools > Bun3 > Audio > Disable Adapters** (`Bun3.Unity.Audio.Editor.SoundSdkSetup.DisableAdapters`) and let compilation finish. This removes the three Bun3 symbols on the four supported targets and preserves the SDK-owned `STEAMAUDIO_ENABLED` flag; optional packages can remain installed. Synchronize again after installing or removing SDK components. Activation is explicit and does not run automatically on domain reload. Deleting SDK files outside the Editor while old activation symbols remain is not an automatically recoverable cold-start workflow; restore the SDK or clear the managed symbols before reopening.

## Distance profiles and final-path diagnostics

`SteamAudioSoundOutput.Acoustics` exposes the resolved `SoundAcousticSettings`. A selected `SoundDef.SpatialProfile` has highest precedence; otherwise the SoundDef's `Acoustics` selection resolves its shared `SoundAcousticProfile` or retained local settings. The output applies distance enablement, curve or fallback minimum/maximum range, and mono/full-width selection from that one resolved value. Read profile snapshots on the control thread, not the audio callback. A null attenuation profile falls back to the resolved minimum/maximum distances and a 0.2 edge fade. The snapshot produced by `DistanceAttenuationProfile.GetSnapshot()` runs inside each native path evaluation, before weighted SH accumulation. Do not apply the curve again to the rendered output. Disabling distance attenuation preserves occlusion and direction.

`TryGetPathDistanceRange` works with the stock SDK. Optional `SupportsPathDiagnostics`, `SetPathDiagnosticsEnabled`, `CopyPathDiagnostics` and `CopyPathDiagnosticPoints` additionally expose each actual native SH contribution, its virtual source and ordered probe vertices. The official library does not have this extension: capability is false, enabling returns false, and audio continues normally. See [native build and capture contract](Native~/README.md) for the versioned extension, supported Windows CPU configuration, buffer limits and rollback instructions. Validation-ray segments remain a distinct diagnostic API; they are not final routes.

### Editable Unity distance curves

Profiles expose `VolumeByDistance` (AnimationCurve): X is the native evaluated distance in metres, Y is gain clamped to [0,1]. Apply `profile.GetSnapshot()` with `SetSourceDistanceCurve(handle, snapshot)` and independently set attenuation enabled. Snapshots copy keys and weighted tangents only after edits; warm access/evaluation allocates no managed memory. At and beyond the last key's time output is zero regardless of post-wrap mode. Empty curves are silent. Put the final key at zero for a continuous end. Passing null to `SetSourceDistanceCurve` restores scalar range evaluation. New profiles default to the 1 m/15 m inverse-distance curve with a 0.2 edge fade. `CreateInverseDistanceCurve` creates the same editable curve for another range. Do not apply both scalar attenuation and the authored curve to a path.


## Planar acoustic runtime

`PlanarAcousticMap` stores the baked asset and planar grid geometry. `PlanarAcousticWorld` owns native simulation, source generations, dynamic obstacles and path diagnostics. Planar Unity XY coordinates map to native XZ; this is an explicit top-down runtime, not a general 3D scene solver.

Create `PlanarAcousticOutputs`, register `PlanarAcousticSfxBinding` instances, then create `PlanarAcousticSessionScope` with a world factory and optional geometry-update/release callbacks. Call `Tick` on the control thread with unscaled time and your simulation interval. The scope finds the active listener, gates outputs, prepares sources, simulates and publishes. It retains output registrations across device reconstruction and detaches them before native world disposal. `TryReinitialize` retries a failed world; `LastInitializationError` reports factory failure. Dispose the scope when the owning scene/session ends. The caller still owns SFX output disposal. Geometry callbacks must not mutate output registration.

World settings are supplied through `PlanarAcousticSettings`; applications may override its getters for live tuning. Dynamic obstacles use stable integer IDs and BoxCollider2D geometry. Game-specific door discovery, map authoring and balance policy remain application responsibilities.

The optional combined Dissonance adapter provides `DissonancePlanarAcousticOutput` for the same registry and consumes `IResolvedSoundAcousticSettings`. In the Editor, `PlanarAcousticDiagnostics.Capture` reads typed `IPlanarAcousticDiagnostics` sources without reflection, and `PlanarAcousticDiagnosticView` draws snapshots. Snapshot capture allocates and is intended for opt-in editor diagnostics, never audio callbacks. Applications own remote-editor transport and capture toggles.


## Near-field mono output

Override `PlanarAcousticSettings.MonoDistance` and `FullSpatialDistance` to collapse nearby output to identical channels. Package defaults (0/0) disable this behavior. Valid ranges require finite nonnegative near distance and a strictly greater far distance. Width is zero at/below near, one at/above far, and smoothstep between them. Invalid ranges or missing native path measurements retain full width.

`PlanarAcousticWorld.GetSpatialBlend` uses the shortest native evaluated path distance, never an inferred grid or straight-line distance. Both planar SFX and Dissonance bindings publish the same width through their parameter mailboxes. Multiple native contributions share this final output width. This is not per-path width processing.

`SteamAudioPathRenderer.RenderSpatial` processes native audio first, then scales the side component of its stereo output. Width zero duplicates the processed mid signal; width one preserves the original samples. Path EQ, gain, attenuation and independent output gates remain effective, with no dry-audio bypass. The first frame takes the requested width immediately; later changes slew at a full-range rate of 20 ms. Tails retain the target and reset clears width history. This operation needs no per-frame allocation. Summing HRTF output may change timbre or perceived loudness; it is intentionally not unprocessed microphone PCM.


### Shared and per-output width profiles

`PlanarAcousticSettings.SpatialBlendProfile` optionally supplies the world default; its inline distances remain the null-profile fallback. SFX bindings honor the resolved SoundDef acoustics: inherit world defaults, or select a shared blend profile/inline range. Voice bindings created with `DissonancePlanarAcousticOutput.FromAcoustics` use the same `IResolvedSoundAcousticSettings` contract. Diagnostics read the output's effective width rather than assuming the world default.

### Definition preparation

For a direct `SoundDef`, call `cache.PrepareDefinition(definition)` and `soundSystem.Prepare(definition)` during loading. The cache uses effective playback clips, including Addressables overrides; nonpositional definitions bypass native PCM preparation. Null clip entries are ignored. `IsDefinitionPrepared` and `IsPrepared` are allocation-free readiness checks. Replacing clips requires another preparation call before positional playback. Budget or format failures leave the previously committed PCM unchanged, allowing the caller to reject one definition while keeping other sounds available.

## Bake and map authoring workflow

1. Produce validated native-coordinate vertices, triangles, per-triangle material indices, materials and exact probe spheres. `com.bun3.unity.acoustics` supplies deterministic grid geometry and probe utilities; gameplay code supplies occupancy and authoring policy. No automatic collider scan or bake menu is provided here.
2. Create an SDK `SteamAudio.Context` on the control thread and call `SteamAudioAcousticBaker.Bake(context, vertices, triangles, materialIndices, materials, probes, SteamAudioPathBakeSettings.Default, geometryFingerprint)`. Keep a nonblank fingerprint tied to the authored geometry. The default settings use 1 visibility sample, 0.05 radius, 0.99 threshold, 4.5 visibility range, 100 path range and 1 bake thread. Tune and rebake deliberately; baking is synchronous and unsuitable for frame/audio callbacks.
3. Save the returned `SteamAudioAcousticAsset` using application Editor code (`AssetDatabase.CreateAsset`), or retain it for a runtime session. The caller owns this ScriptableObject and the context; release the context with its SDK `Release()` after use. Persisted blobs include checksums, SDK/format metadata and exact probe spheres.
4. Create a `PlanarAcousticMap` using `ScriptableObject.CreateInstance<PlanarAcousticMap>()`, then call `Initialize(asset, blockedMask, frame, floor, ceiling, ear)`. The mask is indexed `[x, y]`; true means blocked. The frame is an `AcousticGridFrame` from `Bun3.Unity.Acoustics` in native coordinates with `Vector3.up` height axis. Require finite `floor < ear < ceiling`. Persist the map too, keeping its baked-asset reference valid.
5. Instantiate a world/session and check source/listener coverage before accepting audible results. Unity `(x, y)` positions map to native `(x, frame.Origin.y + earHeight, -y)`. The current world uses order-one SH (four coefficients) and a fixed listener orientation suitable for a top-down game; it does not track listener-transform rotation.

The fingerprint is metadata, not automatic comparison with current game geometry. Rebuild and replace the asset/map when static authoring changes. For dynamic geometry, call `PlanarAcousticWorld.SetObstacle(stableId, boxCollider2D, closed)` and `RemoveObstacle(stableId)`. These manage native mesh membership, scene commits, geometry revision and grid connectivity. New or resized/moved obstacles can allocate; unchanged obstacle updates are the warm path. A session geometry callback should report whether geometry changed so simulation runs immediately, and must not change the output registry. Call `outputs.Block()` before an externally scheduled geometry transition if outputs must be silent throughout it.

## Ownership, threads and allocation limits

| Object | Lifetime and thread contract |
| --- | --- |
| SoundSystem / SteamAudioSoundOutput | Create/dispose from Unity control code. SoundSystem owns factory-returned outputs; native processing runs through the source audio filter. Held processing readers defer final native release. |
| SteamAudioClipCache | Prepare during loading on the Unity control thread. Its fixed byte budget covers decoded float PCM; disposing drops cache references, while active outputs may still retain their PCM. |
| PlanarAcousticSessionScope | Owns world reconstruction and device subscription, not playback outputs or cached clips. Dispose before the SoundSystem/output pool. |
| PlanarAcousticWorld / simulation / mesh scopes | Use their creating control thread; dispose in dependency order. Source capacity is fixed and registration can fail without throwing. |
| SteamAudioAcousticSceneScope | Owns loaded scene/probes and a retained context. Exposed wrappers are borrowed; callers must not release them. |
| SteamAudioPathRenderer | One processing owner. Quiesce that owner before reset/disposal; no concurrent-disposal protection or finalizer. |
| PathParameterMailbox / leases | One control writer and one processing reader. Keep generation-bound leases; stale leases cannot write into reused output generations. |

Warm native render/simulation and prepared playback paths reuse storage. This is not a promise that every operation allocates zero memory: construction, baking, clip decoding, listener discovery (`FindObjectsByType`), changed profile snapshots, new obstacle geometry and Editor diagnostic capture allocate. The session searches for a listener again when needed, at up to one-second intervals while none is found. Avoid profile edits and diagnostics capture in audio callbacks.

The low-level renderer supports orders 0 through 3; planar bindings use order 1. Native simulation requires the supported 64-bit, three-band 4.8.1 ABI and default scene implementation. PCM cache input is mono or stereo; spatial stereo is downmixed to a point source. Output is stereo. Native output uses forward pitch clamped to 0..3, not reverse playback. Transmission and source directivity are not enabled by the simulation scope. SDK activation on a build target does not validate native library support for that platform.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| Adapter namespace is unavailable | SDK `SteamAudioUnity` assembly and required types must compile first; run Sync Installed Adapters and inspect both scripting defines. |
| UPM cannot resolve a Bun3 dependency | Add explicit Git/embedded entries for the complete chain, or configure the registry serving the declared versions. |
| Legacy spatializer has no effect | Select Steam Audio Spatializer and supply SDK scene/geometry setup. The validator does not warn for an empty selection. |
| Native positional sound is silent | Check definition/cache preparation, active listener, mono/stereo readable clip, positive profile gain, connected grid cells and probe coverage. Native output deliberately starts blocked. |
| Output becomes unavailable | Inspect `SteamAudioSoundOutput.Failure` and `Fault`; check clip preparation, retired readers, sample rate and DSP frame size. Rebuild the output pool after a format change. |
| A source never receives a path | Check the world's source capacity (default 64), generation validity, simulation Tick and current geometry. Stale or missing results remain blocked. |
| World creation fails or stays null | Inspect `LastInitializationError`; check asset validation/native libraries, then call `TryReinitialize` after correcting the cause. A Tick exception releases the world and is rethrown. |
| Gain falls faster than expected | Remove duplicate distance/user-volume multiplication. Native SH includes distance attenuation; the core owns source/fade/mixer gain. |
| Near-field output stays fully spatial | Check effective profile inheritance, a valid near/far range, and available native path distances. Defaults 0/0 disable mono collapse. |
| Final-path diagnostics are unavailable | Stock SDK behavior is expected. Check capability before enabling the optional native extension; validation rays are not final weighted routes. |
| Door or static-map edits do not match sound | Keep topology, baked geometry and probe coverage consistent. Low-level scene edits require commit plus increased simulation geometry revision. |

## Tests and validation

Tests are included under `Tests/Runtime`, `Tests/Editor` and `Tests/PlayMode`. All require the installed SDK and both adapter defines. Runtime and Editor test asmdefs additionally require `UNITY_INCLUDE_TESTS`; the PlayMode asmdef uses Unity's `TestAssemblies` optional reference. For a package installed through UPM, add `com.bun3.unity.audio.steamaudio` to the consuming manifest's `testables` array and install/configure Unity Test Framework.

Use **Window > General > Test Runner** and select the relevant assemblies:

- `Bun3.Unity.Audio.SteamAudio.Tests`: setup idempotence and per-voice binding.
- `Bun3.Unity.Audio.SteamAudio.Editor.Tests`: native renderer/tails/factory, simulation, bake persistence, profiles, planar world/session and near-field behavior.
- `Bun3.Unity.Audio.SteamAudio.PlayMode.Tests`: clip-cache and actual source-filter output, gain routing, retirement/reuse and warm allocations.

Run both EditMode and PlayMode suites on a machine with the supported native library and functioning Unity audio processing; an audio-disabled/headless run cannot substitute for source-filter tests. Optional extension tests detect the capability of the loaded library. Review the result XML/log from the current run rather than treating historical counts as a compatibility guarantee. This README's examples are matched to the public source API; documentation maintenance alone does not establish a fresh Unity compile, test pass or listening result.
