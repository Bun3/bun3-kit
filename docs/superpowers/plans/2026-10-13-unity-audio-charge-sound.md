# Unity Audio 차지 사운드 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `com.bun3.unity.audio`에 진행도 구동 차지 사운드(`ChargeSoundDef` / `SoundSystem.PlayCharge` / `ChargeSoundHandle`)를 추가하고 0.5.0으로 올린다.

**Architecture:** 기존 SFX 재생 경로(`PlayCore`)와 슬롯 상태(`Table.Slots`) 위에 얹는 얇은 합성 계층이다. `SoundSystem.Charge.cs` partial이 루프 보이스 1개와 원샷 보이스(시작·완료·취소)를 조합하고, `ChargeSoundHandle`은 루프 `SoundHandle` + def + 롤된 기본 피치만 담는 readonly struct다. 코어 Tick·VoiceTable·`PlayCore` 시그니처는 바꾸지 않는다.

**Tech Stack:** Unity 6000.3, C# 9(netstandard2.1 프로필), NUnit + Unity Test Framework(PlayMode), `UnityEngine.TestTools.Constraints`(GC 할당 단언), `unity` CLI 배치 테스트.

**Spec:** `docs/superpowers/specs/2026-10-13-unity-audio-charge-sound-design.md`

## Global Constraints

- 패키지 루트: `unity/Packages/com.bun3.unity.audio/` (이하 `PKG/`). 네임스페이스 `Bun3.Unity.Audio`, 테스트는 `Bun3.Unity.Audio.Tests`. 폴더 평평 유지.
- C# 9 블록 네임스페이스, 모든 public 멤버 영어 XML 문서, 빌드 경고 0.
- 코드·README·CHANGELOG·package.json은 **영어만**. 한글은 `docs/`에만. 레거시 프로젝트·타 패키지 언급 금지. 주석은 코드가 못 보여주는 계약만.
- partial 파일은 `타입.역할.cs`, 머리에 역할 주석 한 줄, `SoundSystem` 본체 XML 문서의 partial 목록에 추가.
- 핫패스(`SetProgress`, `Complete`, `Cancel`, `PlayCharge`의 정상 경로) 무할당 — 클로저·LINQ·문자열 생성 금지. 경고 로그는 오설정 경로에서만, `#if DEVELOPMENT_BUILD || UNITY_EDITOR`로 감싼다.
- 버전 0.4.0 → **0.5.0**. 같은 버전 재퍼블리시 금지. 태그·푸시는 이 플랜 범위 밖.
- 테스트 실행: `unity test "<ROOT>\unity" --mode PlayMode --filter "<필터>"`. `<ROOT>`는 작업 중인 워크트리 루트의 **절대 경로**(main이면 `E:\Projects\bun3-kit`). 맨 이름 금지. 통과인데 total="0"이면 필터 미매칭 오탐. 배치 인스턴스 동시 실행 금지. 프로젝트가 에디터에 열려 있으면 락 에러 — 사용자에게 닫아 달라고 요청.
- 배치 실행 후 `git status`로 의도치 않은 `unity/ProjectSettings/` 변경(헤드리스 실행이 `SENTIS_ANALYTICS_ENABLED` define을 벗겨내는 알려진 현상)을 확인하고 그 변경만 되돌린다. 기존 미추적 `unity/ProjectSettings/GameplayTagSettings.asset`은 건드리지 않는다.
- 새 `.cs` 파일의 `.meta`는 배치 실행 시 Unity가 생성한다 — 같은 커밋에 포함한다.
- 커밋은 `writing-gitmoji-commits` 스킬 양식(제목 `<이모지> <What>` + Why/Changes/Impact/Results). 문단마다 `-m`으로 넘긴다. Results에는 실제 실행한 명령과 결과(통과 수)만 적는다.

## Review Focus

1. **추적 대상이 완료 전에 파괴됨** — 차지 도중 `Transform`이 Destroy된 뒤 `Complete()` → 예외 없이 소스의 마지막 추적 위치에서 완료음 재생. (Task 2 `Complete_FollowDestroyed_PlaysAtLastTrackedPosition`)
2. **풀 포화로 루프가 스틸됨**(MaxInstances가 아닌 전역 최고령 스틸) — `Complete()`가 `Invalid`를 반환하고 완료음 보이스가 생기지 않음. (Task 2 `LoopStolenByPoolPressure_CompletePlaysNothing`)
3. **완료 후에도 게이지 갱신이 한 프레임 더 옴** — `Complete()` 뒤 `SetProgress`가 페이드아웃 중인 루프 볼륨·피치를 바꾸지 않음. (Task 2 `EndTwice_PlaysOneStingerAndKeepsFirstFade`)
4. **`PitchWithTimescale = true`에서 타임스케일 변화** — 루프 피치 = 기본 피치 × 곡선 × 타임스케일이 틱 재적용과 `SetProgress` 양쪽에서 유지됨. (Task 1 `PitchWithTimescale_KeepsCurvePitchUnderTimescale`)
5. **한 번도 대입되지 않은 핸들**(`default(ChargeSoundHandle)`) — `IsValid` false, 모든 호출이 예외 없이 무시. (Task 1 `DefaultHandle_AllCallsAreNoOps`)

---

## File Structure

| 파일 | 책임 |
|---|---|
| Create `PKG/Runtime/ChargeSoundDef.cs` | 에셋: 구성 SoundDef 4칸, 곡선 2개, 페이드아웃 2개, 곡선 평가(`VolumeAt`, `PitchScaleAt`) |
| Create `PKG/Runtime/ChargeSoundHandle.cs` | 공개 핸들 struct — 상태 3필드, 모든 동작은 `SoundSystem`에 위임 |
| Create `PKG/Runtime/SoundSystem.Charge.cs` | `PlayCharge` 3종 + 내부 `IsChargeActive` / `SetChargeProgress` / `EndCharge` |
| Modify `PKG/Runtime/SoundSystem.cs:11-20` | 본체 XML 문서 partial 목록에 Charge 항목 추가 |
| Create `PKG/Tests/Runtime/ChargeSoundTests.cs` | 차지 사운드 PlayMode 테스트 전부 |
| Modify `PKG/README.md` | `### Charge sounds` 섹션, API 레퍼런스 행 |
| Modify `PKG/CHANGELOG.md` | `[0.4.0]` 따라잡기 + `[0.5.0]` |
| Modify `PKG/package.json` | 버전 0.5.0, description |
| Modify `docs/superpowers/specs/2026-10-13-unity-audio-charge-sound-design.md` | 상태 → 구현 완료 |

---

### Task 1: ChargeSoundDef + PlayCharge + SetProgress

**Files:**
- Create: `PKG/Runtime/ChargeSoundDef.cs`
- Create: `PKG/Runtime/ChargeSoundHandle.cs`
- Create: `PKG/Runtime/SoundSystem.Charge.cs`
- Modify: `PKG/Runtime/SoundSystem.cs:11-20`
- Test: `PKG/Tests/Runtime/ChargeSoundTests.cs`

**Interfaces:**
- Consumes (기존 코드): `SoundSystem.PlayCore(SoundDef def, Vector3 position, Transform follow, float fadeIn, SpatialMode? spatial = null, float volumeScale = 1f)` (private), `SoundSystem.TryGetSlot(SoundHandle, out int)` (internal), `SoundSystem.SetSourcePitch(int slot, float pitch)` (internal), `SoundSystem.Table.Slots[slot]`(`VoiceSlot`: `State`, `Pitch`, `VolumeScale`, `Follow`), `VoiceState.FadingOut`, `SoundHandle.Owner` / `SlotIndex` (internal), 테스트 헬퍼 `SoundSystem.Tick(float)`, `SourcePitchForTest(int)`, `SourceForTest(int)` (internal).
- Produces:
  - `public sealed class ChargeSoundDef : ScriptableObject` — 필드 `SoundDef Loop, Start, Complete, Cancel`; `AnimationCurve VolumeByProgress, PitchByProgress`; `float CompleteFadeOut = 0.05f, CancelFadeOut = 0.15f`; `internal float VolumeAt(float progress)`; `internal float PitchScaleAt(float progress)`.
  - `public readonly struct ChargeSoundHandle` — `internal readonly SoundHandle Loop; internal readonly ChargeSoundDef Def; internal readonly float BasePitch;` `public static ChargeSoundHandle Invalid`; `public bool IsValid`; `public void SetProgress(float)`; `public SoundHandle Complete()`; `public SoundHandle Cancel()`.
  - `SoundSystem`: `public ChargeSoundHandle PlayCharge(ChargeSoundDef def)`, `PlayCharge(ChargeSoundDef def, Vector3 position)`, `PlayCharge(ChargeSoundDef def, Transform follow)`; `internal bool IsChargeActive(in ChargeSoundHandle charge, out int slot)`; `internal void SetChargeProgress(in ChargeSoundHandle charge, float progress)`; `private void ApplyChargePitch(int slot, in ChargeSoundHandle charge, float progress)`.
  - `ChargeSoundHandle.Complete()`/`Cancel()`과 `SoundSystem.EndCharge`는 Task 2에서 추가한다(Task 1에는 없다).
  - 경고 문자열(테스트가 정확히 매칭): `"SoundSystem.PlayCharge: def or its Loop is null; returning an invalid handle."`, `"SoundSystem.PlayCharge: Loop definition does not loop; the charge ends with its clip."`

- [ ] **Step 1: 실패하는 테스트 작성** — `PKG/Tests/Runtime/ChargeSoundTests.cs`

```csharp
using System;
using Bun3.Unity.Audio;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class ChargeSoundTests
    {
        private const string NullDefWarning = "SoundSystem.PlayCharge: def or its Loop is null; returning an invalid handle.";
        private const string NotLoopingWarning = "SoundSystem.PlayCharge: Loop definition does not loop; the charge ends with its clip.";

        private static SoundDef LoopDef(float pitch = 1f)
        {
            var def = ScriptableObject.CreateInstance<SoundDef>();
            def.Clips = new[] { AudioClip.Create("charge-loop", 44100, 1, 44100, false) }; // 1 s
            def.Loop = true;
            def.Pitch = new FloatRange(pitch, pitch);
            def.MaxInstances = 1;
            return def;
        }

        private static SoundDef OneShotDef(string name, SpatialMode spatial = SpatialMode.None)
        {
            var def = ScriptableObject.CreateInstance<SoundDef>();
            def.Clips = new[] { AudioClip.Create(name, 4410, 1, 44100, false) }; // 0.1 s
            def.Spatial = spatial;
            return def;
        }

        private static ChargeSoundDef Charge(SoundDef loop, SoundDef start = null, SoundDef complete = null, SoundDef cancel = null)
        {
            var def = ScriptableObject.CreateInstance<ChargeSoundDef>();
            def.Loop = loop;
            def.Start = start;
            def.Complete = complete;
            def.Cancel = cancel;
            return def;
        }

        private static int CountVoices(SoundSystem sys, SoundDef def)
        {
            var count = 0;
            for (var i = 0; i < sys.Table.Slots.Length; i++)
            {
                if (sys.Table.Slots[i].State != VoiceState.Idle && ReferenceEquals(sys.Table.Slots[i].Def, def))
                {
                    count++;
                }
            }
            return count;
        }

        [Test]
        public void PlayCharge_StartsLoopAtCurveZeroAndPlaysStart()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 4 });
            var start = OneShotDef("charge-start");
            var charge = sys.PlayCharge(Charge(LoopDef(), start));

            Assert.That(charge.IsValid, Is.True);
            var slot = charge.Loop.SlotIndex;
            Assert.That(sys.Table.Slots[slot].VolumeScale, Is.EqualTo(0f));
            Assert.That(sys.SourceForTest(slot).volume, Is.EqualTo(0f), "Curve-zero volume must be applied before the source starts.");
            Assert.That(sys.SourcePitchForTest(slot), Is.EqualTo(0.667f).Within(1e-4f));
            Assert.That(CountVoices(sys, start), Is.EqualTo(1));
        }

        [Test]
        public void SetProgress_ScalesLoopVolumeAndRolledPitchByCurves()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 4 });
            var def = Charge(LoopDef(pitch: 1.2f));
            def.VolumeByProgress = AnimationCurve.Linear(0f, 0.2f, 1f, 1f);
            def.PitchByProgress = AnimationCurve.Linear(0f, 0.5f, 1f, 1f);
            var charge = sys.PlayCharge(def);
            var slot = charge.Loop.SlotIndex;

            charge.SetProgress(0.5f);
            Assert.That(sys.Table.Slots[slot].VolumeScale, Is.EqualTo(0.6f).Within(1e-4f));
            Assert.That(sys.SourcePitchForTest(slot), Is.EqualTo(1.2f * 0.75f).Within(1e-4f));

            charge.SetProgress(1f);
            charge.SetProgress(0.25f); // progress may decrease
            Assert.That(sys.Table.Slots[slot].VolumeScale, Is.EqualTo(0.4f).Within(1e-4f));
            Assert.That(sys.SourcePitchForTest(slot), Is.EqualTo(1.2f * 0.625f).Within(1e-4f));
        }

        [Test]
        public void SetProgress_ClampsRangeAndTreatsNaNAsZero()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 4 });
            var def = Charge(LoopDef());
            def.VolumeByProgress = AnimationCurve.Linear(0f, 0.2f, 1f, 1f);
            var charge = sys.PlayCharge(def);
            var slot = charge.Loop.SlotIndex;

            charge.SetProgress(float.NaN);
            Assert.That(sys.Table.Slots[slot].VolumeScale, Is.EqualTo(0.2f).Within(1e-4f));
            charge.SetProgress(-3f);
            Assert.That(sys.Table.Slots[slot].VolumeScale, Is.EqualTo(0.2f).Within(1e-4f));
            charge.SetProgress(7f);
            Assert.That(sys.Table.Slots[slot].VolumeScale, Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void NullOrEmptyCurves_AreConstantOne()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 4 });
            var def = Charge(LoopDef(pitch: 1.2f));
            def.VolumeByProgress = null;
            def.PitchByProgress = new AnimationCurve();
            var charge = sys.PlayCharge(def);
            var slot = charge.Loop.SlotIndex;

            charge.SetProgress(0.3f);
            Assert.That(sys.Table.Slots[slot].VolumeScale, Is.EqualTo(1f));
            Assert.That(sys.SourcePitchForTest(slot), Is.EqualTo(1.2f).Within(1e-4f));
        }

        [Test]
        public void PitchWithTimescale_KeepsCurvePitchUnderTimescale()
        {
            var previous = Time.timeScale;
            try
            {
                using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 4, PitchWithTimescale = true });
                var def = Charge(LoopDef());
                def.PitchByProgress = AnimationCurve.Linear(0f, 0.5f, 1f, 1f);
                var charge = sys.PlayCharge(def);
                var slot = charge.Loop.SlotIndex;

                Time.timeScale = 0.5f;
                sys.Tick(0.01f);
                Assert.That(sys.SourcePitchForTest(slot), Is.EqualTo(0.5f * 0.5f).Within(1e-4f));

                charge.SetProgress(1f);
                Assert.That(sys.SourcePitchForTest(slot), Is.EqualTo(1f * 0.5f).Within(1e-4f));
            }
            finally
            {
                Time.timeScale = previous;
            }
        }

        [Test]
        public void PlayCharge_LoopWithoutClips_ReturnsInvalidAndSkipsStart()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 4 });
            var loop = ScriptableObject.CreateInstance<SoundDef>();
            loop.Loop = true;
            var start = OneShotDef("charge-start");

            var charge = sys.PlayCharge(Charge(loop, start));

            Assert.That(charge.IsValid, Is.False);
            Assert.That(CountVoices(sys, start), Is.EqualTo(0));
        }

        [Test]
        public void PlayCharge_LoopOnCooldown_ReturnsInvalidAndSkipsStart()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 4 });
            var loop = LoopDef();
            loop.Cooldown = 10f;
            var start = OneShotDef("charge-start");
            var def = Charge(loop, start);

            Assert.That(sys.PlayCharge(def).IsValid, Is.True);
            Assert.That(sys.PlayCharge(def).IsValid, Is.False);
            Assert.That(CountVoices(sys, start), Is.EqualTo(1));
        }

        [Test]
        public void PlayCharge_NullDefOrLoop_WarnsAndReturnsInvalid()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 4 });
            LogAssert.Expect(LogType.Warning, NullDefWarning);
            Assert.That(sys.PlayCharge(null).IsValid, Is.False);
            LogAssert.Expect(LogType.Warning, NullDefWarning);
            Assert.That(sys.PlayCharge(Charge(null)).IsValid, Is.False);
        }

        [Test]
        public void PlayCharge_NonLoopingLoopDef_WarnsAndStillPlays()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 4 });
            var loop = LoopDef();
            loop.Loop = false;
            LogAssert.Expect(LogType.Warning, NotLoopingWarning);
            Assert.That(sys.PlayCharge(Charge(loop)).IsValid, Is.True);
        }

        [Test]
        public void PlayCharge_OnDisposedSystem_Throws()
        {
            var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 4 });
            sys.Dispose();
            Assert.Throws<ObjectDisposedException>(() => sys.PlayCharge(Charge(LoopDef())));
        }

        [Test]
        public void DefaultHandle_AllCallsAreNoOps()
        {
            var charge = default(ChargeSoundHandle);
            Assert.That(charge.IsValid, Is.False);
            Assert.That(ChargeSoundHandle.Invalid.IsValid, Is.False);
            Assert.DoesNotThrow(() => charge.SetProgress(1f));
        }
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `unity test "<ROOT>\unity" --mode PlayMode --filter "ChargeSoundTests"`
Expected: 테스트 어셈블리 컴파일 실패 — `ChargeSoundDef` / `ChargeSoundHandle` / `PlayCharge` 미정의(CS0246/CS1061).

- [ ] **Step 3: 구현**

`PKG/Runtime/ChargeSoundDef.cs`:

```csharp
using UnityEngine;

namespace Bun3.Unity.Audio
{
    /// <summary>
    /// Progress-driven charge sound: a looping layer whose volume and pitch follow a 0..1
    /// progress value, plus optional start, complete and cancel one-shots. Each part is a
    /// regular <see cref="SoundDef"/> and keeps its own clips, variation, routing, spatial
    /// mode and profiles. Played with <see cref="SoundSystem.PlayCharge(ChargeSoundDef)"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Bun3/Audio/Charge Sound Def", fileName = "ChargeSoundDef")]
    public sealed class ChargeSoundDef : ScriptableObject
    {
        /// <summary>Required looping layer. Its definition should enable Loop.</summary>
        public SoundDef Loop;

        /// <summary>Optional one-shot played when the charge starts.</summary>
        public SoundDef Start;

        /// <summary>Optional one-shot played by <see cref="ChargeSoundHandle.Complete"/>.</summary>
        public SoundDef Complete;

        /// <summary>Optional one-shot played by <see cref="ChargeSoundHandle.Cancel"/>.</summary>
        public SoundDef Cancel;

        /// <summary>Loop volume multiplier (Y) by progress (X, 0..1). Null or empty is a constant 1.</summary>
        public AnimationCurve VolumeByProgress = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        /// <summary>Multiplier (Y) on the loop's rolled pitch by progress (X, 0..1). Null or empty is a constant 1.</summary>
        public AnimationCurve PitchByProgress = AnimationCurve.Linear(0f, 0.667f, 1f, 1f);

        /// <summary>Seconds the loop fades out over when the charge completes.</summary>
        public float CompleteFadeOut = 0.05f;

        /// <summary>Seconds the loop fades out over when the charge is cancelled.</summary>
        public float CancelFadeOut = 0.15f;

        internal float VolumeAt(float progress) => Mathf.Max(0f, Evaluate(VolumeByProgress, progress));

        internal float PitchScaleAt(float progress) => Evaluate(PitchByProgress, progress);

        private static float Evaluate(AnimationCurve curve, float progress)
        {
            if (curve == null || curve.length == 0)
            {
                return 1f;
            }
            return curve.Evaluate(float.IsNaN(progress) ? 0f : Mathf.Clamp01(progress));
        }
    }
}
```

`PKG/Runtime/ChargeSoundHandle.cs` (Task 1 범위 — `Complete`/`Cancel`은 Task 2에서 추가):

```csharp
namespace Bun3.Unity.Audio
{
    /// <summary>
    /// Reference to a charge sound started by <see cref="SoundSystem.PlayCharge(ChargeSoundDef)"/>.
    /// Valid while its loop plays and the charge has not been completed or cancelled. Once the
    /// loop is stolen by voice limits, ends, or its system is disposed, every member no-ops (or
    /// reports false) like a stale <see cref="SoundHandle"/>, and no completion sound plays.
    /// </summary>
    public readonly struct ChargeSoundHandle
    {
        internal readonly SoundHandle Loop;
        internal readonly ChargeSoundDef Def;
        internal readonly float BasePitch;

        internal ChargeSoundHandle(SoundHandle loop, ChargeSoundDef def, float basePitch)
        {
            Loop = loop;
            Def = def;
            BasePitch = basePitch;
        }

        /// <summary>A handle that never refers to a charge.</summary>
        public static ChargeSoundHandle Invalid => default;

        /// <summary>True while the loop plays and the charge has not been completed or cancelled.</summary>
        public bool IsValid => Loop.Owner != null && Loop.Owner.IsChargeActive(this, out _);

        /// <summary>
        /// Applies the definition's curves at <paramref name="progress"/>, clamped to 0..1 (NaN is 0).
        /// Takes effect immediately without smoothing; progress may decrease.
        /// </summary>
        public void SetProgress(float progress) => Loop.Owner?.SetChargeProgress(this, progress);
    }
}
```

`PKG/Runtime/SoundSystem.Charge.cs`:

```csharp
// Charge sounds: a progress-driven loop voice composed with start/complete/cancel one-shots.
using System;
using UnityEngine;

namespace Bun3.Unity.Audio
{
    public sealed partial class SoundSystem
    {
        /// <summary>Starts a 2D (or loop-definition-default) charge sound. Returns an invalid handle when the loop cannot play.</summary>
        /// <param name="def">Charge definition; its Loop is required.</param>
        public ChargeSoundHandle PlayCharge(ChargeSoundDef def) => PlayChargeCore(def, Vector3.zero, null);

        /// <summary>Starts a charge sound at a fixed world position. Returns an invalid handle when the loop cannot play.</summary>
        /// <param name="def">Charge definition; its Loop is required.</param>
        /// <param name="position">Fixed world position shared by every part of the charge.</param>
        public ChargeSoundHandle PlayCharge(ChargeSoundDef def, Vector3 position) => PlayChargeCore(def, position, null);

        /// <summary>Starts a charge sound tracking a transform every frame. Returns an invalid handle when the loop cannot play.</summary>
        /// <param name="def">Charge definition; its Loop is required.</param>
        /// <param name="follow">Transform tracked by every part of the charge.</param>
        public ChargeSoundHandle PlayCharge(ChargeSoundDef def, Transform follow)
            => PlayChargeCore(def, follow != null ? follow.position : Vector3.zero, follow);

        private ChargeSoundHandle PlayChargeCore(ChargeSoundDef def, Vector3 position, Transform follow)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(SoundSystem));
            }
            if (def == null || def.Loop == null)
            {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                Debug.LogWarning("SoundSystem.PlayCharge: def or its Loop is null; returning an invalid handle.");
#endif
                return ChargeSoundHandle.Invalid;
            }
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (!def.Loop.EffectiveLoop)
            {
                Debug.LogWarning("SoundSystem.PlayCharge: Loop definition does not loop; the charge ends with its clip.");
            }
#endif
            // Curve-zero volume goes in before the source starts; pitch follows in the same call.
            var loop = PlayCore(def.Loop, position, follow, 0f, null, def.VolumeAt(0f));
            if (!TryGetSlot(loop, out var slot))
            {
                return ChargeSoundHandle.Invalid;
            }
            var charge = new ChargeSoundHandle(loop, def, Table.Slots[slot].Pitch);
            ApplyChargePitch(slot, charge, 0f);
            if (def.Start != null)
            {
                PlayCore(def.Start, position, follow, 0f);
            }
            return charge;
        }

        internal bool IsChargeActive(in ChargeSoundHandle charge, out int slot)
            => TryGetSlot(charge.Loop, out slot) && Table.Slots[slot].State != VoiceState.FadingOut;

        internal void SetChargeProgress(in ChargeSoundHandle charge, float progress)
        {
            if (!IsChargeActive(charge, out var slot))
            {
                return;
            }
            Table.Slots[slot].VolumeScale = charge.Def.VolumeAt(progress);
            ApplyChargePitch(slot, charge, progress);
        }

        private void ApplyChargePitch(int slot, in ChargeSoundHandle charge, float progress)
        {
            var pitch = Mathf.Max(0f, charge.BasePitch * charge.Def.PitchScaleAt(progress));
            Table.Slots[slot].Pitch = pitch;
            SetSourcePitch(slot, pitch);
        }
    }
}
```

`PKG/Runtime/SoundSystem.cs` — 본체 XML 문서 partial 목록(`SoundSystem.Async.cs` 항목 다음 줄)에 추가:

```csharp
    /// <item><description>SoundSystem.Charge.cs: progress-driven charge sounds composed over voice playback.</description></item>
```

- [ ] **Step 4: 통과 확인**

Run: `unity test "<ROOT>\unity" --mode PlayMode --filter "ChargeSoundTests"`
Expected: 11 passed, 0 failed (total="11"). 컴파일 경고 0 — 에디터 로그(`unity test` 출력)에 새 파일 경고가 없는지 확인.

- [ ] **Step 5: 커밋** — 새 `.meta` 포함, ProjectSettings 부수 변경 되돌린 뒤

```bash
git add unity/Packages/com.bun3.unity.audio/Runtime/ChargeSoundDef.cs* unity/Packages/com.bun3.unity.audio/Runtime/ChargeSoundHandle.cs* unity/Packages/com.bun3.unity.audio/Runtime/SoundSystem.Charge.cs* unity/Packages/com.bun3.unity.audio/Runtime/SoundSystem.cs unity/Packages/com.bun3.unity.audio/Tests/Runtime/ChargeSoundTests.cs*
git commit -m "✨ 진행도를 따라 볼륨·피치가 변하는 차지 사운드 재생 추가" \
  -m "Why:
- 홀드·차징 사운드를 게임마다 Play(fadeIn)와 매 프레임 SetPitch로 조립하던 코드를 프레임워크로 내린다(스펙 2026-10-13-unity-audio-charge-sound-design.md)." \
  -m "Changes:
- ChargeSoundDef: 구성 SoundDef 4칸, VolumeByProgress·PitchByProgress 곡선(null·빈 곡선은 상수 1, 진행도 NaN→0·0~1 고정), 완료·취소 페이드아웃.
- SoundSystem.PlayCharge 3종: 곡선 0 지점 볼륨을 PlayCore volumeScale로 재생 전에 넣고, 롤된 피치를 기본 피치로 저장해 같은 호출에서 곡선 피치 적용, 이어 시작음 재생. 루프 실패 시 시작음 없이 무효 핸들.
- ChargeSoundHandle.SetProgress: 루프 VolumeScale과 기본 피치×곡선을 기존 SetSourcePitch 경로로 반영. IsValid는 루프 유효 그리고 FadingOut 아님." \
  -m "Impact:
- 공개 API 추가만 있고 기존 재생·Tick·VoiceTable 동작은 그대로. Complete/Cancel은 다음 커밋." \
  -m "Results:
- <실행한 unity test 명령>: <실제 통과/전체 수>." \
  -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

(Results의 `<…>`는 실제로 실행한 명령과 출력의 통과/전체 수로 바꾼다 — 추측 금지. 트레일러는 커밋하는 세션의 시스템 귀속 안내를 따른다.)

---

### Task 2: Complete / Cancel + 무효 처리 + 무할당

**Files:**
- Modify: `PKG/Runtime/ChargeSoundHandle.cs` (Complete/Cancel 추가)
- Modify: `PKG/Runtime/SoundSystem.Charge.cs` (EndCharge 추가)
- Test: `PKG/Tests/Runtime/ChargeSoundTests.cs` (테스트 추가)

**Interfaces:**
- Consumes: Task 1의 `ChargeSoundHandle(Loop, Def, BasePitch)`, `IsChargeActive`, `SetChargeProgress`; 기존 `VoiceTable.BeginFadeOut(int slot, float duration)`(0 이하는 다음 틱 즉시 완료), `_sources[slot].transform.position`, `Table.Slots[slot].Follow`.
- Produces: `public SoundHandle ChargeSoundHandle.Complete()`, `public SoundHandle ChargeSoundHandle.Cancel()`, `internal SoundHandle SoundSystem.EndCharge(in ChargeSoundHandle charge, bool completed)`.

- [ ] **Step 1: 실패하는 테스트 추가** — `ChargeSoundTests` 클래스 끝(마지막 `}` 앞)에

```csharp
        [Test]
        public void Complete_FadesLoopAndPlaysCompleteAtLoopPosition()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 4 });
            var complete = OneShotDef("charge-complete", SpatialMode.Positional);
            var def = Charge(LoopDef(), complete: complete);
            var at = new Vector3(3f, 0f, 4f);
            var charge = sys.PlayCharge(def, at);
            var loopSlot = charge.Loop.SlotIndex;

            var done = charge.Complete();

            Assert.That(done.IsValid, Is.True);
            Assert.That(charge.IsValid, Is.False);
            Assert.That(sys.Table.Slots[loopSlot].State, Is.EqualTo(VoiceState.FadingOut));
            Assert.That(sys.SourceForTest(done.SlotIndex).transform.position, Is.EqualTo(at));
            sys.Tick(def.CompleteFadeOut + 0.01f);
            Assert.That(charge.Loop.IsValid, Is.False, "Loop must be released when its CompleteFadeOut finishes.");
        }

        [Test]
        public void Cancel_FollowingCharge_PlaysCancelTrackingSameTransform()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 4 });
            var cancel = OneShotDef("charge-cancel", SpatialMode.Follow);
            var target = new GameObject("charge-target").transform;
            try
            {
                target.position = new Vector3(1f, 2f, 3f);
                var charge = sys.PlayCharge(Charge(LoopDef(), cancel: cancel), target);

                var cancelled = charge.Cancel();

                Assert.That(cancelled.IsValid, Is.True);
                Assert.That(sys.Table.Slots[cancelled.SlotIndex].Follow, Is.SameAs(target));
                target.position = new Vector3(5f, 5f, 5f);
                sys.Tick(0.01f);
                Assert.That(sys.SourceForTest(cancelled.SlotIndex).transform.position, Is.EqualTo(new Vector3(5f, 5f, 5f)));
            }
            finally
            {
                Object.DestroyImmediate(target.gameObject);
            }
        }

        [Test]
        public void Complete_FollowDestroyed_PlaysAtLastTrackedPosition()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 4 });
            var complete = OneShotDef("charge-complete", SpatialMode.Positional);
            var target = new GameObject("charge-target").transform;
            target.position = new Vector3(1f, 2f, 3f);
            var charge = sys.PlayCharge(Charge(LoopDef(), complete: complete), target);
            sys.Tick(0.01f);
            Object.DestroyImmediate(target.gameObject);

            var done = charge.Complete();

            Assert.That(done.IsValid, Is.True);
            Assert.That(sys.Table.Slots[done.SlotIndex].Follow == null, Is.True);
            Assert.That(sys.SourceForTest(done.SlotIndex).transform.position, Is.EqualTo(new Vector3(1f, 2f, 3f)));
        }

        [Test]
        public void EndTwice_PlaysOneStingerAndKeepsFirstFade()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 8 });
            var complete = OneShotDef("charge-complete");
            var cancel = OneShotDef("charge-cancel");
            var def = Charge(LoopDef(), complete: complete, cancel: cancel);
            var charge = sys.PlayCharge(def);
            var loopSlot = charge.Loop.SlotIndex;
            charge.SetProgress(0.5f);

            Assert.That(charge.Complete().IsValid, Is.True);
            Assert.That(charge.Complete().IsValid, Is.False);
            Assert.That(charge.Cancel().IsValid, Is.False);
            charge.SetProgress(1f);

            Assert.That(CountVoices(sys, complete), Is.EqualTo(1));
            Assert.That(CountVoices(sys, cancel), Is.EqualTo(0));
            Assert.That(sys.Table.Slots[loopSlot].VolumeScale, Is.EqualTo(0.5f).Within(1e-4f), "SetProgress after Complete must not touch the fading loop.");
            sys.Tick(def.CompleteFadeOut + 0.01f);
            Assert.That(charge.Loop.IsValid, Is.False, "A second end call must not restart the loop fade with CancelFadeOut.");
        }

        [Test]
        public void Cancel_WithoutCancelSound_StopsLoopAndReturnsInvalid()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 4 });
            var def = Charge(LoopDef());
            var charge = sys.PlayCharge(def);
            var loopSlot = charge.Loop.SlotIndex;

            Assert.That(charge.Cancel().IsValid, Is.False);
            Assert.That(sys.Table.Slots[loopSlot].State, Is.EqualTo(VoiceState.FadingOut));
            sys.Tick(def.CancelFadeOut + 0.01f);
            Assert.That(charge.Loop.IsValid, Is.False);
        }

        [Test]
        public void Replay_StealsPreviousLoop_OldHandleNoOps()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 8 });
            var loop = LoopDef();
            var complete = OneShotDef("charge-complete");
            var def = Charge(loop, complete: complete);
            var first = sys.PlayCharge(def);
            first.Cancel();

            var second = sys.PlayCharge(def);

            Assert.That(first.IsValid, Is.False);
            Assert.That(second.IsValid, Is.True);
            Assert.That(first.Complete().IsValid, Is.False);
            first.SetProgress(1f);
            Assert.That(sys.Table.Slots[second.Loop.SlotIndex].VolumeScale, Is.EqualTo(0f));
            Assert.That(CountVoices(sys, loop), Is.EqualTo(1));
            Assert.That(CountVoices(sys, complete), Is.EqualTo(0));
        }

        [Test]
        public void LoopStolenByPoolPressure_CompletePlaysNothing()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 1 });
            var complete = OneShotDef("charge-complete");
            var charge = sys.PlayCharge(Charge(LoopDef(), complete: complete));

            sys.Play(OneShotDef("other")); // full pool: steals the loop

            Assert.That(charge.IsValid, Is.False);
            Assert.That(charge.Complete().IsValid, Is.False);
            Assert.That(CountVoices(sys, complete), Is.EqualTo(0));
        }

        [Test]
        public void DisposedSystem_ChargeCallsAreNoOps()
        {
            var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 4 });
            var charge = sys.PlayCharge(Charge(LoopDef(), complete: OneShotDef("charge-complete")));
            sys.Dispose();

            Assert.That(charge.IsValid, Is.False);
            Assert.DoesNotThrow(() => charge.SetProgress(1f));
            Assert.That(charge.Complete().IsValid, Is.False);
            Assert.That(charge.Cancel().IsValid, Is.False);
        }

        [Test]
        public void ChargeCycle_DoesNotAllocate()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 8 });
            var def = Charge(LoopDef(), OneShotDef("charge-start"), OneShotDef("charge-complete"));
            sys.PlayCharge(def).Complete(); // warm every lazy path once
            sys.Tick(0.2f);

            Assert.That(() => GC.KeepAlive(new byte[1024]), Is.AllocatingGCMemory(),
                "GC allocation recorder must detect a known allocation before measuring this path.");
            Assert.That(() =>
            {
                var charge = sys.PlayCharge(def);
                charge.SetProgress(0.5f);
                sys.Tick(0.02f);
                charge.SetProgress(1f);
                charge.Complete();
                sys.Tick(0.2f);
            }, Is.Not.AllocatingGCMemory());
        }
```

`DefaultHandle_AllCallsAreNoOps`(Task 1)에 끝 두 줄 추가:

```csharp
            Assert.That(charge.Complete().IsValid, Is.False);
            Assert.That(charge.Cancel().IsValid, Is.False);
```

- [ ] **Step 2: 실패 확인**

Run: `unity test "<ROOT>\unity" --mode PlayMode --filter "ChargeSoundTests"`
Expected: 컴파일 실패 — `ChargeSoundHandle`에 `Complete`/`Cancel` 없음(CS1061).

- [ ] **Step 3: 구현**

`PKG/Runtime/ChargeSoundHandle.cs` — `SetProgress` 아래에 추가:

```csharp
        /// <summary>
        /// Fades the loop out over <see cref="ChargeSoundDef.CompleteFadeOut"/> and plays the
        /// Complete sound where the loop is (tracking its transform while that is alive). Returns
        /// the Complete sound's handle, or an invalid handle when the definition has none or the
        /// charge is no longer valid. Ends the charge; later calls do nothing.
        /// </summary>
        public SoundHandle Complete() => Loop.Owner != null ? Loop.Owner.EndCharge(this, completed: true) : SoundHandle.Invalid;

        /// <summary>
        /// Fades the loop out over <see cref="ChargeSoundDef.CancelFadeOut"/> and plays the
        /// Cancel sound where the loop is (tracking its transform while that is alive). Returns
        /// the Cancel sound's handle, or an invalid handle when the definition has none or the
        /// charge is no longer valid. Ends the charge; later calls do nothing.
        /// </summary>
        public SoundHandle Cancel() => Loop.Owner != null ? Loop.Owner.EndCharge(this, completed: false) : SoundHandle.Invalid;
```

`PKG/Runtime/SoundSystem.Charge.cs` — `ApplyChargePitch` 위에 추가:

```csharp
        internal SoundHandle EndCharge(in ChargeSoundHandle charge, bool completed)
        {
            if (!IsChargeActive(charge, out var slot))
            {
                return SoundHandle.Invalid;
            }
            var def = charge.Def;
            var follow = Table.Slots[slot].Follow;
            var position = _sources[slot].transform.position;
            // FadingOut is what makes IsChargeActive false; Stop's fade restart never runs twice.
            Table.BeginFadeOut(slot, completed ? def.CompleteFadeOut : def.CancelFadeOut);
            var stinger = completed ? def.Complete : def.Cancel;
            if (stinger == null)
            {
                return SoundHandle.Invalid;
            }
            return follow != null
                ? PlayCore(stinger, follow.position, follow, 0f)
                : PlayCore(stinger, position, null, 0f);
        }
```

- [ ] **Step 4: 통과 확인**

Run: `unity test "<ROOT>\unity" --mode PlayMode --filter "ChargeSoundTests"`
Expected: 20 passed, 0 failed (total="20").
`ChargeCycle_DoesNotAllocate`가 실패하면 할당 위치를 찾아 고친다(예: 람다·박싱). 단언을 약화시키지 않는다.

- [ ] **Step 5: 커밋**

```bash
git add unity/Packages/com.bun3.unity.audio/Runtime/ChargeSoundHandle.cs unity/Packages/com.bun3.unity.audio/Runtime/SoundSystem.Charge.cs unity/Packages/com.bun3.unity.audio/Tests/Runtime/ChargeSoundTests.cs
git commit -m "✨ 차지 사운드 완료·취소와 스틸·종료 시 무효 처리 추가" \
  -m "Why:
- 차지 종료 시 루프를 끄고 완료음·취소음을 루프 위치에서 한 번만 내야 한다(스펙 §5.4·§5.5)." \
  -m "Changes:
- EndCharge: 루프의 추적 대상(살아 있을 때) 또는 소스 마지막 위치를 읽고 CompleteFadeOut/CancelFadeOut으로 페이드아웃, 이어 해당 원샷을 같은 배치로 재생해 핸들 반환.
- 첫 호출이 루프를 FadingOut으로 만들어 IsValid가 false가 되므로 재호출은 효과음을 내지 않고 페이드도 재시작하지 않는다.
- 스틸·자연 종료·Dispose된 차지는 모든 호출이 무시된다. 할당 없는 한 사이클을 테스트로 고정." \
  -m "Impact:
- 루프가 스틸된 차지는 완료음이 나지 않는다(스펙 결정). 기존 재생 경로·Tick 동작은 그대로." \
  -m "Results:
- <실행한 unity test 명령>: <실제 통과/전체 수>." \
  -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: 문서·버전·CHANGELOG + 전체 회귀

**Files:**
- Modify: `PKG/README.md` (Usage의 `### Music` 앞, `## API and tuning reference` 표)
- Modify: `PKG/CHANGELOG.md` (맨 위)
- Modify: `PKG/package.json`
- Modify: `docs/superpowers/specs/2026-10-13-unity-audio-charge-sound-design.md` (상태 줄)

**Interfaces:**
- Consumes: Task 1·2의 공개 API 이름 그대로(`ChargeSoundDef`, `PlayCharge`, `ChargeSoundHandle.SetProgress/Complete/Cancel/IsValid`, `VolumeByProgress`, `PitchByProgress`, `CompleteFadeOut`, `CancelFadeOut`).
- Produces: 없음.

- [ ] **Step 1: README — `### Music` 바로 앞에 섹션 삽입**

````markdown
### Charge sounds

A charge sound follows a progress value the game owns — hold-to-charge attacks,
reloads, crafting gauges. It plays a looping layer whose volume and pitch track
progress, plus optional start, complete and cancel one-shots.

```csharp
ChargeSoundHandle charge = sound.PlayCharge(myChargeDef, playerTransform); // start one-shot + loop
charge.SetProgress(heldSeconds / requiredSeconds);  // every frame, 0..1; may decrease
charge.Complete();  // loop fades out over CompleteFadeOut; Complete one-shot plays where the loop is
charge.Cancel();    // loop fades out over CancelFadeOut; Cancel one-shot plays if assigned
```

`ChargeSoundDef` references up to four `SoundDef`s — `Loop` (required), `Start`,
`Complete`, `Cancel` — so each part keeps its own clips, variation, routing,
spatial mode and profiles. `VolumeByProgress` scales the loop's rolled volume and
`PitchByProgress` scales its rolled pitch; X is progress 0..1 and a null or empty
curve is a constant 1. Defaults ramp volume 0 → 1 and pitch 0.667 → 1.

Progress applies immediately, without smoothing. `Complete` and `Cancel` end the
charge once: a second call plays nothing. If voice limits steal the loop, the loop
ends, or the system is disposed, every call does nothing and no completion sound
plays — keep `SfxVoices` headroom for long charges.

Authoring:

- **Loop clip:** a seamless 1–2 s loop (2–3 s if its texture has distinct events)
  with no build-up baked in, recorded as the state just before completion at pitch 1.
  The pitch curve's start sets the rise: 0.667 is a fifth below, 0.5 an octave,
  0.84 a minor third.
- **Loop definition:** `Loop` on, `Pitch` 1–1, `MaxInstances = 1` so a re-press
  replaces a fading charge, `Cooldown = 0`. Import from WAV with Decompress On Load.
- **One-shots:** Start 0.1–0.3 s; Complete 0.3–1 s with an immediate attack in the
  loop's key; Cancel optional.
````

- [ ] **Step 2: README — API 레퍼런스 표에 `SetCompletionCallback` 행 다음 행 추가**

```markdown
| `PlayCharge` / `ChargeSoundHandle` | Progress-driven loop with optional start/complete/cancel one-shots. `SetProgress` applies the definition's curves; `Complete`/`Cancel` end the charge once. Stolen, ended or disposed charges do nothing. |
```

- [ ] **Step 3: CHANGELOG — `# Changelog` 다음, 기존 `## [0.3.0]` 앞에 삽입** (`<날짜>`는 커밋 당일 `date +%F` 값)

```markdown
## [0.5.0] - <날짜>

### Added

- `ChargeSoundDef` and `SoundSystem.PlayCharge` (2D, fixed position, transform
  follow) returning `ChargeSoundHandle`: a looping layer whose volume and pitch
  follow `SetProgress(0..1)` through `VolumeByProgress`/`PitchByProgress` curves,
  with optional start, complete and cancel one-shots. `Complete`/`Cancel` fade the
  loop out once and return the one-shot's handle; stolen, ended or disposed charges
  do nothing. Progress updates do not allocate.

## [0.4.0] - 2026-09-20

Covers 0.3.1 through 0.4.0.

### Added

- `SoundAcousticSettings`, the shareable `SoundAcousticProfile` asset and
  `SoundAcousticSelection` (shared profile or local settings) as
  `SoundDef.Acoustics` and `SoundSpatialProfile.Acoustics`;
  `SoundDef.EffectiveAcoustics` resolves the active settings. Inspector drawers for
  acoustic selections. `IResolvedSoundAcousticSettings` exposes live resolved
  settings to native playback adapters.
- `SoundSystem.Prepare(SoundDef)` / `Prepare(SoundCatalog)` prewarm cooldown
  bookkeeping so the first cooldown playback does not allocate.
- `SoundDef.PlaybackClips` exposes the clips playback uses (preloaded clips first).

### Changed

- `DistanceAttenuationProfile.CreateLegacyCurve` is now `CreateInverseDistanceCurve`.
- Basic AudioSource playback honors the resolved distance-attenuation setting
  (flat rolloff when disabled).

### Removed

- **Breaking:** per-field acoustic members on `SoundDef` and `SoundSpatialProfile`
  (`MinDistance`, `MaxDistance`, `DistanceAttenuation`, `AttenuationProfile`,
  `InheritSpatialBlend`, `SpatialBlendProfile`, `MonoDistance`,
  `FullSpatialDistance`) and their `Effective*` accessors. Use `Acoustics` /
  `EffectiveAcoustics`.
- **Breaking:** `DistanceAttenuationProfile.MinimumDistance`, `MaximumDistance`
  and `FadeFraction`.

```

- [ ] **Step 4: package.json**

`"version": "0.4.0"` → `"version": "0.5.0"`.
`description`을 다음으로 교체:

```json
  "description": "Sound manager for Unity: pooled AudioSources, generation-validated handles, coroutine-free fades, voice limits, cooldowns, pitch/volume variation, progress-driven charge sounds, and UniTask-based playback awaiting.",
```

- [ ] **Step 5: 스펙 상태 갱신**

`docs/superpowers/specs/2026-10-13-unity-audio-charge-sound-design.md`의 `- 상태: 승인됨` → `- 상태: 구현 완료 (0.5.0)`.

- [ ] **Step 6: 전체 회귀**

Run: `unity test "<ROOT>\unity" --mode PlayMode --filter "Bun3.Unity.Audio.Tests"`
Expected: 0 failed, total에 `ChargeSoundTests` 20건 포함. 실패가 있으면 이번 변경이 원인인지 먼저 확인한다(기존 결함이면 별도 보고, 이번 변경이면 해당 Task 범위에서 수정).
Run: `unity test "<ROOT>\unity" --mode EditMode --filter "Bun3.Unity.Audio.Tests"`
Expected: 0 failed (에디터 픽스처 회귀 — 이번 변경과 무관해야 함).

- [ ] **Step 7: 커밋**

```bash
git add unity/Packages/com.bun3.unity.audio/README.md unity/Packages/com.bun3.unity.audio/CHANGELOG.md unity/Packages/com.bun3.unity.audio/package.json docs/superpowers/specs/2026-10-13-unity-audio-charge-sound-design.md
git commit -m "🔖 Unity Audio 0.5.0: 차지 사운드 문서화와 누락된 0.4.0 변경 기록" \
  -m "Why:
- 새 공개 API(차지 사운드)를 소비 게임이 README만 보고 쓸 수 있어야 하고, CHANGELOG가 0.3.0에서 멈춰 0.4.0의 breaking 변경이 기록에 없었다." \
  -m "Changes:
- README: 차지 사운드 사용 예, 곡선 의미·기본값, 스틸 시 완료음 없음 제약, 루프·원샷 에셋 저작 기준, API 표 행.
- CHANGELOG: 0.5.0 Added, 그리고 git 이력 기준 0.3.1~0.4.0 따라잡기(공통 음향 설정·Prepare·PlaybackClips 추가, 감쇠 반영, 개별 음향 필드 제거 breaking).
- package.json 0.4.0 → 0.5.0, description에 차지 사운드 추가. 스펙 상태를 구현 완료로 갱신." \
  -m "Impact:
- 문서·버전만 변경. 태그·퍼블리시는 별도 확인 후 진행." \
  -m "Results:
- <PlayMode 전체 회귀 명령>: <실제 결과>.
- <EditMode 전체 회귀 명령>: <실제 결과>." \
  -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

## Self-Review 결과

- 스펙 대응: §3 파일 3종 → Task 1; §4 API → Task 1·2; §5.1 곡선 → Task 1 테스트 2건; §5.2 시작 1~6 → Task 1(경고 2종·실패·쿨다운·Dispose 예외); §5.3 → Task 1; §5.4 → Task 2(위치·추적·파괴·def 없음·재호출); §5.5 → Task 2(연타 스틸·풀 스틸·Dispose); §5.6 → Task 2 무할당; §6 표 1~11 → Task 1·2; §7 → Task 3. §8은 비목표.
- 스펙 §5.2-5의 "Start가 루프를 스틸하는 풀 포화 극단"은 별도 테스트 없이 §5.5 스틸 규칙(Task 2 `LoopStolenByPoolPressure_CompletePlaysNothing`)으로 같은 결과를 보장한다.
- 이름 일관성: `VolumeAt` / `PitchScaleAt` / `IsChargeActive` / `SetChargeProgress` / `EndCharge` / `ApplyChargePitch`가 Task 1·2 전체에서 동일.
