# Unity Telemetry GameAnalytics Sink Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A bun3-kit package forwards telemetry records to GameAnalytics through a game-supplied mapping, and Jurassic Paradise (JP) sends its analytics records there alongside PostHog.

**Architecture:** New embedded package `com.bun3.unity.telemetry.gameanalytics` beside the PostHog/Sentry sink packages. The sink lazily initializes the GameAnalytics SDK on the first record (custom id, build, environment dimension, keys, error reporting off), converts each record with a `GameAnalyticsMap` delegate (default: design event named after the record), and toggles submission on consent changes. SDK statics sit behind `IGameAnalyticsApi` so the sink is unit-tested with a fake. JP supplies `JpGameAnalytics.Map` and the keys.

**Tech Stack:** Unity 6000.5, C#, UniTask, ZString, NUnit (Unity Test Framework), GameAnalytics Unity SDK `com.gameanalytics.sdk` (MIT).

**Spec:** `E:/Projects/orca/workspace/bun3-kit/jp-telemetry/docs/superpowers/specs/2026-11-05-unity-telemetry-gameanalytics-design.md` (Korean). SDK facts: JP `docs/notes/2026-11-05-gameanalytics-unity-sdk.md`.

## Global Constraints

- Repos: kit worktree `E:/Projects/orca/workspace/bun3-kit/jp-telemetry` (branch `Bun3/jp-telemetry`, same commit as kit main 6314c0a); JP worktree `E:/Projects/orca/workspace/jurassic-paradise/sparkles-analytics-log-system` (branch `✨/analytics-log-system`).
- Kit code, README, package metadata: English only, no Korean; every public member has English XML docs; 0 build warnings; asmdef `defineConstraints: ["UNITY_6000_5_OR_NEWER"]` like the sibling packages; no mention of legacy projects or other packages as references.
- Kit: bump the version on every package content change and add a CHANGELOG entry. New package starts at `0.1.0`; core `com.bun3.unity.telemetry` goes `0.1.0` → `0.1.1`.
- The GameAnalytics SDK is NOT listed in the kit package's `package.json`; each consuming project's `manifest.json` adds it (same as `io.sentry.unity`). First choice: unpinned git URL `https://github.com/GameAnalytics/GA-SDK-UNITY.git`; fallback: OpenUPM scoped registry `https://package.openupm.com` with scope `com.gameanalytics`. `packages-lock.json` pins the version.
- JP code rules: no string `+`, interpolation or `string.Format` (use ZString); no per-frame allocation; telemetry never throws into gameplay.
- GameAnalytics receives the Analytics tier only (`GameAnalyticsSinkOptions.Tiers` default `Analytics`). Never put player ids, match ids, numbers or room names into event ids.
- Commits: Gitmoji + Korean Why/Changes/Impact/Results, ending `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Kit commits unsigned; JP commits are signed (gpgsign on). Never commit editor by-products (fonts, shaders, URP/Graphics settings, EditorBuildSettings, codecoverage settings, unrelated ProjectSettings lines). Never push without the user's approval.
- JP keys (GameAnalytics game key + secret key) are committed in `TelemetryConfig.asset` (spec decision 4).

## Review Focus

1. The SDK's native Windows DLL fails to load (DllNotFound/EntryPoint) in a player → the sink logs one warning and stops; PostHog/Sentry keep working. Test: `SdkFailureStopsTheSinkOnce` (Task 3).
2. The first record arrives with an empty `distinct_id` → no custom id is set (SDK random id), init still runs. Test: `EmptyDistinctIdKeepsSdkId` (Task 3).
3. A game mapping throws for one record → only that record is dropped, later records still flow. Test: `MapFailureDropsOnlyThatRecord` (Task 3).
4. A mapped id contains characters GameAnalytics rejects or more than 5 segments → it is cleaned, not silently lost by the SDK. Tests in Task 2, plus `SanitizedIdsReachTheSdk` (Task 3).
5. Consent turned off mid-run after init → submission stops; turned off before any record → the SDK is never initialized. Test: `ConsentChangeTogglesSubmissionAfterInit` (Task 3).

---

### Task 1: Core exposes the remote-consent answer

**Files:**
- Modify: `unity/Packages/com.bun3.unity.telemetry/Runtime/TelemetryHub.cs` (the private `Allowed(Entry)` check)
- Modify: `unity/Packages/com.bun3.unity.telemetry/package.json` (0.1.1), `CHANGELOG.md`
- Test: `unity/Packages/com.bun3.unity.telemetry/Tests/Editor/` (the existing hub test file)

**Interfaces:**
- Produces: `public bool TelemetryHub.RemoteAllowed { get; }` — true when no consent callback is set or it returns true; false when it returns false or throws. `Allowed(Entry)` uses it for remote sinks.

- [ ] **Step 1: Write the failing test** `RemoteAllowedFollowsConsentAndTreatsThrowAsNo`: build hubs with `.Consent(() => true)`, `.Consent(() => false)`, `.Consent(() => throw new InvalidOperationException())`, and no consent; assert `true, false, false, true`.
- [ ] **Step 2: Run** the kit EditMode tests for the core (command in Task 4 Step 5, `-assemblyNames Bun3.Unity.Telemetry.Editor.Tests`). Expected: compile failure (no `RemoteAllowed`).
- [ ] **Step 3: Implement** `RemoteAllowed` and route `Allowed(Entry)` through it. Bump version, CHANGELOG `### Added`.
- [ ] **Step 4: Run** the same command. Expected: all core tests pass.
- [ ] **Step 5: Commit** (kit): `✨ 텔레메트리 허브가 원격 저장소 동의 값을 공개` with the gitmoji body.

### Task 2: Package scaffold, event model and id cleaning

**Files:**
- Modify: `unity/Packages/manifest.json` (kit project: add `"com.gameanalytics.sdk"` per Global Constraints)
- Create: `unity/Packages/com.bun3.unity.telemetry.gameanalytics/package.json` (name `com.bun3.unity.telemetry.gameanalytics`, displayName `Bun3 Unity Telemetry GameAnalytics`, version `0.1.0`, unity `6000.5`, dependencies `com.bun3.unity.telemetry: 0.1.1` and the UniTask git URL copied from the PostHog package), `README.md`, `CHANGELOG.md`
- Create: `Runtime/Bun3.Unity.Telemetry.GameAnalytics.asmdef` (references `Bun3.Unity.Telemetry`, `UniTask`, `ZString`, and the SDK runtime asmdef name found in the installed package), `Runtime/AssemblyInfo.cs` (`InternalsVisibleTo("Bun3.Unity.Telemetry.GameAnalytics.Editor.Tests")`)
- Create: `Runtime/GameAnalyticsEvent.cs`, `Runtime/GameAnalyticsNames.cs`
- Test: `Tests/Editor/Bun3.Unity.Telemetry.GameAnalytics.Editor.Tests.asmdef` (copy of the PostHog test asmdef with this package's names), `Tests/Editor/GameAnalyticsNamesTests.cs`

**Interfaces:**
- Produces:
  - `public enum GameAnalyticsEventKind : byte { Design, ProgressionStart, ProgressionComplete, ProgressionFail }`
  - `public readonly struct GameAnalyticsEvent { Kind; string Id; double? Value; static Design(string id, double? value = null); static Progression(GameAnalyticsEventKind status, string id); }` — design ids have 1–5 `:` segments, progression ids 1–3; `Value` is used for design only.
  - `public delegate bool GameAnalyticsMap(TelemetryRecord record, List<GameAnalyticsEvent> output);` — false = default conversion (design event named `record.Name`); true with 0 events = skip; true with n = send in order. `output` is cleared by the sink before each call.
  - `internal static string GameAnalyticsNames.Clean(string id, int maxSegments, out bool changed)` — per segment: characters outside `[A-Za-z0-9 _\-.()!?]` become `_`, cut to 64 chars, empty segment becomes `none`; segments beyond `maxSegments` are dropped. Null/empty id → `none`.

- [ ] **Step 1: Install the SDK** in the kit project manifest (git URL first; if UPM cannot resolve it, OpenUPM scoped registry). Let the open editor resolve on its own; if it hangs on "Resolving packages", close and reopen the editor (do not call `Client.Resolve`). Record the resolved version and the SDK runtime asmdef name.
- [ ] **Step 2: Write the failing tests** in `GameAnalyticsNamesTests`:
  - `Clean("mission:reports:success", 5)` → same, `changed == false`
  - `Clean("a:b c#1", 5)` → `"a:b c_1"`, changed
  - a 70-char segment → its first 64 chars, changed
  - `Clean("a:b:c:d:e:f", 5)` → `"a:b:c:d:e"`; `Clean("a:b:c:d", 3)` → `"a:b:c"`
  - `Clean("a::b", 5)` → `"a:none:b"`; `Clean("", 5)` and `Clean(null, 5)` → `"none"`
- [ ] **Step 3: Run** `-assemblyNames Bun3.Unity.Telemetry.GameAnalytics.Editor.Tests`. Expected: compile failure.
- [ ] **Step 4: Implement** the event model and `Clean` (allocation is fine; called per analytics record only).
- [ ] **Step 5: Run** again. Expected: pass, 0 warnings from `com.bun3.unity.telemetry.gameanalytics` in the log.
- [ ] **Step 6: Commit** (kit): manifest + lock, package scaffold, tests.

### Task 3: The sink, options and builder extension

**Files:**
- Create: `Runtime/IGameAnalyticsApi.cs`, `Runtime/GameAnalyticsSink.cs`, `Runtime/GameAnalyticsSinkOptions.cs`, `Runtime/TelemetryBuilderGameAnalyticsExtensions.cs`
- Test: `Tests/Editor/GameAnalyticsSinkTests.cs` (with a `FakeApi : IGameAnalyticsApi` that appends one string per call to a `List<string>` and can throw on a named method). Call strings: method name, then arguments separated by single spaces; a null argument or missing value is `-`; a status is the enum name (e.g. `Progression ProgressionFail match - -`, `Design bad_id -`, `SetEnabled False`).

**Interfaces:**
- Consumes: Task 1 `TelemetryHub.RemoteAllowed`; Task 2 event model and `GameAnalyticsNames.Clean`.
- Produces:
  - `public interface IGameAnalyticsApi { void SetCustomId(string id); void SetBuild(string build); void SetEnvironment(string environment); void SetKeys(string gameKey, string secretKey); void DisableErrorReporting(); void Initialize(); void SetEnabled(bool enabled); void Design(string id, float? value); void Progression(GameAnalyticsEventKind status, string p1, string p2, string p3); }` — `SetEnvironment` makes the value allowed for custom dimension 01 and sets it.
  - `public sealed class GameAnalyticsSinkOptions : TelemetrySinkOptions { public string GameKey; public string SecretKey; }` with `Tiers` initialized to `TelemetryTier.Analytics`.
  - `public sealed class GameAnalyticsSink : ITelemetrySink { public GameAnalyticsSink(string gameKey, string secretKey, GameAnalyticsMap map, IGameAnalyticsApi api, Func<bool> consent); }` — `Remote == true`, `Tick` no-op, `FlushAsync` completes immediately (the SDK flushes itself). Subscribes to `Telemetry.ConsentChanged`.
  - `public static TelemetryBuilder AddGameAnalytics(this TelemetryBuilder builder, GameAnalyticsMap map = null, IGameAnalyticsApi api = null)` — attaches only when the environment has `GameAnalyticsSinkOptions` with both keys and a non-None tier; passes `api ?? new GameAnalyticsSdkApi()` (Task 4) and `() => Telemetry.Hub?.RemoteAllowed ?? false`.
- Behavior the tests pin: first `Write` calls, in order, `SetCustomId` (skipped when `Context.DistinctId` is null/empty), `SetBuild(Context.Build)`, `SetEnvironment(Context.Environment)`, `SetKeys`, `DisableErrorReporting`, `Initialize`, then the record's events. Values are sent as `float`; NaN/Infinity → no value. Ids pass through `Clean` (design 5, progression 3 segments; progression split on `:` into p1..p3, missing parts null); the first changed id per run logs one `Debug.LogWarning`. Any `IGameAnalyticsApi` exception: one warning, the sink stops for the run. A mapping exception: one warning, that record is dropped.

- [ ] **Step 1: Write the failing tests:**
  - `NothingHappensBeforeTheFirstRecord`
  - `FirstRecordInitializesInOrder` — record `app_started`, context `DistinctId "7656"`, `Build "0.50.1"`, `Environment "playtest"`, keys `g`/`s`, no map → calls `["SetCustomId 7656","SetBuild 0.50.1","SetEnvironment playtest","SetKeys g s","DisableErrorReporting","Initialize","Design app_started -"]`; a second record adds only its `Design` call.
  - `EmptyDistinctIdKeepsSdkId` — no `SetCustomId` call.
  - `MapSkipAndManyEvents` — map returns true+0 for `a`, true+[ProgressionFail `match`, Design `match_end:expert:left` 3.5] for `b` → `Progression ProgressionFail match - -`, `Design match_end:expert:left 3.5`.
  - `SanitizedIdsReachTheSdk` — map emits `bad#id` → `Design bad_id -`, one warning (`LogAssert.Expect`).
  - `ConsentChangeTogglesSubmissionAfterInit` — before init `Telemetry.NotifyConsentChanged()` makes no call; after init with consent false → `SetEnabled False`, back true → `SetEnabled True`.
  - `SdkFailureStopsTheSinkOnce` — `Initialize` throws → no exception escapes, one warning, later writes make no calls.
  - `MapFailureDropsOnlyThatRecord` — map throws for `x`, next record `y` sends.
  - `AddGameAnalyticsNeedsBothKeys` — environment with options missing either key attaches nothing; both keys attach one sink with the options' tiers.
- [ ] **Step 2: Run** the package tests. Expected: compile failure.
- [ ] **Step 3: Implement** the four files. Unsubscribe from `Telemetry.ConsentChanged` when the sink stops.
- [ ] **Step 4: Run** again. Expected: all pass, 0 package warnings.
- [ ] **Step 5: Commit** (kit).

### Task 4: Real SDK adapter, docs and kit release

**Files:**
- Create: `Runtime/GameAnalyticsSdkApi.cs` (`public sealed class GameAnalyticsSdkApi : IGameAnalyticsApi`)
- Modify: package `README.md` (setup: manifest line for the SDK, the SDK settings asset at `Resources/GameAnalytics/Settings` with a Windows platform entry, options in the environment bundle, `AddGameAnalytics(map)`), `CHANGELOG.md`
- Modify: the spec §8 — write what each check found (one line each)

- [ ] **Step 1: Verify the spec §8 facts against the installed SDK source** and write them into spec §8: runtime access to the settings object (`GameAnalytics.SettingsGA`?), setting keys (`Settings.UpdateKeys(index, gameKey, secretKey)` with the Windows platform index), turning off `SubmitErrors` at runtime, the build setter, the custom dimension 01 allow-list and setter, the event-id character rule (update `GameAnalyticsNames` and its tests if it differs), whether `Initialize` needs a `GameAnalytics` component instance (create a `GameObject("GameAnalytics")` with it and `DontDestroyOnLoad` if so), whether a custom-id settings flag is needed, and whether `SetEnabledEventSubmission(false)` stops session pings.
- [ ] **Step 2: Implement** `GameAnalyticsSdkApi` with what Step 1 found. If keys or `SubmitErrors` cannot be set at runtime, `SetKeys`/`DisableErrorReporting` only check that the settings asset already holds a Windows entry with keys and `SubmitErrors == false`, logging one warning otherwise (spec §4.6 fallback).
- [ ] **Step 3: README and CHANGELOG.**
- [ ] **Step 4: Compile check:** the kit editor compiles with 0 warnings from `com.bun3.unity.telemetry*` (kit `CLAUDE.md`).
- [ ] **Step 5: Run all telemetry EditMode tests** with no editor open on the kit project:
  ```powershell
  & 'E:\Unitys\6000.5.5f1\Editor\Unity.exe' -batchmode -projectPath 'E:\Projects\orca\workspace\bun3-kit\jp-telemetry\unity' -runTests -testPlatform EditMode -assemblyNames 'Bun3.Unity.Telemetry.Editor.Tests;Bun3.Unity.Telemetry.PostHog.Editor.Tests;Bun3.Unity.Telemetry.Sentry.Editor.Tests;Bun3.Unity.Telemetry.GameAnalytics.Editor.Tests' -testResults "$env:TEMP\ga-edit.xml" -logFile "$env:TEMP\ga-edit.log"
  ```
  Expected: exit code 0, `failed="0"` in the XML; `Select-String 'com\.bun3\.unity\.telemetry[^ ]*[\\/].*warning CS'` finds nothing in the log. Use the asmdef names that actually exist (check each package's `Tests/` folder).
- [ ] **Step 6: Commit** (kit), then **stop and ask the user to approve pushing kit main** (`git push origin HEAD:main`). JP Task 5 needs the package on kit main.

### Task 5: JP sends its analytics records to GameAnalytics

**Files (JP worktree):**
- Modify: `unity/Packages/manifest.json` — add `"com.bun3.unity.telemetry.gameanalytics": "https://github.com/Bun3/bun3-kit.git?path=unity/Packages/com.bun3.unity.telemetry.gameanalytics"` and the SDK line from Task 2; let the editor resolve and update `packages-lock.json` (core lock moves to the new kit main commit).
- Modify: `unity/Assets/_Game/Scripts/JP.Game.asmdef` (reference `Bun3.Unity.Telemetry.GameAnalytics`), `unity/Assets/_Game/Tests/EditMode/JP.Tests.EditMode.asmdef` (same)
- Create: `unity/Assets/_Game/Scripts/Telemetry/JpGameAnalytics.cs`
- Modify: `unity/Assets/_Game/Scripts/Telemetry/TelemetryBootstrap.cs` — `.AddGameAnalytics(JpGameAnalytics.Map)` right after `.AddPostHog()`
- Modify: `unity/Assets/_Game/Resources/TelemetryConfig.asset` — `GameAnalyticsSinkOptions` (Tiers Analytics, keys empty until Task 6) in `Dev`, `Playtest`, `Release`; not in `Editor`. Edit through the editor API, not YAML by hand.
- Create: `unity/Assets/Resources/GameAnalytics/Settings.asset` — via an editor script: Windows platform entry, empty keys, `SubmitErrors = false`, custom dimension 01 values `dev`, `playtest`, `release`.
- Create: `docs/third-party/gameanalytics-unity.md` (same shape as `sentry-unity.md`: version, lock commit, MIT, note the bundled native DLL's third-party components from the research note)
- Modify: `docs/playtest-guide.md` (connected services: GameAnalytics, its keys, the 1.5 s quit delay, data processed in the US), `unity/ProjectSettings/ProjectSettings.asset` `bundleVersion` `0.50.0` → `0.50.1` (only that line)
- Test: `unity/Assets/_Game/Tests/EditMode/Telemetry/JpGameAnalyticsTests.cs`

**Interfaces:**
- Consumes: `GameAnalyticsMap`, `GameAnalyticsEvent`, `AddGameAnalytics`.
- Produces: `public static bool JpGameAnalytics.Map(TelemetryRecord record, List<GameAnalyticsEvent> output)` implementing spec §4.7 exactly: `match_entered` → ProgressionStart `match`; `match_ended` → ProgressionComplete `match` when `result == "win"` else ProgressionFail `match`, plus Design `match_end:{role}:{reason}` (`role` from `Context.Get("role")`, `unknown` when null); `mission_step_finished` → Design `mission:{kind}:{mission}:{result}` plus `:{cause}` when `cause` is present, value `work_seconds` ?? `seconds` ?? none; `mission_completed` → Design `mission_done:{kind}:{mission}:{finisher|contributor|none}` value `my_steps`; `meeting_ended` → Design `meeting:{reason}:{outcome}` value `seconds`; `status_changed` → Design `status:{to}` plus `:{cause}` when present; `sabotage_used` → Design `sabotage:{type}`; `stage_changed` → Design `stage:{track}:{stage}`; `app_started`/`app_quit` → true with no events; anything else → false. Numeric props may be `int`, `long`, `float` or `double`. Build ids with ZString.

- [ ] **Step 1: Write the failing tests** in `JpGameAnalyticsTests`:
  - `MatchEndedSendsProgressionAndDesign` — `match_ended`, props `result "left"`, `reason "left"`, no role → `[ProgressionFail "match", Design "match_end:unknown:left" (no value)]`.
  - `AbandonedStepCarriesCause` — `mission_step_finished`, props `mission "reports"`, `kind "public"`, `result "abandoned"`, `cause "dead"`, `seconds 1.3` → `[Design "mission:public:reports:abandoned:dead" 1.3]`; and `app_started` → returns true, no events.
- [ ] **Step 2: Run** via the JP editor (`Logs/tools/mkrun.py edit '^JP\.Tests\.JpGameAnalyticsTests\.' ...` + `run_script`). Expected: compile failure.
- [ ] **Step 3: Implement** `JpGameAnalytics.Map`, the bootstrap line, config options, settings asset, docs and version.
- [ ] **Step 4: Verify:** JP compile shows 0 new CS warnings; EditMode `JpGameAnalyticsTests` plus the telemetry suites (`MissionPopupTrackerTests|ActivityRecorderTests|ActivityLedgerTests|MatchSummaryTests|MatchTelemetryTests|MatchHistoryTests|TelemetryBootstrapTests|TelemetryConfigTests`) all pass.
- [ ] **Step 5: Commit** (JP, signed), excluding editor by-products.

### Task 6: Keys and a live check (needs the user's GameAnalytics keys)

- [ ] **Step 1:** Ask the user for the game key and secret key; put them into the three `GameAnalyticsSinkOptions` through the editor API.
- [ ] **Step 2:** `JP/Build/Windows Playtest` (schedule `ExecuteMenuItem` from an `EditorApplication.update` callback), copy `unity/steam_appid.txt` next to the exe, run the build ~45 s, close it gracefully.
- [ ] **Step 3:** Check the local JSONL for `app_started` with `environment` `playtest`, and the player log for GameAnalytics errors. Ask the user to confirm in GameAnalytics Realtime: a session, and a design event; and whether the environment dimension filters (spec §8 last item).
- [ ] **Step 4:** Commit the keys (JP, signed). Ask before pushing JP.
