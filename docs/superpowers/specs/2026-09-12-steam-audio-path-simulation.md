# Steam Audio path simulation scope

Status: implemented and validated. Unity native editor fixture passed 8/8 on 2026-09-12 (`E:/Temp/bun3-sound-validation-20260911/Results/path-simulation-green.xml`).

## Purpose

Expose a reusable control-thread simulation owner between caller-authored acoustic geometry and audio renderers. The existing game integrated-sound contract requires distinct computation state, geometry revision, and copied spatial results. This scope does not interpret game rooms, doors, AI events, or voice permissions.

## Ownership and lifetime

`SteamAudioPathSimulationScope` retains native references to an existing context, scene and baked probe batch. It owns its simulator, a fixed number of precreated native sources, copied SH storage, and all source registrations. Construction and disposal occur on one creating thread. Every mutating or result-copy operation enforces that thread. The scope is not driven by SteamAudioManager and must not share simultaneous scene mutation with any other simulator owner.

The caller authors/bakes geometry and probes before construction. The caller may release its wrapper references after construction, because the scope retains native references. To change geometry, the caller commits the scene on the same control thread, calls `InvalidateGeometry(revision)`, and then calls `Simulate(time)`. The scope commits its simulator before simulation. The scope does not own meshes or bake jobs and does not infer that Collider2D changes update native geometry.

## API

- Constructor: context, scene, probes, maximum source count, sample rate, frame size, Ambisonic order, visibility radius/threshold/range, maximum occlusion samples (default 32).
- `TryRegisterSource(in CoordinateSpace3, out SteamAudioSimulationSourceHandle)` returns false on capacity without allocating or throwing.
- `SetSource(handle, in CoordinateSpace3)` and `RemoveSource(handle)` reject stale or foreign handles without touching another registration.
- `SetOcclusion(handle, OcclusionType, float radius, int samples)` configures raycast or fractional volumetric direct occlusion per source, bounded by the constructor's sample capacity. Registration defaults to one raycast sample.
- `SetListener(in CoordinateSpace3)` invalidates existing results. Simulation requires a listener.
- `InvalidateGeometry(ulong revision)` accepts monotonically increasing revisions and invalidates every cached result.
- `Simulate(double time)` accepts finite nonnegative monotonically increasing times, runs direct occlusion and validated pathing with alternate-path search, and copies native data immediately.
- `TryCopyResult(handle, float[] coefficients, out SteamAudioPathSimulationResult)` copies only into caller-owned storage. It exposes no borrowed native pointers.
- Source handles carry scope identity, slot and generation. Removing and reusing a slot never revives an old handle.
- Disposal is idempotent and invalidates every handle.

`SteamAudioPathSimulationResult` reports Pending, Valid or Failed; current geometry revision; computation time; direct occlusion, distance attenuation and air absorption; three path EQ bands; and whether copied coefficients with EQ contain a nonzero path signal. Direct simulation computes distance attenuation, air absorption and occlusion; transmission and source directivity are not enabled. A valid zero signal means only that the native solver produced no signal for the supplied probe setup. It does not prove a physically sealed region or adequate probe coverage. Coverage metadata and its validation remain caller responsibilities. Missing/uninitialized computation is never mapped to an open or blocked route. No path-distance scalar is exposed because the native output does not provide one.

## Native ABI and allocation

Bindings match the locally installed Steam Audio 4.8.1 `phonon.h`. Inputs include the trailing deviation-model pointer; outputs include the trailing `normalizeEQ` field. All native structs use scalar fields and pointers rather than managed fixed-array marshalling. On x64, SimulationOutputs is 216 bytes, pathing begins at 120, and normalizeEQ occupies offset 208. Layout tests cover these assumptions before native output calls.

All source wrappers, native sources, copied coefficient rows and working buffers are prepared during construction. Warm source/listener updates, simulation and copying introduce no managed allocations. Native SDK internal work is outside a claim of zero native allocation. No native work is executed from an audio callback.

## Validation

Reflection-first RED tests establish missing behavior before implementation. A real native fixture bakes probes around a wall, checks direct occlusion and nonzero indirect output, adds a closing panel, invalidates the geometry revision, requires Pending before the new solve, checks zero path signal, and verifies recovery after reopening. Additional tests cover source capacity/reuse, foreign handles, retained wrapper lifetime, invalid inputs before mutation, single-thread enforcement, ABI size/offsets, and zero warm managed allocations. Tests use generated geometry, never game assets.

## Exclusions

No map generator, distance reconstruction, direct/indirect PCM mixing, renderer parameter mailbox, audio output, spatial smoothing, or game-specific reach policy is included. Those are separate layers consuming this scope.

## Verification record

The initial fixture recorded 7/7 intended missing-type failures in `path-simulation-red.xml`. The native GREEN fixture passed 8/8, including generated open/closed/reopened pathing, fractional volumetric occlusion, retained caller references, source generations, wrong-thread/invalid-time rejection, exact x64 ABI and zero warm managed allocations. A separate netstandard2.1/C#9 build against actual SteamAudioUnity/UnityEngine assemblies passed with XML documentation enabled, zero warnings and zero errors (`E:/Temp/steam-path-simulation-harness/Runtime.csproj`). Native results establish this control-thread scope, not game map coverage or runtime PCM parameter integration.
