# Sound Catalog 테이블과 카탈로그 단일 관리 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 사운드를 `SoundCatalog` 하나로 관리한다. Def는 카탈로그의 숨긴 하위 에셋이 되고, 카탈로그 인스펙터는 모든 키를 찾고·비교하고·고치고·추가·삭제하는 표가 된다. 프로파일은 칸마다 덮어쓸 수 있고, 차지 사운드는 키로 재생한다. 끝으로 JP를 옮겨 홀드 상호작용을 차오르는 소리로 바꾼다.

**Architecture:**
- **런타임:** `SoundDef`와 `SoundAcousticSelection`에 비트 플래그 덮어쓰기를 더해 `Effective*`/`Resolve()`가 칸 단위로 고른다. 차지 설정은 `SoundDef.Charge`로 옮긴다. 부분 소리는 카탈로그 색인이 `.start/.complete/.cancel` 키로 미리 찾는다.
- **에디터:** 공개 정적 API 두 개(`SoundDefOverrides`, `SoundCatalogEditing`)가 모든 수정 규칙을 가진다. 표(`SoundCatalogTable`)·Def 인스펙터·음향 drawer·게임 셋업 코드가 같은 API를 쓴다.

**Tech Stack:** Unity 6000.3+ (호스트 6000.5.5f1), C# 9, UnityEditor IMGUI 또는 UI Toolkit, NUnit + Unity Test Framework.

**Spec:** `docs/superpowers/specs/2026-11-05-unity-audio-catalog-table-design.md`

## Global Constraints

- 패키지: `unity/Packages/com.bun3.unity.audio`, `package.json` 버전 `0.9.0`. 런타임 asmdef `Bun3.Unity.Audio`, 에디터 asmdef `Bun3.Unity.Audio.Editor`.
- kit 규칙:
  - `docs/` 밖은 영어만 쓴다(주석·XML 문서·UI 문구).
  - 모든 public 멤버에 영어 XML 문서를 단다.
  - 컴파일 경고 0건.
  - 필요한 계약만 주석으로 남긴다.
  - partial 파일은 `타입.역할.cs`로 짓고, 머리에 역할 주석 한 줄을 둔다.
  - 폴더 = 네임스페이스.
- 핫패스 무할당: `Effective*`, `SoundAcousticSelection.Resolve()`, `PlayCharge`, `ChargeSoundHandle.SetProgress`는 힙 할당이 없다.
- `BUN3_ADDRESSABLES` 정의가 있을 때와 없을 때 모두 컴파일된다.
- 1.0 전이라 깨지는 변경(`ChargeSoundDef` 제거)은 minor 0.9.0에 넣고 CHANGELOG의 Breaking 항목에 적는다.
- 커밋: `writing-gitmoji-commits` 스킬을 따른다. 제목은 한국어 gitmoji 형식이고, 여러 문단은 `-m`을 문단마다 쓴다. kit main 병합·푸시와 JP 푸시는 사용자 확인을 받은 뒤에만 한다. main 병합은 `--no-ff`, 제목은 "🔀 …를 main에 통합"이다.
- **kit 테스트 실행 방법:**
  - kit 패키지 테스트는 JP 워크트리 에디터에서 돌린다(`E:/Projects/orca/workspace/jurassic-paradise/low-volume-bind-sfx-keys/unity`, Pipeline API 헬퍼 `.agents/skills/jp-unity-api/scripts/unity_api.py`).
  - 돌리는 동안만 JP `Packages/manifest.json`을 바꾼다. `com.bun3.unity.audio`를 `file:E:/Projects/orca/workspace/bun3-kit/audio-bootstrap/unity/Packages/com.bun3.unity.audio`로 두고 `testables`에 `com.bun3.unity.audio`를 넣는다.
  - JP 커밋 전에 manifest와 packages-lock을 원래대로 되돌린다. 이 변경은 절대 커밋하지 않는다.
  - 실행: `--action execute --command run_tests`에 `{"mode":"editor","filter":"<테스트 이름>","filter_type":"testName","async_tests":true,"timeout":1500}`(PlayMode면 `"mode":"playmode"`)를 넘긴다. `test_status`를 완료까지 읽는다.
  - 테스트 전에는 컴파일이 끝났고 `EditorUtility.scriptCompilationFailed == false`인지 확인한다.
- 테스트가 다시 저장한 JP 출고 에셋(TestDinosaur.prefab, DinosaurAI.unity, ProjectSettings, 줄바꿈만 바뀐 셰이더 등)은 경로를 지정해 되돌린다. `.meta`는 손으로 쓰지 않는다.
- **JP 규칙:**
  - 런타임 코드에 문자열 `+`·보간·`string.Format`을 쓰지 않는다(ZString). 코루틴을 쓰지 않는다.
  - 셋업 산출물이 바뀌면 `M1EditorSetup.GeneratedAssetVersion`을 올린다.
  - 셋업 코드는 빌드 버전을 쓰지 않는다.
  - JP에는 자동 테스트를 더하지 않는다.

## Review Focus

1. **잘못된 항목이 있는 카탈로그:** 아직 옮기지 않은 게임, Def가 빠진 항목, 카탈로그 밖 Def 파일, 같은 Def를 쓰는 두 키가 있어도 표가 예외 없이 그려지고 해당 행에 오류를 보여야 한다. → Task 5 `Validate_ReportsInvalidEntriesPerRow`
2. **프로파일이 없는 칸의 Override:** 프로파일이 없는 칸에서 Override를 부르면 값이 바뀌지 않고 표시도 켜지지 않아야 한다. → Task 3 `Override_WithoutProfile_ChangesNothing`
3. **충돌하는 키로 추가·이름 변경:** 빈 키나 이미 있는 키로 추가·이름 변경하면 카탈로그가 전혀 바뀌지 않은 채 거절되어야 한다. → Task 5 `AddAndRename_RejectBlankOrDuplicateKeys_WithoutChanges`
4. **프로파일 교체:** 프로파일을 다른 것으로 바꿔도 덮어쓴 칸은 개인 값을 유지하고, 나머지 칸만 새 프로파일을 따라야 한다. → Task 1 `ProfileSwap_KeepsOverriddenFields`
5. **무할당:** `Effective*`·`Resolve()`가 재생마다 불려도 할당이 없어야 한다. → Task 1·2의 무할당 테스트

---

### Task 0: 브랜치 준비

**Files:** 없음(작업 공간만).

- [ ] **Step 1:** kit 워크트리 `E:/Projects/orca/workspace/bun3-kit/audio-bootstrap`에서 `git fetch origin` 후 `git switch -c Bun3/audio-catalog-table origin/main`.
  - 추적 안 되는 스펙·플랜 파일은 그대로 따라온다.
  - 작업 트리에 다른 변경이 있으면 멈추고 보고한다.
- [ ] **Step 2:** 스펙과 이 플랜을 커밋한다. 제목 `📝 사운드 카탈로그 표와 단일 관리 설계·구현 계획 추가`.
- [ ] **Step 3: JP 선행 조건.** JP 브랜치 `🔈/bind-sfx-keys`에는 아직 커밋하지 않은 팝업 소리 변경이 있다(`GamePopup.cs`, `GamePopups.cs`, `MeetingView.cs`, `ProbeBlockPopup.cs`, `docs/sound-authoring-guide.md`).
  - Task 8을 시작하기 전에 사용자에게 그 커밋을 요청받았는지 확인한다.
  - 받지 않았으면 Task 7까지만 진행하고 멈춘다.

### Task 1: `SoundDef` 칸별 덮어쓰기

**Files:**
- Create: `Runtime/SoundDefField.cs`
- Modify: `Runtime/SoundDef.cs`
- Test: `Tests/Runtime/SoundDefOverrideTests.cs`

**Interfaces:**
- Produces:
  - `[Flags] public enum SoundDefField { None = 0, Volume = 1 << 0, Pitch = 1 << 1, Loop = 1 << 2, MixerGroup = 1 << 3, VolumeGroup = 1 << 4, MaxInstances = 1 << 5, Cooldown = 1 << 6, Priority = 1 << 7, Spatial = 1 << 8, Occlusion = 1 << 9, OcclusionVolumeAtFull = 1 << 10, Acoustics = 1 << 11 }`
  - `SoundDef`: 직렬화 필드 `[SerializeField, HideInInspector] private SoundDefField _overrides;`(직렬화 경로 `"_overrides"`)
  - `public bool IsOverridden(SoundDefField field)`
  - `internal SoundDefField Overrides { get; set; }`

- [ ] **Step 1: 실패하는 테스트를 쓴다.** `SoundDefOverrideTests`:
  - `Effective_UsesProfileUnlessFieldIsOverridden`
    - 준비: Playback 프로파일 Volume (0.2, 0.2), 로컬 Volume (0.7, 0.7).
    - 표시 없음: `EffectiveVolume.Min == 0.2f`.
    - `Overrides = SoundDefField.Volume`이면 0.7, 이때 `EffectivePitch`는 여전히 프로파일 값.
    - 같은 방식으로 Routing(`VolumeGroup` "ui" 대 "sfx"), Concurrency(`Priority` 10 대 200), Spatial(`Spatial` Positional 대 None)을 확인한다.
  - `EffectiveAcoustics_FollowsAcousticsOverride`
    - SpatialProfile의 Acoustics.Local.MaxDistance 50, Def 로컬 18.
    - 표시 없으면 50, `SoundDefField.Acoustics` 표시면 18.
  - `ProfileSwap_KeepsOverriddenFields`
    - Volume만 덮어쓴 상태에서 PlaybackProfile을 Pitch (2, 2)인 다른 프로파일로 바꾼다.
    - `EffectiveVolume`은 로컬, `EffectivePitch.Min == 2f`.
  - `NoProfile_IgnoresOverrideFlags`
    - 프로파일 없이 `Overrides = SoundDefField.Volume`이어도 `EffectiveVolume`은 로컬 값이다.
  - `Effective_DoesNotAllocate`
    - 기존 `SoundCatalogTests`처럼 할당 감지기를 먼저 검증한다.
    - 그다음 모든 `Effective*`를 1000번 읽는 델리게이트가 `Is.Not.AllocatingGCMemory()`인지 본다.
- [ ] **Step 2:** 테스트를 돌려 컴파일 실패(없는 멤버)를 확인한다.
- [ ] **Step 3:** 구현한다.
  - 각 `Effective*`: `Profile != null && (_overrides & 해당 플래그) == 0 ? profile 값 : 로컬 값`.
  - `EffectiveAcoustics`와 `EffectiveOcclusion`·`EffectiveOcclusionVolumeAtFull`·`EffectiveSpatial`은 `SpatialProfile`과 각 플래그를 쓴다.
  - XML 문서에 덮어쓰기 규칙을 적는다.
- [ ] **Step 4:** `SoundDefOverrideTests`와 기존 `SoundProfileTests`·`SoundProfilePlaybackTests`가 모두 통과하는지 확인한다.
- [ ] **Step 5:** 커밋 `✨ SoundDef에 프로파일 칸별 덮어쓰기 추가`.

### Task 2: 음향 설정 칸별 덮어쓰기

**Files:**
- Create: `Runtime/SoundAcousticField.cs`
- Modify: `Runtime/SoundAcousticSelection.cs`
- Test: `Tests/Runtime/SoundAcousticSettingsTests.cs`(기존 파일에 추가)

**Interfaces:**
- Produces:
  - `[Flags] public enum SoundAcousticField { None = 0, DistanceAttenuation = 1 << 0, AttenuationProfile = 1 << 1, MinDistance = 1 << 2, MaxDistance = 1 << 3, InheritSpatialBlend = 1 << 4, SpatialBlendProfile = 1 << 5, MonoDistance = 1 << 6, FullSpatialDistance = 1 << 7 }`
  - `SoundAcousticSelection`: `public SoundAcousticField Overrides;`(직렬화 경로 `"Overrides"`)

- [ ] **Step 1: 실패하는 테스트를 쓴다.**
  - `Resolve_MergesProfileAndOverriddenLocalFields`
    - 프로파일 Settings: MinDistance 2, MaxDistance 40.
    - Local: MinDistance 5, MaxDistance 18.
    - `Overrides = SoundAcousticField.MaxDistance`이면 결과는 MinDistance 2, MaxDistance 18.
    - 표시 없음이면 프로파일 값 그대로, 프로파일이 null이면 Local 그대로.
  - `Resolve_DoesNotAllocate`(Task 1과 같은 방식).
- [ ] **Step 2:** 실패를 확인한다.
- [ ] **Step 3:** `Resolve()`가 프로파일이 있으면 `Profile.Settings`를 복사한 뒤 표시된 필드만 Local에서 덮어쓴 구조체를 돌려주도록 구현한다.
- [ ] **Step 4:** 이 테스트와 기존 `SoundAcousticSettingsTests`·`SoundAcousticSelectionEditorTests`가 통과하는지 확인한다.
- [ ] **Step 5:** 커밋 `✨ 음향 설정 선택에 칸별 덮어쓰기 추가`.

### Task 3: 덮어쓰기 에디터 규칙과 Def 인스펙터·음향 drawer

**Files:**
- Create: `Editor/SoundDefOverrides.cs`
- Modify: `Editor/SoundDefEditor.cs`, `Editor/SoundAcousticSelectionDrawer.cs`, `Editor/SoundSpatialProfileEditor.cs`(같은 drawer를 쓰는 곳이면)
- Modify: `Tests/Editor/Bun3.Unity.Audio.Editor.Tests.asmdef`(references에 `Bun3.Unity.Audio.Editor` 추가)
- Test: `Tests/Editor/SoundDefOverridesTests.cs`

**Interfaces:**
- Consumes: Task 1·2의 `SoundDefField`, `SoundAcousticField`, 직렬화 경로 `"_overrides"`, `"Overrides"`
- Produces (`namespace Bun3.Unity.Audio.Editor`, 모두 `public static class SoundDefOverrides`):
  - `public enum SoundProfileGroup { Playback, Routing, Concurrency, Spatial }`(같은 파일)
  - `SoundProfileGroup GroupOf(SoundDefField field)`
  - `string PropertyPath(SoundDefField field)`: `"Volume"`, `"Pitch"`, `"Loop"`, `"MixerGroup"`, `"VolumeGroup"`, `"MaxInstances"`, `"Cooldown"`, `"Priority"`, `"Spatial"`, `"Occlusion"`, `"OcclusionVolumeAtFull"`, `"_acoustics"`. 프로파일 에셋의 필드 경로도 같다.
  - `string ProfilePropertyPath(SoundProfileGroup group)`: `"PlaybackProfile"`, `"RoutingProfile"`, `"ConcurrencyProfile"`, `"SpatialProfile"`
  - `bool IsInherited(SerializedObject def, SoundDefField field)`
  - `void Override(SerializedObject def, SoundDefField field)`
  - `void Revert(SerializedObject def, SoundDefField field)`
  - `void UseLocal(SerializedObject def, SoundProfileGroup group)`
  - `bool IsAcousticInherited(SerializedProperty selection, SoundAcousticField field)`
  - `void OverrideAcoustic(SerializedProperty selection, SoundAcousticField field)`
  - `void RevertAcoustic(SerializedProperty selection, SoundAcousticField field)`
  - `void UseLocalAcoustics(SerializedProperty selection)`
  - 모든 함수는 스스로 `ApplyModifiedProperties()`를 불러 Undo에 남긴다.

- [ ] **Step 1: 실패하는 테스트를 쓴다.** `SoundDefOverridesTests`:
  - `Override_CopiesProfileValueThenFlags`: 프로파일 Volume 0.2, 로컬 0.7. `Override(Volume)` 뒤 로컬 Volume이 0.2이고, `IsOverridden(Volume)`, `EffectiveVolume.Min == 0.2f`.
  - `Revert_ClearsFlagAndKeepsLocalValue`
  - `UseLocal_CopiesWholeGroupAndClearsProfile`
    - Concurrency 프로파일 (MaxInstances 3, Cooldown 0.5, Priority 20), 로컬 다른 값.
    - `UseLocal(Concurrency)` 뒤 프로파일이 null이고, 로컬이 3·0.5·20, 해당 묶음 플래그가 꺼져 있다.
  - `Override_WithoutProfile_ChangesNothing`
  - `OverrideAcoustic_CopiesProfileSetting`
  - `UseLocalAcoustics_CopiesSettingsAndClearsProfile`
- [ ] **Step 2:** 실패를 확인한다.
- [ ] **Step 3:** `SoundDefOverrides`를 구현한다. 값 복사는 `SerializedProperty.boxedValue`로 한다. 프로파일 쪽 `SerializedObject`는 읽기 전용으로 만든다.
- [ ] **Step 4: Def 인스펙터와 음향 drawer를 바꾼다.**
  - `SoundDefEditor`
    - 프로파일이 있는 묶음의 필드는 프로파일 값을 비활성으로 보여 준다.
    - 덮어쓴 필드는 로컬 값을 굵게 보여 주고 왼쪽에 파란 줄을 둔다.
    - 필드 우클릭 메뉴에 `Override with local value` / `Revert to profile`을 둔다.
    - 묶음마다 `Use local` 버튼과 `Edit profile` 버튼(프로파일 에셋 선택)을 둔다.
    - 프로파일 칸을 None으로 바꾸면 `UseLocal`로 처리한다.
    - "Shared settings…" 도움말과 프로파일 값 직접 편집은 지운다.
  - `SoundAcousticSelectionDrawer`도 같은 방식이다. 프로파일 칸을 None으로 바꾸면 `UseLocalAcoustics`로 처리한다.
- [ ] **Step 5:** 에디터 테스트 전체와 `SoundAcousticSelectionEditorTests`가 통과하는지 확인한다.
  - 실제 에디터에서 JP의 `sfx.door.open` Def를 열어 비활성·덮어쓰기·Use local 표시를 눈으로 확인한다.
  - 이 시점 JP Def는 아직 파일이다. 확인만 하고 저장된 변경은 경로를 지정해 되돌린다.
- [ ] **Step 6:** 커밋 `✨ 프로파일 칸별 덮어쓰기 에디터 규칙과 Def·음향 인스펙터 적용`.

### Task 4: 차지 사운드를 카탈로그 키로

**Files:**
- Create: `Runtime/SoundChargeSettings.cs`
- Modify: `Runtime/SoundDef.cs`, `Runtime/SoundCatalog.cs`, `Runtime/SoundSystem.Charge.cs`, `Runtime/SoundSystem.Static.cs`, `Runtime/ChargeSoundHandle.cs`
- Delete: `Runtime/ChargeSoundDef.cs`(+ `.meta`)
- Test: `Tests/Runtime/ChargeSoundTests.cs`(다시 씀)

**Interfaces:**
- Produces:
  - `[Serializable] public sealed class SoundChargeSettings`
    - 필드: `AnimationCurve VolumeByProgress = AnimationCurve.Linear(0f, 0f, 1f, 1f)`, `AnimationCurve PitchByProgress = AnimationCurve.Linear(0f, 0.667f, 1f, 1f)`, `float CompleteFadeOut = 0.05f`, `float CancelFadeOut = 0.15f`
    - `internal float VolumeAt(float progress)`, `internal float PitchScaleAt(float progress)`: 동작은 기존 `ChargeSoundDef`와 같다.
  - `SoundDef`: `public SoundChargeSettings Charge = new();`
  - `SoundCatalog`
    - 상수: `public const string ChargeStartSuffix = ".start"`, `ChargeCompleteSuffix = ".complete"`, `ChargeCancelSuffix = ".cancel"`
    - `internal void GetChargeParts(string loopKey, out SoundDef start, out SoundDef complete, out SoundDef cancel)`: 색인을 만들 때 함께 만든 표에서 찾는다. 없으면 null이다.
  - `SoundSystem`(정적)
    - `public static ChargeSoundHandle PlayCharge(string key)`
    - `public static ChargeSoundHandle PlayCharge(string key, string fallbackKey)`
    - `public static ChargeSoundHandle PlayChargeAt(string key, string fallbackKey, Vector3 position)`
    - `public static ChargeSoundHandle PlayCharge(string key, string fallbackKey, Transform follow)`
  - `SoundSystem`(내부): `internal ChargeSoundHandle PlayChargeCore(SoundDef loop, SoundDef start, SoundDef complete, SoundDef cancel, Vector3 position, Transform follow)`
  - `ChargeSoundHandle`
    - 내부 필드: `SoundHandle Loop`, `SoundDef LoopDef`, `SoundDef CompleteDef`, `SoundDef CancelDef`, `float BasePitch`
    - 공개 API는 그대로(`IsValid`, `SetProgress`, `Complete()`, `Cancel()`)이고 `public void Stop()`을 더한다. `Stop()`은 효과음 없이 반복음을 바로 멈춘다.
- Removes: `ChargeSoundDef`, `SoundSystem.PlayCharge(ChargeSoundDef …)` 인스턴스 오버로드 3개

- [ ] **Step 1: `ChargeSoundTests`를 키 방식으로 다시 쓴다.**
  - 기존 곡선·진행도·완료·취소·도둑맞은 루프 시나리오는 `PlayChargeCore`로 옮기고 기대값을 그대로 둔다.
  - 새 테스트(정적 API, 테스트가 만든 카탈로그로 `SoundSystem.Initialize`, TearDown에서 `Shutdown`):
    - `PlayCharge_ResolvesPartsByKeySuffix`: 키 `x.hold`, `x.hold.start`, `x.hold.complete`, `x.hold.cancel`이 있으면 시작 때 start 1개가 재생되고 `Complete()`는 complete를 재생한다.
    - `PlayCharge_FallsBackPerPart`
      - `x.hold`(클립 있음)와 `x.hold.start`(클립 없음), 대체 키 `t.hold`·`t.hold.start`(클립 있음)를 둔다.
      - 루프는 `x.hold`, 시작음은 `t.hold.start`가 재생된다.
    - `PlayCharge_EmptyOrUnknownKey_ReturnsInvalidSilently`: 두 키 모두 클립이 없으면 잘못된 핸들이고 경고 로그가 없다.
    - `PlayCharge_BeforeInitialize_WarnsOnceAndReturnsInvalid`: 기존 `WarnUninitialized` 규칙과 같다.
    - `Stop_EndsLoopWithoutStinger`
    - `SetProgress_DoesNotAllocate`: 기존 무할당 테스트 방식.
- [ ] **Step 2:** 실패(없는 API)를 확인한다.
- [ ] **Step 3: 구현한다.**
  - `ChargeSoundDef`의 곡선 평가를 `SoundChargeSettings`로 옮긴다.
  - `PlayChargeCore`는 기존 `PlayChargeCore` 흐름을 `loop.Charge` 기준으로 그대로 따른다.
  - 정적 API의 대체 규칙:
    - 루프: `TryGetPlayable(key) || TryGetPlayable(fallbackKey)`
    - 부분 소리: `key`의 부분이 재생 가능하면 그것, 아니면 `fallbackKey`의 같은 부분, 아니면 null
  - 부분 키 문자열은 색인 재구성 때만 만든다.
  - `ChargeSoundDef.cs`와 `.meta`를 지운다.
- [ ] **Step 4:** `ChargeSoundTests`, `SoundSystemStaticTests`, `AllocationTests`가 통과하는지 확인한다.
- [ ] **Step 5:** 커밋 `💥 차지 사운드를 카탈로그 키 재생으로 옮기고 ChargeSoundDef 제거`.

### Task 5: 카탈로그 하위 에셋 편집 API와 검증

**Files:**
- Create: `Editor/SoundCatalogEditing.cs`
- Test: `Tests/Editor/SoundCatalogEditingTests.cs`

**Interfaces:**
- Produces (`namespace Bun3.Unity.Audio.Editor`):
  - `public enum SoundCatalogIssue { None, BlankKey, DuplicateKey, MissingDefinition, ExternalDefinition, SharedDefinition }`
  - `public static class SoundCatalogEditing`
    - `SoundDef AddSound(SoundCatalog catalog, string key)`
      - 새 `SoundDef`를 만든다(`name = key`, `hideFlags = HideFlags.HideInHierarchy`).
      - `AssetDatabase.AddObjectToAsset`로 카탈로그 파일에 넣고 항목을 끝에 붙인다.
    - `SoundDef Duplicate(SoundCatalog catalog, string sourceKey, string newKey)`: 직렬화 값 전체를 복사한다.
    - `void Rename(SoundCatalog catalog, string key, string newKey)`: 키와 Def 이름을 함께 바꾼다.
    - `void Delete(SoundCatalog catalog, string key)`: 항목과 하위 에셋 Def를 지운다. Def가 카탈로그 밖 파일이면 항목만 지운다.
    - `bool IsOwnedBy(SoundCatalog catalog, SoundDef def)`: 같은 에셋 경로에 있고 주 에셋이 아니면 true다.
    - `SoundCatalogIssue[] Validate(SoundCatalog catalog)`: 항목 순서대로 하나씩 돌려준다.
  - 규칙:
    - `catalog`가 저장된 에셋이 아니면 `InvalidOperationException`을 던진다.
    - 빈 키·중복 키·없는 키는 `ArgumentException`을 던지고 아무것도 바꾸지 않는다.
    - 성공하면 `EditorUtility.SetDirty` → `AssetDatabase.SaveAssetIfDirty(catalog)` → `Undo.ClearUndo`를 카탈로그와 관련 Def에 한다.

- [ ] **Step 1: 실패하는 테스트를 쓴다.**
  - 테스트용 임시 폴더 `Assets/__Bun3AudioCatalogEditingTests`에 카탈로그 에셋을 만들고, TearDown에서 폴더째 지운다.
  - `AddSound_CreatesHiddenSubAssetNamedByKey`: 하위 에셋이고, 숨김 플래그, 이름이 키, `catalog.Get(key)`가 같은 객체, `ValidateOrThrow` 통과.
  - `Duplicate_CopiesValues`: Volume 0.4로 만든 Def를 복제하면 새 키의 Volume이 0.4이고 다른 객체다.
  - `Rename_ChangesKeyAndName`
  - `Delete_RemovesEntryAndSubAsset`: 에셋 경로의 하위 에셋 수가 줄어든다.
  - `AddAndRename_RejectBlankOrDuplicateKeys_WithoutChanges`: 실패 뒤 항목 수와 키가 그대로다.
  - `Validate_ReportsInvalidEntriesPerRow`
    - `SetEntries`로 문제 항목을 만든다: 공유 Def 두 키, 카탈로그 밖 `ScriptableObject.CreateInstance` Def(저장 안 함), 정상 키.
    - 결과가 `SharedDefinition, SharedDefinition, ExternalDefinition, None`이다.
    - 빈 키·중복 키·null Def는 `SetEntries`가 거절하므로, 직렬화 필드를 `SerializedObject`로 직접 써서 만든다.
- [ ] **Step 2:** 실패를 확인한다.
- [ ] **Step 3:** 구현한다.
- [ ] **Step 4:** 통과를 확인한다.
- [ ] **Step 5:** 커밋 `✨ 사운드 카탈로그 하위 에셋 편집 API와 항목 검증 추가`.

### Task 6: 표 인스펙터 — 보기·찾기

**Files:**
- Create: `Editor/SoundCatalogTable.cs`(표 상태·행 모델·그리기)
- Create: `Editor/SoundCatalogTable.Columns.cs`(열 정의·툴팁)
- Create: `Editor/SoundCatalogWindow.cs`(EditorWindow, 메뉴 `Window/Bun3/Sound Catalog`)
- Modify: `Editor/SoundCatalogEditor.cs`(표를 그리고 "Open in Window" 버튼)

**Interfaces:**
- Consumes:
  - Task 3: `SoundDefOverrides.IsInherited`, `IsAcousticInherited`
  - Task 5: `SoundCatalogEditing.Validate`, `IsOwnedBy`
- Produces: `internal sealed partial class SoundCatalogTable`(인스펙터와 창이 함께 쓴다). 생성자는 `SoundCatalog`를 받고 `void OnGUI(Rect)` 또는 UI Toolkit 루트를 연다. 방식은 구현자가 정한다.

- [ ] **Step 1: 열과 칸 상태.** 스펙 3절 표의 열 12개와 묶음 머리글 4개를 그린다.
  - 프로파일 값은 회색 비활성과 연결 아이콘, 툴팁 `From <profile name>`이다.
  - 덮어쓴 값은 굵게, 왼쪽 파란 줄, 툴팁 `Overrides <profile name>`이다.
  - Volume은 0~1 막대(Min까지 진하게, Min~Max는 옅게)와 숫자를 보여 준다.
  - Max 0은 `∞`, Cooldown 0은 `—`로 표시한다.
- [ ] **Step 2: 툴팁.** 이 문구를 그대로 쓴다. `\n`은 줄바꿈이다.
  - 묶음:
    - Playback: `How the sound plays: volume, pitch and looping. Can come from a shared Playback profile.`
    - Routing: `Which player volume setting and mixer group the sound uses. Can come from a shared Routing profile.`
    - Concurrency: `How overlapping requests are handled: instance limit, request cooldown and channel priority. Can come from a shared Concurrency profile.`
    - Spatial: `Whether the sound has a position in the world. Can come from a shared Spatial profile.`
  - 열:
    - 선택: `Select rows for bulk editing. Ctrl+click adds a row, Shift+click selects a range.`
    - 미리 듣기: `Preview with this sound's volume and pitch. Disabled when the sound has no clips.`
    - Key: `Name passed to SoundSystem.Play. Also the name of the definition stored in this catalog.`
    - Clips: `Number of AudioClips. One is picked at random on each play.\n0 means nothing plays; the fallback key plays instead when one is given.`
    - Volume: `Volume from 0 to 1, picked between Min and Max on each play. Set both to the same value for a fixed level.\nThe player's volume setting for this sound's Volume Group is applied on top.`
    - Pitch: `Pitch multiplier picked between Min and Max on each play. 1 is the original pitch; a range like 0.95–1.05 adds slight variation.`
    - Loop: `Repeats until the code stops it. A charge sound uses this loop as its rising layer.`
    - Volume Group: `Which player volume setting scales this sound, e.g. sfx, music or ui.`
    - Max: `Maximum copies of this sound playing at once. ∞ means no limit.\nWhen the limit is reached, the oldest copy is replaced.`
    - Cooldown: `Minimum seconds between play requests. Requests inside this window are ignored. — means no limit.`
    - Priority: `0 is the most important, 255 the least (default 128).\nWhen every channel is busy, the least important sound is replaced. A new sound less important than all playing ones does not play.`
    - Spatial: `None: heard the same everywhere.\nPositional or Follow: heard from a world position, with distance falloff and occlusion.`
- [ ] **Step 3: 찾기.**
  - 검색어는 공백으로 나눈 모든 단어가 키에 포함되면 남긴다(대소문자 무시).
  - 칩: All, Missing clips, Has clips, Uses profile, Overrides, Spatial(None이 아님). 카탈로그에 있는 Volume Group 값마다 칩을 하나씩 두고, 칩마다 개수를 표시한다.
  - 열 머리글을 누르면 정렬한다. 누를 때마다 내림차순 → 오름차순 → 해제다. Volume·Pitch는 가운데 값으로 정렬한다.
  - 기본은 카탈로그 순서의 펼친 목록이다.
  - `Group by prefix` 토글을 둔다. 마디가 둘 이하인 키는 첫 마디, 그보다 긴 키는 앞 두 마디로 묶는다.
  - 상태는 `SessionState`나 `EditorPrefs`에 사용자별로 기억한다.
- [ ] **Step 4: 경고·검증·상태 줄.**
  - 클립 0개인 행은 Clips에 `⚠ 0`, Key 왼쪽에 노란 줄을 표시한다.
  - Addressable 클립만 있는 행은 `Addressable` 표시를 한다(`BUN3_ADDRESSABLES`).
  - `Validate` 결과가 None이 아닌 행은 오류로 표시하고, 표 위에 오류 줄을 띄운다.
  - 상태 줄에는 전체·보이는·클립 없음·프로파일 사용·덮어쓰기 있음 수를 보여 준다.
- [ ] **Step 5: 확인.**
  - 테스트 대상이 아니다. JP 카탈로그를 인스펙터와 창에서 열어 본다.
  - 147행이 펼친 목록으로 나오는지, `raptor foot` 검색, Missing clips 103, Priority 정렬, 툴팁이 맞는지 확인한다.
  - 키 1000개로 만든 임시 카탈로그에서 스크롤·검색이 끊기지 않는지 본다. 임시 카탈로그는 확인 뒤 지운다.
  - 컴파일 경고 0건을 확인한다.
- [ ] **Step 6:** 커밋 `✨ 사운드 카탈로그를 찾기·정렬·툴팁이 있는 표 인스펙터와 창으로 표시`.

### Task 7: 표 인스펙터 — 고치기·관리·상세·미리 듣기

**Files:**
- Create: `Editor/SoundCatalogTable.Edit.cs`(칸 편집·일괄 편집·우클릭 메뉴)
- Create: `Editor/SoundCatalogTable.Detail.cs`(상세 칸)
- Create: `Editor/SoundPreview.cs`(미리 듣기)
- Modify: `Editor/SoundDefEditor.cs`(미리 듣기를 `SoundPreview`로 교체)
- Modify: `README.md`, `CHANGELOG.md`, `package.json`

**Interfaces:**
- Consumes: Task 3의 `SoundDefOverrides.*`, Task 5의 `SoundCatalogEditing.*`, Task 4의 `SoundDef.Charge`
- Produces: `internal static class SoundPreview { void Play(SoundDef def); void Stop(); bool AppliesVolumeAndPitch { get; } }`

- [ ] **Step 1: 칸 편집.**
  - 더블클릭이나 선택 후 Enter로 고친다.
  - 범위 입력: `0.75-0.9`는 Min·Max, 숫자 하나면 Min = Max. 숫자가 아니면 무시한다.
  - 체크·열거형은 바로 바꾼다.
  - 프로파일 값 칸은 고칠 수 없다. 더블클릭하면 `Right-click → Override with local value to edit` 안내를 띄운다.
  - 모든 값 수정은 `SerializedObject`로 하여 Undo가 된다. 여러 Def를 고친 동작은 `Undo.CollapseUndoOperations`로 하나로 묶는다.
- [ ] **Step 2: 여러 행 편집.**
  - Ctrl·Shift 선택으로 두 행 이상이면 일괄 편집 줄이 나타난다. Volume(고정값)과 Priority를 넣는다.
  - 프로파일 값인 칸에 넣으면 `SoundDefOverrides.Override` 뒤 값을 넣는다.
- [ ] **Step 3: 우클릭 메뉴.**
  - 메뉴 항목:
    - `Override with local value` / `Revert to profile (<name>)`: 프로파일이 있는 칸만
    - `Select profile asset`
    - `Copy <column> to selected rows`
    - `Preview`
    - `Duplicate sound…`, `Rename key…`
    - `Delete sound`
  - Rename 입력 창에 `Game code that uses the old key string is not updated.`를 한 줄 둔다.
  - Delete만 확인 창(`EditorUtility.DisplayDialog`)을 띄운다.
  - 표 위에 `+ Add Sound`(키 입력)를 둔다.
  - 공유 Def 오류 행에는 `Duplicate for this key`를 둔다.
- [ ] **Step 4: 상세 칸.** 선택한 행을 표 아래에 자세히 보여 준다.
  - Clips(끌어다 놓기). Addressable 클립이 있으면 같이 보인다.
  - 묶음별 프로파일 칸과 `Use local`·`Edit profile`.
  - Mixer Group, Spatial·Occlusion·Occlusion Volume At Full.
  - Acoustics: Task 3의 drawer를 쓴다.
  - Charge: Loop가 켜진 Def에만 보인다. 곡선 2개와 Fade Out 2개다.
  - Preview·Stop.
- [ ] **Step 5: 미리 듣기.**
  - `SoundPreview.Play`는 클립 하나를 무작위로 고르고, `EffectiveVolume`·`EffectivePitch`를 굴려 적용해 들려준다. 예: 숨긴 에디터 전용 `AudioSource`.
  - 적용할 수 없는 환경이면 기존 `AudioUtil` 원본 재생으로 대신하고 `AppliesVolumeAndPitch = false`다. 이때 표 미리 듣기 툴팁에 `Plays the raw clip; volume and pitch are not applied here.`를 덧붙인다.
- [ ] **Step 6: 문서·버전.**
  - `package.json` 0.9.0으로 올린다.
  - CHANGELOG:
    - Added: 표 인스펙터·창, 칸별 덮어쓰기, `SoundCatalogEditing`, `SoundDefOverrides`, 정적 `PlayCharge`, `ChargeSoundHandle.Stop`.
    - Changed: 프로파일을 뗄 때 값을 복사함, Def는 카탈로그 하위 에셋.
    - Breaking: `ChargeSoundDef`와 인스턴스 `PlayCharge(ChargeSoundDef…)` 제거.
  - README:
    - 카탈로그 관리 절을 표 기준으로 고친다.
    - 프로파일 절의 "never copies over the inactive local values" 약속을 바꾼다.
    - 차지 절을 키 방식 예시(`SoundSystem.PlayCharge("sfx.box.hold", "sfx.interaction.hold")` / `SetProgress` / `Complete`·`Cancel`·`Stop`)로 고친다.
- [ ] **Step 7: 확인.**
  - JP 카탈로그 사본에서 확인한다(Task 8 전이면 확인 뒤 되돌린다).
  - 칸 편집·Undo, 여러 행 편집, Override·Revert·Use local, Add·Duplicate·Rename·Delete, 미리 듣기 볼륨 차이를 직접 확인한다.
  - kit 오디오 테스트 전체를 실행해 통과하는지 본다. 기존 기준선이 있으면 보고서에 적는다.
  - 컴파일 경고 0건을 확인한다.
- [ ] **Step 8:** 커밋 `✨ 사운드 카탈로그 표에서 값 편집·일괄 편집·추가·삭제·미리 듣기 지원, 0.9.0`.
- [ ] **Step 9: 멈춤.**
  - 사용자에게 kit 브랜치 푸시와 kit main `--no-ff` 병합·푸시를 확인받는다.
  - 받기 전에는 Task 8의 패키지 잠금 갱신을 하지 않는다. 개발 중에는 임시 `file:` 참조로 진행할 수 있다.

### Task 8: JP — Def를 카탈로그 안으로 옮기고 셋업 코드 전환

**Files (JP 워크트리):**
- Modify: `unity/Assets/_Game/Data/Audio/SoundCatalog.asset`
- Delete: `unity/Assets/_Game/Data/Audio/<키>.asset` 147개(+ `.meta`)
- Modify: `unity/Assets/_Game/Editor/M1EditorSetup.cs`(`EnsureSoundCatalog`, `GeneratedAssetVersion` 876 → 877)
- Modify: `unity/Assets/_Game/Editor/SirenSabotageSetup.cs`
- Modify: `unity/Packages/packages-lock.json`(kit main 병합 뒤 audio 해시)

**Interfaces:**
- Consumes: `SoundCatalogEditing.AddSound`, `IsOwnedBy`, `Validate`

- [ ] **Step 1: 옮기기 전 기록.**
  - 카탈로그 항목마다 `EditorJsonUtility.ToJson(def)`에서 `m_Name`을 뺀 값을 키별 JSON 파일로 저장한다.
  - 파일은 세션 스크래치 폴더에 둔다.
- [ ] **Step 2: 한 번 옮기기(커밋하지 않는 eval 스크립트).**
  - 항목마다 `Object.Instantiate(def)`로 복사하고 `name = key`, `hideFlags = HideFlags.HideInHierarchy`를 준다.
  - `AssetDatabase.AddObjectToAsset(copy, catalog)`로 넣고, `SetEntries`로 새 Def를 연결한 뒤 저장한다.
  - 그다음 옛 Def 파일을 `AssetDatabase.DeleteAsset`으로 지운다.
- [ ] **Step 3: 옮긴 뒤 비교.**
  - 키별 JSON이 Step 1과 같아야 한다.
  - `SoundCatalogEditing.Validate`가 모두 None이어야 한다.
  - `Assets/_Game/Data/Audio`에 SoundDef 파일이 남지 않아야 한다.
- [ ] **Step 4: 셋업 코드 전환.**
  - `EnsureSoundCatalog`
    - 없는 키는 `SoundCatalogEditing.AddSound(catalog, id)`로 만든 뒤 기존 기본값 코드를 적용한다.
    - `AssetDatabase.CreateAsset(definition, …)`과 `ordered`·`SetEntries` 재구성을 지운다.
  - `SirenSabotageSetup`도 `AddSound`를 쓴다.
  - JP 전체에서 `Data/Audio/` 아래 Def 경로를 쓰는 코드·문서를 검색해 고친다.
  - `GeneratedAssetVersion`을 877로 올린다.
- [ ] **Step 5: 확인.**
  - JP 컴파일 경고 0건(게임 코드 기준)을 확인한다.
  - JP EditMode 전체를 돌린다. 기존 실패(Alpha2 2건, 언어 상태 3건, Search 잠금 2건) 외에 새 실패가 없어야 한다.
  - Boot에서 Play해 타이틀 배경음, 버튼 소리, 팝업 소리가 그대로 나는지 확인한다.
- [ ] **Step 6:** 커밋 `🚚 사운드 Def 147개를 SoundCatalog 하위 에셋으로 옮기고 셋업이 카탈로그 API로 Def를 만들게 함`.

### Task 9: JP — 홀드 상호작용을 차오르는 소리로

**Files (JP 워크트리):**
- Modify: `unity/Assets/_Game/Scripts/Player/PlayerInteractor.cs`
- Modify: `docs/sound-authoring-guide.md`, `docs/playtest-guide.md`(Def 경로·조정 방법이 적힌 곳)

**Interfaces:**
- Consumes: `SoundSystem.PlayCharge(string key, string fallbackKey)`, `ChargeSoundHandle.SetProgress/Complete/Cancel/Stop`

- [ ] **Step 1: 차지로 전환.**
  - `holdLoop`(SoundHandle)를 `holdCharge`(ChargeSoundHandle)로 바꾼다.
  - `BeginInteractionSound`: `holdCharge = SoundSystem.PlayCharge(holdSounds.Hold, SoundKeys.InteractionHold)`. 시작음 별도 재생은 지운다.
  - `Update`: 홀드 중이면 `holdCharge.SetProgress(HoldProgress)`.
  - `EndHoldSound(completed)`: 완료면 `holdCharge.Complete()`, 아니면 `holdCharge.Cancel()`. 완료·취소음 별도 재생은 지운다.
  - `OnNetworkDespawn`: `holdCharge.Stop()`.
- [ ] **Step 2: 확인.**
  - 혼자 방을 만들어 Play한다. 상자·화로 홀드에서 진행 막대가 찰수록 `.hold` 소리가 커지고 높아지는지, 완료·취소음이 나는지 직접 듣는다.
  - 콘솔 경고·오류 0건을 확인한다.
- [ ] **Step 3: 문서.**
  - `sound-authoring-guide.md`
    - "기존 효과음 바꾸기"를 표 사용법으로 고친다: 검색, 칸 편집, 우클릭 덮어쓰기, Add·Delete.
    - 홀드 설명을 차지 기준으로 고친다: `.hold`의 Charge 곡선, `.hold.start/.complete/.cancel`.
    - Def 파일 경로 언급을 지운다.
  - `playtest-guide.md`의 같은 내용도 고친다.
- [ ] **Step 4: 빌드 버전.** 함께 플레이할 수 있는 변경이라 patch만 올린다.
  - 브랜치의 bundleVersion이 이미 origin/main보다 patch 하나 위이고 아직 배포 전이면 그대로 둔다. 지금은 0.49.3 대 0.49.2다.
  - 그사이 main이 올라갔으면 main 값에서 patch를 하나 올린다.
- [ ] **Step 5:** 커밋 `🔊 홀드 상호작용 소리를 진행도에 따라 차오르는 차지 사운드로 전환`.
- [ ] **Step 6:** 임시 manifest·testables 변경이 남지 않았는지 확인한다. packages-lock의 audio 해시가 kit main 병합 커밋인지 확인한다.
- [ ] **Step 7:** JP 푸시·main 병합은 사용자 확인을 받은 뒤에만 한다.
