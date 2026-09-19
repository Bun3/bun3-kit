# Shared SFX and Voice Acoustics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give SFX and voice the same shared-profile/local-input acoustic settings without changing existing audible behavior.

**Architecture:** Extract SDK-independent acoustic settings from SoundDef's spatial group. Both SFX and Dissonance adapters consume the resolved value; playback ownership remains separate. Versioned migration preserves local values, shared references and existing disabled attenuation.

**Tech Stack:** Unity 6000.5.5f1 project, C# 9 package conventions, ScriptableObject/SerializedProperty, Steam Audio, Dissonance, NGO, Unity Test Framework, authenticated JP Pipeline API.

**Spec:** [Approved design](../specs/2026-09-19-shared-sfx-voice-acoustics.md).

## Global Constraints

- SDK-independent common types belong in `com.bun3.unity.audio`; preserve existing package Unity version floors and assembly gates.
- English package code, comments, tooltips and README; Korean JP operating documentation.
- Keep SoundDef clip-based. Do not add VoiceDef, a central voice playback manager, or per-player volume UI.
- Preserve Master × Voice gain applied once, decoder lifecycle, native tails, generation/read barriers, AI events and room rules.
- Native path distances remain the attenuation/near-field inputs; do not introduce grid-distance attenuation.
- No per-frame allocations in settings resolution. No ScriptableObject access or added curve copying in DSP callbacks.
- Preserve old voice interfaces through compatibility entry points. Preserve serialization, inactive local settings and profile GUIDs.
- Unity generates .meta. Bump changed package versions and JP generated-asset version when changing authoring.
- Preserve unrelated dirty assets. Existing worktrees are bun3 `Bun3/jp-sound-integration` and JP `rnd/sound-system`.
- Use API-based Unity operations. Actual microphone listening remains the user's follow-up.
- Repository workflow selects subagent-driven implementation; review this plan before execution. Dependent tasks run in order with a fresh implementation/review gate, not simultaneous edits to shared interfaces.

## Review Focus

1. Old assets with both a shared spatial profile and different inactive local values: switching back to local after migration restores the original local values (Task 2).
2. Missing/deleted profile references and disabled attenuation: resolver falls back to local values and never silently enables attenuation (Tasks 1–2).
3. Mono inheritance changes during active speech, including explicit disabled collapse: both adapters use the same effective width settings (Task 3).
4. SoloVoice reload/stale update while the target owns a shared profile: restore exact original selection and never mutate its asset (Task 4).
5. Repeated setup/import and SDK-absent compilation: no duplicate assets, reset tuning, or newly unconditional SDK references (Tasks 2, 5).

## Paths and validation workflow

Paths below beginning `unity/Packages/` are in bun3. Paths beginning `JP/` are relative to the JP repository root. Read both repositories' CLAUDE.md before execution. Read JP `.agents/skills/jp-unity-api/SKILL.md` before controlling the Editor.

Use the JP API helper to discover `run_tests` and its current schema; run focused EditMode/PlayMode fixtures after each change, and full suites once at the end. Store UTF-8 JSON/C# request files outside Assets. Example after schema verification:

```powershell
python .agents/skills/jp-unity-api/scripts/unity_api.py --project unity --action commands --query run_tests
python .agents/skills/jp-unity-api/scripts/unity_api.py --project unity --action execute --command run_tests --parameters-file E:/Temp/shared-acoustics-tests.json --timeout 30
```

Use `{"mode":"editor","filter":"SoundAcousticSettingsTests","async_tests":true}` for the first fixture; poll the test status until completion. A successful HTTP response is not a passing test. When new API types are initially absent, observe the expected compile failure once, then implement immediately and require zero compiler errors before the test run. Tests belong to existing SDK-gated test assemblies; create no new runtime dependencies.

## Task 1: Common data, resolution and Inspector

**Files:** Create `unity/Packages/com.bun3.unity.audio/Runtime/SoundAcousticSettings.cs`, `SoundAcousticSelection.cs`, `SoundAcousticProfile.cs`; create `Editor/SoundAcousticSelectionDrawer.cs`, `Editor/SoundAcousticProfileEditor.cs`; create `Tests/Runtime/SoundAcousticSettingsTests.cs` under the same package.

**Interfaces:** Serializable value `SoundAcousticSettings` contains `bool DistanceAttenuation`, `DistanceAttenuationProfile AttenuationProfile`, `float MinDistance`, `float MaxDistance`, `bool InheritSpatialBlend`, `SpatialBlendProfile SpatialBlendProfile`, `float MonoDistance`, `float FullSpatialDistance`. `SoundAcousticSettings.Default` returns the old SFX defaults (true/null/1/30/true/null/1/3). Serializable class `SoundAcousticSelection` exposes `SoundAcousticProfile Profile`, `SoundAcousticSettings Local`, and `SoundAcousticSettings Resolve()`. Profile exposes `SoundAcousticSettings Settings` and cannot reference another acoustic profile.

- [ ] Add profile-precedence/local-retention tests using actual assets and destroyed-reference fallback. Destroy created assets in finally/teardown.

```csharp
var selection = new SoundAcousticSelection();
selection.Local = SoundAcousticSettings.Default;
var local = selection.Local;
local.DistanceAttenuation = false;
selection.Local = local;
var profile = ScriptableObject.CreateInstance<SoundAcousticProfile>();
try
{
    selection.Profile = profile;
    Assert.That(selection.Resolve().DistanceAttenuation, Is.True);
    selection.Profile = null;
    Assert.That(selection.Resolve().DistanceAttenuation, Is.False);
}
finally { Object.DestroyImmediate(profile); }
```

- [ ] Implement resolution as `return Profile != null ? Profile.Settings : Local;`. Defaults must be explicit because default(struct) does not create the required distance values. Test warmed resolution with `GC.GetAllocatedBytesForCurrentThread` across repeated calls, retaining the result outside the measured loop and expecting zero bytes.
- [ ] Implement the shared PropertyDrawer via `FindPropertyRelative("Profile")`/`FindPropertyRelative("Local")`. Show a profile slot and local controls only when no profile is assigned. Use SerializedProperty for Undo/multi-object editing; do not allocate or modify a profile while repainting. Profile editor draws its Settings fields with the same labels and conditional visibility. Avoid recursive cached profile editors.
- [ ] Verify through SerializedObject that editing Profile leaves Local unchanged; test Undo for local edits and mixed profile selection. Run fixture and import compilation, then commit this package change with gitmoji/trailer.

## Task 2: SoundDef and shared spatial-profile migration

**Files:** Modify audio `Runtime/SoundDef.cs`, `Runtime/SoundSpatialProfile.cs`, `Editor/SoundDefEditor.cs`, `Tests/Runtime/SoundProfileTests.cs`; create `Tests/Runtime/SoundAcousticMigrationTests.cs`. Add editor-only migration tests under `Editor/Tests/` only if the existing assembly layout requires it; do not put UnityEditor references in the runtime test assembly.

**Interfaces:** Both SoundDef and SoundSpatialProfile expose `SoundAcousticSelection Acoustics`. SoundDef exposes `SoundAcousticSettings EffectiveAcoustics`, resolving its selected spatial owner first. Both expose idempotent `bool UpgradeAcoustics()` (true only when conversion occurs). Existing public acoustic names remain forwarding properties so existing source callers compile. Existing Effective* accessors delegate to EffectiveAcoustics.

- [ ] Create serialized legacy fixtures in code using the old field names with custom values: Min=3, Max=18, attenuation=false, mono=2/6. Include shared SoundSpatialProfile with different local settings and references to actual curve/width assets. Import/deserialize and assert effective values before and after explicit upgrade.
- [ ] Keep private serialized legacy storage with `[FormerlySerializedAs("MinDistance")]` and equivalent attributes for every moved field. Hide it in Inspector. Keep serialization version zero for legacy data; resolve/upgrade legacy values without loading or creating ScriptableObjects in serialization callbacks. New code writes through forwarding properties into the new local selection. Do not mark migrated assets dirty from DSP or runtime code.

```csharp
public SoundAcousticSettings EffectiveAcoustics => SpatialProfile != null
    ? SpatialProfile.Acoustics.Resolve()
    : Acoustics.Resolve();
```

- [ ] Test `UpgradeAcoustics()` twice, profile identity, inactive local values, no-profile values and false attenuation. Test old property writes after upgrade; setters must update local settings without detaching a shared profile.
- [ ] Replace old acoustic controls in SoundDefEditor with the shared selection drawer. Keep Spatial mode and AudioSource-specific occlusion controls outside Acoustics. Add Inspector serialization/Undo coverage and rerun SoundProfileTests.
- [ ] Commit after tests pass. Do not reserialize JP authored assets until Task 4 has the migration entry point.

## Task 3: Shared native adapter input and old voice compatibility

**Files:** Modify `unity/Packages/com.bun3.unity.audio.steamaudio/Runtime/SteamAudioSoundOutput.cs`, `Runtime/PlanarAcousticSfxBinding.cs`; modify `unity/Packages/com.bun3.unity.audio.dissonance.steamaudio/Runtime/DissonancePlanarAcousticOutput.cs`. Add `Tests/Editor/SharedAcousticSettingsTests.cs` to the Steam Audio package and relevant gated tests to the Dissonance/Steam Audio package's existing test directory.

**Interfaces:** Add audio-core `IResolvedSoundAcousticSettings` in `Runtime/SoundAcousticSettings.cs` with `bool IsAvailable { get; }` and `SoundAcousticSettings Acoustics { get; }`. Add a DissonancePlanarAcousticOutput constructor accepting this interface; retain the old constructor/interfaces through a one-time compatibility wrapper. SteamAudioSoundOutput exposes current `SoundAcousticSettings Acoustics`, resolving its active SoundDef live on the main thread.

- [ ] Add tests for old voice interface behavior (null profile=1/15m and world mono inheritance), new settings (custom 3/18m, disabled attenuation, explicit mono=0), and live shared-profile mutation. Ensure unavailable settings still gate silent.
- [ ] Read each resolved value once per Prepare/Publish phase and pass its fields to the existing binding methods. Do not change decoder/pump/retirement internals.

```csharp
var acoustic = settings.Acoustics;
binding.ApplyDistanceProfile(acoustic.DistanceAttenuation,
    acoustic.AttenuationProfile, acoustic.MinDistance, acoustic.MaxDistance);
```

- [ ] Route width consistently: inherit uses `world.GetSpatialBlend(handle)`; otherwise use `world.GetSpatialBlend(handle, acoustic.SpatialBlendProfile, acoustic.MonoDistance, acoustic.FullSpatialDistance)`. Same logic applies to SFX and voice. Preserve existing blocking paths even when attenuation is disabled.
- [ ] Run new tests, PlanarAcousticRuntimeTests, NearFieldSpatialTests and existing gated native voice playback tests. Include an active-generation update assertion instead of only comparing struct fields. Verify profile resolution adds no warm allocations. Commit each affected package independently after the dependent change passes.

## Task 4: JP authoring, migration and SoloVoice

**Files:** Modify `JP/unity/Assets/_Game/Scripts/Core/GameConfig.cs`, `Scripts/Audio/GameAcousticVoicePlayback.cs`, `Editor/M1EditorSetup.Sound.cs`, `Editor/M1EditorSetup.cs`, `Editor/SoloVoiceAudioSync.cs`, `Tests/EditMode/SoloVoiceAudioSyncTests.cs`; create `Editor/SoundAcousticMigration.cs` and `Tests/EditMode/Audio/SoundAcousticMigrationTests.cs` under the JP _Game folder.

**Interfaces:** GameConfig exposes `SoundAcousticSelection VoiceAcoustics`, `SoundAcousticSettings EffectiveVoiceAcoustics`, and idempotent `bool UpgradeVoiceAcoustics()`. Preserve old voice public names as forwarding accessors and hidden serialized legacy fields. JP settings adapter implements `IResolvedSoundAcousticSettings`. Editor `SoundAcousticMigration.Run()` upgrades/saves SoundDef, SoundSpatialProfile and GameConfig assets without replacing GUIDs.

- [ ] Test voice migration using enabled=false, a non-default curve reference, separate common and voice mono profiles. Assert old no-voice-profile inheritance, null attenuation fallback 1/15m, and repeated migration. Test an already-authored new profile survives M1 setup.
- [ ] Implement JP adapter properties: `IsAvailable => GameConfig.I != null`, `Acoustics => GameConfig.I.EffectiveVoiceAcoustics`; pass it to the new voice output constructor. Keep volume and room code unchanged.
- [ ] Update setup assignments to new local/shared settings; invoke idempotent migration before saving. Increment GeneratedAssetVersion based on its current value, not a remembered literal. Serialize existing authored assets through Unity and compare effective acoustic values and GUID references against a captured pre-migration inventory. Preserve false attenuation in WorldSpatial.
- [ ] Update SoloVoice message to transmit resolved attenuation enable, curve and fallback Min/Max, plus resolved mono distances. On receive, use a temporary selection/curve with inheritance disabled because distances are already resolved. Save the original selection object and restore it exactly on Stop/stale/role change. Never write into its Profile.Settings.

```csharp
// Assertions to add around the existing sync fixture's send/receive/Stop calls:
Assert.That(target.EffectiveVoiceAcoustics.MaxDistance, Is.EqualTo(18f));
Assert.That(originalProfile.Settings.MaxDistance, Is.EqualTo(originalMaximum));
sync.Stop();
Assert.That(target.VoiceAcoustics, Is.SameAs(originalSelection));
```

- [ ] Extend existing SoloVoiceAudioSyncTests to check stale (>2s) restore, repeated receive without curve replacement, custom fallback range, disabled attenuation, profile replacement, and explicit mono collapse disabled. Use fixture-owned temporary files and controlled timestamps; no real mic needed.
- [ ] Run JP migration/authoring, SoloVoice and SoundOutputPlayModeTests. Commit JP code separately from migrated assets when each diff is reviewable; preserve unrelated dirty assets.

## Task 5: Integration validation, documentation and publication

**Files:** Changed bun3 package.json files and READMEs; JP `docs/sound-authoring-guide.md`, `docs/playtest-guide.md`, `docs/design/development_task/git_branch_history.md`, `unity/Packages/packages-lock.json`; final verification notes appended to this plan.

- [ ] With local package sources, run full EditMode and PlayMode suites once after focused tests pass. Inspect unexpected Console errors and pending async operations. Investigate failures instead of suppressing logs.
- [ ] Verify optional SDK gates using a separate temporary Unity project or existing package compile fixture without proprietary SDK assemblies. Include audio core's required common/core/UniTask/Addressables dependencies; do not delete SDK assets from either working project.
- [ ] Verify migration/setup idempotence by two Unity authoring runs and normalized serialized comparisons. Assert existing key order/content and unrelated tuning unchanged. Check shared-profile UI through SerializedObject/Inspector tooling and preserve Undo behavior.
- [ ] Update the English package examples and Korean guide to show the same AcousticSelection control for SoundDef and Voice Acoustics. Remove obsolete voice field instructions and label basic AudioSource-only settings. Check links/fences and compile any complete changed examples.
- [ ] Bump every changed package version, commit/push by package, and resolve JP's nine bun3 branch Git URLs via UPM API. Never commit a local `file:` path. Verify actual PackageInfo source, hashes, compiler state and targeted JP audio tests after the switch.
- [ ] Commit/push JP package lock, migration assets, guide and branch history. Report exact test results, preserved tuning and remaining user microphone verification. Do not claim perceptual quality from automated tests.

## Self-review

- Common settings/Inspector: Task 1; nested precedence and serialization: Task 2; shared native interpretation and legacy callers: Task 3; JP/temporary test settings: Task 4; docs, SDK absence and publication: Task 5.
- Review Focus 1–5 each has explicit owning tests above. Existing playback ownership and gain paths have no planned modifications.
- Constructor/interface names are shared across Tasks 1–4. Profile stores Settings, Selection stores Profile/Local, consumers read resolved Acoustics.
- This is an implementation plan awaiting user review, not a record of completed code or tests.
