# Bun3.Unity.Audio Addressables 조건부 로딩 (슬라이스 5) 구현 플랜

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `com.unity.addressables` 설치 시에만 활성화되는 SoundDef 어드레서블 클립 경로 — `PreloadAsync`/`ReleasePreloaded` 선로드 모델, 미설치 프로젝트는 코드가 컴파일 아웃.

**Architecture:** SFX는 지연 민감이라 **재생은 항상 동기, 로드는 선로드**로 분리한다. SoundDef에 `#if BUN3_ADDRESSABLES` 게이트된 `AssetReferenceT<AudioClip>[]` 필드와 **무게이트** 런타임 캐시(`RuntimeClips`)를 두고, Play 경로는 `EffectiveClips`(캐시 우선, 폴백 직접 참조)만 본다 — 핫패스는 Addressables의 존재를 모른다. 로드/해제/핸들 추적은 `#if` 전용 partial(`SoundSystem.Addressables.cs`)에 격리하고, Dispose 연동은 C# partial method(미구현 시 컴파일 아웃)로 잇는다. asmdef `versionDefines`(진짜 UPM이라 이번엔 그대로 통함)로 define 주입.

**Tech Stack:** com.unity.addressables(6000.3 호환 최신 2.x), UniTask(선로드는 콜드 패스 — `AsyncOperationHandle.Task` await 허용), 기존 audio 패키지 0.1.0.

**Spec:** `docs/superpowers/specs/2026-08-31-unity-audio-design.md` — 확정 결정 "직접 AudioClip 참조 기본 + Addressables 조건부 지원(versionDefines)", SoundDef의 `BUN3_ADDRESSABLES` 대체 경로 주석, 에러 처리 "Addressables 로드 실패 → 무음 스킵 + 경고".

## 플랜 레벨 룰링

- **선로드 모델(스펙 구체화)**: 어드레서블 def는 `await sound.PreloadAsync(def)` 후 일반 `Play` — 재생 시점 비동기 로드는 없다(SFX 타이밍 슬롭 금지). 미선로드 어드레서블 def의 `Play`는 기존 "클립 없음" 경로(무효 핸들 + 개발 빌드 경고, 경고 문구에 preload 안내 추가) — 스펙의 "무음 스킵 + 경고" 계약 그대로.
- **MusicDef 어드레서블 필드는 만들지 않는다.** 음악이야말로 대용량 클립이지만, 게임이 직접 `Addressables.LoadAssetAsync<AudioClip>`으로 로드한 클립을 런타임 `ScriptableObject.CreateInstance<MusicDef>()`에 꽂으면 프레임워크 코드 0으로 해결된다 — 이 패턴을 README에 문서화(Task 4). 두 번째 게임에서 반복 수요가 확인되면 그때 내린다.
- **`RuntimeClips`/`EffectiveClips`는 #if 밖**: 어드레서블 미설치 게임도 런타임 생성 def에 클립을 꽂는 용도로 쓸 수 있는 범용 시섬이고, 핫패스 분기(`RuntimeClips ?? Clips`)가 define과 무관하게 단일 코드로 유지된다.
- **선로드는 콜드 패스** — `handle.Task` await·핸들 배열 할당 허용(XML 문서 명시). `UniTask.Addressables` 어셈블리 참조는 쓰지 않는다(의존 최소화; `.Task`로 충분). ct 취소 시 이미 로드된 핸들 전부 해제 후 `OperationCanceledException` 전파.
- **해제 계약**: `ReleasePreloaded(def)`는 해당 def의 보이스가 재생 중이지 않을 때 호출해야 한다(재생 중 해제 시 Unity가 소스를 멈춤 — 크래시는 아니므로 가드 없이 XML 문서로 계약 명시, 개발 빌드에서 활성 보이스 감지 시 경고 1줄).

## Global Constraints

- 코드·주석·XML 문서 영어만; public 멤버 XML 문서; 빌드 경고 0; 블록 네임스페이스.
- Play/Tick 핫패스 무할당 유지(`EffectiveClips`는 필드 참조 2회 — 무할당). AllocationTests 그린 유지.
- 커밋: gitmoji 제목 + 이중 `-m` 트레일러(`Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`).
- 테스트: `unity test "E:\Projects\orca\workspace\bun3-kit\Bun3-sound-manager\unity" --mode PlayMode --filter "<F>" --output "<scratch>.xml" --timeout 1200` — 절대 경로, total>0, 배치/에디터 동시 실행 금지, 런 후 패키지 밖 더럽힘 revert(Sentis define 스트립 포함; Addressables 설치로 인한 manifest/lock 변경은 Task 1의 의도된 커밋). 기준선: 코어 69/69 + 어댑터 7/7.
- 어댑터 패키지(`com.bun3.unity.audio.steamaudio`)는 이 슬라이스에서 불변.

---

### Task 1: Addressables 설치 + versionDefines + EffectiveClips 경로

**Files:**
- Modify: `unity/Packages/manifest.json` (`com.unity.addressables` 최신 6000.3 호환 버전 — 레지스트리에서 확인해 고정)
- Modify: `unity/Packages/packages-lock.json` (에디터 재생성분 커밋)
- Modify: `unity/Packages/com.bun3.unity.audio/Runtime/Bun3.Unity.Audio.asmdef` (versionDefines)
- Modify: `unity/Packages/com.bun3.unity.audio/Tests/Runtime/Bun3.Unity.Audio.Tests.asmdef` (동일 versionDefines)
- Modify: `unity/Packages/com.bun3.unity.audio/Runtime/SoundDef.cs`
- Modify: `unity/Packages/com.bun3.unity.audio/Runtime/SoundSystem.cs` (PlayCore·PickClip → EffectiveClips)
- Test: `unity/Packages/com.bun3.unity.audio/Tests/Runtime/RuntimeClipsTests.cs`

**Interfaces:**
- Produces (Task 2·3이 소비):
  - asmdef 양쪽에 `"versionDefines": [{ "name": "com.unity.addressables", "expression": "", "define": "BUN3_ADDRESSABLES" }]`
  - `SoundDef`:

```csharp
#if BUN3_ADDRESSABLES
        /// <summary>
        /// Addressable clip alternative to <see cref="Clips"/>. Load with
        /// SoundSystem.PreloadAsync before playing; unpreloaded defs play nothing.
        /// </summary>
        public UnityEngine.AddressableAssets.AssetReferenceT<AudioClip>[] AddressableClips;
#endif

        /// <summary>
        /// Runtime clip cache; when set it takes precedence over <see cref="Clips"/>.
        /// Populated by preloading (or manually for runtime-created defs).
        /// </summary>
        [System.NonSerialized] internal AudioClip[] RuntimeClips;

        internal AudioClip[] EffectiveClips => RuntimeClips ?? Clips;
```

  - `SoundSystem.PlayCore`: 클립 없음 가드와 `PickClip`이 `def.EffectiveClips` 사용(경고 문구를 "def has no loaded clips (assign Clips or preload AddressableClips); returning an invalid handle."로 갱신). `PickClip`의 `clips` 로컬도 EffectiveClips.

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
using System.Collections;
using Bun3.Unity.Audio;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class RuntimeClipsTests
    {
        [UnityTest]
        public IEnumerator RuntimeClips_TakePrecedenceOverDirectClips()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            var def = ScriptableObject.CreateInstance<SoundDef>();
            def.Clips = new[] { AudioClip.Create("direct", 4410, 1, 44100, false) };
            var runtime = AudioClip.Create("runtime", 4410, 1, 44100, false);
            def.RuntimeClips = new[] { runtime };
            var h = sys.Play(def);
            Assert.IsTrue(h.IsValid);
            Assert.That(sys.SourceForTest(0).clip, Is.SameAs(runtime));
            yield break;
        }

        [UnityTest]
        public IEnumerator NoClipsAnywhere_ReturnsInvalidHandle()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            var def = ScriptableObject.CreateInstance<SoundDef>(); // Clips null, RuntimeClips null
            LogAssert.Expect(LogType.Warning,
                "SoundSystem.Play: def has no loaded clips (assign Clips or preload AddressableClips); returning an invalid handle.");
            var h = sys.Play(def);
            Assert.IsFalse(h.IsValid);
            yield break;
        }
    }
}
```

- [ ] **Step 2: 컴파일/테스트 실패 확인** (RuntimeClips 미정의)

- [ ] **Step 3: 구현** — manifest에 Addressables 추가(에디터 1회 기동으로 lock 재생성·임포트 확인 후 종료), asmdef versionDefines, SoundDef/PlayCore/PickClip 변경. `#if BUN3_ADDRESSABLES` 필드는 이제 define이 켜지므로 실컴파일된다.

- [ ] **Step 4: 테스트 통과 확인** (`--filter "RuntimeClipsTests"` total=2) + 코어 회귀(`--filter "Bun3.Unity.Audio.Tests"` 71/71 — 기존 69+2) + 어댑터 회귀(`--filter "Bun3.Unity.Audio.SteamAudio.Tests"` 7/7 — Addressables 설치가 어댑터를 안 깨는지)

- [ ] **Step 5: 커밋** — `✨ Addressables install + BUN3_ADDRESSABLES gate + runtime clip cache path`

---

### Task 2: PreloadAsync / ReleasePreloaded / IsPreloaded

**Files:**
- Create: `unity/Packages/com.bun3.unity.audio/Runtime/SoundSystem.Addressables.cs` (파일 전체 `#if BUN3_ADDRESSABLES`)
- Modify: `unity/Packages/com.bun3.unity.audio/Runtime/SoundSystem.cs` (partial method 선언 + Dispose 호출)
- Test: `unity/Packages/com.bun3.unity.audio/Tests/Runtime/PreloadGuardTests.cs`

**Interfaces:**
- Consumes: Task 1의 `AddressableClips`/`RuntimeClips`.
- Produces:
  - `public UniTask PreloadAsync(SoundDef def, CancellationToken ct = default)` — 로드 성공 시 `def.RuntimeClips` 채움 + 핸들 추적. 실패 클립: 로드된 것 전부 해제, `RuntimeClips` 미설정, 개발 빌드 경고, 정상 반환(스펙: 무음 스킵 + 경고 — 예외 없음). ct 취소: 로드된 핸들 해제 후 `OperationCanceledException` 전파. 이미 선로드된 def·`AddressableClips` 비어 있는 def → 즉시 완료. Dispose 후 호출 → `ObjectDisposedException`.
  - `public bool IsPreloaded(SoundDef def)` — 추적 중이고 RuntimeClips 설정됨.
  - `public void ReleasePreloaded(SoundDef def)` — 핸들 전부 `Addressables.Release`, `def.RuntimeClips = null`, 추적 제거. 미선로드 def는 no-op. 개발 빌드: 해당 def의 활성 보이스 존재 시 경고 1줄(가드 아님).
  - `SoundSystem.cs`: `partial void ReleaseAllPreloadedOnDispose();` 선언(무조건) + `Dispose()`에서 소스 파괴 전에 호출. 구현은 Addressables 파일에만 존재 — define 꺼지면 호출 자체가 컴파일 아웃(C# partial method 시맨틱).
  - 내부: `Dictionary<SoundDef, AsyncOperationHandle<AudioClip>[]> _preloaded` — lazy 초기화(선로드 최초 1회), 콜드 패스 할당 허용(XML 명시).

- [ ] **Step 1: 실패하는 테스트 작성** (실제 어드레서블 에셋 없이도 검증 가능한 가드 경로 — 실로드는 Task 3)

```csharp
#if BUN3_ADDRESSABLES
using System.Collections;
using Bun3.Unity.Audio;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class PreloadGuardTests
    {
        [UnityTest]
        public IEnumerator Preload_NoAddressableClips_CompletesImmediately() => UniTask.ToCoroutine(async () =>
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            var def = ScriptableObject.CreateInstance<SoundDef>();
            await sys.PreloadAsync(def); // AddressableClips null → no-op
            Assert.IsFalse(sys.IsPreloaded(def));
        });

        [UnityTest]
        public IEnumerator Release_NotPreloaded_IsNoOp() => UniTask.ToCoroutine(async () =>
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            var def = ScriptableObject.CreateInstance<SoundDef>();
            sys.ReleasePreloaded(def); // must not throw
            Assert.IsFalse(sys.IsPreloaded(def));
            await UniTask.Yield();
        });

        [UnityTest]
        public IEnumerator UnpreloadedAddressableDef_PlayReturnsInvalid() => UniTask.ToCoroutine(async () =>
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            var def = ScriptableObject.CreateInstance<SoundDef>();
            def.AddressableClips = new[] { new UnityEngine.AddressableAssets.AssetReferenceT<AudioClip>(System.Guid.NewGuid().ToString("N")) };
            LogAssert.Expect(LogType.Warning,
                "SoundSystem.Play: def has no loaded clips (assign Clips or preload AddressableClips); returning an invalid handle.");
            var h = sys.Play(def);
            Assert.IsFalse(h.IsValid);
            await UniTask.Yield();
        });
    }
}
#endif
```

- [ ] **Step 2: 컴파일 실패 확인** → **Step 3: 구현** (Produces 계약 전부; `using UnityEngine.AddressableAssets; using UnityEngine.ResourceManagement.AsyncOperations;`) → **Step 4: 통과 확인** (`--filter "PreloadGuardTests"` total=3 + 코어 74/74)

- [ ] **Step 5: 커밋** — `✨ Addressables preload/release lifecycle (BUN3_ADDRESSABLES)`

---

### Task 3: 실로드 테스트 (탐색 태스크 — 어드레서블 테스트 에셋 저작)

**Files:**
- Create: `unity/Assets/Bun3AudioTestAssets/bun3-test-clip.wav` (+ .meta) — 0.1초 무음 소형 wav(스크립트로 생성 가능: 44바이트 헤더 + 4410 샘플 PCM16 — 구현자가 파이썬/파워셸로 생성)
- Create: `unity/Assets/AddressableAssetsData/**` (Addressables 기본 설정 + 그룹 + 위 클립 엔트리 — 에디터 저작, 커밋)
- Test: `unity/Packages/com.bun3.unity.audio/Tests/Runtime/PreloadRealLoadTests.cs`

**절차:**

- [ ] **Step 1:** wav 파일 생성·배치. 커넥티드 에디터 기동 → `unity cmd` C#으로: `AddressableAssetSettingsDefaultObject.GetSettings(true)`(기본 설정 생성), 클립의 GUID를 default group에 엔트리 추가(`settings.CreateOrMoveEntry(guid, settings.DefaultGroup)`), 주소는 `"bun3-test-clip"`, `AssetDatabase.SaveAssets()`. 에디터 종료. (에디터 플레이모드 테스트는 어드레서블을 AssetDatabase 모드로 로드하므로 카탈로그 빌드 불필요 — 이게 안 통하면 3회 시도 후 DONE_WITH_CONCERNS 폴백: 실로드 커버리지는 오너 수동 검증으로 남기고 Task 2 가드 테스트가 자동화 상한.)
- [ ] **Step 2: 실패하는 테스트 작성**

```csharp
#if BUN3_ADDRESSABLES
using System.Collections;
using Bun3.Unity.Audio;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class PreloadRealLoadTests
    {
        private const string TestClipAddress = "bun3-test-clip";

        [UnityTest]
        public IEnumerator Preload_Play_Release_RoundTrip() => UniTask.ToCoroutine(async () =>
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            var def = ScriptableObject.CreateInstance<SoundDef>();
            def.AddressableClips = new[]
            {
                new UnityEngine.AddressableAssets.AssetReferenceT<AudioClip>(
                    UnityEditor.AssetDatabase.AssetPathToGUID(
                        "Assets/Bun3AudioTestAssets/bun3-test-clip.wav")),
            };
            await sys.PreloadAsync(def);
            Assert.IsTrue(sys.IsPreloaded(def));
            var h = sys.Play(def);
            Assert.IsTrue(h.IsValid, "preloaded addressable def must play synchronously");
            h.Stop();
            sys.Tick(0.05f);
            sys.ReleasePreloaded(def);
            Assert.IsFalse(sys.IsPreloaded(def));
        });

        [UnityTest]
        public IEnumerator Preload_InvalidGuid_WarnsAndStaysUnpreloaded() => UniTask.ToCoroutine(async () =>
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            var def = ScriptableObject.CreateInstance<SoundDef>();
            def.AddressableClips = new[]
            {
                new UnityEngine.AddressableAssets.AssetReferenceT<AudioClip>(System.Guid.NewGuid().ToString("N")),
            };
            LogAssert.ignoreFailingMessages = true; // Addressables' own error spam
            await sys.PreloadAsync(def); // must not throw (silent-skip contract)
            LogAssert.ignoreFailingMessages = false;
            Assert.IsFalse(sys.IsPreloaded(def));
        });
    }
}
#endif
```

주의: `UnityEditor.AssetDatabase` 사용은 에디터 전용 — 이 테스트 파일은 `#if BUN3_ADDRESSABLES && UNITY_EDITOR`로 이중 게이트(플레이어 빌드 테스트 제외 허용; 리포트에 기록). 잘못된 GUID 로드 실패 시 Addressables가 뿜는 자체 에러 로그는 `LogAssert.ignoreFailingMessages`로 스코프 한정 허용 — 우리 계약(무예외·미선로드 유지)만 어설션.

- [ ] **Step 3: 구현 확인·통과** (`--filter "PreloadRealLoadTests"` total=2 + 전체 회귀 76/76) → **Step 4: 커밋** — `✅ Real addressable load round-trip test + test asset authoring`

---

### Task 4: 문서·최종 회귀

**Files:**
- Modify: 코어 `README.md`(Addressables 섹션: AddressableClips + PreloadAsync/Release 사용례, **음악 어드레서블 패턴** — 게임이 직접 로드한 클립으로 런타임 MusicDef 생성하는 코드 조각, 미설치 시 컴파일 아웃 설명), `CHANGELOG.md`(0.1.0 Added 줄)
- 최종 회귀: 코어 전체(76/76 예상 — Task 3 폴백 시 74/74) + 어댑터 7/7 + AllocationTests 그린, 경고 0

- [ ] **Step 1: 문서 작성** (음악 패턴 코드 조각 — 실시그니처 검증 필수):

```csharp
// Music via Addressables: load the clip yourself, then build a runtime MusicDef.
var handle = Addressables.LoadAssetAsync<AudioClip>("bgm-main");
var clip = await handle.Task;
var def = ScriptableObject.CreateInstance<MusicDef>();
def.Loop = clip;
sound.PlayMusic(def, fade: 2f);
// Keep `handle` and Addressables.Release(handle) when the track is retired.
```

- [ ] **Step 2: 최종 회귀** → **Step 3: 커밋** — `✅ Addressables docs + slice 5 final regression`

---

## Self-Review 결과

- 스펙 커버리지: "Addressables 조건부 지원(versionDefines)" → T1; SoundDef 대체 경로 → T1; "로드 실패 → 무음 스킵 + 경고" → T2 계약 + T3 실검증. 선로드 모델·MusicDef 제외·RuntimeClips 무게이트는 상단 룰링에 명시.
- 플레이스홀더: 없음 — 전 스텝 실코드. T3 에디터 저작만 탐색 프로토콜(+폴백 사전 정의).
- 타입 일관성: `EffectiveClips`(T1) ↔ PlayCore/PickClip 소비; `PreloadAsync/IsPreloaded/ReleasePreloaded`(T2) ↔ T3 테스트; `SourceForTest`(슬라이스 4 기존 internal) 재사용; partial method 이름 `ReleaseAllPreloadedOnDispose` 단일.
- 리스크: T3의 AssetDatabase-모드 어드레서블 로드가 에디터 플레이모드 테스트에서 카탈로그 없이 동작하는지가 유일한 불확실성 — 3회 시도 폴백 정의. T1의 Addressables 설치가 기존 테스트(특히 어댑터·AllocationTests)에 영향 없는지 회귀로 확인.
