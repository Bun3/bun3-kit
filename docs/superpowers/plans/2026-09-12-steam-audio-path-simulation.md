# Steam Audio path simulation implementation plan

> Execute against the approved scope contract. Use test-first changes; controller owns Unity test execution. No commits or SDK-source edits.

**Goal:** A bounded, generation-safe, single-control-thread native simulation scope that publishes copied direct/path results.

**Architecture:** Exact 4.8.1 blittable bindings plus a native owner with precreated source slots. Caller owns scene geometry and baked probes; native reference retention protects lifetime.

**Technology:** Unity 6000.5.5f1, installed Steam Audio 4.8.1, C#9/netstandard2.1, NUnit editor tests, real phonon native library.

**Execution status:** complete. Controller verified `path-simulation-red.xml` (7 intended failures) and `path-simulation-green.xml` (8/8 passed, 2026-09-12). Native scene transitions, fractional occlusion, retained native lifetimes and both warm allocation checks passed. Actual-SDK netstandard2.1/C#9 documentation build completed with zero warnings/errors. No SDK source changes or commits were made.

## Files

- Create `unity/Packages/com.bun3.unity.audio.steamaudio/Runtime/NativePathSimulation.cs`: exact native input/output structs and simulation/reference bindings.
- Create `Runtime/SteamAudioPathSimulationScope.cs`: scope, closely related handle/result/status types and XML contracts.
- Create `Tests/Editor/SteamAudioPathSimulationTests.cs`: reflection RED, native fixture, lifetime and allocation tests.
- Do not modify the existing renderer or combined playback facade owned by the parallel implementation.

## Task 1: Establish failing contract tests

1. Add reflection tests for scope existence, exact native output ABI and bounded registration/result API.
2. Add generated native wall/probe fixture based on the existing SteamAudioPathSceneTests; use a rooted nonnull path-bake progress callback.
3. Ask the controller to run only the new editor fixture and capture intended missing-type/method failures.

## Task 2: Implement native binding and scope

1. Define all output fields and source input fields from the local 4.8.1 header; avoid managed arrays/delegates in native structs.
2. Retain native context/scene/probe references and create the simulator plus source pool with exception-safe cleanup.
3. Implement generation handles, source/listener validation, immediate invalidation, monotonic revision/time and explicit simulation.
4. Copy finite output scalars and coefficients into preallocated rows; publish Valid only after a complete copy. Keep failures distinct from zero signal.
5. Implement creating-thread guards and idempotent disposal in reverse native ownership order.

## Task 3: Verify and review

1. Controller runs the new editor fixture against actual Steam Audio binaries.
2. Verify wall-open/door-closed/reopened states and copied coefficient lifetime, capacity reuse and stale handles, retained references, invalid-input preservation and wrong-thread rejection.
3. Measure warm simulation/copy managed allocations after native warmup.
4. Run netstandard2.1/C#9 XML-documentation warning checks with real SDK assemblies if available; otherwise report the limitation explicitly.
5. Record actual test evidence and any limitations in the spec/plan; request focused review without broad unrelated changes.
