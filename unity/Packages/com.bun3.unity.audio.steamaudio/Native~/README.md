# Native Steam Audio builds

This folder builds two Windows x64 binaries from the pinned Steam Audio v4.8.1 source: an optional diagnostic `phonon.dll` and the pathing-width `audioplugin_phonon.dll` that native SFX and voice output require for near-field mono.

## Optional native final-path diagnostics

The stock Steam Audio 4.8.1 SDK supports distance callback statistics but does not expose the final weighted probe paths. The included patch adds the versioned `bun3SteamAudioSetPathDiagnosticCallbackV1` export without changing existing SDK structures or simulation decisions. The managed adapter detects the export. A stock SDK returns `SupportsPathDiagnostics == false` and continues normal audio playback.

### Build and activate

Requires Python, Git, CMake and a Visual Studio C++ toolchain. Build in a **new directory**:

```powershell
python build_diagnostics.py E:/Temp/steam-audio-diagnostics --generator "Visual Studio 18 2026" --cmake C:/tools/cmake/bin/cmake.exe
```

The script pins the upstream v4.8.1 commit and applies the adjacent patch. Upstream dependencies and their licenses remain in the checkout. This build targets **Windows x64, Default scene, CPU simulation, PFFFT**. It does not include Embree, Radeon Rays, TrueAudio Next or IPP. It is not a feature-equivalent replacement for every SDK configuration. Other platforms remain on the stock SDK until separately built and verified.

Close every Editor/player loading the target project's library before replacement. Back up `Assets/Plugins/SteamAudio/Binaries/Windows/x86_64/phonon.dll` outside Assets, then replace only that DLL with the generated `core/build-bun3/src/core/Release/phonon.dll`. Keep its existing Unity importer metadata. Restart and run `SteamAudioAcousticAssetTests.FinalPathDiagnosticsAreOptionalBoundedAndDoNotChangeAudio`. Restore the backup and restart to return to the stock SDK. Do not hot-swap a loaded DLL or load two copies and pass native handles between them.

### Capture contract

Use `SetPathDiagnosticsEnabled(true)` on the simulation's creating thread. Read `CopyPathDiagnostics` and `CopyPathDiagnosticPoints` after simulation, before changing inputs. Both copy into caller-owned spans. Disabled, pending, failed, stale and foreign handles expose no records. Buffers retain at most 64 paths and 4096 vertices per source; truncation is explicit. Normal updates/copies have no managed allocations after enabling capture. The native debug-only reconstruction can allocate temporary probe paths while enabled.

Each record contains the actual source/listener, virtual source, attenuation evaluation distance, attenuation gain, interpolation weight and ordered probe vertices used by that path. The product `DistanceGain * Weight` is the input to SH accumulation **before** EQ/HRTF and downstream gain; it is not final perceived loudness. A zero-gain path remains visible to explain silence. Zero vertices without truncation means direct line of sight. Probe vertices exclude source/listener attachment legs. The native evaluation distance is not necessarily the sum of an endpoint-inclusive polyline. Virtual-source coordinates explain direction and must not be drawn as a physical propagation route.

The callback is synchronous and thread-local, installed only around `iplSimulatorRunPathing` and cleared in `finally`. The current adapter uses one control thread. Validation-ray diagnostics are a separate API and include rejected candidates; never present those segments as final paths.

The patch derives from Valve Steam Audio (Apache-2.0). Preserve upstream license and third-party notices when distributing a rebuilt binary; the build checkout contains those notices. No compiled SDK binary is bundled in this package.

## Pathing stereo width spatializer

Native SFX and voice output play through the Steam Audio Unity spatializer, which renders the pathing coefficients of each registered source on the audio thread without managed code. The stock spatializer has no control over the stereo width of its pathing output. `steam-audio-4.8.1-unity-pathing-width.patch` adds one spatializer parameter, `PathWidth` (index 34, 0 = dual mono, 1 = full binaural, default 1), and applies it to the pathing output with a 20 ms linear slew. Parameters 0-33 and the stock behavior at width 1 are unchanged. The patch also exports `bun3UnityPluginGetPatchVersion` (returns 1), which the managed adapter uses to detect the build, and `bun3UnityPluginApplyPathingWidthForTest` for equivalence tests.

### Build

The same command builds both binaries. After the diagnostic `phonon.dll`, the script applies the width patch, fetches Unity's `AudioPluginInterface.h` at a pinned commit (SHA-256 checked) and builds `audioplugin_phonon`. It prints both output paths:

- `core/build-bun3/src/core/Release/phonon.dll`
- `unity/build-bun3/src/native/Release/audioplugin_phonon.dll`

The plugin links the `phonon` import library and works with either the stock or the diagnostic `phonon.dll`.

### Install

1. Close every Editor and player that loads the target project.
2. Back up `Assets/Plugins/SteamAudio/Binaries/Windows/x86_64/audioplugin_phonon.dll` outside Assets.
3. Replace only that DLL with the built one. Keep its existing Unity importer metadata.
4. Set **Edit > Project Settings > Audio > Spatializer Plugin** to **Steam Audio Spatializer**.
5. Restart and run `SteamAudioSpatializerPatchTests`. The tests are inconclusive while the stock plugin is loaded.

Restore the backup and restart to return to the stock plugin. With the stock plugin, positional sound still renders natively; only near-field mono is unavailable. Other platforms use the stock plugin until separately built and verified.

The patch derives from Valve Steam Audio (Apache-2.0). Preserve upstream license and third-party notices when distributing a rebuilt binary. No compiled binary is bundled in this package.
