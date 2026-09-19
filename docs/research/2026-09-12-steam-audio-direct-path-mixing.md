# Steam Audio 4.8.1 direct/path mixing and Unity PCM seam

Status: source-verified research and proposed integration policy, 2026-09-12. No implementation or Unity execution in this research task.

## Finding that determines the mix

**Pathing is not an indirect-only bus.** In `PathSimulator::findPaths`, an unobstructed source/listener ray selects one direct sound path with weight one. Otherwise the solver searches compatible baked probe paths, including optional dynamic validation and alternate-path search. The ordinary simulator calls this method without the special forced-occlusion override. SH coefficients already incorporate the supplied distance attenuation model. Applying the direct-distance gain to path-renderer input again would attenuate distance twice. [4.8.1 path simulator](https://github.com/ValveSoftware/steam-audio/blob/v4.8.1/core/src/core/path_simulator.cpp#L107-L212), [SH gain construction](https://github.com/ValveSoftware/steam-audio/blob/v4.8.1/core/src/core/path_simulator.cpp#L366-L411), [ordinary simulation caller](https://github.com/ValveSoftware/steam-audio/blob/v4.8.1/core/src/core/simulation_manager.cpp#L602-L648).

**The official Unity native plugin does not multiply pathing gain by the occlusion complement.** It processes the original input through the direct effect and binaural/panning effect, applies direct mix gain, then separately downmixes the original input for pathing, applies pathing mix gain, renders `IPLPathEffect`, and adds that output. The managed bridge forwards the two authored mix levels without deriving either from occlusion. Therefore enabling both branches at unit gain can duplicate the clear direct route. [Native direct and path branches](https://github.com/ValveSoftware/steam-audio/blob/v4.8.1/unity/src/native/spatialize_effect.cpp#L816-L968), [managed parameter bridge](https://github.com/ValveSoftware/steam-audio/blob/v4.8.1/unity/src/project/SteamAudioUnity/Assets/Plugins/SteamAudio/Scripts/Runtime/UnityAudioEngineSource.cs#L57-L98).

## Minimal integration policy proposal

For the existing raycast policy, use the direct branch when its unoccluded fraction `O` is one and the path branch when `O` is zero. Generalizing this to fractional volumetric occlusion gives the explicit application policy:

```text
direct = Binaural(DirectEffect(dry, distance + air absorption + occlusion))
path   = PathEffect(dry, copied SH + EQ, listener orientation)
output = directMix * direct + pathMix * (1 - clamp01(O)) * path
```

This complement is **our proposed arbitration**, not native-plugin behavior or proof of energy-preserving physical mixing. Do not multiply the direct branch by `O` a second time: `IPLDirectEffect` already applies it when the occlusion flag is enabled. With transmission disabled, its gain contains the occlusion fraction; air absorption supplies per-band filtering. [Direct effect gain calculation](https://github.com/ValveSoftware/steam-audio/blob/v4.8.1/core/src/core/direct_effect.cpp#L91-L162).

Keep separate dry input for both branches. Never feed the output of `IPLDirectEffect` into the path effect: it would incorrectly apply direct occlusion and direct distance to an alternate route. Keep `normalizeEQ=false` unless a profile deliberately selects normalization. Path EQ models bending; it is not a substitute for a separately simulated indirect air-absorption result. The path effect EQ-filters mono PCM, weights it with SH coefficients, and spatializes it; the existing native renderer already owns that stage. [Path DSP](https://github.com/ValveSoftware/steam-audio/blob/v4.8.1/core/src/core/path_effect.cpp#L99-L201), [public path API](https://github.com/ValveSoftware/steam-audio/blob/v4.8.1/core/src/core/phonon.h#L2670-L2727).

Required native additions for this policy are a retained context/HRTF, fixed mono/stereo buffers, `IPLDirectEffect` with one input channel, and `IPLBinauralEffect` for the direct branch. Pass the finite direct simulation scalars with flags for distance, air absorption and occlusion only; leave transmission/directivity disabled. Pass listener-relative direct direction to the binaural effect with spatial blend one. Retain the existing `IPLPathEffect` branch and add its stereo output with the explicit complement. These are separate native effects; no new middleware is necessary. [Native effect usage](https://github.com/ValveSoftware/steam-audio/blob/v4.8.1/unity/src/native/spatialize_effect.cpp#L816-L964).

Pending/failed simulation is not a sealed-room result. The controller must publish validity/revision and select its failure policy explicitly. Neither value of `HasPathSignal` proves valid probe coverage. In particular, when the center ray is blocked and endpoint probe neighborhoods are invalid, `findPaths` returns before updating SH/EQ; the ordinary simulation caller ignores the return value and copies its existing state. A finite native output can therefore preserve an earlier route after coverage is lost. Consumer-side coverage validation is necessary before audible publication. [Early-return path](https://github.com/ValveSoftware/steam-audio/blob/v4.8.1/core/src/core/path_simulator.cpp#L157-L206), [caller output copy](https://github.com/ValveSoftware/steam-audio/blob/v4.8.1/core/src/core/simulation_manager.cpp#L633-L645).

Smooth branch gains when the controller changes them and drain/reset both effect histories on source retirement; do not reuse a previous emitter's native tail.

## Same-source SFX filter seam

Unity's documented `OnAudioFilterRead(float[], int)` receives the clip or prior filter's interleaved PCM on the audio thread; components' Inspector ordering defines filter order. This supports replacing the buffer with native-processed output on the same source. The callback cannot assume channel count from clip metadata and cannot access arbitrary Unity APIs. [Unity 6.5 callback contract](https://docs.unity3d.com/6000.5/Documentation/ScriptReference/MonoBehaviour.OnAudioFilterRead.html).

For an owned point-source SFX voice, configure `spatialBlend=0`, `spatialize=false`, neutral stereo pan, and disable Doppler so the native renderer owns position and distance. Unity documents that spatial blend zero excludes 3D processing; pan remains independently relevant. Preserve the ordinary source/mixer gain once, with no duplicated native user-volume multiplier. [Unity spatial blend](https://docs.unity3d.com/ScriptReference/AudioSource-spatialBlend.html).

Proposed callback implementation contract: accept a verified stereo callback format, downmix dry point-source input into preallocated mono storage, process fixed native frames, and overwrite the entire interleaved output. Deliberately stereo-authored content needs a separate preservation policy. If callback chunks do not match the native frame size, use preallocated framing storage and document its latency; never resize or recreate native effects in the callback. A changed sample rate/channel layout requires control-thread reinitialization and a defined interim output. Publication to the audio owner must carry a coherent generation/revision snapshot; disposal waits until that owner is quiescent.

The installed Dissonance 9.0.9 source is local evidence that this seam participates in real playback: `Core/Audio/Playback/SamplePlaybackComponent.cs`, lines 113–201, reads decoded mono in `OnAudioFilterRead`, and its channel loop multiplies the incoming channel buffer. `VoicePlayback.cs`, lines 69–85, supplies a flatline clip and forcibly disables Unity spatialization. This proves an SDK use of the callback, **not** our future SFX implementation, its stereo callback format on every backend, or hardware output correctness. No SDK implementation is copied.

A pool must not return an SFX source merely because its clip has ended if native effects still have tails. A stopped source is not a guaranteed tail pump. Keep an owned silent continuation/source schedule until both effect tails finish, or explicitly document truncation. This lifecycle must be designed together with the SFX pool and verified on the actual callback path.

## Endpoint candidate selection detail

The native 4.8.1 probe neighborhood limit is eight per batch. `ProbeTree::getInfluencingProbes` walks its spatial tree and stops once that many containing influence spheres are collected; it does not promise the nearest eight. Only afterward does neighborhood occlusion checking remove hidden candidates. Sphere containment includes the boundary. A custom grid query finding one visible probe anywhere in the batch therefore does not prove the native selected subset includes it. The SDK-independent grid companion accepts an explicit consumer selection limit and returns `SelectionUncertain` when hidden containing candidates could fill that subset. Even its `GridVisible` state remains model evidence, not native geometry validation. [Neighborhood limit](https://github.com/ValveSoftware/steam-audio/blob/v4.8.1/core/src/core/probe_batch.h#L30-L90), [bounded tree selection](https://github.com/ValveSoftware/steam-audio/blob/v4.8.1/core/src/core/probe_tree.cpp#L135-L188), [occlusion filtering](https://github.com/ValveSoftware/steam-audio/blob/v4.8.1/core/src/core/probe_manager.cpp#L51-L85), [sphere boundary](https://github.com/ValveSoftware/steam-audio/blob/v4.8.1/core/src/core/sphere.h#L47-L51).

## Acceptance evidence still required

- Native dry impulse/sine: clear route equals the direct-only branch under arbitration, rather than the sum of two clear routes.
- Occluded alternate route: direct contribution is zero, path stereo remains nonzero; closing every validated route yields silence without treating coverage failure as a block.
- Fractional opening: both explicit weights apply once; output remains finite and branch transitions do not jump unexpectedly.
- Two source distances: path SH attenuation is not multiplied by direct attenuation again.
- Actual `AudioSource` clip and `OnAudioFilterRead`: confirm callback channels, source/mixer gain ownership, directional channel change, chunk framing, tail completion, pool reuse and disable/dispose concurrency.
- Warm callback allocation and separate-source independence. These are future tests, not results established by this source review.

Research inputs were the installed Steam Audio 4.8.1 Unity scripts/docs and header plus the official `v4.8.1` Git tag. Native files downloaded for inspection are under `E:/Temp/steam-audio-481-research/`; no installed SDK sources were changed.
