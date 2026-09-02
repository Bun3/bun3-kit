# Bun3.Unity.Audio 오클루전+믹서+헬퍼 (슬라이스 3) 구현 플랜

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 오클루전 훅(`IOcclusionProvider`) + raycast 기본 구현 + 볼륨/LPF 적용, 동봉 기본 AudioMixer 에셋, 타임스케일 pitch 연동·스냅샷 전환 헬퍼.

**Architecture:** 오클루전은 3층 분리 — **평가**(주입 가능한 `IOcclusionProvider`, 기본 raycast)는 SoundSystem 틱의 프레임당 N개 라운드로빈에서, **스무딩**(occlusionCurrent→Target 보간)은 VoiceTable.Tick(순수, EditMode 테스트 대상)에서, **적용**(볼륨 곱 + `AudioLowPassFilter` cutoff)은 SoundSystem 미러층에서. 믹서 에셋은 커넥티드 에디터의 내부 API(reflection)로 1회 저작 후 패키지 `Runtime/Resources`에 커밋. 슬라이스 1·2 규율 계승(2단계 신호·unscaledDeltaTime·무할당 틱).

**Tech Stack:** Unity 6000.3 `Physics.Raycast`/`AudioLowPassFilter`/`AudioMixerSnapshot.TransitionTo`, unity-cli 커넥티드 에디터(`unity open` + `unity cmd`, com.unity.pipeline 설치됨), 기존 패키지 0.1.0.

**Spec:** `docs/superpowers/specs/2026-08-31-unity-audio-design.md` — "오클루전"·"부가 헬퍼"·확정 결정 테이블(기본 믹서 동봉) 섹션.

## 스펙 대비 확정 디비에이션 (플랜 레벨 룰링)

- **`SoundDef.Occlusion`은 enum(`OcclusionMode Off/Raycast`)이 아니라 `bool`.** 평가 "방식"은 시스템의 `IOcclusionProvider`가 정하므로 def에 남는 결정은 켜고/끄기뿐 — 방식 이름이 박힌 enum은 provider 추상화와 모순. 어댑터(Steam Audio, 슬라이스 4)도 bool을 그대로 매핑한다.
- **동봉 믹서의 duck 이펙트·Paused 스냅샷의 low-pass 먹먹함은 이번 슬라이스에서 제외.** 이펙트 서브에셋 저작은 내부 API 의존이 가장 깊은 부분이라, 동봉 믹서는 그룹 4개(Master/Music/SFX/Voice) + 노출 볼륨 파라미터 4개 + 스냅샷 2개(Normal/Paused — Paused는 Master 볼륨 감쇠만)로 시작한다. duck·LPF 이펙트는 오너가 에디터에서 1회 추가(2분 작업)하거나 후속 백로그. README에 명시.
- **타임스케일 pitch는 시스템 플래그 하나(`PitchWithTimescale`)로 전 SFX 보이스 적용, 음악 제외.** 스펙의 "옵트인한 보이스"를 def 단위 플래그로 세분하지 않음 — 첫 수요가 생기면 def 플래그 추가는 한 줄.

## Global Constraints

- 코드·주석·XML 문서 **영어만**; public 멤버 영어 XML 문서; 빌드 경고 0; 블록 네임스페이스.
- 오클루전 평가·스무딩·적용 경로 전부 **무할당**(클로저·LINQ·boxing 금지). raycast는 `Physics.Raycast`(non-alloc 버전 불필요 — bool 반환 오버로드는 무할당).
- 2단계 신호 규율·`unscaledDeltaTime` 틱·`Live` 역순 순회 유지(이 슬라이스는 완료 신호를 새로 만들지 않음 — 기존 규율을 깨지만 않으면 됨).
- 커밋: gitmoji 제목 + 이중 `-m` 트레일러(`Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`).
- 테스트: `unity test "E:\Projects\orca\workspace\bun3-kit\Bun3-sound-manager\unity" --mode PlayMode --filter "<F>" --output "<scratch>.xml" --timeout 1200` — 절대 경로 필수, `total`>0 확인(0=필터 오탐), 배치 인스턴스 동시 실행 금지, 런 후 패키지 밖 더럽혀진 파일 `git checkout`. 전체 회귀 기준선: `--filter "Bun3.Unity.Audio.Tests"` 49/49.
- 버전 0.1.0 Unreleased 누적(미퍼블리시).

---

### Task 1: 오클루전 데이터·훅·스무딩 (순수 로직)

**Files:**
- Create: `unity/Packages/com.bun3.unity.audio/Runtime/IOcclusionProvider.cs`
- Create: `unity/Packages/com.bun3.unity.audio/Runtime/RaycastOcclusionProvider.cs`
- Modify: `unity/Packages/com.bun3.unity.audio/Runtime/SoundDef.cs` (`Occlusion` bool 추가)
- Modify: `unity/Packages/com.bun3.unity.audio/Runtime/VoiceSlot.cs` (`OcclusionCurrent`, `OcclusionTarget` 추가)
- Modify: `unity/Packages/com.bun3.unity.audio/Runtime/VoiceTable.cs` (Tick에 스무딩, TryAllocate에 초기화)
- Modify: `unity/Packages/com.bun3.unity.audio/Runtime/SoundSystemConfig.cs` (오클루전 설정 4종)
- Test: `unity/Packages/com.bun3.unity.audio/Tests/Runtime/OcclusionSmoothingTests.cs`

**Interfaces:**
- Produces (Task 2·4가 소비):
  - `public interface IOcclusionProvider { float Evaluate(in Vector3 listenerPos, in Vector3 sourcePos); }` (0=개방, 1=차폐)
  - `public sealed class RaycastOcclusionProvider : IOcclusionProvider { public LayerMask ObstructionMask; ctor(LayerMask); }` — 단일 `Physics.Linecast` 이진 판정(막히면 1, 아니면 0)
  - `SoundDef.Occlusion` (bool, 기본 false)
  - `VoiceSlot.OcclusionCurrent/OcclusionTarget` (float; TryAllocate에서 0으로 초기화)
  - `VoiceTable.Tick`: 활성 보이스의 `OcclusionCurrent`를 `OcclusionTarget`으로 지수 스무딩 — `s.OcclusionCurrent = Mathf.MoveTowards(s.OcclusionCurrent, s.OcclusionTarget, dt / smoothing)`; smoothing 상수는 `Tick(float dt, List<...> completed, float occlusionSmoothing = 0.15f)`이 아니라 **VoiceTable 생성자 파라미터 `float occlusionSmoothingSeconds = 0.15f`**로 주입(틱 시그니처 불변)
  - Config: `IOcclusionProvider OcclusionProvider`(null→기본 raycast), `int OcclusionChecksPerFrame = 4`, `float OcclusionMuffledCutoffHz = 1200f`, `float OcclusionVolumeAtFull = 0.35f`, `float OcclusionSmoothingSeconds = 0.15f`, `LayerMask OcclusionMask = ~0`(기본 provider용), `Transform Listener`(null→AudioListener 자동 탐색, Task 2)

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
using System.Collections.Generic;
using Bun3.Unity.Audio;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class OcclusionSmoothingTests
    {
        private readonly List<(int Slot, AutoResetUniTaskCompletionSource Completion)> _scratch = new();

        private static SoundDef LoopingDef()
        {
            var def = ScriptableObject.CreateInstance<SoundDef>();
            def.Loop = true;
            def.Occlusion = true;
            return def;
        }

        [Test]
        public void Tick_MovesCurrentTowardTarget()
        {
            var table = new VoiceTable(2, occlusionSmoothingSeconds: 0.2f);
            table.TryAllocate(LoopingDef(), 1f, out var slot, out _, out _);
            table.Slots[slot].OcclusionTarget = 1f;
            table.Tick(0.1f, _scratch);
            Assert.That(table.Slots[slot].OcclusionCurrent, Is.EqualTo(0.5f).Within(0.01f));
            table.Tick(0.2f, _scratch);
            Assert.That(table.Slots[slot].OcclusionCurrent, Is.EqualTo(1f));
        }

        [Test]
        public void Allocate_ResetsOcclusionState()
        {
            var table = new VoiceTable(1, occlusionSmoothingSeconds: 0.2f);
            table.TryAllocate(LoopingDef(), 1f, out var slot, out _, out _);
            table.Slots[slot].OcclusionTarget = 1f;
            table.Tick(1f, _scratch);
            table.Release(slot);
            table.TryAllocate(LoopingDef(), 1f, out var slot2, out _, out _);
            Assert.That(table.Slots[slot2].OcclusionCurrent, Is.EqualTo(0f));
            Assert.That(table.Slots[slot2].OcclusionTarget, Is.EqualTo(0f));
        }

        [Test]
        public void Provider_BinaryContract()
        {
            var provider = new RaycastOcclusionProvider(~0);
            // No colliders in a fresh test scene: line is clear → 0.
            Assert.That(provider.Evaluate(Vector3.zero, new Vector3(0f, 0f, 10f)), Is.EqualTo(0f));
        }
    }
}
```

- [ ] **Step 2: 컴파일 실패 확인** (Occlusion 필드·생성자 파라미터 미정의)

- [ ] **Step 3: 구현**

`IOcclusionProvider.cs`:

```csharp
using UnityEngine;

namespace Bun3.Unity.Audio
{
    /// <summary>
    /// Occlusion evaluation strategy. Returns 0 for fully open, 1 for fully occluded;
    /// intermediate values are allowed. Called from the sound system's tick on a
    /// round-robin budget — implementations must not allocate.
    /// </summary>
    public interface IOcclusionProvider
    {
        /// <summary>Evaluates occlusion between the listener and a playing source.</summary>
        float Evaluate(in Vector3 listenerPos, in Vector3 sourcePos);
    }
}
```

`RaycastOcclusionProvider.cs`:

```csharp
using UnityEngine;

namespace Bun3.Unity.Audio
{
    /// <summary>
    /// Default occlusion strategy: a single physics linecast from listener to source.
    /// Binary verdict (blocked = 1, clear = 0); the voice-side smoothing turns it into
    /// a soft transition. Wall thickness and material are ignored.
    /// </summary>
    public sealed class RaycastOcclusionProvider : IOcclusionProvider
    {
        /// <summary>Layers treated as sound obstructions.</summary>
        public LayerMask ObstructionMask;

        /// <summary>Creates a provider testing against the given obstruction layers.</summary>
        public RaycastOcclusionProvider(LayerMask obstructionMask)
        {
            ObstructionMask = obstructionMask;
        }

        /// <inheritdoc/>
        public float Evaluate(in Vector3 listenerPos, in Vector3 sourcePos)
            => Physics.Linecast(listenerPos, sourcePos, ObstructionMask) ? 1f : 0f;
    }
}
```

`SoundDef.cs` — `MaxDistance` 아래에 추가:

```csharp
        /// <summary>Whether this sound participates in occlusion evaluation (3D sounds only).</summary>
        public bool Occlusion;
```

`VoiceSlot.cs` — 필드 추가:

```csharp
        public float OcclusionCurrent;
        public float OcclusionTarget;
```

`VoiceTable.cs`:
- 생성자: `public VoiceTable(int capacity, System.Random rng = null, float occlusionSmoothingSeconds = 0.15f)` — `_occlusionSmoothing = Mathf.Max(occlusionSmoothingSeconds, 0.0001f)` 필드 저장.
- `TryAllocate` 슬롯 초기화 블록에 `slot.OcclusionCurrent = 0f; slot.OcclusionTarget = 0f;` 추가.
- `Tick`의 활성 보이스 처리(Elapsed 누적 직후)에 추가:

```csharp
                if (s.OcclusionCurrent != s.OcclusionTarget)
                {
                    s.OcclusionCurrent = Mathf.MoveTowards(
                        s.OcclusionCurrent, s.OcclusionTarget, dt / _occlusionSmoothing);
                }
```

`SoundSystemConfig.cs` — `MusicGroup` 아래에 추가:

```csharp
        /// <summary>Occlusion evaluation strategy; null uses the built-in single-linecast provider.</summary>
        public IOcclusionProvider OcclusionProvider;

        /// <summary>Obstruction layers for the built-in raycast provider (ignored with a custom provider).</summary>
        public LayerMask OcclusionMask = ~0;

        /// <summary>Occlusion-enabled voices evaluated per frame (round-robin).</summary>
        public int OcclusionChecksPerFrame = 4;

        /// <summary>Low-pass cutoff (Hz) at full occlusion; 22000 = open.</summary>
        public float OcclusionMuffledCutoffHz = 1200f;

        /// <summary>Volume multiplier at full occlusion (1 = no attenuation).</summary>
        public float OcclusionVolumeAtFull = 0.35f;

        /// <summary>Seconds for the occlusion factor to travel 0→1 (click-free transitions).</summary>
        public float OcclusionSmoothingSeconds = 0.15f;

        /// <summary>Listener transform for occlusion rays; null finds the scene AudioListener.</summary>
        public Transform Listener;
```

(`using UnityEngine;` 추가 필요.)

- [ ] **Step 4: 테스트 통과 확인** (`--filter "OcclusionSmoothingTests"`, total=3) + 기존 회귀 스팟(`--filter "VoiceTableTickTests"` 5/5 — 틱 시그니처 불변 확인)

- [ ] **Step 5: 커밋** — `✨ Occlusion hook, raycast provider, voice-side smoothing`

---

### Task 2: SoundSystem 오클루전 통합 (평가 라운드로빈 + 볼륨/LPF 적용)

**Files:**
- Create: `unity/Packages/com.bun3.unity.audio/Runtime/SoundSystem.Occlusion.cs`
- Modify: `unity/Packages/com.bun3.unity.audio/Runtime/SoundSystem.cs` (워밍 시 LPF 부착, provider 초기화)
- Modify: `unity/Packages/com.bun3.unity.audio/Runtime/SoundSystem.Tick.cs` (평가 호출 + 볼륨 적용 시 오클루전 곱)
- Test: `unity/Packages/com.bun3.unity.audio/Tests/Runtime/SoundSystemOcclusionTests.cs`

**Interfaces:**
- Consumes: Task 1 전부; 기존 `_sources`, `_config`, `Table`, `Tick(float dt)`.
- Produces:
  - `SoundSystem.Occlusion.cs` (partial): `AudioLowPassFilter[] _lowPassFilters`(워밍 시 부착·비활성), `int _occlusionCursor`, `internal void EvaluateOcclusion()`(라운드로빈 — 테스트가 직접 호출 가능), `internal float OcclusionVolumeMultiplier(int slot)`, `internal void ApplyOcclusionFilter(int slot)`
  - 볼륨 합성: `source.volume = Table.CurrentVolume(i) * OcclusionVolumeMultiplier(i)` — 기존 Tick의 볼륨 적용 지점 교체
  - 리스너 캐시: `Transform ResolveListener()` — `_config.Listener` 우선, 없으면 `Object.FindAnyObjectByType<AudioListener>()` 1회 캐시(파괴 시 재탐색; 탐색은 콜드 패스 — 무할당 규율 예외로 XML 문서에 명시)

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
using System.Collections;
using Bun3.Unity.Audio;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class SoundSystemOcclusionTests
    {
        private sealed class FakeProvider : IOcclusionProvider
        {
            public float Value;
            public int Calls;
            public float Evaluate(in Vector3 listenerPos, in Vector3 sourcePos)
            {
                Calls++;
                return Value;
            }
        }

        private static SoundDef OccludedDef()
        {
            var def = ScriptableObject.CreateInstance<SoundDef>();
            def.Clips = new[] { AudioClip.Create("occ", 44100, 1, 44100, false) }; // 1 s
            def.Spatial = SpatialMode.Positional;
            def.Occlusion = true;
            return def;
        }

        private static GameObject ListenerGo()
        {
            var go = new GameObject("listener");
            go.AddComponent<AudioListener>();
            return go;
        }

        [UnityTest]
        public IEnumerator OccludedVoice_TargetsOne_VolumeAndFilterFollow()
        {
            var listener = ListenerGo();
            var provider = new FakeProvider { Value = 1f };
            using var sys = new SoundSystem(new SoundSystemConfig
            {
                SfxVoices = 2,
                OcclusionProvider = provider,
                Listener = listener.transform,
                OcclusionSmoothingSeconds = 0.1f,
                OcclusionVolumeAtFull = 0.5f,
            });
            var h = sys.Play(OccludedDef(), new Vector3(0f, 0f, 5f));
            sys.EvaluateOcclusion();
            Assert.That(provider.Calls, Is.GreaterThanOrEqualTo(1));
            sys.Tick(0.2f); // smoothing reaches 1
            var slot = 0;
            Assert.That(sys.Table.Slots[slot].OcclusionCurrent, Is.EqualTo(1f));
            Assert.That(sys.OcclusionVolumeMultiplier(slot), Is.EqualTo(0.5f).Within(0.001f));
            Object.Destroy(listener);
            yield break;
        }

        [UnityTest]
        public IEnumerator NonOccludedDef_NeverEvaluated()
        {
            var listener = ListenerGo();
            var provider = new FakeProvider { Value = 1f };
            using var sys = new SoundSystem(new SoundSystemConfig
            {
                SfxVoices = 2,
                OcclusionProvider = provider,
                Listener = listener.transform,
            });
            var def = OccludedDef();
            def.Occlusion = false;
            sys.Play(def, new Vector3(0f, 0f, 5f));
            sys.EvaluateOcclusion();
            sys.EvaluateOcclusion();
            Assert.That(provider.Calls, Is.EqualTo(0));
            Object.Destroy(listener);
            yield break;
        }

        [UnityTest]
        public IEnumerator RoundRobin_HonorsPerFrameBudget()
        {
            var listener = ListenerGo();
            var provider = new FakeProvider { Value = 0f };
            using var sys = new SoundSystem(new SoundSystemConfig
            {
                SfxVoices = 8,
                OcclusionProvider = provider,
                Listener = listener.transform,
                OcclusionChecksPerFrame = 2,
            });
            for (var i = 0; i < 6; i++)
            {
                sys.Play(OccludedDef(), new Vector3(i, 0f, 5f));
            }
            sys.EvaluateOcclusion();
            Assert.That(provider.Calls, Is.EqualTo(2), "budget caps evaluations per frame");
            sys.EvaluateOcclusion();
            sys.EvaluateOcclusion();
            Assert.That(provider.Calls, Is.EqualTo(6), "cursor advances across frames");
            Object.Destroy(listener);
            yield break;
        }
    }
}
```

- [ ] **Step 2: 컴파일 실패 확인**

- [ ] **Step 3: 구현**

`SoundSystem.Occlusion.cs`:

```csharp
// Occlusion integration: round-robin provider evaluation and LPF/volume application.
// Evaluation targets are written to VoiceTable slots; smoothing happens in VoiceTable.Tick.
using UnityEngine;

namespace Bun3.Unity.Audio
{
    public sealed partial class SoundSystem
    {
        private IOcclusionProvider _occlusionProvider;
        private AudioLowPassFilter[] _lowPassFilters;
        private Transform _listener;
        private int _occlusionCursor;

        private const float OpenCutoffHz = 22000f;

        private void InitializeOcclusion()
        {
            _occlusionProvider = _config.OcclusionProvider
                ?? new RaycastOcclusionProvider(_config.OcclusionMask);
            _lowPassFilters = new AudioLowPassFilter[_sources.Length];
            for (var i = 0; i < _sources.Length; i++)
            {
                _lowPassFilters[i] = _sources[i].gameObject.AddComponent<AudioLowPassFilter>();
                _lowPassFilters[i].cutoffFrequency = OpenCutoffHz;
                _lowPassFilters[i].enabled = false;
            }
        }

        /// <summary>
        /// Round-robin occlusion evaluation: up to OcclusionChecksPerFrame occlusion-enabled
        /// voices per call. Listener lookup on loss is the one sanctioned cold-path allocation.
        /// </summary>
        internal void EvaluateOcclusion()
        {
            var listener = ResolveListener();
            if (listener == null)
            {
                return;
            }
            var listenerPos = listener.position;
            var slots = Table.Slots;
            var budget = _config.OcclusionChecksPerFrame;
            var checkedCount = 0;
            for (var step = 0; step < slots.Length && checkedCount < budget; step++)
            {
                var i = _occlusionCursor;
                _occlusionCursor = (_occlusionCursor + 1) % slots.Length;
                ref var s = ref slots[i];
                if (s.State == VoiceState.Idle || s.Def == null || !s.Def.Occlusion)
                {
                    continue;
                }
                checkedCount++;
                s.OcclusionTarget = _occlusionProvider.Evaluate(
                    listenerPos, _sources[i].transform.position);
            }
        }

        /// <summary>Volume multiplier for the slot's current occlusion (1 = open).</summary>
        internal float OcclusionVolumeMultiplier(int slot)
        {
            var occ = Table.Slots[slot].OcclusionCurrent;
            return occ <= 0f ? 1f : Mathf.Lerp(1f, _config.OcclusionVolumeAtFull, occ);
        }

        /// <summary>Mirrors the slot's occlusion onto its low-pass filter (enabled only when occluded).</summary>
        internal void ApplyOcclusionFilter(int slot)
        {
            var occ = Table.Slots[slot].OcclusionCurrent;
            var filter = _lowPassFilters[slot];
            if (occ <= 0.001f)
            {
                if (filter.enabled)
                {
                    filter.cutoffFrequency = OpenCutoffHz;
                    filter.enabled = false;
                }
                return;
            }
            filter.enabled = true;
            filter.cutoffFrequency = Mathf.Lerp(OpenCutoffHz, _config.OcclusionMuffledCutoffHz, occ);
        }

        private Transform ResolveListener()
        {
            if (_config.Listener != null)
            {
                return _config.Listener;
            }
            if (_listener == null)
            {
                var found = UnityEngine.Object.FindAnyObjectByType<AudioListener>();
                _listener = found != null ? found.transform : null;
            }
            return _listener;
        }
    }
}
```

`SoundSystem.cs` 생성자 — 뮤직 소스 워밍 뒤에 `InitializeOcclusion();` 호출 추가.

`SoundSystem.Tick.cs` — `Tick(float dt)`에서:
- `Table.Tick(...)` 호출 전(또는 후 — 평가는 타깃만 쓰므로 순서 무관, 단 한 곳)에 `EvaluateOcclusion();` 추가.
- 활성 보이스 볼륨 적용 지점을 교체: `_sources[i].volume = Table.CurrentVolume(i) * OcclusionVolumeMultiplier(i); ApplyOcclusionFilter(i);`

- [ ] **Step 4: 테스트 통과 확인** (`--filter "SoundSystemOcclusionTests"`, total=3) + 전체 회귀(`--filter "Bun3.Unity.Audio.Tests"` — 49+6=55 예상, AllocationTests 포함 전부 그린; LPF 부착·EvaluateOcclusion이 무할당 어설션을 깨지 않는지 확인 — provider가 기본 raycast일 때 Linecast는 무할당, 리스너 캐시는 워밍됨)

- [ ] **Step 5: 커밋** — `✨ Occlusion evaluation round-robin + volume/LPF application`

---

### Task 3: 타임스케일 pitch + 스냅샷 전환 헬퍼

**Files:**
- Modify: `unity/Packages/com.bun3.unity.audio/Runtime/SoundSystemConfig.cs` (`PitchWithTimescale`)
- Modify: `unity/Packages/com.bun3.unity.audio/Runtime/SoundSystem.Tick.cs` (타임스케일 감지·적용)
- Modify: `unity/Packages/com.bun3.unity.audio/Runtime/SoundSystem.cs` (`TransitionTo` 헬퍼)
- Test: `unity/Packages/com.bun3.unity.audio/Tests/Runtime/TimescalePitchTests.cs`

**Interfaces:**
- Consumes: `VoiceSlot.Pitch`(롤된 기준값), `_sources`, `SetSourcePitch`
- Produces:
  - `SoundSystemConfig.PitchWithTimescale` (bool, 기본 false)
  - Tick: `Time.timeScale`이 직전 프레임과 다르면 활성 SFX 보이스 전체에 `source.pitch = voice.Pitch * timeScale` 재적용(플래그 켜진 경우만; 음악 소스는 건드리지 않음). timeScale 1 복귀 시에도 같은 경로로 원복.
  - `public void TransitionTo(AudioMixerSnapshot snapshot, float seconds)` — null 가드 + `snapshot.TransitionTo(seconds)` 위임(스펙 API 표면 유지용 얇은 래퍼)

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
using System.Collections;
using Bun3.Unity.Audio;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class TimescalePitchTests
    {
        private static SoundDef LoopDef()
        {
            var def = ScriptableObject.CreateInstance<SoundDef>();
            def.Clips = new[] { AudioClip.Create("ts", 4410, 1, 44100, false) };
            def.Loop = true;
            def.Pitch = new FloatRange(1f, 1f);
            return def;
        }

        [UnityTest]
        public IEnumerator TimescaleChange_ScalesSfxPitch_AndRestores()
        {
            using var sys = new SoundSystem(new SoundSystemConfig
            {
                SfxVoices = 2,
                PitchWithTimescale = true,
            });
            var h = sys.Play(LoopDef());
            try
            {
                Time.timeScale = 0.5f;
                sys.Tick(0.02f);
                Assert.That(sys.SourcePitchForTest(0), Is.EqualTo(0.5f).Within(0.001f));
                Time.timeScale = 1f;
                sys.Tick(0.02f);
                Assert.That(sys.SourcePitchForTest(0), Is.EqualTo(1f).Within(0.001f));
            }
            finally
            {
                Time.timeScale = 1f;
            }
            yield break;
        }

        [UnityTest]
        public IEnumerator FlagOff_PitchUntouched()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            sys.Play(LoopDef());
            try
            {
                Time.timeScale = 0.5f;
                sys.Tick(0.02f);
                Assert.That(sys.SourcePitchForTest(0), Is.EqualTo(1f).Within(0.001f));
            }
            finally
            {
                Time.timeScale = 1f;
            }
            yield break;
        }
    }
}
```

(`internal float SourcePitchForTest(int slot) => _sources[slot].pitch;`를 SoundSystem.cs에 추가 — 테스트 전용 접근자, XML 문서 불필요(internal).)

- [ ] **Step 2: 컴파일 실패 확인**

- [ ] **Step 3: 구현**

`SoundSystemConfig.cs`:

```csharp
        /// <summary>When true, SFX voice pitch is multiplied by Time.timeScale (slow-motion effect). Music is unaffected.</summary>
        public bool PitchWithTimescale;
```

`SoundSystem.Tick.cs` — 필드 `private float _lastTimeScale = 1f;` 추가, `Tick(float dt)`에:

```csharp
            if (_config.PitchWithTimescale)
            {
                var scale = Time.timeScale;
                if (!Mathf.Approximately(scale, _lastTimeScale))
                {
                    _lastTimeScale = scale;
                    for (var i = 0; i < Table.Slots.Length; i++)
                    {
                        if (Table.Slots[i].State != VoiceState.Idle)
                        {
                            _sources[i].pitch = Table.Slots[i].Pitch * scale;
                        }
                    }
                }
            }
```

`SoundSystem.cs`:

```csharp
        /// <summary>Thin wrapper over AudioMixerSnapshot.TransitionTo; no-op on null.</summary>
        public void TransitionTo(UnityEngine.Audio.AudioMixerSnapshot snapshot, float seconds)
        {
            if (snapshot == null)
            {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                Debug.LogWarning("SoundSystem.TransitionTo: null snapshot; ignored.");
#endif
                return;
            }
            snapshot.TransitionTo(seconds);
        }

        internal float SourcePitchForTest(int slot) => _sources[slot].pitch;
```

주의: `SoundHandle.SetPitch`는 `voice.Pitch`를 갱신하므로 타임스케일 재적용과 자연 합성된다(재적용 시 `Pitch * scale`). 신규 재생(`PlayCore`)의 `source.pitch = voice.Pitch`도 플래그 켜짐+scale≠1이면 `* _lastTimeScale` 곱 — PlayCore에 한 줄 추가:

```csharp
            source.pitch = _config.PitchWithTimescale ? voice.Pitch * _lastTimeScale : voice.Pitch;
```

(`_lastTimeScale`은 Tick에서만 갱신 — PlayCore는 저장값 사용으로 Time.timeScale 접근 최소화.)

- [ ] **Step 4: 테스트 통과 확인** (`--filter "TimescalePitchTests"`, total=2) + 전체 회귀

- [ ] **Step 5: 커밋** — `✨ Timescale pitch scaling + snapshot transition helper`

---

### Task 4: 동봉 기본 믹서 에셋 (커넥티드 에디터 저작)

**Files:**
- Create: `unity/Packages/com.bun3.unity.audio/Runtime/Resources/Bun3DefaultAudioMixer.mixer` (+ .meta)
- Modify: `unity/Packages/com.bun3.unity.audio/Runtime/SoundSystem.cs` (config.Mixer null → 동봉 믹서 로드 + 그룹 기본 배선)
- Test: `unity/Packages/com.bun3.unity.audio/Tests/Runtime/DefaultMixerTests.cs`

**Interfaces:**
- Consumes: `SoundSystemConfig.Mixer/SfxGroup/MusicGroup`, `SetChannelVolume`/`GetChannelVolume`(노출 파라미터 `MasterVolume/MusicVolume/SfxVolume/VoiceVolume`)
- Produces:
  - 믹서 에셋: Master 루트 아래 그룹 `Music`/`SFX`/`Voice`, 노출 파라미터 4종(각 그룹 볼륨), 스냅샷 `Normal`/`Paused`(Paused는 MasterVolume −20dB — 볼륨 값만, 이펙트 없음)
  - SoundSystem 생성자: `_config.Mixer == null`이면 `Resources.Load<AudioMixer>("Bun3DefaultAudioMixer")` 시도 — 성공 시 `_config.Mixer` 대신 내부 `_mixer` 필드에 보관하고 `SetChannelVolume/GetChannelVolume`이 그것을 쓰도록 `_config.Mixer` 직접 참조를 `_mixer`로 교체(`_mixer = _config.Mixer != null ? _config.Mixer : Resources.Load<AudioMixer>("Bun3DefaultAudioMixer")`); `SfxGroup`/`MusicGroup` 미지정 시 동봉 믹서의 해당 그룹(`FindMatchingGroups("SFX")[0]` 등)을 기본으로 배선. 로드 실패(에셋 없음)는 기존 null-mixer 동작으로 폴백(경고 없음 — 에셋 미동봉 빌드 허용).

**저작 절차 (에디터 내부 API — 실행 순서 엄수):**

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
using Bun3.Unity.Audio;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Audio;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class DefaultMixerTests
    {
        [Test]
        public void BundledMixer_LoadsWithGroupsSnapshotsAndParams()
        {
            var mixer = Resources.Load<AudioMixer>("Bun3DefaultAudioMixer");
            Assert.IsNotNull(mixer, "bundled mixer asset must load from package Resources");
            Assert.That(mixer.FindMatchingGroups("Music"), Has.Length.GreaterThanOrEqualTo(1));
            Assert.That(mixer.FindMatchingGroups("SFX"), Has.Length.GreaterThanOrEqualTo(1));
            Assert.That(mixer.FindMatchingGroups("Voice"), Has.Length.GreaterThanOrEqualTo(1));
            Assert.IsNotNull(mixer.FindSnapshot("Normal"));
            Assert.IsNotNull(mixer.FindSnapshot("Paused"));
            Assert.IsTrue(mixer.GetFloat("MasterVolume", out _), "MasterVolume must be exposed");
            Assert.IsTrue(mixer.GetFloat("MusicVolume", out _));
            Assert.IsTrue(mixer.GetFloat("SfxVolume", out _));
            Assert.IsTrue(mixer.GetFloat("VoiceVolume", out _));
        }

        [Test]
        public void NullMixerConfig_FallsBackToBundled_ChannelVolumeRoundTrips()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            sys.SetChannelVolume(SoundChannel.Sfx, 0.5f);
            Assert.That(sys.GetChannelVolume(SoundChannel.Sfx), Is.EqualTo(0.5f).Within(0.01f));
        }
    }
}
```

- [ ] **Step 2: 테스트 실패 확인** (에셋 없음 → 첫 테스트 실패; 두 번째는 폴백 전 null-mixer 동작으로 1 반환 → 실패)

- [ ] **Step 3: 커넥티드 에디터로 믹서 저작**

1. `unity open "E:\Projects\orca\workspace\bun3-kit\Bun3-sound-manager\unity"` (백그라운드, 부팅 대기 — `unity status`로 확인)
2. `unity cmd`(Pipeline 패키지)로 에디터에서 C# 실행 — `UnityEditor.Audio.AudioMixerController` 내부 API를 reflection으로 사용해:
   - `AudioMixerController.CreateMixerControllerAtPath("Packages/com.bun3.unity.audio/Runtime/Resources/Bun3DefaultAudioMixer.mixer")`(또는 동등 시그니처 — reflection으로 정확한 멤버를 덤프해 확인)
   - 마스터 아래 그룹 3개 생성(`CreateNewGroup` + `AddChildToParent`), 이름 `Music`/`SFX`/`Voice`
   - 각 그룹 볼륨 파라미터 노출(`ExposedParameters` 배열 조작 또는 `AudioMixerController`의 노출 API) — 이름을 정확히 `MasterVolume`/`MusicVolume`/`SfxVolume`/`VoiceVolume`으로 리네임
   - 스냅샷 `Normal`(기본), `Paused` 생성 — `Paused`에서 Master 볼륨 −20dB
   - `AssetDatabase.SaveAssets()`
3. 에디터 종료(또는 유지 — 단 이후 배치 테스트와 동시 실행 금지이므로 **종료 필수**), 생성된 `.mixer`+`.meta` 커밋 대상 확인.
4. **내부 API가 3회 시도 내에 협조하지 않으면 중단하고 DONE_WITH_CONCERNS로 보고** — 컨트롤러가 폴백(오너 수동 저작 + 로드 경로만 구현)을 결정한다. YAML 손저작은 시도하지 않는다(직렬화 포맷 리스크).

- [ ] **Step 4: SoundSystem 폴백 로드 구현** (위 Produces 서술대로 `_mixer` 필드 도입, `SetChannelVolume`/`GetChannelVolume`/음악·SFX 그룹 기본 배선 교체)

- [ ] **Step 5: 테스트 통과 확인** (`--filter "DefaultMixerTests"`, total=2) + 전체 회귀

- [ ] **Step 6: 커밋** — `✨ Bundled default AudioMixer + null-config fallback wiring`

---

### Task 5: 문서·무할당·최종 회귀

**Files:**
- Modify: `unity/Packages/com.bun3.unity.audio/README.md`, `CHANGELOG.md`
- Modify: `unity/Packages/com.bun3.unity.audio/Tests/Runtime/AllocationTests.cs`

**Interfaces:**
- Consumes: 전체.

- [ ] **Step 1: AllocationTests에 오클루전 경로 어설션 추가**

```csharp
        [Test]
        public void OcclusionTickPath_DoesNotAllocate()
        {
            var listenerGo = new GameObject("alloc-listener");
            listenerGo.AddComponent<AudioListener>();
            using var sys = new SoundSystem(new SoundSystemConfig
            {
                SfxVoices = 4,
                Listener = listenerGo.transform,
                OcclusionChecksPerFrame = 4,
            });
            var def = ScriptableObject.CreateInstance<SoundDef>();
            def.Clips = new[] { AudioClip.Create("occ-alloc", 44100, 1, 44100, false) };
            def.Loop = true;
            def.Occlusion = true;
            sys.Play(def, new Vector3(0f, 0f, 5f));
            sys.Tick(0.02f); // warm all paths (listener cached, provider constructed)

            Assert.That(() =>
            {
                sys.Tick(0.02f);
                sys.Tick(0.02f);
            }, Is.Not.AllocatingGCMemory());

            Object.DestroyImmediate(listenerGo);
        }
```

실패 시 예상 함정: `FindAnyObjectByType` 경로가 매 틱 실행(리스너 캐시 버그), LPF `enabled` 토글의 내부 할당(발견 시 토글 빈도 줄이기 — occ 경계 히스테리시스), provider 기본 생성 위치.

- [ ] **Step 2: README/CHANGELOG 갱신** — 오클루전 사용법(SoundDef.Occlusion + config 튜닝 + 커스텀 provider 주입), 기본 믹서(스냅샷 이름·노출 파라미터 이름·duck 미포함 명시 + "에디터에서 duck/LPF 이펙트 추가 가능"), PitchWithTimescale·TransitionTo. CHANGELOG 0.1.0 Added에 세 줄.

- [ ] **Step 3: 최종 전체 회귀** — `--filter "Bun3.Unity.Audio.Tests"` 전부 그린(55 + Task3 2 + Task4 2 + 본 태스크 1 = 60 예상; total 값 XML 확인), 경고 0.

- [ ] **Step 4: 커밋** — `✅ Occlusion alloc guard + slice 3 docs`

---

## Self-Review 결과

- 스펙 커버리지: 오클루전 섹션(인터페이스·기본 raycast·코어 적용 책임·스무딩·라운드로빈 비용 제어) → Task 1·2. 부가 헬퍼(타임스케일·스냅샷) → Task 3. 확정 결정의 "기본 믹서 에셋 동봉" → Task 4. 에러 처리(리스너 부재 시 무동작) → Task 2. 디비에이션 3건은 상단 룰링 섹션에 명시.
- 플레이스홀더: Task 4의 에디터 저작 절차는 내부 API 특성상 정확한 시그니처를 사전 확정할 수 없어 "reflection으로 덤프해 확인 + 3회 시도 후 에스컬레이션" 프로토콜로 대체 — 의도된 탐색 태스크이며 실패 폴백이 정의돼 있음.
- 타입 일관성: `VoiceTable` 생성자 확장(occlusionSmoothingSeconds)은 기존 호출부(SoundSystem: `new VoiceTable(config.SfxVoices, _rng)` → `new VoiceTable(config.SfxVoices, _rng, config.OcclusionSmoothingSeconds)` — Task 2 Step 3에서 수정)와 테스트 호출부(named argument 사용) 정합. `EvaluateOcclusion`/`OcclusionVolumeMultiplier`/`ApplyOcclusionFilter` 명칭이 Task 2·5에서 일치.
- 리스크: Task 4가 최대 불확실성(내부 API) — 에스컬레이션 경로와 폴백 룰링 사전 정의로 루프 소모 방지. Task 2의 AllocationTests 회귀(LPF 추가로 기존 `PlayAndTick_DoNotAllocate`가 깨질 가능성 — LPF는 워밍 시 부착이므로 괜찮아야 하나 확인 필수)를 Step 4에 명시.
