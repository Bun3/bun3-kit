# Bun3 Unity Audio

A lightweight sound manager for Unity: a prewarmed `AudioSource` pool driven by a
single player-loop tick. The service itself requires no MonoBehaviour or coroutine.
Prepared playback and tick paths reuse storage; setup, first-time preparation,
profile changes, Addressables and cancellation are cold paths that can allocate.

Features:

- Generation-validated `SoundHandle` — safe to hold after a voice ends or its
  slot is stolen; every member no-ops on a stale handle instead of throwing.
- Coroutine-free fades (fade-in on play, fade-out on stop).
- Per-`SoundDef` voice limits (oldest steals when exceeded), cooldowns, and
  pitch/volume variation.
- 2D, fixed-position, and transform-following 3D playback.
- Logical channel volumes (`Master` / `Music` / `Sfx` / `Voice`) via an
  `AudioMixer`.
- Optional UniTask-based awaiting (`PlayAsync`, `SoundHandle.WaitAsync`).
- Music subsystem: sample-accurate intro+loop handoff, crossfade with
  newest-wins channel stealing, pause/resume, and awaitable transitions
  (`PlayMusicAsync`, `StopMusicAsync`).
- Occlusion: round-robin per-frame evaluation with a pluggable
  `IOcclusionProvider` (built-in single-linecast default), smoothed
  volume attenuation and low-pass filtering.
- Optional timescale-scaled SFX pitch (`PitchWithTimescale`) and a thin
  `TransitionTo` wrapper over `AudioMixerSnapshot` transitions.
- Bundled default `AudioMixer` (`Bun3DefaultAudioMixer`) so channel volumes
  and routing work with zero mixer setup.

## Install

The package declares Unity **6000.3.14f1** as its minimum. For the development
branch, use **Window > Package Manager > Install package from Git URL**:

```text
https://github.com/Bun3/bun3-kit.git?path=unity/Packages/com.bun3.unity.audio#Bun3/jp-sound-integration
```

Git must be available and the machine must have repository access. Commit both
`Packages/manifest.json` and `Packages/packages-lock.json`; the lock records the
resolved commit while the branch moves. No developer-specific absolute path is needed.

A Git install does not discover sibling monorepo packages. Unless a registry
supplies them, resolve Bun3 dependencies explicitly in the consuming manifest.
Merge this **dependency fragment** with existing dependencies:

```json
{
  "com.bun3.common": "https://github.com/Bun3/bun3-kit.git?path=common/src/com.bun3.common#Bun3/jp-sound-integration",
  "com.bun3.unity.core": "https://github.com/Bun3/bun3-kit.git?path=unity/Packages/com.bun3.unity.core#Bun3/jp-sound-integration",
  "com.bun3.unity.audio": "https://github.com/Bun3/bun3-kit.git?path=unity/Packages/com.bun3.unity.audio#Bun3/jp-sound-integration",
  "com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask",
  "com.mackysoft.serializereference-extensions": "https://github.com/Bun3/Unity-SerializeReferenceExtensions.git?path=Assets/MackySoft/MackySoft.SerializeReferenceExtensions",
  "com.mrdav30.fixedmathsharp.lean": "https://github.com/mrdav30/FixedMathSharp-Unity.git?path=/com.mrdav30.fixedmathsharp.lean#v7.0.0",
  "com.unity.addressables": "2.10.2"
}
```

These versions match the repository's authoring setup. Preserve compatible versions
already resolved by your project rather than downgrading them. The non-audio
libraries above are transitive dependencies of the shared Bun3 foundation.

Reference `Bun3.Unity.Audio` from an application asmdef. Also reference `UniTask`
and `Unity.Addressables` when your own code uses them. **Addressables is required
even for direct clips** by the current runtime asmdef. Its version define supplies
`BUN3_ADDRESSABLES` to the package assembly, not globally to application code.
No Dissonance or Steam Audio SDK is needed for basic playback.

Using [Steam Audio](https://valvesoftware.github.io/steam-audio/) for
spatialization/occlusion? See the optional
[`com.bun3.unity.audio.steamaudio`](../com.bun3.unity.audio.steamaudio) adapter.

A runnable, asset-free demo (procedurally-generated music + SFX) is available via
**Package Manager > Bun3 Unity Audio > Samples > Audio Demo**.

## Responsibilities

This package owns audible SFX/music playback, pooling, fades, routing and selected
external-source controls. It does not transmit voice packets, interpret AI hearing,
decide game voice groups, or synchronize volume preferences over a network.

| Need | Package |
|---|---|
| Audible pooled playback, definitions and profiles | This package |
| Logical sound events interpreted by gameplay | `com.bun3.unity.sound-events` |
| Grid geometry and connectivity | `com.bun3.unity.acoustics` |
| Native acoustic simulation and SFX rendering | `com.bun3.unity.audio.steamaudio` |
| Dissonance rooms, activity and output bridge | `com.bun3.unity.audio.dissonance` |
| NGO voice identity/session binding | `com.bun3.unity.audio.dissonance.netcode` |
| Dissonance PCM rendered through Steam Audio | `com.bun3.unity.audio.dissonance.steamaudio` |

## Quick start

Create a **Bun3 > Audio > Sound Def** asset, assign a non-null clip, and leave Spatial
at None for a UI sound. Assign it to this component in a scene with an active
`AudioListener`. Use an application-level owner for audio that survives scene changes.

```csharp
using Bun3.Unity.Audio;
using UnityEngine;

public sealed class AudioExample : MonoBehaviour
{
    [SerializeField] private SoundDef click = null;
    private SoundSystem sound;
    private SoundHandle lastClick;

    private void Awake()
    {
        sound = new SoundSystem(new SoundSystemConfig { SfxVoices = 24 });
        sound.Prepare(click);
    }

    private void Start()
    {
        sound.SetChannelVolume(SoundChannel.Master, 0.8f);
    }

    public void PlayClick() => lastClick = sound.Play(click);
    public void StopClick() => lastClick.Stop(fadeOut: 0.1f);

    private void OnDestroy() => sound?.Dispose();
}
```

`Prepare` reserves cooldown bookkeeping. It does not load Addressables or prepare
a native adapter's PCM cache. Perform those operations separately before playback.
The service inserts its own player-loop callback: do not tick it from Update.

## Usage

The snippets below are method-body fragments. Supply the named assets, positions,
transforms and mixer references. Put asynchronous snippets in an `async UniTask`
method. Each independently constructed service must eventually be disposed.

```csharp
using Bun3.Unity.Audio;
using UnityEngine;

// Create once (e.g. in a bootstrap script) and keep it alive for the app lifetime.
var sound = new SoundSystem(new SoundSystemConfig
{
    Mixer = myMixer,
    SfxGroup = mySfxGroup,
    SfxVoices = 24,
});

// Fire-and-forget playback.
SoundHandle handle = sound.Play(mySoundDef);
sound.Play(mySoundDef, worldPosition);      // fixed 3D position
sound.Play(mySoundDef, followTransform);    // tracks a Transform every frame
sound.Play(mySoundDef, fadeIn: 0.3f);       // ramps volume from silence

// Stop, optionally fading out.
handle.Stop(fadeOut: 0.2f);

// Channel volumes (linear 0..1), persisted by the game.
sound.SetChannelVolume(SoundChannel.Music, 0.5f);
var current = sound.GetChannelVolume(SoundChannel.Music);

// UniTask awaiting — completes on natural end, steal, or Stop.
await sound.PlayAsync(mySoundDef);
await handle.WaitAsync();

// Or a callback instead of awaiting — fires once, with the original (now-stale) handle.
sound.SetCompletionCallback(handle, h => Debug.Log("voice ended"));

// Tear down (stops all voices, unregisters the tick).
sound.Dispose();
```

### Music

```csharp
// Intro + loop: the intro plays once, then hands off to the loop sample-accurately.
sound.PlayMusic(introLoopDef);              // fade = -1 uses def.DefaultFade
sound.PlayMusic(loopOnlyDef, fade: 0f);     // no intro clip, no fade: starts instantly

// Crossfade: while a track is playing, PlayMusic fades the old one out while the
// new one fades in on the other channel. A third call mid-crossfade steals the
// fading-out channel (newest wins).
sound.PlayMusic(nextTrackDef, fade: 1.5f);

// Awaitable transitions — completes on fade-in end (or immediately if fade is 0).
// Cancelling stops the music and throws OperationCanceledException.
await sound.PlayMusicAsync(introLoopDef, fade: 1.5f);
await sound.StopMusicAsync(fadeOut: 1f);

sound.PauseMusic();
sound.ResumeMusic();   // reschedules a cancelled loop from the intro's remaining time
```

### Occlusion

```csharp
// Enable per SoundDef; only 3D sounds (Positional/Follow) are evaluated.
mySoundDef.Occlusion = true;

var sound = new SoundSystem(new SoundSystemConfig
{
    SfxVoices = 24,
    Listener = playerHead,                 // null finds the scene AudioListener
    OcclusionMask = wallsLayerMask,         // layers the built-in raycast provider tests against
    OcclusionChecksPerFrame = 4,            // round-robin budget; not every voice re-checked each frame
    OcclusionMuffledCutoffHz = 1200f,       // low-pass cutoff at full occlusion (22000 = open)
    OcclusionVolumeAtFull = 0.35f,          // volume multiplier at full occlusion
    OcclusionSmoothingSeconds = 0.15f,      // seconds for the occlusion factor to travel 0->1
});
```

The default provider (`RaycastOcclusionProvider`) does a single
`Physics.Linecast` from listener to source (binary blocked/clear). Supply a
custom strategy via `SoundSystemConfig.OcclusionProvider` — implement
`IOcclusionProvider.Evaluate(in Vector3 listenerPos, in Vector3 sourcePos)`
returning 0 (open) .. 1 (fully occluded); it is called from the tick on the
round-robin budget above, so implementations must not allocate.

### Timescale and mixer snapshots

```csharp
// Scales SFX voice pitch by Time.timeScale (slow-motion); music is unaffected.
var sound = new SoundSystem(new SoundSystemConfig { SfxVoices = 24, PitchWithTimescale = true });

// Thin wrapper over AudioMixerSnapshot.TransitionTo; no-op on a null snapshot.
sound.TransitionTo(pausedSnapshot, seconds: 0.3f);
```

Non-loop voice completion follows playback progress (pitch × timescale), not
real time, so a one-shot played at a low `Time.timeScale` plays out its full
audio instead of being cut short. At `Time.timeScale = 0` SFX freeze in
place — audio and lifetime both stop advancing — and resume intact once
timeScale recovers. `Time.timeScale = 0` is still **not** the recommended
pause path — use `AudioListener.pause` or the bundled `Paused` snapshot
instead, since a sound started while timeScale is 0 begins silent/frozen
rather than paused.

### Bundled default mixer

Games that never assign `SoundSystemConfig.Mixer` fall back to the package's
own `Bun3DefaultAudioMixer` (loaded from `Resources`), so channel volumes and
group routing work out of the box:

- Groups: `Music`, `SFX`, `Voice` (matched by `AudioMixer.FindMatchingGroups`;
  `SfxGroup`/`MusicGroup` are populated in place on the config when left null).
- Exposed parameters: `MasterVolume`, `MusicVolume`, `SfxVolume`, `VoiceVolume`
  (used by `SetChannelVolume`/`GetChannelVolume`).
- Snapshots: `Normal`, `Paused`.
- The bundled mixer has no sidechain ducking or low-pass effect. The `Paused`
  snapshot provides fixed attenuation; add custom effects when needed.
- Snapshot ducking routes through an unexposed `Mix` stage (`Master` → `Mix`
  → `Music`/`SFX`/`Voice`): the bundled `Paused` snapshot lowers `Mix`, never
  an exposed channel parameter, so `SetChannelVolume` and
  `TransitionTo(Paused)` are independent by design — turning a volume slider
  can never disarm pause ducking. The general Unity caveat still applies to
  custom mixers you build yourself: `AudioMixer.SetFloat` on an *exposed*
  parameter permanently takes it out of snapshot control until
  `AudioMixer.ClearFloat` is called.

### Preparing pooled sources

Set `SoundSystemConfig.OnSourceCreated` before construction to add reusable
components to each SFX source once. The hook runs after `playOnAwake` is disabled
and excludes music sources. If it throws, construction destroys the partial
pool without registering a player-loop tick. Use `OnVoiceConfigured` for values
that need resetting on every play. When another component owns filtering, set
`OcclusionChecksPerFrame = 0` so the built-in filter does not overwrite it.

### External playback ownership

`ExternalAudioRegistry` controls selected properties of sources played by another
system without adding those sources to the SFX pool. Create and use it on Unity's
main thread, and dispose it when its session ends.

```csharp
var external = new ExternalAudioRegistry(capacity: 32);
var handle = external.Register(sdkSource, new ExternalAudioSettings(
    gain: 1f, overrideMixerGroup: true, mixerGroup: voiceGroup));
handle.SetGain(0.5f);
handle.Release(); // Restores the original source volume and mixer route.
external.Dispose(); // Releases every remaining registration; safe to repeat.
```

Default settings own nothing. A non-null `gain` explicitly owns source volume;
`SetGain` accepts finite values in [0,1] and throws when volume was not opted into.
This value is a direct source gain. Apply user volume once through the shared
mixer rather than multiplying it into this gain as well. Mixer routing is owned
only when `overrideMixerGroup` is true; a null group then explicitly clears it.

For low-pass control, supply an existing `AudioLowPassFilter` on the same
GameObject through `lowPassFilter`, optionally with `lowPassCutoff` in [10,22000]
hertz. Registration enables the filter; `SetLowPassCutoff` updates it. Release
restores its original enabled state and cutoff. The registry never creates or
removes components or changes filter resonance.

Keep each source in one live registry and give that registration exclusive write
access to the selected properties until release. Other properties remain under
external control: the registry never plays, stops, pauses, mutes, or changes clip,
loop, pitch, spatial blend, playback position, or transform. SDKs that overwrite
source volume should leave `gain` null and use mixer control instead.

Duplicate sources or shared owned filters in a registry and capacity exhaustion
throw without stealing registrations. Destroyed sources are invalid and their slots are reclaimed on
the next registration. Stale/default handles cannot affect reused slots and
silently ignore mutations and release. Hot control updates allocate no managed
memory after registration. Release/disposal restores only explicitly owned
properties; it tolerates source or filter destruction.

### Custom SFX output ownership

Set `SoundSystemConfig.CreateVoiceOutput` to a cached factory returning one
`ISoundVoiceOutput` per SFX source. Construction invokes it after `OnSourceCreated`;
music sources are excluded. A null factory or null owner keeps ordinary playback.
Owner creation failure disposes owners already prepared and tears down the partial pool.

The core configures a stopped source and invokes `OnVoiceConfigured`, then calls
`TryStart(definition, selectedClip, logicalPitch)` before source Play:

- `Unsupported` permits ordinary clip playback, only after previous owned processing
  is quiescent.
- `Started` delegates driver clip/loop/pitch and spatial DSP to the owner. The core
  still calls source Play and owns volume, fades, followed position and mixer routing.
- `Unavailable` releases the request and clears its clip without dry fallback.

Started voices complete through `IsComplete`, including any output tail, rather
than elapsed clip time. Pitch changes, including timescale changes, go through
`SetPitch`; zero pitch must pause progress while preserving an active voice.
Built-in occlusion queries, low-pass and occlusion gain are bypassed for owned voices
so spatial attenuation is applied once. Explicit stop and fade completion still end
the voice.

`Retire` gates the old generation before source Stop or reconfiguration and never
waits for an audio reader. `TryStart` returns Unavailable while a reader prevents
reuse. Implementations must retain their own deferred cleanup mechanism after
source Stop/destruction; core does not reset or dispose reader-owned native state.
Owner Dispose requests final cleanup and must remain safe after Retire. Control
methods run on the sound-system thread, must not reenter the system, and expected
start failures must return Unavailable. Playback, pitch updates, retirement and
completion polling must not allocate managed memory.

### Addressable clips

Requires [com.unity.addressables](https://docs.unity3d.com/Packages/com.unity.addressables@latest)
installed. The current runtime and test assembly definitions directly reference
Addressables assemblies, so this package currently requires that dependency even
when only non-Addressable playback is used. `BUN3_ADDRESSABLES` gates the related
source API but does not make those assembly references optional.

`SoundDef.AddressableClips` is an alternative to `Clips`: assign an
`AssetReferenceT<AudioClip>[]` instead of direct clip references, then preload
before playing — an unpreloaded addressable def plays nothing (invalid handle
plus a development-build warning).

```csharp
// Preload once (e.g. a loading screen), then play normally.
await sound.PreloadAsync(mySoundDef);
sound.Play(mySoundDef);

sound.IsPreloaded(mySoundDef);   // true once the def's addressable clips have finished loading

// Release when the def's voices are no longer playing (a dev-build warning
// fires, but does not block, if a voice is still active on it).
sound.ReleasePreloaded(mySoundDef);
```

An Addressables operation with failed status skips the definition and leaves it
unpreloaded. The package emits a development-build warning; Addressables may
separately log its own error. Cancellation propagates `OperationCanceledException`;
malformed references and other unexpected exceptions can also propagate. Handles
not transferred to the preload cache are released on every exit path.
Concurrent `PreloadAsync(def)` calls on the same def
are safe — the loser's redundant batch is released instead of leaking. A
def's preload belongs to exactly one `SoundSystem`: don't preload the same
def on two live systems, since releasing it on one nulls the shared runtime
clips out from under the other.

`MusicDef` has no `AddressableClips` field of its own; for Addressable music,
load the clip yourself and build a runtime `MusicDef`:

```csharp
// Music via Addressables: load the clip yourself, then build a runtime MusicDef.
var handle = UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<AudioClip>("bgm-main");
var clip = await handle.Task;
var def = ScriptableObject.CreateInstance<MusicDef>();
def.Loop = clip;
sound.PlayMusic(def, fade: 2f);
// Keep both objects until playback has stopped. At final teardown:
sound.StopMusic(fadeOut: 0f);
UnityEngine.AddressableAssets.Addressables.Release(handle);
UnityEngine.Object.Destroy(def);
```

Sound definitions are authored as `SoundDef` assets
(`Assets > Create > Bun3 > Audio > Sound Def`), which hold clips, volume/pitch
ranges, loop/spatial settings, max instances, and cooldown. Music tracks are
authored as `MusicDef` assets (`Assets > Create > Bun3 > Audio > Music Def`),
which hold the optional intro clip, the required loop clip, volume, and the
default fade duration.

## Optional SDK activation

Adapters are disabled by default. Import the required SDKs and let Unity finish compiling, then run **Tools > Bun3 > Audio > Sync Installed Adapters**. The SDK-independent `Bun3.Unity.Audio.Editor.SoundSdkSetup.SyncInstalledAdapters` method is also available through Unity `-executeMethod` or Editor automation. It checks SDK assembly-definition assets and required loaded types before enabling adapters on Standalone, Android, iOS and WebGL, preserving unrelated scripting defines.

Dissonance requires `BUN3_DISSONANCE`; the official NGO binding additionally requires `BUN3_DISSONANCE_NFGO`. Steam Audio requires both `BUN3_STEAMAUDIO` and the SDK's `STEAMAUDIO_ENABLED`. Combined voice path playback requires both SDKs. A leftover `STEAMAUDIO_ENABLED` alone does not activate Bun3 adapters. Runtime, Editor and test assemblies share these gates.

Before removing SDK assets, remove or guard application references to adapter types, then run **Tools > Bun3 > Audio > Disable Adapters** (`Bun3.Unity.Audio.Editor.SoundSdkSetup.DisableAdapters`) and let compilation finish. This removes the three Bun3 symbols on the four supported targets and preserves the SDK-owned `STEAMAUDIO_ENABLED` flag; optional packages can remain installed. Synchronize again after installing or removing SDK components. Activation is explicit and does not run automatically on domain reload. Deleting SDK files outside the Editor while old activation symbols remain is not an automatically recoverable cold-start workflow; restore the SDK or clear the managed symbols before reopening.


### String catalogs and request-local playback

`SoundCatalog` maps ordinal, case-sensitive string keys to `SoundDef` assets. Create it through **Bun3/Audio/Sound Catalog**. Definitions remain the single source of playback settings; catalogs contain no duplicate clip, gain, or cooldown fields. `ValidateOrThrow()` rejects blank/duplicate keys and missing definitions. `TryGet` returns false for unknown keys. `SetEntries` validates atomically; warm lookup allocates no managed memory.

```csharp
sound.Prepare(catalog); // Validate and prewarm cooldown tracking.
if (catalog.TryGet("player.footstep", out var footstep))
{
    sound.Play(footstep, position);
    sound.Play(footstep, position, SpatialMode.None, volumeScale: 0.5f);
}
```

The spatial/gain overload overrides a single request without editing the asset. Cooldown and instance limits remain shared by definition. Optional output owners implement `ISpatialSoundVoiceOutput` to receive the effective request mode; legacy owners retain their existing interface.

`SoundDef.VolumeGroup` is an optional logical string group. `SoundSystemConfig.GroupGain` resolves its live gain at play and on each tick, independent of `MixerGroup` routing. Cache the callback and avoid allocations or exceptions. Catalogs and groups have no game-specific enums or network IDs.


## Shared definition profiles

`SoundDef` optionally references four independently shared assets: `SoundPlaybackProfile` (volume, pitch, loop), `SoundRoutingProfile` (mixer and logical volume group), `SoundConcurrencyProfile` (per-definition limits/cooldown), and `SoundSpatialProfile` (positioning, occlusion and an acoustic selection). Clips and sound identity stay on the definition. Sharing a concurrency profile does not combine active voice counts or cooldown state between definitions.

`SoundAcousticSelection` gives SFX and voice the same choice between a shared `SoundAcousticProfile` and retained local `SoundAcousticSettings`. The acoustic settings contain distance attenuation, its optional curve, fallback minimum/maximum distances, and the mono/full-width selection. A selected `SoundDef.SpatialProfile` has the highest precedence and supplies its own acoustic selection; otherwise `SoundDef.Acoustics` resolves its selected profile or local values. Assigning or clearing either profile never copies over the inactive local values. No per-field override mask is applied.

Use `Effective*` getters in custom `SoundDef` consumers. The older public properties such as `DistanceAttenuation`, `AttenuationProfile`, `MonoDistance` and `FullSpatialDistance` address the retained local acoustic values for source compatibility; they are not necessarily the resolved values. Duplicate a profile for a sound-specific variation, or clear its reference and author the local group.

`SpatialBlendProfile` is a reusable mono/full-width native-distance range. With `EffectiveInheritSpatialBlend` enabled, native adapters use their world default. Otherwise the selected blend profile wins, with inline mono/full distances as fallback. A shared spatial profile can select the same blend profile for many sounds. Zero/invalid full-width ranges retain spatial output. The blend asset is SDK-independent; actual path-distance evaluation is provided by the native adapter.

```csharp
mySoundDef.PlaybackProfile = sharedPlayback;
mySoundDef.SpatialProfile = sharedSpatial;
// The spatial profile's acoustic selection now has highest precedence.

// Per-sound spatial and acoustic settings instead of the shared group:
mySoundDef.SpatialProfile = null;
mySoundDef.Acoustics.Profile = sharedAcoustics;

// Return to the SoundDef's retained local acoustic settings:
mySoundDef.Acoustics.Profile = null;
var localAcoustics = mySoundDef.Acoustics.Local;
localAcoustics.InheritSpatialBlend = false;
localAcoustics.SpatialBlendProfile = speechAndFootstepBlend;
mySoundDef.Acoustics.Local = localAcoustics;
```

The ordinary AudioSource path uses resolved minimum/maximum distance and the
`DistanceAttenuation` switch. It does not evaluate `AttenuationProfile` curves or
native path-distance mono settings; those fields are consumed by native adapters.

Playback parameters are primarily consumed when starting a voice. Native adapters continue reading effective attenuation and width profiles on the control thread during simulation. No profile allocation occurs on a warm playback or audio-processing path.

## Reading the implementation

Start with `SoundSystem.Play` for a request and `SoundSystem.Tick` for frame updates.
Tick lists the processing stages in execution order. Completed sources retire before
callbacks run, because a callback can immediately reuse a voice slot.

Distance evaluation uses scalar inputs and returns a gain without changing playback
state. Profile snapshots isolate that calculation from live Inspector edits.
`SoundSystem.Addressables` handles the separate load/release lifecycle: each call
owns its handles until a successful preload transfers them to the system. A single
`finally` block releases any batch that did not transfer.

When extending this package, keep calculations separate from Unity operations,
use named intermediate values and guard clauses, and keep related steps in the
same file. Hot paths use direct calls and reusable storage; function composition
must not introduce per-frame closures or collection allocations.

## API and tuning reference

| Entry point / setting | Meaning |
|---|---|
| `Play` / `PlayAsync` | Start a direct or preloaded definition; rejected requests return an invalid handle. Async variants await completion. |
| `Prepare(SoundDef)` / `Prepare(SoundCatalog)` | Prewarm cooldown bookkeeping. Catalog preparation also validates mappings. |
| `SoundHandle` | Generation-checked stop, fade, pitch, volume, follow and await controls. Stale handles do nothing. |
| `SetCompletionCallback` | One callback per voice, invoked after retirement. A later registration replaces the previous one. |
| `PreloadAsync` / `ReleasePreloaded` | Own an Addressables batch until release or system disposal. Stop users of a clip before release. |
| `PlayMusic` / `StopMusic` | Two-channel intro/loop crossfade subsystem; music does not consume SFX slots. |
| `SfxVoices` | Fixed pool capacity. Per-definition limits steal the oldest matching voice; a full global pool steals its oldest voice. |
| `MaxInstances` / `Cooldown` | Definition-scoped concurrency limit and minimum retrigger seconds; zero disables the respective restriction. |
| `Acoustics` | Shared/local acoustic selection on a SoundDef, or within its selected spatial profile. |
| `DistanceAttenuation` | Disable distance gain independently of direction. Basic AudioSource output uses a flat rolloff when disabled. |
| `AttenuationProfile` | Native distance curve. Basic AudioSource output uses resolved MinDistance/MaxDistance instead. |
| `SpatialBlendProfile` | Native path-distance mono/full-width transition, independent of distance volume. |
| `VolumeGroup` / `GroupGain` | Live logical gain; does not create a mixer group or network synchronization. |

For `DistanceAttenuationProfile.VolumeByDistance`, X is metres and Y is gain.
Invalid distances and empty curves are silent; output is zero at and beyond the
last key. Use a zero-valued final key to avoid an abrupt edge. `GetSnapshot`
returns an immutable copy for native callbacks and allocates when rebuilding
changed authored data. Do not access Unity profile assets from an audio callback.

## Lifetime, threading and performance

- Construct, configure, prepare, play and dispose on Unity's main thread. Core
  source/control APIs are not a worker-thread queue.
- Keep one owner for a service and dispose it when that owner ends. Disposal stops
  voices, retires outputs, releases preloads and unregisters the player-loop entry.
- Completion callbacks can run inline and start another voice. Output adapters and
  configuration hooks must not reenter or throw.
- One waiter per voice is supported. Concurrent `WaitAsync` calls on the same
  voice are not supported.
- Prewarm definitions with cooldowns, cache delegates, and keep custom gain and
  occlusion implementations allocation-free. A new closure per play still allocates.
- Construction, first-time setup, changed snapshots, Addressables and cancellation
  can allocate. The package does not promise every API is allocation-free.
- Preferences and routing are local controls. Persisting or transmitting settings
  is the application's responsibility.

## Troubleshooting

| Symptom | Check |
|---|---|
| Invalid handle / no SFX | Assigned non-null clips or successful preload; cooldown; output adapter availability. |
| Missing Addressables namespace | Install Addressables; the current asmdefs reference its assemblies directly. |
| UI sound changes with distance | Use Spatial=None; check the effective shared profile and output adapter. |
| Profile edit appears ignored | Effective getters can resolve a shared asset. Most playback parameters apply at the next play. |
| Closed obstacle remains audible | Basic occlusion defaults to nonzero blocked gain and uses a 3D linecast. It does not find diffraction paths or query Collider2D. |
| Custom distance curve ignored | Basic AudioSource output does not consume the native distance curve; use the corresponding adapter. |
| Voice ends too early/late | Check pitch, timescale and custom output completion/tail behavior. |
| Mixer volume has no effect | Check exposed parameter names and source routing. |
| Gain reduced twice | Keep mixer preference gain, request gain and acoustic gain distinct; apply each once. |
| Error during preload tests | Expected fixture exceptions are captured; unrelated errors remain visible and need investigation. |

## Tests

Install Unity Test Framework. For Git-installed packages, merge this root-level
entry into the consumer's `Packages/manifest.json`, preserving existing testables:

```json
"testables": ["com.bun3.unity.audio"]
```

Open **Window > General > Test Runner** and run both EditMode and PlayMode.
Filter `Bun3.Unity.Audio.Tests`. Editor-only fixtures cover catalogs and curves;
PlayMode covers pooling, callbacks, profiles, music, occlusion, allocation checks
and Addressables lifetime. Unity test compilation supplies `UNITY_INCLUDE_TESTS`.

`PreloadRealLoadTests` registers its own locator/provider and needs no project WAV.
It exercises real Addressables operation reference counts, not AssetBundle delivery
or microphone/network behavior. The host still needs working Addressables initialization.
Optional adapters have separate gated test assemblies; core tests do not establish
SDK integration or target-platform build compatibility.
