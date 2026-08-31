# Bun3.Unity.Audio 코어 (슬라이스 1) 구현 플랜

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** AudioSource 풀·세대 검증 핸들·무-coroutine fade·보이스 제한·쿨다운·variation·채널 볼륨·UniTask 경로를 갖춘 `com.bun3.unity.audio` 패키지 코어.

**Architecture:** 순수 상태 기계 `VoiceTable`(EditMode 테스트 대상, AudioSource 무접촉)과 그것을 AudioSource 배열에 반영하는 `SoundSystem`(PlayerLoopSystemHelper 틱)의 2층 구조. 핸들은 `(slot, generation)` struct로 stale 접근을 no-op 처리.

**Tech Stack:** Unity 6000.3 임베디드 UPM 패키지, UniTask(AutoResetUniTaskCompletionSource), Bun3.Unity.Core(PlayerLoopSystemHelper), Unity Test Framework 1.6.

**Spec:** `docs/superpowers/specs/2026-08-31-unity-audio-design.md` (슬라이스 2~5 — 음악·오클루전+믹서 에셋+헬퍼·Steam Audio 어댑터·Addressables — 는 후속 플랜)

## Global Constraints

- 코드·주석·XML 문서 **영어만**. 한국어 설명은 docs/ md에만.
- 모든 public 멤버 영어 XML 문서, 빌드 경고 0, 블록 네임스페이스.
- 핫패스(Play/Tick) **무할당**: 클로저·LINQ·boxing·문자열 생성 금지. 예외: cooldown Dictionary는 def당 최초 1회 성장(스펙 승인).
- 폴더=네임스페이스, 평평하게. partial은 `타입.역할.cs`.
- 네임스페이스 `Bun3.Unity.Audio`, asmdef `Bun3.Unity.Audio`(refs: `UniTask`, `Bun3.Unity.Core`).
- 커밋: gitmoji 제목 + 이중 `-m` 플래그로 트레일러(`Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`).
- 테스트 실행은 Unity Editor Test Runner 경유(unity-cli 스킬 참조). 에디터 부팅 비용 때문에 태스크마다 강제하지 않고, 태스크의 "테스트 실행" 스텝은 컴파일+해당 테스트 통과 확인을 의미한다.

---

### Task 1: 패키지 스캐폴드

**Files:**
- Create: `unity/Packages/com.bun3.unity.audio/package.json`
- Create: `unity/Packages/com.bun3.unity.audio/Runtime/Bun3.Unity.Audio.asmdef`
- Create: `unity/Packages/com.bun3.unity.audio/Runtime/AssemblyInfo.cs`
- Create: `unity/Packages/com.bun3.unity.audio/Tests/Runtime/Bun3.Unity.Audio.Tests.asmdef`
- Create: `unity/Packages/com.bun3.unity.audio/CHANGELOG.md`

**Interfaces:**
- Produces: 어셈블리 `Bun3.Unity.Audio`(internal을 Tests에 공개), 테스트 어셈블리 `Bun3.Unity.Audio.Tests`. 이후 전 태스크가 이 안에 파일을 추가한다.

- [ ] **Step 1: package.json 작성**

```json
{
  "name": "com.bun3.unity.audio",
  "displayName": "Bun3 Unity Audio",
  "version": "0.1.0",
  "unity": "6000.3",
  "unityRelease": "14f1",
  "description": "Sound manager for Unity: pooled AudioSources, generation-validated handles, coroutine-free fades, voice limits, cooldowns, pitch/volume variation, and UniTask-based playback awaiting.",
  "author": { "name": "Bun3", "url": "https://github.com/Bun3", "email": "bun3.dev@gmail.com" },
  "dependencies": {
    "com.bun3.unity.core": "0.5.1",
    "com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask"
  }
}
```

- [ ] **Step 2: Runtime asmdef 작성**

```json
{
    "name": "Bun3.Unity.Audio",
    "rootNamespace": "Bun3.Unity.Audio",
    "references": [ "UniTask", "Bun3.Unity.Core" ],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 3: AssemblyInfo.cs 작성** (테스트에서 VoiceTable 등 internal 접근)

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Bun3.Unity.Audio.Tests")]
```

- [ ] **Step 4: Tests asmdef 작성** (core 패키지 컨벤션 복제)

```json
{
    "name": "Bun3.Unity.Audio.Tests",
    "rootNamespace": "Bun3.Unity.Audio.Tests",
    "references": [ "Bun3.Unity.Audio", "UniTask", "UnityEngine.TestRunner", "UnityEditor.TestRunner" ],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": [ "nunit.framework.dll" ],
    "autoReferenced": false,
    "defineConstraints": [ "UNITY_INCLUDE_TESTS" ],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 5: CHANGELOG.md 작성**

```markdown
# Changelog

## [0.1.0] - Unreleased

### Added

- Initial core: pooled AudioSources, generation-validated `SoundHandle`,
  coroutine-free fades, per-sound voice limits and cooldowns,
  pitch/volume variation, channel volumes, UniTask playback awaiting.
```

- [ ] **Step 6: 에디터에서 컴파일 확인 후 커밋** (.meta는 Unity가 생성 — 함께 커밋)

```bash
git add unity/Packages/com.bun3.unity.audio
git commit -m "🎉 Scaffold com.bun3.unity.audio package" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: FloatRange + SoundDef

**Files:**
- Create: `unity/Packages/com.bun3.unity.audio/Runtime/FloatRange.cs`
- Create: `unity/Packages/com.bun3.unity.audio/Runtime/SoundDef.cs`
- Test: `unity/Packages/com.bun3.unity.audio/Tests/Runtime/FloatRangeTests.cs`

**Interfaces:**
- Produces: `struct FloatRange { float Min, Max; float Roll(); }`, `enum SpatialMode { None, Positional, Follow }`, `class SoundDef : ScriptableObject`(public 필드: `Clips`, `Volume`, `Pitch`, `Loop`, `MixerGroup`, `MaxInstances`, `Cooldown`, `Spatial`, `MinDistance`, `MaxDistance`). Task 3+ 전부가 `SoundDef`를 키로 소비.

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
using Bun3.Unity.Audio;
using NUnit.Framework;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class FloatRangeTests
    {
        [Test]
        public void Roll_StaysWithinBounds()
        {
            var range = new FloatRange(0.8f, 1.2f);
            for (var i = 0; i < 100; i++)
            {
                var value = range.Roll();
                Assert.That(value, Is.InRange(0.8f, 1.2f));
            }
        }

        [Test]
        public void Roll_DegenerateRange_ReturnsConstant()
        {
            var range = new FloatRange(1f, 1f);
            Assert.That(range.Roll(), Is.EqualTo(1f));
        }
    }
}
```

- [ ] **Step 2: 컴파일 실패 확인** (FloatRange 미정의)

- [ ] **Step 3: 구현**

`FloatRange.cs`:

```csharp
using System;
using UnityEngine;

namespace Bun3.Unity.Audio
{
    /// <summary>Inclusive [Min, Max] range rolled per play for volume/pitch variation.</summary>
    [Serializable]
    public struct FloatRange
    {
        /// <summary>Lower bound (inclusive).</summary>
        public float Min;

        /// <summary>Upper bound (inclusive).</summary>
        public float Max;

        /// <summary>Creates a range with the given bounds.</summary>
        public FloatRange(float min, float max)
        {
            Min = min;
            Max = max;
        }

        /// <summary>Returns a uniformly random value in [Min, Max].</summary>
        public float Roll() => UnityEngine.Random.Range(Min, Max);
    }
}
```

`SoundDef.cs` (`SpatialMode`는 밀접한 형제 타입 — 같은 파일):

```csharp
using UnityEngine;
using UnityEngine.Audio;

namespace Bun3.Unity.Audio
{
    /// <summary>How a played sound is positioned in the world.</summary>
    public enum SpatialMode
    {
        /// <summary>2D playback, no spatialization.</summary>
        None,

        /// <summary>3D playback at a fixed position.</summary>
        Positional,

        /// <summary>3D playback tracking a Transform every frame.</summary>
        Follow,
    }

    /// <summary>
    /// Designer-tuned sound definition. The asset reference itself is the runtime key —
    /// no string or enum IDs. Fields are read once at play time; live edits apply to
    /// subsequent plays.
    /// </summary>
    [CreateAssetMenu(menuName = "Bun3/Audio/Sound Def", fileName = "SoundDef")]
    public sealed class SoundDef : ScriptableObject
    {
        /// <summary>Candidate clips; one is chosen per play, avoiding the previous pick.</summary>
        public AudioClip[] Clips;

        /// <summary>Base volume range rolled per play.</summary>
        public FloatRange Volume = new(1f, 1f);

        /// <summary>Pitch range rolled per play.</summary>
        public FloatRange Pitch = new(1f, 1f);

        /// <summary>Whether playback loops until stopped.</summary>
        public bool Loop;

        /// <summary>Target mixer group; null falls back to the system's SFX group.</summary>
        public AudioMixerGroup MixerGroup;

        /// <summary>Max simultaneous voices for this def; 0 = unlimited. Exceeding steals the oldest.</summary>
        public int MaxInstances;

        /// <summary>Minimum seconds between retriggers; 0 = none. Blocked plays return an invalid handle.</summary>
        public float Cooldown;

        /// <summary>Spatialization mode.</summary>
        public SpatialMode Spatial = SpatialMode.None;

        /// <summary>3D attenuation minimum distance (used when Spatial != None).</summary>
        public float MinDistance = 1f;

        /// <summary>3D attenuation maximum distance (used when Spatial != None).</summary>
        public float MaxDistance = 30f;

        /// <summary>Round-robin memory: index of the clip chosen on the previous play.</summary>
        [System.NonSerialized] internal int LastClipIndex = -1;
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

- [ ] **Step 5: 커밋** — `✨ FloatRange variation + SoundDef asset`

---

### Task 3: VoiceTable — 할당·해제·세대 검증

**Files:**
- Create: `unity/Packages/com.bun3.unity.audio/Runtime/VoiceSlot.cs`
- Create: `unity/Packages/com.bun3.unity.audio/Runtime/VoiceTable.cs`
- Test: `unity/Packages/com.bun3.unity.audio/Tests/Runtime/VoiceTableTests.cs`

**Interfaces:**
- Consumes: `SoundDef` (Task 2)
- Produces (internal — Task 4~7이 소비):
  - `enum VoiceState : byte { Idle, FadingIn, Playing, FadingOut }`
  - `struct VoiceSlot` — `Generation`, `State`, `Def`, `Elapsed`, `ClipLength`, `Loop`, `FadeElapsed/FadeDuration/FadeFrom/FadeTo/FadeFactor`, `BaseVolume`, `VolumeScale`, `Pitch`, `StartTime`, `Follow(Transform)`, `Completion(AutoResetUniTaskCompletionSource)`
  - `class VoiceTable` — `VoiceSlot[] Slots`, `bool TryAllocate(SoundDef def, float clipLength, out int slotIndex, out int stolenSlot)`, `bool IsValid(int slot, uint generation)`, `void Release(int slot)`, `void Tick(float dt, List<int> completed)`(Task 5), `float CurrentVolume(int slot)`

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
using Bun3.Unity.Audio;
using NUnit.Framework;
using UnityEngine;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class VoiceTableTests
    {
        private static SoundDef NewDef(int maxInstances = 0, float cooldown = 0f)
        {
            var def = ScriptableObject.CreateInstance<SoundDef>();
            def.MaxInstances = maxInstances;
            def.Cooldown = cooldown;
            return def;
        }

        [Test]
        public void Allocate_ReturnsValidSlot()
        {
            var table = new VoiceTable(4);
            Assert.IsTrue(table.TryAllocate(NewDef(), 1f, out var slot, out _));
            Assert.IsTrue(table.IsValid(slot, table.Slots[slot].Generation));
        }

        [Test]
        public void Release_InvalidatesOldGeneration()
        {
            var table = new VoiceTable(4);
            table.TryAllocate(NewDef(), 1f, out var slot, out _);
            var gen = table.Slots[slot].Generation;
            table.Release(slot);
            Assert.IsFalse(table.IsValid(slot, gen));
        }

        [Test]
        public void ReusedSlot_OldHandleStaysInvalid()
        {
            var table = new VoiceTable(1);
            table.TryAllocate(NewDef(), 1f, out var slot, out _);
            var oldGen = table.Slots[slot].Generation;
            table.Release(slot);
            table.TryAllocate(NewDef(), 1f, out var slot2, out _);
            Assert.That(slot2, Is.EqualTo(slot));
            Assert.IsFalse(table.IsValid(slot, oldGen));
            Assert.IsTrue(table.IsValid(slot2, table.Slots[slot2].Generation));
        }
    }
}
```

- [ ] **Step 2: 컴파일 실패 확인**

- [ ] **Step 3: 구현**

`VoiceSlot.cs`:

```csharp
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Bun3.Unity.Audio
{
    /// <summary>Lifecycle state of a voice slot.</summary>
    internal enum VoiceState : byte
    {
        Idle,
        FadingIn,
        Playing,
        FadingOut,
    }

    /// <summary>
    /// Per-voice state driven entirely by <see cref="VoiceTable.Tick"/> — no coroutines.
    /// The struct never touches audio APIs; <c>SoundSystem</c> mirrors it onto an AudioSource.
    /// </summary>
    internal struct VoiceSlot
    {
        public uint Generation;
        public VoiceState State;
        public SoundDef Def;
        public float Elapsed;
        public float ClipLength;
        public bool Loop;
        public float FadeElapsed;
        public float FadeDuration;
        public float FadeFrom;
        public float FadeTo;
        public float FadeFactor;
        public float BaseVolume;
        public float VolumeScale;
        public float Pitch;
        public float StartTime;
        public Transform Follow;
        public AutoResetUniTaskCompletionSource Completion;
    }
}
```

`VoiceTable.cs` (할당·해제만 — Tick/스틸/쿨다운 본문은 Task 4·5에서 채움):

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace Bun3.Unity.Audio
{
    /// <summary>
    /// Pure voice-slot state machine: allocation, stealing, cooldowns, fades, completion.
    /// Holds no AudioSource references so EditMode tests can drive it with injected delta time.
    /// </summary>
    internal sealed class VoiceTable
    {
        public readonly VoiceSlot[] Slots;

        private readonly Dictionary<SoundDef, float> _lastPlayTime = new();
        private float _time;

        public VoiceTable(int capacity)
        {
            Slots = new VoiceSlot[capacity];
            for (var i = 0; i < Slots.Length; i++)
            {
                Slots[i].VolumeScale = 1f;
                Slots[i].FadeFactor = 1f;
            }
        }

        /// <summary>
        /// Reserves a slot for <paramref name="def"/>. Returns false when blocked by cooldown
        /// (or zero capacity). <paramref name="stolenSlot"/> is the slot whose previous voice
        /// was cut short (-1 if none) so the caller can complete its awaiter.
        /// </summary>
        public bool TryAllocate(SoundDef def, float clipLength, out int slotIndex, out int stolenSlot)
        {
            stolenSlot = -1;
            slotIndex = -1;
            if (Slots.Length == 0)
            {
                return false;
            }
            if (def.Cooldown > 0f
                && _lastPlayTime.TryGetValue(def, out var last)
                && _time - last < def.Cooldown)
            {
                return false;
            }

            slotIndex = FindSlot(def, ref stolenSlot);
            ref var slot = ref Slots[slotIndex];
            slot.Generation++;
            slot.State = VoiceState.Playing;
            slot.Def = def;
            slot.Elapsed = 0f;
            slot.ClipLength = clipLength;
            slot.Loop = def.Loop;
            slot.FadeElapsed = 0f;
            slot.FadeDuration = 0f;
            slot.FadeFactor = 1f;
            slot.BaseVolume = def.Volume.Roll();
            slot.VolumeScale = 1f;
            slot.Pitch = def.Pitch.Roll();
            slot.StartTime = _time;
            slot.Follow = null;
            slot.Completion = null;
            _lastPlayTime[def] = _time;
            return true;
        }

        /// <summary>True when the slot is active and its generation matches.</summary>
        public bool IsValid(int slot, uint generation)
            => Slots[slot].State != VoiceState.Idle && Slots[slot].Generation == generation;

        /// <summary>Frees the slot and invalidates all outstanding handles to it.</summary>
        public void Release(int slot)
        {
            ref var s = ref Slots[slot];
            s.State = VoiceState.Idle;
            s.Generation++;
            s.Def = null;
            s.Follow = null;
            s.Completion = null;
        }

        /// <summary>Effective playback volume for the slot (base × handle scale × fade).</summary>
        public float CurrentVolume(int slot)
        {
            ref var s = ref Slots[slot];
            return s.BaseVolume * s.VolumeScale * s.FadeFactor;
        }

        private int FindSlot(SoundDef def, ref int stolenSlot)
        {
            // Task 4 replaces this body with maxInstances + global-oldest stealing.
            for (var i = 0; i < Slots.Length; i++)
            {
                if (Slots[i].State == VoiceState.Idle)
                {
                    return i;
                }
            }
            stolenSlot = 0;
            return 0;
        }
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

- [ ] **Step 5: 커밋** — `✨ VoiceTable allocation and generation validation`

---

### Task 4: VoiceTable — 보이스 스틸링 + 쿨다운

**Files:**
- Modify: `unity/Packages/com.bun3.unity.audio/Runtime/VoiceTable.cs` (`FindSlot` 본문 교체)
- Test: `unity/Packages/com.bun3.unity.audio/Tests/Runtime/VoiceTableStealTests.cs`

**Interfaces:**
- Consumes: Task 3의 `VoiceTable`/`VoiceSlot`
- Produces: 확정 스틸 규칙 — ① def의 활성 보이스 수 ≥ `MaxInstances`(>0)면 같은 def 최고참 스틸, ② 아니면 Idle 선형 스캔, ③ 없으면 전역 최고참 스틸. 쿨다운 차단 시 `TryAllocate` false.
- 참고: `Tick`이 아직 없으므로 이 태스크의 시간 전진은 internal `AdvanceTime(float)` 헬퍼로 (Task 5에서 `Tick`이 `_time` 전진을 흡수하면 삭제).

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
using Bun3.Unity.Audio;
using NUnit.Framework;
using UnityEngine;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class VoiceTableStealTests
    {
        private static SoundDef NewDef(int maxInstances = 0, float cooldown = 0f)
        {
            var def = ScriptableObject.CreateInstance<SoundDef>();
            def.MaxInstances = maxInstances;
            def.Cooldown = cooldown;
            return def;
        }

        [Test]
        public void MaxInstances_StealsOldestOfSameDef()
        {
            var table = new VoiceTable(8);
            var def = NewDef(maxInstances: 2);
            table.TryAllocate(def, 1f, out var first, out _);
            table.AdvanceTime(0.1f);
            table.TryAllocate(def, 1f, out _, out _);
            table.AdvanceTime(0.1f);
            Assert.IsTrue(table.TryAllocate(def, 1f, out var third, out var stolen));
            Assert.That(stolen, Is.EqualTo(first));
            Assert.That(third, Is.EqualTo(first));
        }

        [Test]
        public void FullTable_StealsGlobalOldest()
        {
            var table = new VoiceTable(2);
            var defA = NewDef();
            var defB = NewDef();
            table.TryAllocate(defA, 1f, out var oldest, out _);
            table.AdvanceTime(0.1f);
            table.TryAllocate(defA, 1f, out _, out _);
            table.AdvanceTime(0.1f);
            Assert.IsTrue(table.TryAllocate(defB, 1f, out var slot, out var stolen));
            Assert.That(stolen, Is.EqualTo(oldest));
            Assert.That(slot, Is.EqualTo(oldest));
        }

        [Test]
        public void Cooldown_BlocksRetrigger_ThenAllows()
        {
            var table = new VoiceTable(4);
            var def = NewDef(cooldown: 0.5f);
            Assert.IsTrue(table.TryAllocate(def, 1f, out _, out _));
            Assert.IsFalse(table.TryAllocate(def, 1f, out _, out _));
            table.AdvanceTime(0.6f);
            Assert.IsTrue(table.TryAllocate(def, 1f, out _, out _));
        }
    }
}
```

- [ ] **Step 2: 컴파일/테스트 실패 확인** (`AdvanceTime` 미정의, 스틸 규칙 미구현)

- [ ] **Step 3: 구현** — `FindSlot` 교체 + `AdvanceTime` 추가

```csharp
        /// <summary>Test hook: advances internal time without ticking voices.</summary>
        internal void AdvanceTime(float seconds) => _time += seconds;

        private int FindSlot(SoundDef def, ref int stolenSlot)
        {
            if (def.MaxInstances > 0)
            {
                var count = 0;
                var oldestOfDef = -1;
                var oldestTime = float.MaxValue;
                for (var i = 0; i < Slots.Length; i++)
                {
                    ref var s = ref Slots[i];
                    if (s.State == VoiceState.Idle || !ReferenceEquals(s.Def, def))
                    {
                        continue;
                    }
                    count++;
                    if (s.StartTime < oldestTime)
                    {
                        oldestTime = s.StartTime;
                        oldestOfDef = i;
                    }
                }
                if (count >= def.MaxInstances)
                {
                    stolenSlot = oldestOfDef;
                    return oldestOfDef;
                }
            }

            var globalOldest = 0;
            var globalOldestTime = float.MaxValue;
            for (var i = 0; i < Slots.Length; i++)
            {
                ref var s = ref Slots[i];
                if (s.State == VoiceState.Idle)
                {
                    return i;
                }
                if (s.StartTime < globalOldestTime)
                {
                    globalOldestTime = s.StartTime;
                    globalOldest = i;
                }
            }
            stolenSlot = globalOldest;
            return globalOldest;
        }
```

- [ ] **Step 4: 테스트 통과 확인** (Task 3 테스트 포함 전체 그린)

- [ ] **Step 5: 커밋** — `✨ Voice stealing (per-def cap, global oldest) + cooldown`

---

### Task 5: VoiceTable — fade·완료 틱

**Files:**
- Modify: `unity/Packages/com.bun3.unity.audio/Runtime/VoiceTable.cs`
- Test: `unity/Packages/com.bun3.unity.audio/Tests/Runtime/VoiceTableTickTests.cs`

**Interfaces:**
- Consumes: Task 3·4의 `VoiceTable`
- Produces (Task 6·7이 소비):
  - `void Tick(float dt, List<int> completed)` — 시간 전진 + fade 보간 + 경과 시간 완료 감지. 끝난 슬롯 index를 `completed`에 추가하고 Release.
  - `void BeginFadeIn(int slot, float duration)` / `void BeginFadeOut(int slot, float duration)` — 현재 `FadeFactor`에서 1/0으로. duration ≤ 0이면 즉시(fade-out은 즉시 Release가 아니라 다음 Tick에서 완료 — 완료 통지 경로 단일화).

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
using System.Collections.Generic;
using Bun3.Unity.Audio;
using NUnit.Framework;
using UnityEngine;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class VoiceTableTickTests
    {
        private static SoundDef NewDef(bool loop = false)
        {
            var def = ScriptableObject.CreateInstance<SoundDef>();
            def.Loop = loop;
            return def;
        }

        private readonly List<int> _completed = new();

        [SetUp]
        public void SetUp() => _completed.Clear();

        [Test]
        public void Tick_CompletesVoiceAfterClipLength()
        {
            var table = new VoiceTable(2);
            table.TryAllocate(NewDef(), clipLength: 0.5f, out var slot, out _);
            table.Tick(0.4f, _completed);
            Assert.IsEmpty(_completed);
            table.Tick(0.2f, _completed);
            Assert.That(_completed, Is.EqualTo(new[] { slot }));
            Assert.That(table.Slots[slot].State, Is.EqualTo(VoiceState.Idle));
        }

        [Test]
        public void Tick_LoopingVoiceNeverCompletes()
        {
            var table = new VoiceTable(2);
            table.TryAllocate(NewDef(loop: true), 0.5f, out _, out _);
            table.Tick(10f, _completed);
            Assert.IsEmpty(_completed);
        }

        [Test]
        public void FadeIn_RampsFactorFromZeroToOne()
        {
            var table = new VoiceTable(2);
            table.TryAllocate(NewDef(loop: true), 1f, out var slot, out _);
            table.BeginFadeIn(slot, 1f);
            Assert.That(table.Slots[slot].FadeFactor, Is.EqualTo(0f));
            table.Tick(0.5f, _completed);
            Assert.That(table.Slots[slot].FadeFactor, Is.EqualTo(0.5f).Within(0.001f));
            table.Tick(0.5f, _completed);
            Assert.That(table.Slots[slot].FadeFactor, Is.EqualTo(1f));
            Assert.That(table.Slots[slot].State, Is.EqualTo(VoiceState.Playing));
        }

        [Test]
        public void FadeOut_CompletesAndReleases()
        {
            var table = new VoiceTable(2);
            table.TryAllocate(NewDef(loop: true), 1f, out var slot, out _);
            table.BeginFadeOut(slot, 0.5f);
            table.Tick(0.25f, _completed);
            Assert.That(table.Slots[slot].FadeFactor, Is.EqualTo(0.5f).Within(0.001f));
            table.Tick(0.25f, _completed);
            Assert.That(_completed, Is.EqualTo(new[] { slot }));
            Assert.That(table.Slots[slot].State, Is.EqualTo(VoiceState.Idle));
        }

        [Test]
        public void FadeOut_DuringFadeIn_StartsFromCurrentFactor()
        {
            var table = new VoiceTable(2);
            table.TryAllocate(NewDef(loop: true), 1f, out var slot, out _);
            table.BeginFadeIn(slot, 1f);
            table.Tick(0.5f, _completed);
            table.BeginFadeOut(slot, 1f);
            Assert.That(table.Slots[slot].FadeFrom, Is.EqualTo(0.5f).Within(0.001f));
        }
    }
}
```

- [ ] **Step 2: 컴파일 실패 확인**

- [ ] **Step 3: 구현** — `VoiceTable`에 추가, `AdvanceTime` 삭제하고 테스트의 시간 전진을 `Tick(dt, scratch)`로 대체(Task 4 테스트 수정 포함)

```csharp
        /// <summary>Starts a fade from the current factor to full volume.</summary>
        public void BeginFadeIn(int slot, float duration)
        {
            ref var s = ref Slots[slot];
            if (duration <= 0f)
            {
                s.FadeDuration = 0f;
                s.FadeFactor = 1f;
                s.State = VoiceState.Playing;
                return;
            }
            s.FadeFrom = 0f;
            s.FadeTo = 1f;
            s.FadeFactor = 0f;
            s.FadeElapsed = 0f;
            s.FadeDuration = duration;
            s.State = VoiceState.FadingIn;
        }

        /// <summary>
        /// Starts a fade from the current factor to silence; the voice completes and is
        /// released on the tick the fade finishes (immediately next tick when duration ≤ 0).
        /// </summary>
        public void BeginFadeOut(int slot, float duration)
        {
            ref var s = ref Slots[slot];
            s.FadeFrom = s.FadeFactor;
            s.FadeTo = 0f;
            s.FadeElapsed = 0f;
            s.FadeDuration = Mathf.Max(duration, float.Epsilon);
            s.State = VoiceState.FadingOut;
        }

        /// <summary>
        /// Advances all active voices: fade interpolation, elapsed-time completion
        /// (never AudioSource.isPlaying — pause would misread). Completed slot indices are
        /// appended to <paramref name="completed"/> after being released.
        /// </summary>
        public void Tick(float dt, List<int> completed)
        {
            _time += dt;
            for (var i = 0; i < Slots.Length; i++)
            {
                ref var s = ref Slots[i];
                if (s.State == VoiceState.Idle)
                {
                    continue;
                }

                s.Elapsed += dt;

                if (s.FadeDuration > 0f)
                {
                    s.FadeElapsed += dt;
                    var t = Mathf.Clamp01(s.FadeElapsed / s.FadeDuration);
                    s.FadeFactor = Mathf.Lerp(s.FadeFrom, s.FadeTo, t);
                    if (t >= 1f)
                    {
                        s.FadeDuration = 0f;
                        if (s.State == VoiceState.FadingOut)
                        {
                            Release(i);
                            completed.Add(i);
                            continue;
                        }
                        s.State = VoiceState.Playing;
                    }
                }

                if (!s.Loop && s.Elapsed >= s.ClipLength)
                {
                    Release(i);
                    completed.Add(i);
                }
            }
        }
```

주의: `Release`가 `Completion`을 null로 지우므로, 완료 통지가 필요한 호출자(Task 7)는 `completed` 처리 시점에 이미 사라진 참조를 잃지 않도록 `Release` 전에 꺼내야 한다 → `Release`에서 `Completion` 정리는 유지하되, `Tick`은 Release 직전에 로컬로 꺼내 `TrySetResult`용으로 보관할 수 있게 **`completed`에 추가되는 시점에 Completion을 함께 넘길 필요가 있다**. 구현 단순화: `Tick`의 시그니처를 `Tick(float dt, List<(int slot, AutoResetUniTaskCompletionSource completion)> completed)`로 하고 Release 직전 값을 캡처해 담는다(테스트는 slot만 검사하도록 `completed.Select` 없이 `completed[i].slot` 비교로 작성). ValueTuple 리스트는 무할당.

- [ ] **Step 4: 전체 테스트 그린 확인**

- [ ] **Step 5: 커밋** — `✨ Coroutine-free fades + elapsed-time completion in VoiceTable`

---

### Task 6: SoundSystem + SoundHandle (AudioSource 반영층)

**Files:**
- Create: `unity/Packages/com.bun3.unity.audio/Runtime/SoundSystemConfig.cs`
- Create: `unity/Packages/com.bun3.unity.audio/Runtime/SoundSystem.cs`
- Create: `unity/Packages/com.bun3.unity.audio/Runtime/SoundSystem.Tick.cs`
- Create: `unity/Packages/com.bun3.unity.audio/Runtime/SoundHandle.cs`
- Test: `unity/Packages/com.bun3.unity.audio/Tests/Runtime/SoundSystemPlayModeTests.cs`

**Interfaces:**
- Consumes: `VoiceTable`(Task 3~5), `PlayerLoopSystemHelper`(`Bun3.Unity.Core.PlayerLoop` — `InsertSystemAfter(typeof(TickMarker), TickAll, typeof(UnityEngine.PlayerLoop.Update.ScriptRunBehaviourUpdate))`, `TryRemoveSystem`)
- Produces (게임 코드 + Task 7~9가 소비):
  - `sealed class SoundSystemConfig { AudioMixer Mixer; AudioMixerGroup SfxGroup; int SfxVoices = 24; }`
  - `sealed class SoundSystem : IDisposable` — `SoundHandle Play(SoundDef)`, `Play(SoundDef, Vector3)`, `Play(SoundDef, Transform)`, `void Stop(SoundHandle, float fadeOut = 0f)`, internal `void Tick(float dt)`, internal `VoiceTable Table`, internal `bool TryGetSlot(SoundHandle, out int)`
  - `readonly struct SoundHandle` — `IsValid`, `IsPlaying`, `Stop(float fadeOut = 0f)`, `SetVolume(float)`, `SetPitch(float)`, `SetPosition(Vector3)`, `Follow(Transform)`, `static SoundHandle.Invalid`

- [ ] **Step 1: 실패하는 PlayMode 테스트 작성**

```csharp
using System.Collections;
using Bun3.Unity.Audio;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class SoundSystemPlayModeTests
    {
        private static SoundDef ShortClipDef()
        {
            var def = ScriptableObject.CreateInstance<SoundDef>();
            def.Clips = new[] { AudioClip.Create("test-clip", 4410, 1, 44100, false) }; // 0.1 s
            return def;
        }

        [UnityTest]
        public IEnumerator Play_CompletesAndHandleGoesInvalid()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 4 });
            var handle = sys.Play(ShortClipDef());
            Assert.IsTrue(handle.IsValid);
            Assert.IsTrue(handle.IsPlaying);
            yield return new WaitForSeconds(0.3f);
            Assert.IsFalse(handle.IsPlaying);
            Assert.IsFalse(handle.IsValid);
        }

        [UnityTest]
        public IEnumerator StaleHandle_AllCallsAreNoOps()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 1 });
            var first = sys.Play(ShortClipDef());
            var second = sys.Play(ShortClipDef()); // steals slot 0
            Assert.IsFalse(first.IsValid);
            first.Stop();          // must not throw, must not affect `second`
            first.SetVolume(0f);
            Assert.IsTrue(second.IsPlaying);
            yield break;
        }

        [UnityTest]
        public IEnumerator Dispose_DestroysPoolAndUnregistersTick()
        {
            var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            var handle = sys.Play(ShortClipDef());
            sys.Dispose();
            Assert.IsFalse(handle.IsValid);
            Assert.That(Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None), Is.Empty);
            yield break;
        }
    }
}
```

- [ ] **Step 2: 컴파일 실패 확인**

- [ ] **Step 3: 구현**

`SoundSystemConfig.cs`:

```csharp
using UnityEngine.Audio;

namespace Bun3.Unity.Audio
{
    /// <summary>Construction-time settings for <see cref="SoundSystem"/>. Validated once; not live-tunable.</summary>
    public sealed class SoundSystemConfig
    {
        /// <summary>Mixer used for channel volumes; null skips mixer integration until the bundled asset ships.</summary>
        public AudioMixer Mixer;

        /// <summary>Fallback group for defs without an explicit MixerGroup.</summary>
        public AudioMixerGroup SfxGroup;

        /// <summary>Number of prewarmed SFX voices. Fixed for the system's lifetime.</summary>
        public int SfxVoices = 24;
    }
}
```

`SoundSystem.cs` (본체 — 생성·재생·해제):

```csharp
using System;
using System.Collections.Generic;
using Bun3.Unity.Core.PlayerLoop;
using UnityEngine;

namespace Bun3.Unity.Audio
{
    /// <summary>
    /// Instance sound service: a prewarmed AudioSource pool driven by a single
    /// player-loop tick. No MonoBehaviours, no coroutines, no per-play allocation.
    /// Partial layout: this file owns construction/playback/disposal;
    /// SoundSystem.Tick.cs owns the per-frame mirror of <see cref="VoiceTable"/> state.
    /// </summary>
    public sealed partial class SoundSystem : IDisposable
    {
        private static readonly List<SoundSystem> Live = new();

        private struct TickMarker
        {
        }

        internal readonly VoiceTable Table;
        private readonly AudioSource[] _sources;
        private readonly List<(int Slot, Cysharp.Threading.Tasks.AutoResetUniTaskCompletionSource Completion)> _completedScratch = new();
        private readonly SoundSystemConfig _config;
        private GameObject _root;
        private bool _disposed;

        /// <summary>Creates the pool and registers the tick. Dispose to tear both down.</summary>
        public SoundSystem(SoundSystemConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }
            if (config.SfxVoices <= 0)
            {
                throw new ArgumentException("SfxVoices must be positive.", nameof(config));
            }

            _config = config;
            Table = new VoiceTable(config.SfxVoices);
            _sources = new AudioSource[config.SfxVoices];
            _root = new GameObject("Bun3.SoundSystem");
            if (Application.isPlaying)
            {
                UnityEngine.Object.DontDestroyOnLoad(_root);
            }
            for (var i = 0; i < _sources.Length; i++)
            {
                var go = new GameObject("Voice");
                go.transform.SetParent(_root.transform, false);
                _sources[i] = go.AddComponent<AudioSource>();
                _sources[i].playOnAwake = false;
            }

            if (Live.Count == 0)
            {
                PlayerLoopSystemHelper.InsertSystemAfter(
                    typeof(TickMarker), TickAll,
                    typeof(UnityEngine.PlayerLoop.Update.ScriptRunBehaviourUpdate));
            }
            Live.Add(this);
        }

        /// <summary>Plays a 2D (or def-default) sound. Returns an invalid handle when blocked.</summary>
        public SoundHandle Play(SoundDef def) => PlayCore(def, Vector3.zero, null);

        /// <summary>Plays at a fixed world position (def should use SpatialMode.Positional).</summary>
        public SoundHandle Play(SoundDef def, Vector3 position) => PlayCore(def, position, null);

        /// <summary>Plays tracking a transform every frame (def should use SpatialMode.Follow).</summary>
        public SoundHandle Play(SoundDef def, Transform follow) => PlayCore(def, follow != null ? follow.position : Vector3.zero, follow);

        /// <summary>Stops the voice, optionally fading out first. No-op for stale handles.</summary>
        public void Stop(SoundHandle handle, float fadeOut = 0f)
        {
            if (!TryGetSlot(handle, out var slot))
            {
                return;
            }
            Table.BeginFadeOut(slot, fadeOut);
        }

        internal bool TryGetSlot(SoundHandle handle, out int slot)
        {
            slot = handle.SlotIndex;
            return !_disposed && handle.Owner == this && Table.IsValid(slot, handle.Generation);
        }

        private SoundHandle PlayCore(SoundDef def, Vector3 position, Transform follow)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(SoundSystem));
            }
            if (def == null || def.Clips == null || def.Clips.Length == 0)
            {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                Debug.LogWarning("SoundSystem.Play: def has no clips; returning an invalid handle.");
#endif
                return SoundHandle.Invalid;
            }

            var clip = PickClip(def);
            if (!Table.TryAllocate(def, clip.length, out var slot, out var stolen))
            {
                return SoundHandle.Invalid;
            }
            if (stolen >= 0)
            {
                _sources[stolen].Stop();
            }

            ref var voice = ref Table.Slots[slot];
            voice.Follow = follow;

            var source = _sources[slot];
            source.clip = clip;
            source.loop = def.Loop;
            source.pitch = voice.Pitch;
            source.volume = Table.CurrentVolume(slot);
            source.outputAudioMixerGroup = def.MixerGroup != null ? def.MixerGroup : _config.SfxGroup;
            source.spatialBlend = def.Spatial == SpatialMode.None ? 0f : 1f;
            source.minDistance = def.MinDistance;
            source.maxDistance = def.MaxDistance;
            source.transform.position = position;
            source.Play();

            return new SoundHandle(this, slot, voice.Generation);
        }

        private static AudioClip PickClip(SoundDef def)
        {
            var clips = def.Clips;
            if (clips.Length == 1)
            {
                return clips[0];
            }
            int index;
            do
            {
                index = UnityEngine.Random.Range(0, clips.Length);
            }
            while (index == def.LastClipIndex);
            def.LastClipIndex = index;
            return clips[index];
        }

        /// <summary>Stops all voices, destroys the pool, and unregisters the tick when last alive.</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            for (var i = 0; i < Table.Slots.Length; i++)
            {
                if (Table.Slots[i].State != VoiceState.Idle)
                {
                    Table.Release(i);
                }
            }
            Live.Remove(this);
            if (Live.Count == 0)
            {
                PlayerLoopSystemHelper.TryRemoveSystem(typeof(TickMarker));
            }
            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
                _root = null;
            }
        }
    }
}
```

`SoundSystem.Tick.cs`:

```csharp
// Per-frame mirror: advances VoiceTable and applies volume/position/stop to AudioSources.
using UnityEngine;

namespace Bun3.Unity.Audio
{
    public sealed partial class SoundSystem
    {
        private static void TickAll()
        {
            for (var i = 0; i < Live.Count; i++)
            {
                Live[i].Tick(Time.deltaTime);
            }
        }

        internal void Tick(float dt)
        {
            _completedScratch.Clear();
            Table.Tick(dt, _completedScratch);

            for (var i = 0; i < _completedScratch.Count; i++)
            {
                var (slot, completion) = _completedScratch[i];
                _sources[slot].Stop();
                completion?.TrySetResult();
            }

            for (var i = 0; i < Table.Slots.Length; i++)
            {
                ref var voice = ref Table.Slots[i];
                if (voice.State == VoiceState.Idle)
                {
                    continue;
                }
                _sources[i].volume = Table.CurrentVolume(i);
                if (voice.Follow != null)
                {
                    _sources[i].transform.position = voice.Follow.position;
                }
            }
        }
    }
}
```

`SoundHandle.cs`:

```csharp
using UnityEngine;

namespace Bun3.Unity.Audio
{
    /// <summary>
    /// Generation-validated reference to a playing voice. Safe to keep after the voice
    /// ends or its slot is reused: every member silently no-ops (or reports false) on a
    /// stale handle — playing sound is fire-and-forget, staleness is not an error.
    /// </summary>
    public readonly struct SoundHandle
    {
        internal readonly SoundSystem Owner;
        internal readonly int SlotIndex;
        internal readonly uint Generation;

        internal SoundHandle(SoundSystem owner, int slot, uint generation)
        {
            Owner = owner;
            SlotIndex = slot;
            Generation = generation;
        }

        /// <summary>A handle that never refers to a voice.</summary>
        public static SoundHandle Invalid => default;

        /// <summary>True while this handle still refers to its original voice.</summary>
        public bool IsValid => Owner != null && Owner.TryGetSlot(this, out _);

        /// <summary>True while the voice is audible (fading counts as playing).</summary>
        public bool IsPlaying => IsValid;

        /// <summary>Stops the voice, optionally fading out over <paramref name="fadeOut"/> seconds.</summary>
        public void Stop(float fadeOut = 0f) => Owner?.Stop(this, fadeOut);

        /// <summary>Scales the voice's rolled base volume (1 = as rolled).</summary>
        public void SetVolume(float volume)
        {
            if (Owner != null && Owner.TryGetSlot(this, out var slot))
            {
                Owner.Table.Slots[slot].VolumeScale = volume;
            }
        }

        /// <summary>Overrides the voice's rolled pitch.</summary>
        public void SetPitch(float pitch)
        {
            if (Owner != null && Owner.TryGetSlot(this, out var slot))
            {
                Owner.Table.Slots[slot].Pitch = pitch;
                Owner.SetSourcePitch(slot, pitch);
            }
        }

        /// <summary>Moves the voice to a fixed world position and stops following.</summary>
        public void SetPosition(Vector3 position)
        {
            if (Owner != null && Owner.TryGetSlot(this, out var slot))
            {
                Owner.Table.Slots[slot].Follow = null;
                Owner.SetSourcePosition(slot, position);
            }
        }

        /// <summary>Makes the voice track <paramref name="target"/> every frame.</summary>
        public void Follow(Transform target)
        {
            if (Owner != null && Owner.TryGetSlot(this, out var slot))
            {
                Owner.Table.Slots[slot].Follow = target;
            }
        }
    }
}
```

`SoundSystem`에 internal 헬퍼 2개 추가(`SoundSystem.cs`):

```csharp
        internal void SetSourcePitch(int slot, float pitch) => _sources[slot].pitch = pitch;

        internal void SetSourcePosition(int slot, Vector3 position) => _sources[slot].transform.position = position;
```

- [ ] **Step 4: PlayMode 테스트 통과 확인** (Test Runner PlayMode 탭)

- [ ] **Step 5: 커밋** — `✨ SoundSystem pool + generation-validated SoundHandle`

---

### Task 7: UniTask 경로

**Files:**
- Create: `unity/Packages/com.bun3.unity.audio/Runtime/SoundSystem.Async.cs`
- Test: `unity/Packages/com.bun3.unity.audio/Tests/Runtime/SoundSystemAsyncTests.cs`

**Interfaces:**
- Consumes: `SoundSystem`/`SoundHandle`(Task 6), `VoiceSlot.Completion`(Task 3), UniTask `AutoResetUniTaskCompletionSource`
- Produces:
  - `UniTask SoundSystem.PlayAsync(SoundDef, CancellationToken)` (+ Vector3/Transform 오버로드) — ct 취소 = 사운드 정지 + await 취소
  - `UniTask SoundHandle.WaitAsync(CancellationToken)` — 완료·스틸·Stop 전부 정상 완료로 신호. 무효 핸들은 즉시 완료
  - `UniTask SoundHandle.StopAsync(float fadeOut, CancellationToken)` — fade-out 종료 시점 완료

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
using System.Collections;
using Bun3.Unity.Audio;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class SoundSystemAsyncTests
    {
        private static SoundDef ShortClipDef()
        {
            var def = ScriptableObject.CreateInstance<SoundDef>();
            def.Clips = new[] { AudioClip.Create("test-clip", 4410, 1, 44100, false) }; // 0.1 s
            return def;
        }

        [UnityTest]
        public IEnumerator PlayAsync_CompletesWhenClipEnds() => UniTask.ToCoroutine(async () =>
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 4 });
            var started = Time.time;
            await sys.PlayAsync(ShortClipDef());
            Assert.That(Time.time - started, Is.GreaterThanOrEqualTo(0.09f));
        });

        [UnityTest]
        public IEnumerator WaitAsync_OnInvalidHandle_CompletesImmediately() => UniTask.ToCoroutine(async () =>
        {
            await SoundHandle.Invalid.WaitAsync();
        });

        [UnityTest]
        public IEnumerator WaitAsync_CompletesOnSteal() => UniTask.ToCoroutine(async () =>
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 1 });
            var first = sys.Play(ShortClipDef());
            var waiting = first.WaitAsync();
            sys.Play(ShortClipDef()); // steals the only slot
            await waiting;            // must complete, not hang
        });
    }
}
```

- [ ] **Step 2: 컴파일 실패 확인**

- [ ] **Step 3: 구현** — `SoundSystem.Async.cs`

```csharp
// UniTask entry points: awaitable play/stop built on VoiceSlot.Completion sources.
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Bun3.Unity.Audio
{
    public sealed partial class SoundSystem
    {
        /// <summary>Plays and completes when the voice ends. Cancelling stops the voice.</summary>
        public UniTask PlayAsync(SoundDef def, CancellationToken ct = default)
            => WaitInternal(Play(def), ct);

        /// <summary>Positional variant of <see cref="PlayAsync(SoundDef, CancellationToken)"/>.</summary>
        public UniTask PlayAsync(SoundDef def, Vector3 position, CancellationToken ct = default)
            => WaitInternal(Play(def, position), ct);

        /// <summary>Following variant of <see cref="PlayAsync(SoundDef, CancellationToken)"/>.</summary>
        public UniTask PlayAsync(SoundDef def, Transform follow, CancellationToken ct = default)
            => WaitInternal(Play(def, follow), ct);

        internal UniTask WaitInternal(SoundHandle handle, CancellationToken ct)
        {
            if (!TryGetSlot(handle, out var slot))
            {
                return UniTask.CompletedTask;
            }
            ref var voice = ref Table.Slots[slot];
            voice.Completion ??= AutoResetUniTaskCompletionSource.Create();
            var task = voice.Completion.Task;
            return ct.CanBeCanceled ? WithCancellation(task, handle, ct) : task;
        }

        private async UniTask WithCancellation(UniTask task, SoundHandle handle, CancellationToken ct)
        {
            using var registration = ct.Register(
                static state => ((SoundHandleBox)state).Stop(), SoundHandleBox.Rent(handle));
            await task;
            ct.ThrowIfCancellationRequested();
        }
    }
}
```

주의(구현 시 확정할 디테일): `ct.Register`의 state 박싱을 피하려면 취소 경로는 저빈도라 박싱 1회를 허용하거나(`ponytail: cancellation is cold path`), UniTask의 `WithCancellation` 확장 + 취소 시 `handle.Stop()`을 호출하는 후처리로 단순화한다. **취소는 콜드 패스 — 여기서만 할당 허용을 XML 문서에 명시**하고 `SoundHandleBox` 같은 커스텀 풀은 만들지 않는다(YAGNI). 가장 단순한 형태:

```csharp
        private static async UniTask WithCancellation(UniTask task, SoundHandle handle, CancellationToken ct)
        {
            try
            {
                await task.AttachExternalCancellation(ct);
            }
            catch (System.OperationCanceledException)
            {
                handle.Stop();
                throw;
            }
        }
```

`SoundHandle` 확장(`SoundHandle.cs`에 추가):

```csharp
        /// <summary>Completes when the voice ends (natural end, steal, or Stop — all count as done).</summary>
        public UniTask WaitAsync(System.Threading.CancellationToken ct = default)
            => Owner == null ? UniTask.CompletedTask : Owner.WaitInternal(this, ct);

        /// <summary>Begins a fade-out and completes when it finishes.</summary>
        public UniTask StopAsync(float fadeOut, System.Threading.CancellationToken ct = default)
        {
            if (Owner == null)
            {
                return UniTask.CompletedTask;
            }
            var wait = Owner.WaitInternal(this, ct);
            Owner.Stop(this, fadeOut);
            return wait;
        }
```

그리고 Task 5 주의사항의 `Tick` 완료 처리에서 `completion?.TrySetResult()`가 이미 스틸·자연 종료를 커버한다. **스틸 경로 보강**: `PlayCore`에서 `stolen >= 0`일 때 `Table.Slots[stolen].Completion`은 `TryAllocate`가 이미 슬롯을 덮었으므로 — `TryAllocate` 내부에서 스틸 슬롯의 Completion을 지우기 전에 밖으로 전달해야 한다. `TryAllocate`의 `out int stolenSlot`을 `out AutoResetUniTaskCompletionSource stolenCompletion`과 함께 내보내도록 수정하고(`VoiceTable` 시그니처 변경 + Task 4 테스트의 out 파라미터 수정), `PlayCore`에서 `stolenCompletion?.TrySetResult()` 호출.

- [ ] **Step 4: 전체 테스트 그린 확인**

- [ ] **Step 5: 커밋** — `✨ UniTask play/wait/stop paths`

---

### Task 8: 채널 볼륨

**Files:**
- Create: `unity/Packages/com.bun3.unity.audio/Runtime/SoundChannel.cs`
- Modify: `unity/Packages/com.bun3.unity.audio/Runtime/SoundSystem.cs` (`SetChannelVolume`/`GetChannelVolume`)
- Test: `unity/Packages/com.bun3.unity.audio/Tests/Runtime/ChannelVolumeTests.cs`

**Interfaces:**
- Consumes: `SoundSystemConfig.Mixer`(Task 6)
- Produces:
  - `enum SoundChannel { Master, Music, Sfx, Voice }`
  - 노출 파라미터 이름 상수: `"MasterVolume"`, `"MusicVolume"`, `"SfxVolume"`, `"VoiceVolume"`(슬라이스 3의 동봉 믹서가 이 이름으로 노출)
  - `void SoundSystem.SetChannelVolume(SoundChannel, float linear)` — 믹서 `SetFloat`(선형→dB), Mixer null이면 개발 빌드 경고 후 무시
  - `float SoundSystem.GetChannelVolume(SoundChannel)` — 미설정/Mixer null 시 1
  - `internal static float AudioMath.LinearToDb(float)` / `DbToLinear(float)` — 0 이하 → -80dB 바닥

- [ ] **Step 1: 실패하는 테스트 작성** (순수 변환 함수 — EditMode 가능)

```csharp
using Bun3.Unity.Audio;
using NUnit.Framework;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class ChannelVolumeTests
    {
        [Test]
        public void LinearToDb_FullVolumeIsZeroDb()
            => Assert.That(AudioMath.LinearToDb(1f), Is.EqualTo(0f).Within(0.001f));

        [Test]
        public void LinearToDb_ZeroClampsToFloor()
            => Assert.That(AudioMath.LinearToDb(0f), Is.EqualTo(-80f));

        [Test]
        public void RoundTrip_PreservesValue()
            => Assert.That(AudioMath.DbToLinear(AudioMath.LinearToDb(0.5f)), Is.EqualTo(0.5f).Within(0.001f));
    }
}
```

- [ ] **Step 2: 컴파일 실패 확인**

- [ ] **Step 3: 구현**

`SoundChannel.cs` (`AudioMath`는 밀접한 형제 — 같은 파일):

```csharp
using UnityEngine;

namespace Bun3.Unity.Audio
{
    /// <summary>Logical volume channels mapped to exposed mixer parameters.</summary>
    public enum SoundChannel
    {
        /// <summary>Exposed parameter "MasterVolume".</summary>
        Master,

        /// <summary>Exposed parameter "MusicVolume".</summary>
        Music,

        /// <summary>Exposed parameter "SfxVolume".</summary>
        Sfx,

        /// <summary>Exposed parameter "VoiceVolume" (dialogue).</summary>
        Voice,
    }

    /// <summary>Linear/decibel conversions with a -80 dB silence floor.</summary>
    internal static class AudioMath
    {
        private const float SilenceDb = -80f;

        public static float LinearToDb(float linear)
            => linear <= 0.0001f ? SilenceDb : Mathf.Log10(linear) * 20f;

        public static float DbToLinear(float db)
            => db <= SilenceDb ? 0f : Mathf.Pow(10f, db / 20f);
    }
}
```

`SoundSystem.cs`에 추가:

```csharp
        private static readonly string[] ChannelParams =
        {
            "MasterVolume", "MusicVolume", "SfxVolume", "VoiceVolume",
        };

        /// <summary>Sets a channel's linear volume [0,1] on the mixer. Persisting the value is the game's job.</summary>
        public void SetChannelVolume(SoundChannel channel, float linear)
        {
            if (_config.Mixer == null)
            {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                Debug.LogWarning("SoundSystem.SetChannelVolume: no mixer configured; call ignored.");
#endif
                return;
            }
            _config.Mixer.SetFloat(ChannelParams[(int)channel], AudioMath.LinearToDb(linear));
        }

        /// <summary>Reads a channel's linear volume; 1 when no mixer or parameter is set.</summary>
        public float GetChannelVolume(SoundChannel channel)
        {
            if (_config.Mixer == null || !_config.Mixer.GetFloat(ChannelParams[(int)channel], out var db))
            {
                return 1f;
            }
            return AudioMath.DbToLinear(db);
        }
```

- [ ] **Step 4: 테스트 통과 확인**

- [ ] **Step 5: 커밋** — `✨ Channel volumes via exposed mixer parameters`

---

### Task 9: 무할당 검증 + 마무리

**Files:**
- Test: `unity/Packages/com.bun3.unity.audio/Tests/Runtime/AllocationTests.cs`
- Create: `unity/Packages/com.bun3.unity.audio/README.md`
- Modify: `unity/Packages/com.bun3.unity.audio/CHANGELOG.md` (내용 확정)

**Interfaces:**
- Consumes: 전체 API

- [ ] **Step 1: 무할당 테스트 작성**

```csharp
using Bun3.Unity.Audio;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class AllocationTests
    {
        [Test]
        public void PlayAndTick_DoNotAllocate()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 8 });
            var def = ScriptableObject.CreateInstance<SoundDef>();
            def.Clips = new[] { AudioClip.Create("test-clip", 4410, 1, 44100, false) };
            def.Cooldown = 0f;

            // Warm every lazy path once: first play of a def grows the cooldown dictionary.
            sys.Play(def).Stop();
            sys.Tick(0.02f);

            Assert.That(() =>
            {
                var handle = sys.Play(def);
                sys.Tick(0.02f);
                handle.Stop(0.1f);
                sys.Tick(0.2f);
            }, Is.Not.AllocatingGCMemory());
        }
    }
}
```

- [ ] **Step 2: 테스트 실행 — 실패 시 할당 지점을 프로파일러/스택으로 찾아 제거**

예상 함정: `(int, AutoResetUniTaskCompletionSource)` 튜플 리스트 성장(사전 Capacity 지정으로 해결), foreach의 열거자 박싱(전부 for 인덱스 루프 유지), `??=`의 `AutoResetUniTaskCompletionSource.Create()`(WaitAsync 미사용 경로에서는 생성 안 함 — 위 테스트 경로는 미사용이므로 무할당이어야 정상).

- [ ] **Step 3: README.md 작성** — 패키지 개요(영어), 설치, 최소 사용 예(Play/PlayAsync/Stop/SetChannelVolume), 스펙 문서 링크는 넣지 않음(레포 규칙: 근거는 docs/에만).

- [ ] **Step 4: 전체 테스트 그린 + 빌드 경고 0 확인**

- [ ] **Step 5: 커밋** — `✅ Zero-allocation hot-path assertion + package docs`

---

## Self-Review 결과

- 스펙 커버리지: 슬라이스 1 항목(풀·핸들·fade·보이스 제한·쿨다운·variation·채널 볼륨·UniTask) 전부 태스크 매핑 확인. 음악·오클루전·믹서 에셋·타임스케일·Steam Audio·Addressables·에디터 프리뷰는 후속 플랜(스펙 슬라이스 2~5) — 의도된 제외.
- 타입 일관성: `TryAllocate`의 out 시그니처가 Task 7에서 `stolenCompletion` 추가로 진화함을 Task 7에 명시(집행자가 Task 4 테스트를 함께 수정). `Tick`의 completed 리스트 타입 변화(Task 5 주의사항)도 동일하게 명시.
- 플레이스홀더: 없음. 모든 스텝에 실제 코드/커맨드 포함.
