# Bun3.Unity.Audio Steam Audio 어댑터 (슬라이스 4) 구현 플랜

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `com.bun3.unity.audio.steamaudio` 어댑터 패키지 — 게임이 설치하면 SFX 공간화·오클루전이 Steam Audio(모델 A: 완전 위임)로 넘어가고 게임 호출부는 불변; dev 프로젝트에 Steam Audio 실설치 + 실테스트 포함.

**Architecture:** 코어에 per-play 델리게이트 시섬(`SoundSystemConfig.OnVoiceConfigured`) 하나를 추가하고, 어댑터는 `SteamAudioSoundSetup.Apply(config)` 한 줄로 ① 코어 오클루전 완전 오프(`OcclusionChecksPerFrame = 0` — 슬라이스 3의 공식 스위치, LPF 미부착) ② 델리게이트 체인으로 재생 시점마다 `SteamAudioSource` add-if-missing + `spatialize`/오클루전 플래그를 def에 맞게 배선. Steam Audio 미설치 프로젝트에서 어댑터는 컴파일되지 않는 빈 패키지(asmdef versionDefines + defineConstraints — Addressables 슬라이스 5와 같은 패턴).

**Tech Stack:** Steam Audio Unity 플러그인(Valve, Apache 2.0, GitHub 릴리스 `.tgz` — UPM 레지스트리 미등록), Unity spatializer plugin 슬롯, 기존 audio 패키지 0.1.0, unity-cli 커넥티드 에디터(설정 저작).

**Spec:** `docs/superpowers/specs/2026-08-31-unity-audio-design.md` — "Steam Audio 어댑터" 섹션. 통합 모델 A(완전 위임)는 오너 확정(2026-09-02).

## 플랜 레벨 룰링

- **코어 시섬 추가(스펙 미기재, 필요 확정)**: 어댑터가 재생 시점에 소스를 꾸밀 공식 지점이 없어 `SoundSystemConfig.OnVoiceConfigured`(`Action<AudioSource, SoundDef>`, null 허용)를 추가한다 — `PlayCore`에서 소스 구성 완료 후 `source.Play()` 직전에 1회 호출, SFX 전용(음악 소스 제외). 레포 관례("게임 지식이 필요한 지점은 훅/델리게이트로") 그대로. 델리게이트는 게임/어댑터가 캐시해 등록 — per-play 할당 없음.
- **Steam Audio 배포 방식**: 어댑터 package.json은 Steam Audio에 **하드 의존을 걸지 않는다**(레지스트리 부재로 불가능). asmdef `versionDefines`(`com.valvesoftware.steamaudio` → `STEAMAUDIO_PRESENT`) + `defineConstraints`로 설치 시에만 컴파일. 설치 방법은 어댑터 README가 안내.
- **dev 프로젝트 실설치**: 릴리스 `.tgz`를 `unity/Vendor/`에 **커밋**하고 manifest가 `file:../Vendor/<파일명>`으로 참조(재현 가능한 클론). 단 **80MB 초과 시** 커밋하지 않고 `.gitignore` + `unity/Vendor/README.md`에 다운로드 절차 기록으로 폴백(GitHub 파일 한도 100MB 리스크 회피) — 실측 크기를 리포트에 남긴다.
- **`ProjectSettings/AudioManager.asset`의 spatializer 설정 변경은 의도된 커밋**(기존 "패키지 밖 더럽힘은 revert" 규칙의 명시적 예외 — 이 파일 하나만).
- 어댑터 검증 상한: **배선 검증**(컴포넌트 부착·플래그 매핑·spatialize·코어 LPF 부재)까지. 음향 품질은 귀 검증 영역.

## Global Constraints

- 코드·주석·XML 문서 영어만; public 멤버 XML 문서; 빌드 경고 0; 블록 네임스페이스.
- 네임스페이스/asmdef `Bun3.Unity.Audio.SteamAudio`(Runtime), `Bun3.Unity.Audio.SteamAudio.Editor`.
- 핫패스 무할당: 델리게이트 호출은 캐시된 인스턴스 1회 invoke; 바인더는 `TryGetComponent`(무할당) + AddComponent는 소스당 최초 1회.
- 커밋: gitmoji 제목 + 이중 `-m` 트레일러(`Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`).
- 테스트: `unity test "E:\Projects\orca\workspace\bun3-kit\Bun3-sound-manager\unity" --mode PlayMode --filter "<F>" --output "<scratch>.xml" --timeout 1200` — 절대 경로 필수, `total`>0 확인, 배치/에디터 동시 실행 금지, 런 후 패키지 밖 더럽힘 revert(단 AudioManager.asset 예외는 위 룰링). 기준선: `--filter "Bun3.Unity.Audio.Tests"` 67/67.
- Steam Audio API 필드명(오클루전 토글 등)은 설치된 실소스를 읽고 확정한다 — 탐색 허용 지점이며, 확정한 매핑을 리포트에 기록.

---

### Task 1: 코어 per-play 시섬 `OnVoiceConfigured`

**Files:**
- Modify: `unity/Packages/com.bun3.unity.audio/Runtime/SoundSystemConfig.cs`
- Modify: `unity/Packages/com.bun3.unity.audio/Runtime/SoundSystem.cs` (PlayCore)
- Test: `unity/Packages/com.bun3.unity.audio/Tests/Runtime/VoiceConfiguredHookTests.cs`

**Interfaces:**
- Produces: `SoundSystemConfig.OnVoiceConfigured` — `public Action<AudioSource, SoundDef> OnVoiceConfigured;` XML 문서: "Invoked once per play after the SFX source is fully configured, just before Play. Register a cached delegate (hot path — allocation-free). Music sources are not decorated." `PlayCore`에서 `source.Play()` 직전, `stolenCompletion` 신호보다 먼저 호출: `_config.OnVoiceConfigured?.Invoke(source, def);`

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
using System.Collections;
using Bun3.Unity.Audio;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class VoiceConfiguredHookTests
    {
        private static SoundDef ClipDef()
        {
            var def = ScriptableObject.CreateInstance<SoundDef>();
            def.Clips = new[] { AudioClip.Create("hook", 4410, 1, 44100, false) };
            return def;
        }

        [UnityTest]
        public IEnumerator Hook_InvokedPerPlay_WithConfiguredSource()
        {
            var calls = 0;
            AudioSource seen = null;
            SoundDef seenDef = null;
            var config = new SoundSystemConfig { SfxVoices = 2 };
            config.OnVoiceConfigured = (source, def) => { calls++; seen = source; seenDef = def; };
            using var sys = new SoundSystem(config);
            var played = ClipDef();
            sys.Play(played, new Vector3(1f, 0f, 0f));
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(seenDef, Is.SameAs(played));
            Assert.IsNotNull(seen);
            Assert.That(seen.clip, Is.Not.Null, "source must be fully configured when the hook runs");
            Assert.That(seen.transform.position.x, Is.EqualTo(1f).Within(0.001f));
            sys.Play(ClipDef());
            Assert.That(calls, Is.EqualTo(2));
            yield break;
        }

        [UnityTest]
        public IEnumerator Hook_NotInvokedForMusic()
        {
            var calls = 0;
            var config = new SoundSystemConfig { SfxVoices = 2 };
            config.OnVoiceConfigured = (_, _) => calls++;
            using var sys = new SoundSystem(config);
            sys.PlayMusic(SoundSystemMusicTests.Def(withIntro: false));
            yield return null;
            Assert.That(calls, Is.EqualTo(0));
        }
    }
}
```

- [ ] **Step 2: 컴파일 실패 확인** (OnVoiceConfigured 미정의)

- [ ] **Step 3: 구현** — Config 필드(+`using System;` 필요 시), PlayCore에 위 Produces 서술 위치로 invoke 한 줄. 기존 `AllocationTests`가 델리게이트 미등록 구성이므로 null-invoke는 무할당 — 회귀로 확인.

- [ ] **Step 4: 테스트 통과 확인** (`--filter "VoiceConfiguredHookTests"` total=2) + 전체 회귀(`--filter "Bun3.Unity.Audio.Tests"` 69/69)

- [ ] **Step 5: 커밋** — `✨ OnVoiceConfigured per-play hook for spatializer adapters`

---

### Task 2: Steam Audio 실설치 (탐색 태스크)

**Files:**
- Create: `unity/Vendor/steamaudio_unity.tgz` (80MB 초과 시 폴백: `.gitignore` + `unity/Vendor/README.md`)
- Modify: `unity/Packages/manifest.json` (`com.valvesoftware.steamaudio` — file: 로컬 tarball; 정확한 패키지명은 tgz의 package.json에서 확인해 사용)
- Modify: `unity/Packages/packages-lock.json` (에디터 재생성분 커밋)
- Modify: `unity/ProjectSettings/AudioManager.asset` (spatializer plugin = Steam Audio Spatializer — 의도된 커밋)

**절차 (순서 엄수):**

- [ ] **Step 1:** GitHub 릴리스에서 최신 Steam Audio Unity tgz 다운로드 — `https://github.com/ValveSoftware/steam-audio/releases` 최신 릴리스의 `steamaudio_unity.tgz` 에셋 (curl -L). 크기 실측 → 80MB 기준 커밋/폴백 결정, 리포트 기록.
- [ ] **Step 2:** `unity/Vendor/`에 배치, tgz 내부 `package/package.json`을 열어(tar 부분 추출) 정확한 패키지 name·version 확인.
- [ ] **Step 3:** `unity/Packages/manifest.json`에 `"<확인한 name>": "file:../Vendor/steamaudio_unity.tgz"` 추가.
- [ ] **Step 4:** 커넥티드 에디터 기동(`unity open`, `unity status` 대기) → 임포트 완료 확인 → `unity cmd` C#으로 spatializer 설정: `AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/AudioManager.asset")`의 AudioManager를 SerializedObject로 열어 `m_SpatializerPlugin = "Steam Audio Spatializer"` 기록 후 저장(정확한 플러그인 표시명은 `AudioSettings.GetSpatializerPluginNames()`류 에디터 API 또는 임포트된 플러그인 목록에서 확인해 사용 — 표시명을 리포트에 기록). 설치된 Steam Audio 패키지의 **Runtime asmdef 이름**과 **SteamAudioSource의 오클루전 관련 public 필드명들**을 이 단계에서 덤프해 리포트에 기록(Task 3·4가 소비).
- [ ] **Step 5:** 에디터 종료 → 배치 스모크: `--filter "Bun3.Unity.Audio.Tests"` 67/67(어댑터 이전이므로 기존 그린 유지 + Steam Audio 임포트로 인한 컴파일 오류 없음 확인).
- [ ] **Step 6:** 커밋 — `📦 Vendor Steam Audio Unity plugin + spatializer project setting` (tgz·manifest·lock·AudioManager.asset; 폴백 시 tgz 대신 Vendor/README.md).

**에스컬레이션:** 다운로드 URL 또는 spatializer 설정이 3회 시도에 안 풀리면 중단하고 BLOCKED 보고(시도 내역 명시).

---

### Task 3: 어댑터 패키지 + SteamAudioSoundSetup + 바인더

**Files:**
- Create: `unity/Packages/com.bun3.unity.audio.steamaudio/package.json`
- Create: `unity/Packages/com.bun3.unity.audio.steamaudio/Runtime/Bun3.Unity.Audio.SteamAudio.asmdef`
- Create: `unity/Packages/com.bun3.unity.audio.steamaudio/Runtime/SteamAudioSoundSetup.cs`
- Create: `unity/Packages/com.bun3.unity.audio.steamaudio/Tests/Runtime/Bun3.Unity.Audio.SteamAudio.Tests.asmdef`
- Create: `unity/Packages/com.bun3.unity.audio.steamaudio/CHANGELOG.md`, `README.md`
- Create: `unity/Packages/com.bun3.unity.audio.steamaudio/Runtime/AssemblyInfo.cs` (InternalsVisibleTo Tests)

**Interfaces:**
- Consumes: Task 1의 `OnVoiceConfigured`, Task 2 리포트의 asmdef 이름·SteamAudioSource 필드명, 슬라이스 3의 `OcclusionChecksPerFrame = 0` 오프 스위치.
- Produces:
  - `public static class SteamAudioSoundSetup { public static SoundSystemConfig Apply(SoundSystemConfig config); }` — ① `config.OcclusionChecksPerFrame = 0`(코어 오클루전·LPF 완전 오프) ② 기존 `OnVoiceConfigured`가 있으면 체인(기존 + 바인더 순서로 합성), 없으면 바인더 등록 ③ 같은 config 재적용은 멱등(바인더 중복 등록 금지 — 정적 캐시 델리게이트 참조 비교). 반환은 받은 config(체이닝 편의).
  - 바인더(내부 정적 메서드, 캐시 델리게이트): `TryGetComponent<SteamAudioSource>` 실패 시 1회 `AddComponent`; `source.spatialize = def.Spatial != SpatialMode.None`; 3D일 때 SteamAudioSource 활성 + 오클루전 필드 = `def.Occlusion`(정확한 필드명은 Task 2 리포트 기준 — 매핑을 XML 주석에 기록); 2D일 때 SteamAudioSource 비활성(`enabled = false`).
- asmdef: references `Bun3.Unity.Audio` + Task 2에서 확인한 Steam Audio asmdef; `versionDefines: [{ name: "com.valvesoftware.steamaudio"(실확인명), define: "STEAMAUDIO_PRESENT" }]`; `defineConstraints: ["STEAMAUDIO_PRESENT"]`. Tests asmdef 동일 constraint + 기존 테스트 asmdef 컨벤션(overrideReferences/nunit/UNITY_INCLUDE_TESTS) 복제.
- package.json: name `com.bun3.unity.audio.steamaudio`, version 0.1.0, unity 6000.3, dependencies `{ "com.bun3.unity.audio": "0.1.0" }` (Steam Audio는 의도적 미선언 — README가 설치 안내).

- [ ] **Step 1: 실패하는 테스트 작성** (Task 4의 픽스처를 여기서 함께 작성해도 좋으나 최소 1개는 이 태스크에서):

```csharp
using Bun3.Unity.Audio;
using Bun3.Unity.Audio.SteamAudio;
using NUnit.Framework;

namespace Bun3.Unity.Audio.SteamAudio.Tests
{
    public sealed class SteamAudioSetupTests
    {
        [Test]
        public void Apply_DisablesCoreOcclusion_AndRegistersBinder()
        {
            var config = new SoundSystemConfig();
            var returned = SteamAudioSoundSetup.Apply(config);
            Assert.That(returned, Is.SameAs(config));
            Assert.That(config.OcclusionChecksPerFrame, Is.EqualTo(0));
            Assert.IsNotNull(config.OnVoiceConfigured);
        }

        [Test]
        public void Apply_Twice_IsIdempotent()
        {
            var config = new SoundSystemConfig();
            SteamAudioSoundSetup.Apply(config);
            var once = config.OnVoiceConfigured;
            SteamAudioSoundSetup.Apply(config);
            Assert.That(config.OnVoiceConfigured, Is.SameAs(once), "binder must not be registered twice");
        }

        [Test]
        public void Apply_PreservesExistingHook_ByChaining()
        {
            var config = new SoundSystemConfig();
            var called = false;
            config.OnVoiceConfigured = (_, _) => called = true;
            SteamAudioSoundSetup.Apply(config);
            config.OnVoiceConfigured(null, null); // chained delegate must still call the original
            Assert.IsTrue(called);
        }
    }
}
```

주의: 세 번째 테스트의 `(null, null)` 호출이 바인더에서 NRE가 나지 않도록 바인더는 `source == null || def == null` 조기 반환 가드를 가진다(계약: 방어적 no-op — 핫패스 비용은 null 비교 2회).

- [ ] **Step 2: 컴파일 실패 확인** (SteamAudioSoundSetup 미정의; STEAMAUDIO_PRESENT가 켜져 있어야 테스트가 컴파일됨 — Task 2 설치 덕에 켜짐)

- [ ] **Step 3: 구현** — Produces 계약대로. 멱등성: 바인더 델리게이트를 `private static readonly Action<AudioSource, SoundDef> Binder = Bind;`로 캐시하고 `config.OnVoiceConfigured`의 invocation list에 Binder가 이미 있으면 재등록 생략(`Delegate.GetInvocationList` — Apply는 콜드 패스라 할당 허용, XML에 명시).

- [ ] **Step 4: 테스트 통과 확인** (`--filter "SteamAudioSetupTests"` total=3)

- [ ] **Step 5: 커밋** — `✨ Steam Audio adapter package: Apply() setup + voice binder`

---

### Task 4: 바인딩 실테스트 + 에디터 검증

**Files:**
- Create: `unity/Packages/com.bun3.unity.audio.steamaudio/Tests/Runtime/SteamAudioBindingTests.cs`
- Create: `unity/Packages/com.bun3.unity.audio.steamaudio/Editor/Bun3.Unity.Audio.SteamAudio.Editor.asmdef` (동일 defineConstraints + Editor 플랫폼)
- Create: `unity/Packages/com.bun3.unity.audio.steamaudio/Editor/SteamAudioSetupValidator.cs`

**Interfaces:**
- Consumes: Task 3 전부, Task 2의 spatializer 표시명.
- Produces:
  - PlayMode 실테스트(실제 Steam Audio 컴포넌트 대상): 3D+Occlusion def 재생 → 소스에 `SteamAudioSource` 존재·활성, `source.spatialize == true`, 오클루전 필드 == true, **코어 LPF 컴포넌트 부재**(`GetComponent<AudioLowPassFilter>() == null` — ChecksPerFrame 0이라 미부착); 2D def 재생 → `spatialize == false`, SteamAudioSource 비활성; 소스 재사용(같은 슬롯 재재생) 시 AddComponent 중복 없음(컴포넌트 1개).
  - `SteamAudioSetupValidator`: `[InitializeOnLoadMethod]`로 `AudioSettings.GetSpatializerPluginName()`(또는 에디터 동등 API)이 Task 2에서 확인한 표시명과 다르면 `Debug.LogWarning`(배치모드 포함 안전 — 로그만).

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
using System.Collections;
using Bun3.Unity.Audio;
using Bun3.Unity.Audio.SteamAudio;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using SteamAudioSourceComponent = global::SteamAudio.SteamAudioSource; // 실제 네임스페이스는 Task 2 리포트로 확정

namespace Bun3.Unity.Audio.SteamAudio.Tests
{
    public sealed class SteamAudioBindingTests
    {
        private static SoundDef Def(SpatialMode spatial, bool occlusion)
        {
            var def = ScriptableObject.CreateInstance<SoundDef>();
            def.Clips = new[] { AudioClip.Create("sa", 44100, 1, 44100, false) };
            def.Loop = true;
            def.Spatial = spatial;
            def.Occlusion = occlusion;
            return def;
        }

        [UnityTest]
        public IEnumerator Occluded3D_GetsSteamAudioSource_NoCoreLpf()
        {
            using var sys = new SoundSystem(SteamAudioSoundSetup.Apply(new SoundSystemConfig { SfxVoices = 2 }));
            sys.Play(Def(SpatialMode.Positional, occlusion: true), new Vector3(0f, 0f, 3f));
            var source = sys.SourceForTest(0);
            Assert.IsTrue(source.spatialize);
            Assert.IsTrue(source.TryGetComponent<SteamAudioSourceComponent>(out var sas));
            Assert.IsTrue(sas.enabled);
            // Occlusion field assertion uses the actual field name confirmed in Task 2's report.
            Assert.IsNull(source.GetComponent<AudioLowPassFilter>(), "core LPF must not exist under adapter");
            yield break;
        }

        [UnityTest]
        public IEnumerator TwoD_DisablesSpatializeAndSteamSource()
        {
            using var sys = new SoundSystem(SteamAudioSoundSetup.Apply(new SoundSystemConfig { SfxVoices = 2 }));
            sys.Play(Def(SpatialMode.None, occlusion: false));
            var source = sys.SourceForTest(0);
            Assert.IsFalse(source.spatialize);
            if (source.TryGetComponent<SteamAudioSourceComponent>(out var sas))
            {
                Assert.IsFalse(sas.enabled);
            }
            yield break;
        }

        [UnityTest]
        public IEnumerator SlotReuse_DoesNotDuplicateComponent()
        {
            using var sys = new SoundSystem(SteamAudioSoundSetup.Apply(new SoundSystemConfig { SfxVoices = 1 }));
            sys.Play(Def(SpatialMode.Positional, occlusion: true), Vector3.forward);
            sys.Play(Def(SpatialMode.Positional, occlusion: true), Vector3.forward); // steals slot 0
            var source = sys.SourceForTest(0);
            Assert.That(source.GetComponents<SteamAudioSourceComponent>(), Has.Length.EqualTo(1));
            yield break;
        }
    }
}
```

주의: `sys.SourceForTest(int)`가 코어에 없으면 이 태스크에서 코어에 internal 접근자 추가(`internal AudioSource SourceForTest(int slot) => _sources[slot];`) + 어댑터 Tests asmdef에서 코어 internal 접근이 필요하므로 코어 `AssemblyInfo.cs`에 `[assembly: InternalsVisibleTo("Bun3.Unity.Audio.SteamAudio.Tests")]` 추가(기계적 수정, 리포트 기록).

- [ ] **Step 2: 컴파일/테스트 실패 확인** → **Step 3: 구현**(Validator 포함) → **Step 4: 통과 확인** (`--filter "SteamAudioBindingTests"` total=3 + 어댑터 픽스처 전체 + 코어 회귀 69/69)

- [ ] **Step 5: 커밋** — `✨ Steam Audio binding tests + editor spatializer validator`

---

### Task 5: 문서·최종 회귀

**Files:**
- Modify: 어댑터 `README.md`(설치 절차: Steam Audio tgz 다운로드→manifest 추가→spatializer 설정; `SteamAudioSoundSetup.Apply` 사용법; 모델 A 시맨틱 — 코어 오클루전 완전 오프), `CHANGELOG.md`(0.1.0 초기 릴리스), 코어 `README.md`(어댑터 존재 한 줄 + 링크), 코어 `CHANGELOG.md`(OnVoiceConfigured 추가 줄)
- 최종 회귀: 코어 `--filter "Bun3.Unity.Audio.Tests"` 69/69 + 어댑터 `--filter "Bun3.Unity.Audio.SteamAudio.Tests"` 6/6, 경고 0

- [ ] **Step 1: 문서 작성** → **Step 2: 최종 회귀** → **Step 3: 커밋** — `✅ Steam Audio adapter docs + final regression`

---

## Self-Review 결과

- 스펙 커버리지: "Steam Audio 어댑터" 섹션 3항목 — ① 소스 SteamAudioSource 부착+매핑(T3·4), ② 코어 raycast/LPF 비활성(T3, ChecksPerFrame=0 스위치), ③ 에디터 spatializer 검증(T4). 실설치(T2)와 코어 시섬(T1)은 모델 A 실테스트 결정의 파생.
- 플레이스홀더: Steam Audio API 실명(asmdef명·필드명·spatializer 표시명·패키지명)은 T2가 실물에서 확정해 리포트로 전달하는 **의도된 탐색 지점** — T3·4가 그 리포트를 소비한다고 명시.
- 타입 일관성: `OnVoiceConfigured`(T1)·`SteamAudioSoundSetup.Apply`(T3)·`SourceForTest`(T4) 시그니처가 태스크 간 일치. 어댑터 테스트 수(3+3=6)와 T5 회귀 기대치 정합.
- 리스크: T2가 외부 다운로드+에디터 저작 이중 탐색 — 에스컬레이션 프로토콜 명시. tgz 크기 폴백 룰링 사전 정의.
