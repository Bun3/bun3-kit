# Optional native final-path diagnostics

The stock Steam Audio 4.8.1 SDK supports distance callback statistics but does not expose the final weighted probe paths. The included patch adds the versioned `bun3SteamAudioSetPathDiagnosticCallbackV1` export without changing existing SDK structures or simulation decisions. The managed adapter detects the export. A stock SDK returns `SupportsPathDiagnostics == false` and continues normal audio playback.

## Build and activate

Requires Python, Git, CMake and a Visual Studio C++ toolchain. Build in a **new directory**:

```powershell
python build_diagnostics.py E:/Temp/steam-audio-diagnostics --generator "Visual Studio 18 2026" --cmake C:/tools/cmake/bin/cmake.exe
```

The script pins the upstream v4.8.1 commit and applies the adjacent patch. Upstream dependencies and their licenses remain in the checkout. This build targets **Windows x64, Default scene, CPU simulation, PFFFT**. It does not include Embree, Radeon Rays, TrueAudio Next or IPP. It is not a feature-equivalent replacement for every SDK configuration. Other platforms remain on the stock SDK until separately built and verified.

Close every Editor/player loading the target project's library before replacement. Back up `Assets/Plugins/SteamAudio/Binaries/Windows/x86_64/phonon.dll` outside Assets, then replace only that DLL with the generated `core/build-bun3/src/core/Release/phonon.dll`. Keep its existing Unity importer metadata. Restart and run `SteamAudioAcousticAssetTests.FinalPathDiagnosticsAreOptionalBoundedAndDoNotChangeAudio`. Restore the backup and restart to return to the stock SDK. Do not hot-swap a loaded DLL or load two copies and pass native handles between them.

## Capture contract

Use `SetPathDiagnosticsEnabled(true)` on the simulation's creating thread. Read `CopyPathDiagnostics` and `CopyPathDiagnosticPoints` after simulation, before changing inputs. Both copy into caller-owned spans. Disabled, pending, failed, stale and foreign handles expose no records. Buffers retain at most 64 paths and 4096 vertices per source; truncation is explicit. Normal updates/copies have no managed allocations after enabling capture. The native debug-only reconstruction can allocate temporary probe paths while enabled.

Each record contains the actual source/listener, virtual source, attenuation evaluation distance, attenuation gain, interpolation weight and ordered probe vertices used by that path. The product `DistanceGain * Weight` is the input to SH accumulation **before** EQ/HRTF and downstream gain; it is not final perceived loudness. A zero-gain path remains visible to explain silence. Zero vertices without truncation means direct line of sight. Probe vertices exclude source/listener attachment legs. The native evaluation distance is not necessarily the sum of an endpoint-inclusive polyline. Virtual-source coordinates explain direction and must not be drawn as a physical propagation route.

The callback is synchronous and thread-local, installed only around `iplSimulatorRunPathing` and cleared in `finally`. The current adapter uses one control thread. Validation-ray diagnostics are a separate API and include rejected candidates; never present those segments as final paths.

The patch derives from Valve Steam Audio (Apache-2.0). Preserve upstream license and third-party notices when distributing a rebuilt binary; the build checkout contains those notices. No compiled SDK binary is bundled in this package.
