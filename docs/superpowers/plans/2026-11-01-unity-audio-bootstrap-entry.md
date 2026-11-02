# Unity Audio 정적 진입점과 Steam Audio 조립 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 게임이 Bootstrap에서 `SoundSystem.Initialize(catalog, config)`를 한 번 부르고 어디서나 `SoundSystem.Play(key)`로 재생하게 하고, Steam Audio 공간음은 조립 함수 한 번으로 붙인다. 그 뒤 JP의 `SoundManager`·`Bun3SoundOutput`·`GameAcousticSfxBinding`을 지운다.

**Architecture:**
- **kit 코어:** `SoundSystem`에 정적 partial(`SoundSystem.Static.cs`)을 더한다. 장치 변경 복구도 코어가 맡는다. 실제로 멈춘 효과음만 끝내고, 배경음은 매 프레임 기억한 위치에서 이어 튼다.
- **kit Steam Audio:**
  - 공유 런타임(`SteamAudioSoundRuntime`)이 컨텍스트, HRTF, 드라이버 클립, 정리 오브젝트를 하나씩만 갖는다.
  - 조립 함수(`SteamAudioSoundSetup.ApplyNativeOutput`)가 설정 델리게이트에 이 런타임을 연결한다.
- **JP:**
  - 조립 함수 `SoundSetup.EnsureInitialized(GameConfig)`를 Boot에서 부른다.
  - 에디터에서 Play를 Menu 씬부터 시작했을 때만 Play 시작 훅이 같은 함수를 부른다. 테스트 러너는 자체 씬으로 시작하므로 테스트에서는 사운드가 켜지지 않는다.
  - 호출부는 kit 정적 API를 직접 부른다.

**Tech Stack:** Unity 6, C# 9, NUnit + Unity Test Framework, Steam Audio 4.8.1 C# 바인딩(`SA = global::SteamAudio`), UniTask.

**Spec:** `docs/superpowers/specs/2026-10-31-unity-audio-bootstrap-entry-design.md`. 이 계획은 스펙을 근거로 쓴다. 실행자는 둘 다 읽는다.

## Global Constraints

### 저장소와 경로

- **kit:** 워크트리 `E:/Projects/orca/workspace/bun3-kit/audio-bootstrap`(브랜치 `Bun3/audio-bootstrap`). 아래 kit 경로는 모두 `unity/Packages/` 기준이다.
- **JP:** 워크트리 `E:/Projects/orca/workspace/jurassic-paradise/low-volume-bind-sfx-keys`(브랜치 `🔈/bind-sfx-keys`). 아래 JP 경로는 모두 `unity/Assets/_Game/` 기준이다.

### kit 규칙

- 코드, XML 문서, 문자열, README, CHANGELOG는 영어로 쓴다. `docs/`만 한국어를 허용한다.
- 모든 public 멤버에 XML 문서를 단다.
- 경고 0, 최소 주석.
- partial 파일 머리에 역할 주석을 한 줄 둔다. 본체 XML 문서의 partial 목록도 갱신한다.
- 재생·틱 경로(키 조회, 위치 기억 포함)는 관리 힙을 할당하지 않는다. 장치 변경 처리 경로는 할당을 허용한다.
- 버전: `com.bun3.unity.audio` 0.6.0 → **0.7.0**, `com.bun3.unity.audio.steamaudio` 0.5.0 → **0.6.0**. steamaudio의 의존성은 `"com.bun3.unity.audio": "0.7.0"`로 올린다.

### JP 규칙

- 새 컴파일 경고 금지.
- 모든 변경에 무인 자동 테스트를 둔다.
- 테스트는 출고 콘텐츠(`SoundCatalog.asset`)와 싱글톤을 직접 쓰지 않는다. 테스트 전용 카탈로그를 만든다.
- 정적 `SoundSystem`을 초기화한 테스트는 반드시 `SoundSystem.Shutdown()`으로 되돌린다.
- 저작 결과가 바뀌면 `M1EditorSetup.GeneratedAssetVersion`을 올린다(현재 806).
- `bundleVersion`은 0.41.2 → **0.41.3**(patch)으로 올린다. ProjectSettings 파일에서 직접 고친다. 셋업 코드가 버전을 쓰면 안 된다.
- `.meta`는 손으로 만들지 않는다. 지운 파일의 `.meta`는 함께 지운다.
- 테스트 뒤 다시 저장된 출고 에셋은 경로를 하나씩 지정해 되돌린다.

### 금지 사항 (스펙 실패 조건)

- 게임 레포에 사운드 래퍼·매니저 클래스를 만들지 않는다.
- `UseSteamAudio` 같은 bool 스위치를 두지 않는다.
- 별칭 API(`PlayFx` 등)나 두 번째 카탈로그를 만들지 않는다.
- 장치가 바뀔 때 클립을 다시 디코딩하거나 `SoundSystem`을 통째로 다시 만들지 않는다.

### 테스트 실행

- JP 에디터를 열어 둔 상태에서 `.agents/skills/jp-unity-api/SKILL.md`의 `run_tests`·`test_status`를 쓴다. 배치 모드를 반복 실행하지 않는다.
- 이 세션의 보조 스크립트:
  - 형식: `python -I C:/Users/dudck/AppData/Local/Temp/claude/E--Projects-orca-workspace-jurassic-paradise-low-volume-bind-sfx-keys/075d6564-1d17-4571-aa18-75868c783240/scratchpad/runtests.py <EditMode|PlayMode> <이름 필터>`
  - 끝나면 요약과 실패 목록을 출력한다.
- kit 테스트는 JP 에디터에서 돈다. JP manifest의 `testables`에 두 패키지가 이미 들어 있다. kit 패키지 테스트 어셈블리는 모두 PlayMode다.

### 커밋

- 태스크마다 `writing-gitmoji-commits` 스킬로 커밋한다.
- kit과 JP는 각자 자기 저장소에 커밋한다.
- Git 명령은 저장소별로 차례로 실행한다.
- 맨 bare `git stash`와 `--no-verify`는 쓰지 않는다.
- push와 main 머지는 Task 9에서 사용자 확인을 받은 뒤에만 한다.

## Review Focus

자주 일어날 만한 순서로 적었다. 각 항목의 테스트는 해당 태스크에 있다.

1. **Play 시작 씬이 Menu가 아니다.** 테스트 러너의 테스트 씬, Game 씬 직행, 캡처 씬이 여기에 해당한다.
   - 기대: Play 시작 훅이 사운드를 초기화하지 않는다. 테스트가 Menu 씬을 나중에 열어도 마찬가지다. → Task 7 `PlayStartedFromOtherScene_DoesNotInitialize`
2. **누군가 `SoundSystem.Current.Dispose()`를 직접 부른다.**
   - 기대: 정적 상태가 비워진다. 이후 정적 호출은 `ObjectDisposedException` 없이 무시된다. → Task 1 `DisposingCurrent_ClearsStaticState`
3. **배경음 교차 페이드 중에 장치가 바뀐다.** 이때 한 채널은 FadingIn, 다른 채널은 FadingOut이다.
   - 기대: 나가던 채널은 정리하고, 들어오던 채널은 이어 튼다. → Task 3 `CrossfadeDuringLoss_SilencesOutgoingAndResumesIncoming`
4. **장치 변경 이벤트가 연달아 온다.** 예를 들면 장치를 뽑자마자 기본 장치가 바뀌는 경우다.
   - 기대: 렌더러 교체가 보류된 상태에서 다음 이벤트가 와도 최종 렌더러가 마지막 형식과 맞는다. → Task 6 `ConsecutiveFormatChanges_EndOnLatestFormat`
5. **`AudioListener.pause` 상태에서 장치가 바뀐다.**
   - 기대: 일시정지 때문에 `isPlaying`이 거짓이어도 효과음 음성을 끝내지 않는다. → Task 3 `ListenerPausedVoices_AreNotCompletedOnRecovery`

---

### Task 1: 정적 진입점과 설정 훅 (`com.bun3.unity.audio`)

**Files:**
- Create: `com.bun3.unity.audio/Runtime/SoundSystem.Static.cs`
- Modify:
  - `com.bun3.unity.audio/Runtime/SoundSystemConfig.cs`
  - `com.bun3.unity.audio/Runtime/SoundSystem.cs` (생성자 catch, `Dispose`, partial 목록)
  - `com.bun3.unity.audio/Runtime/SoundSystem.Music.cs` (`WithMusicCancellation`)
  - `com.bun3.unity.audio/package.json`, `CHANGELOG.md`, `README.md`
- Test: `com.bun3.unity.audio/Tests/Runtime/SoundSystemStaticTests.cs`

**Interfaces:**
- Produces (`SoundSystemConfig`):
  - `public float MusicFadeSeconds = 1f;`: 정적 `PlayMusic`·`StopMusic`의 페이드.
  - `public Action<SoundDef> PrepareDefinition;`: 정의마다 준비할 때 불린다. 예외를 던지면 그 정의를 준비할 수 없다는 뜻이다.
  - `public Action OnDisposed;`: 다음 두 경우에 한 번 불린다.
    - 이 설정으로 만든 `SoundSystem`이 해제될 때. 출력 해제와 루트 파괴 뒤다.
    - 생성자가 설정 검증을 통과한 뒤 예외로 실패할 때.
- Produces (`SoundSystem`, 정적):
  - `public static SoundSystem Current { get; }`
  - `public static void Initialize(SoundCatalog catalog, SoundSystemConfig config)`
  - `public static void Shutdown()`
  - `public static void Play(string key)`
  - `public static void PlayAt(string key, Vector3 position)`
  - `public static void PlayMusic(string key)`
  - `public static void StopMusic()`

- [ ] **Step 0: 준비**
  1. JP `unity/Packages/manifest.json`에서 아래 두 항목을 `file:` 로컬 참조로 바꾼다. 이 변경은 커밋하지 않는다. Task 9에서 되돌린다.
     - `"com.bun3.unity.audio": "file:E:/Projects/orca/workspace/bun3-kit/audio-bootstrap/unity/Packages/com.bun3.unity.audio"`
     - `"com.bun3.unity.audio.steamaudio": "file:E:/Projects/orca/workspace/bun3-kit/audio-bootstrap/unity/Packages/com.bun3.unity.audio.steamaudio"`
  2. 에디터가 다시 해석한 뒤 `EditorUtility.scriptCompilationFailed == false`를 확인한다.
  3. kit에서 스펙과 이 계획 문서를 📝 커밋 하나로 먼저 커밋한다.

- [ ] **Step 1: 실패하는 테스트 작성**

  **픽스처**
  - SetUp에서 정의 세 개를 만든다. 셋 다 `AudioClip.Create("t", 4410, 1, 44100, false)` 클립을 쓴다.
    - `fx.2d`: `Spatial = None`
    - `fx.3d`: `Spatial = Positional`
    - `bgm`
  - 카탈로그는 `SetEntries`로 구성한다.
  - 녹음 출력 `RecordingOutput : ISpatialSoundVoiceOutput`를 둔다.
    - `TryStart`에서 (정의, 공간 모드, `source.transform.position`)을 공유 리스트에 기록한다. 리스트 용량은 미리 잡아 둔다.
    - `Unsupported`를 돌려준다.
  - TearDown: `SoundSystem.Shutdown()`을 부르고 생성 객체를 파괴한다.

  **테스트**
  - `StaticCallsBeforeInitialize_AreIgnoredWithOneEditorWarning`
    - 네 정적 호출이 모두 예외 없이 끝난다.
    - `LogAssert.Expect(LogType.Warning, new Regex("before SoundSystem.Initialize"))`가 한 번 맞는다.
    - 두 번째 호출 뒤 `LogAssert.NoUnexpectedReceived()`.
  - `UnknownKey_IsIgnoredWithoutLog`: `Play("missing")` 뒤 기록이 0건이고 로그도 없다.
  - `Play_IsAlwaysTwoD_PlayAtFollowsDefinition`
    - `Play("fx.3d")` → `SpatialMode.None`
    - `PlayAt("fx.3d", new Vector3(1, 2, 0))` → `Positional`, 위치 `(1, 2, 0)`
    - `PlayAt("fx.2d", Vector3.one)` → `None`
  - `PlayMusic_SameKeyKeepsChannel_UnknownKeyStops`
    - `PlayMusic("bgm")`를 두 번 불러도 `Current.ActiveMusic == 0`이다(교차 페이드로 채널 1로 가지 않음).
    - `PlayMusic("missing")` 뒤 `Current.IsMusicPlaying == false`.
    - 다시 `PlayMusic("bgm")` 후 `StopMusic()` 뒤 `IsMusicPlaying == false`.
  - `PlayMusic_UsesConfiguredFade`: `MusicFadeSeconds = 0.5f`이면 `Current.MusicChannels[0].State == MusicState.FadingIn`.
  - `InitializeTwice_Throws_ShutdownAllowsReinitialize`
    - 두 번째 호출은 `InvalidOperationException`을 던진다.
    - `Shutdown` 뒤 다시 `Initialize`하면 성공한다.
  - `InitializeWithInvalidCatalog_ThrowsAndStaysUninitialized`
    - 리플렉션으로 `entries`에 정의가 null인 항목을 넣는다.
    - `ArgumentException`을 던지고 `Current == null`이다.
  - `Initialize_RunsPrepareHookPerDefinition_LogsFailureAndContinues`
    - 훅은 정의 세 개를 모두 받는다.
    - `fx.3d`에서는 `ArgumentException`을 던진다.
    - `LogAssert.Expect(LogType.Exception, new Regex("ArgumentException"))`, `Current != null`.
  - `OnDisposed_RunsOnceOnShutdown`: 카운터가 1이다. `Shutdown`을 다시 불러도 1이다.
  - `DisposingCurrent_ClearsStaticState` (Review Focus 2)
    - `Current.Dispose()` 뒤 `Current == null`.
    - `Play("fx.2d")`가 예외 없이 끝나고 경고를 한 번 기대한다.
  - `StaticKeyPlayback_DoesNotAllocate`
    - `CreateVoiceOutput` 없는 설정을 쓰고 한 번 예열한다.
    - 알려진 할당이 감지되는지 먼저 확인한다(`AllocationTests` 방식).
    - `Play("fx.2d")`와 `PlayAt("fx.3d", v)`가 `Is.Not.AllocatingGCMemory()`를 만족한다.

- [ ] **Step 2: 실패 확인**

  `runtests.py PlayMode SoundSystemStaticTests` → `Initialize` 등이 없어 컴파일 실패.

- [ ] **Step 3: 구현**
  - `SoundSystem.Static.cs` 정적 필드: `_catalog`, `_music`(`SoundDef`), `_warnedUninitialized`.
  - `Initialize` 순서:
    1. null 인자는 `ArgumentNullException`을 던진다.
    2. 이미 `Current`가 있으면 `InvalidOperationException`을 던진다.
    3. `catalog.ValidateOrThrow()`
    4. `new SoundSystem(config)`
    5. `Prepare(catalog)`. 이어서 정의마다 `config.PrepareDefinition`을 try/catch(`Exception`)로 감싸 부르고, 실패는 `Debug.LogException`으로 남긴다.
       - 4~5에서 예외가 나면 인스턴스를 해제한 뒤 다시 던진다.
    6. 정적 상태를 설정한다.
    7. `Application.quitting -= Shutdown; Application.quitting += Shutdown;`
  - `Play`: `Current.Play(def, Vector3.zero, SpatialMode.None)`
  - `PlayAt`: `Current.Play(def, position, def.EffectiveSpatial == SpatialMode.None ? SpatialMode.None : SpatialMode.Positional)`
  - `PlayMusic`
    - 없는 키는 `StopMusic()`으로 처리한다.
    - `def == _music && Current.IsMusicPlaying`이면 그대로 둔다.
    - 그 밖에는 `Current.PlayMusic(def, _config.MusicFadeSeconds)`
  - `StopMusic`: `Current.StopMusic(_config.MusicFadeSeconds)`
  - 초기화 전 경고: `[Conditional("UNITY_EDITOR")]` 메서드로 처음 한 번만 `Debug.LogWarning("SoundSystem: static playback requested before SoundSystem.Initialize; ignored.")`를 남긴다.
  - `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]`로 정적 필드를 비운다.
  - `Dispose()` 끝에서 두 가지를 한다.
    - `Current == this`이면 정적 상태를 비운다.
    - `_config.OnDisposed?.Invoke()`
  - 생성자의 기존 catch에서도 `OnDisposed`를 부른다.
  - 이름 충돌 처리
    - 인스턴스 메서드 안에서 한정자 없이 `StopMusic()`을 부르면 인자 없는 정적 `StopMusic()`이 선택된다. 그러면 자기 음악이 아니라 `Current`의 음악을 끈다.
    - 그래서 `SoundSystem.Music.cs`의 `WithMusicCancellation`에 있는 `StopMusic();`을 `StopMusic(0f);`로 바꾼다. 별도 테스트는 두지 않는다.
    - `grep -n "StopMusic()" com.bun3.unity.audio/Runtime`으로 나머지 한정자 없는 호출이 없는지 확인한다.
  - 문서
    - `package.json`을 0.7.0으로 올린다.
    - CHANGELOG에 `## [0.7.0]` Added 항목을 쓴다.
    - README Quick start에 정적 진입 예시를 넣는다(카탈로그, 설정, `Initialize`, `Play`/`PlayAt`/`PlayMusic`).

- [ ] **Step 4: 통과 확인**

  `runtests.py PlayMode SoundSystemStaticTests` 통과. 회귀 확인으로 `runtests.py PlayMode Bun3.Unity.Audio.Tests`도 전부 통과해야 한다.

- [ ] **Step 5: 커밋 (kit)**

### Task 2: 에디터에서 바뀐 정의 다시 준비 (`com.bun3.unity.audio`)

**Files:**
- Modify: `SoundSystem.Static.cs`, `SoundSystem.Tick.cs` (`Tick` 끝)
- Test: `Tests/Runtime/SoundSystemStaticTests.cs`에 추가

**Interfaces:**
- Consumes: Task 1의 `Initialize`, `PrepareDefinition`.
- Produces: 없음(내부 동작).

- [ ] **Step 1: 실패하는 테스트 작성**
  - `EditedClips_AreRepreparedOnNextTick`
    1. 훅이 정의별 호출 수를 센다.
    2. `fx.3d.Clips`를 새 클립 배열로 바꾸고 `Current.Tick(0.016f)`를 부르면 `fx.3d` 호출 수가 2다.
    3. 다시 `Tick`해도 2다.
  - `EditedSpatialMode_IsRepreparedOnNextTick`: `Spatial`만 바꿔도 다시 준비된다.
  - `UnchangedDefinitionsCheck_DoesNotAllocate`: 예열 뒤 `Current.Tick(0.016f)`가 `Is.Not.AllocatingGCMemory()`를 만족한다.
- [ ] **Step 2: 실패 확인** (`runtests.py PlayMode SoundSystemStaticTests`)
- [ ] **Step 3: 구현**
  - `#if UNITY_EDITOR`에서만 동작한다.
  - `Initialize` 때 정의마다 스냅샷(정의, `PlaybackClips` 복사 배열, `EffectiveSpatial`)을 만든다.
  - `Tick` 끝에서 `ReferenceEquals(this, Current)`일 때만 스냅샷을 비교한다(개수, 클립 참조, 공간 모드).
  - 바뀐 정의만 다시 캡처하고, `Prepare(def)`와 훅(try/catch, 로그)을 부른다.
  - 비교는 할당하지 않는다. 다시 캡처하는 경로는 할당을 허용한다.
- [ ] **Step 4: 통과 확인** (`SoundSystemStaticTests`, `Bun3.Unity.Audio.Tests`)
- [ ] **Step 5: 커밋 (kit)**

### Task 3: 장치 변경 복구 (`com.bun3.unity.audio`)

**Files:**
- Modify:
  - `MusicChannel.cs`: `MusicChannel`에 필드 추가.
  - `SoundSystem.Music.cs`: 위치 기억, 복구, `ResumeMusic`.
  - `SoundSystem.cs`: 이벤트 구독·해제, 효과음 복구.
  - `CHANGELOG.md`, `README.md`: Lifetime 절에 장치 변경 동작.
- Create: `Tests/Runtime/AudioResetBehaviourTests.cs`, `Tests/Runtime/SoundSystemDeviceRecoveryTests.cs`

**Interfaces:**
- Produces:
  - `MusicChannel`: `public bool InIntro; public int PlayheadSamples; public bool RecoverOnResume;`
  - `internal void RecoverLostPlayback()`: 이벤트 핸들러가 부른다. 테스트도 직접 부른다.
  - `private const float MusicRecoveryFadeSeconds = 0.05f;`

- [ ] **Step 1: Unity 동작 확인 테스트 작성**

  `AudioResetBehaviourTests.Reset_StopsPlayingSourceAndReportsScriptReset` ([UnityTest])
  1. 1초 클립을 쓰는 AudioSource를 `Play`하고 한 프레임 기다린다.
  2. 핸들러를 구독하고 `AudioSettings.Reset(AudioSettings.GetConfiguration())`를 부른다.
  3. 확인: 핸들러가 `false`로 한 번 불리고, `source.isPlaying == false`다.

  이 테스트가 실패하면(Reset 뒤에도 소스가 계속 재생되면) 멈추고 보고한다. 아래 실제 Reset 통합 테스트의 전제가 무너지기 때문이다.

- [ ] **Step 2: 복구 테스트 작성**

  `SoundSystemDeviceRecoveryTests`에 넣는다. 상태 손실은 해당 소스를 `Stop()`해서 흉내 낸 뒤 `RecoverLostPlayback()`을 부른다.

  **효과음**
  - `LostLoopVoice_IsCompletedAndSlotReusable`
    - `SfxVoices = 1`, `Loop` 정의로 재생한 뒤 소스를 멈춘다.
    - 복구 뒤 `Table.Slots[0].State == VoiceState.Idle`이고 완료 콜백이 한 번 불린다.
    - 새 `Play`가 유효한 핸들을 돌려준다.
  - `PlayingVoice_IsUntouched`: 멈추지 않은 반복 음성은 복구 뒤에도 슬롯이 활성이고 `isPlaying`이다.
  - `LostExternalOutputVoice_IsCompleted`
    - 가짜 출력이 `Started`를 돌려주고 `IsComplete == false`다.
    - 소스를 멈추고 복구하면 슬롯이 비고 출력 `Retire`가 불린다.
  - `ListenerPausedVoices_AreNotCompletedOnRecovery` (Review Focus 5)
    - `AudioListener.pause = true` 상태에서 반복 음성이 복구 뒤에도 활성이다.
    - finally에서 `AudioListener.pause = false`.

  **배경음** ([UnityTest], 10초 클립, 0.3초 이상 재생한 뒤)
  - `LostMusicLoop_ResumesNearCachedPosition`
    - 기억 위치 `cached = MusicChannels[0].PlayheadSamples`
    - 복구 뒤 루프 소스가 `isPlaying`이다.
    - `timeSamples`가 `cached` 이상이고 `cached + 0.1 * frequency` 이하다.
    - `State == FadingIn`이고 `Fade.Factor < 1`.
  - `LostIntro_ReschedulesLoopFromRemainingIntro`
    - 인트로 2초, 루프 2초인 `MusicDef`를 쓴다.
    - 복구 뒤 인트로 `timeSamples ≈ cached`이고 `LoopScheduled`다.
    - `LoopStartDsp - dspTime`이 남은 인트로 시간 ±0.1초다.
  - `PausedMusic_StaysPausedAndRecoversOnResume`
    - `PauseMusic` 뒤 소스를 멈추고 복구하면 `IsMusicPaused`이고 소스는 꺼져 있다.
    - `ResumeMusic` 뒤 `timeSamples ≈ cached`로 재생된다.
  - `PlayingMusic_IsUntouched`: 멈추지 않으면 `State == Playing`이고 위치가 0으로 돌아가지 않는다.
  - `CrossfadeDuringLoss_SilencesOutgoingAndResumesIncoming` (Review Focus 3)
    - 곡 A 재생 중 1초 페이드로 곡 B를 튼 뒤 두 채널 소스를 멈춘다.
    - 복구 뒤 나가던 채널은 `Idle`, 들어오던 채널은 재생 중이다.
  - `MusicPlayheadCache_DoesNotAllocate`: 음악 재생 중 `Tick(0.016f)`가 무할당이다.

  **실제 Reset 통합**
  - `RealAudioReset_ResumesMusicAndFreesLostVoices` ([UnityTest])
    1. 배경음과 반복 효과음을 재생하고 0.3초 기다린다.
    2. `AudioSettings.Reset(GetConfiguration())`를 부르고 두 프레임 기다린다.
    3. 확인: 배경음이 재생 중이고 위치가 0.2초 이상이다. 효과음 슬롯은 `Idle`이다.

- [ ] **Step 3: 실패 확인** (`runtests.py PlayMode AudioResetBehaviourTests`, `SoundSystemDeviceRecoveryTests`)

- [ ] **Step 4: 구현**

  **구독**
  - 생성자 끝(`Live.Add` 뒤)에서 캐시한 델리게이트로 `AudioSettings.OnAudioConfigurationChanged`를 구독한다.
  - `Dispose`에서 해제한다.
  - 핸들러는 인자와 상관없이 `RecoverLostPlayback()`을 부른다.

  **위치 기억**
  - `AdvanceMusicChannel`에서 일시정지가 아닌 활성 채널마다 매 틱 기록한다.
    - `InIntro = intro 소스에 clip이 있고 LoopScheduled && dspTime < LoopStartDsp`
    - `PlayheadSamples = InIntro ? intro.timeSamples : loop.timeSamples`
  - `StartMusicOnChannel`에서 초기화한다: `InIntro = intro != null`, `PlayheadSamples = 0`, `RecoverOnResume = false`.
  - `SilenceMusicChannel`에서 지운다.

  **`RecoverLostPlayback()`** (할당 허용, 지역 리스트 사용)
  - 효과음: `!AudioListener.pause`일 때만 처리한다.
    - 활성 슬롯 중 `!_sources[i].isPlaying`인 것을 Tick과 같은 2단계로 처리한다. 먼저 수집·Release·`RetireVoiceOutput`·`Stop`을 한 뒤, 마지막에 완료와 콜백을 보낸다.
  - 배경음: 채널별로 처리한다.
    - `FadingOut`: `SilenceMusicChannel`로 정리하고 완료는 마지막에 보낸다.
    - `Paused`: `RecoverOnResume = true`만 표시한다.
    - 그 밖에 들리던 소스(`InIntro ? intro : loop`)가 `!isPlaying`이면 `RestoreMusicChannel(i)`을 부른다.

  **`RestoreMusicChannel(i)`**
  - `InIntro`일 때
    1. `intro.timeSamples = PlayheadSamples; intro.Play(); loop.Stop();`
    2. `LoopStartDsp = dspTime + MusicMath.RemainingSeconds(PlayheadSamples, intro.samples, intro.frequency)`
    3. `loop.PlayScheduled(LoopStartDsp); LoopScheduled = true`
  - 아닐 때: `loop.timeSamples = PlayheadSamples % loop.clip.samples; loop.Play(); LoopScheduled = true`
  - 페이드
    - `Playing`이면 `Fade.Begin(0, 1, MusicRecoveryFadeSeconds)` 후 `State = FadingIn`.
    - `FadingIn`이면 기존 페이드를 유지한다.
  - 끝에서 `ApplyMusicVolume(i)`.

  **`ResumeMusic`**
  - `UnPause` 뒤 `RecoverOnResume`이면 플래그를 지운다.
  - 들리던 소스가 `!isPlaying`이면 `RestoreMusicChannel`을 부르고, 기존 루프 재예약 분기는 건너뛴다.

  **문서**
  - CHANGELOG [0.7.0]에 장치 변경 복구를 추가한다.
  - README "Lifetime, threading and performance"에 동작을 적는다: 멈춘 것만 복구, 배경음 이어 재생과 짧은 페이드인, 효과음은 이어 틀지 않음.

- [ ] **Step 5: 통과 확인** (위 두 클래스, `Bun3.Unity.Audio.Tests` 전체)
- [ ] **Step 6: 커밋 (kit)**

### Task 4: Steam Audio 공유 런타임 (`com.bun3.unity.audio.steamaudio`)

**Files:**
- Create: `com.bun3.unity.audio.steamaudio/Runtime/SteamAudioSoundRuntime.cs`
- Modify:
  - `Runtime/SteamAudioSoundOutput.cs`
  - `Runtime/SteamAudioSoundDriver.cs`: 정리 호스트가 런타임에 바인딩된다.
  - `Runtime/SteamAudioPathRenderer.cs`: 테스트용 핸들 접근자.
  - `package.json`(0.6.0, 의존성 audio 0.7.0), `CHANGELOG.md`
- Test: `Tests/PlayMode/SteamAudioSoundRuntimeTests.cs`. 기존 `SteamAudioSoundOutputTests` 전체도 회귀로 돌린다.

**Interfaces:**
- Produces (`internal sealed class SteamAudioSoundRuntime : IDisposable`):
  - `internal SteamAudioSoundRuntime()`: 현재 `AudioSettings.outputSampleRate`와 `GetDSPBufferSize`로 아래를 만든다.
    - `SA.Context`
    - `SA.HRTF`(인자는 `SteamAudioPathRenderer.CreateDefault`와 같다)
    - 드라이버 클립 `"Bun3 native SFX driver"`(frame, 2ch, rate, 1f로 채움)
    - 숨김·DontDestroyOnLoad 오브젝트 `"Bun3 native sound retirement"` 하나
  - `internal int SampleRate { get; }`, `internal int FrameSize { get; }`, `internal AudioClip DriverClip { get; }`
  - `internal SteamAudioPathRenderer CreateRenderer()`
  - `internal void Attach(SteamAudioSoundOutput output)`, `internal void Detach(SteamAudioSoundOutput output)`
  - `public void Dispose()`: HRTF·컨텍스트 해제, 클립·호스트 파괴.
- Produces (`SteamAudioSoundOutput`):
  - `internal SteamAudioSoundOutput(AudioSource source, SteamAudioClipCache clips, SteamAudioSoundRuntime runtime)`
  - 기존 public 생성자는 시그니처와 동작을 유지한다.
    - 자기 전용 런타임을 만들어 소유하고, 마지막 해제 때 런타임도 해제한다.
    - `createRenderer`가 있으면 렌더러만 그 팩토리로 만든다.
- Produces (`SteamAudioPathRenderer`): `internal IntPtr HrtfHandleForTest => _hrtf;`

- [ ] **Step 1: 실패하는 테스트 작성**
  - `SharedRuntime_OutputsShareHrtfAndDriverClip`
    1. 런타임 하나로 출력 둘을 만들고, 캐시에 준비한 위치 정의로 둘 다 `TryStart`한다.
    2. 두 소스의 `clip`이 `runtime.DriverClip`과 같은 인스턴스다.
    3. 두 렌더러의 `HrtfHandleForTest`가 같다.
  - `SharedRuntime_CreatesOneRetirementHost`: 출력 3개를 만들어도 `Resources.FindObjectsOfTypeAll<SteamAudioSoundRetirementHost>().Length` 증가분이 1이다. 숨김 오브젝트도 세려고 이 API를 쓴다.
  - `OutputDispose_KeepsSharedResources_RuntimeDisposeReleasesThem`
    - 출력을 모두 해제해도 `DriverClip != null`이다.
    - `runtime.Dispose()` 뒤 클립과 호스트가 파괴된다(다음 프레임에 `== null`).
- [ ] **Step 2: 실패 확인** (`runtests.py PlayMode SteamAudioSoundRuntimeTests`)
- [ ] **Step 3: 구현**
  - 출력의 생성자 안 자원 생성(컨텍스트, HRTF, 드라이버 클립, 정리 호스트)을 런타임으로 옮긴다.
  - `TryStart`는 `_source.clip = _runtime.DriverClip`를 쓰고, 시작한 클립을 `_startedDriverClip`에 기억한다.
  - `Dispose`는 소스가 `_startedDriverClip`을 재생 중일 때만 멈춘다.
  - `TryFinalizeDisposal`:
    - 렌더러를 해제하고 `_runtime.Detach(this)`를 부른다.
    - 전용 런타임이면 `Dispose`한다.
    - 공유 클립과 호스트는 파괴하지 않는다.
  - 정리 호스트의 `Update`는 런타임에 붙은 출력마다 `ControlUpdate()`를 부른다. 해제 중 목록이 바뀌므로 뒤에서부터 돈다.
  - 문서: `package.json` 0.6.0과 의존성 갱신, CHANGELOG `## [0.6.0]` Changed(HRTF·드라이버 클립·정리 오브젝트 공유).
- [ ] **Step 4: 통과 확인** (`SteamAudioSoundRuntimeTests`, `SteamAudioSoundOutputTests`, `Bun3.Unity.Audio.SteamAudio` 전체)
- [ ] **Step 5: 커밋 (kit)**

### Task 5: 조립 함수 `ApplyNativeOutput` (`com.bun3.unity.audio.steamaudio`)

**Files:**
- Modify: `Runtime/SteamAudioSoundSetup.cs`, `README.md`("Native pooled SFX output" 절), `CHANGELOG.md`
- Test: `Tests/PlayMode/SteamAudioNativeOutputSetupTests.cs`

**Interfaces:**
- Consumes:
  - Task 1의 `PrepareDefinition`, `OnDisposed`, 정적 API
  - Task 4의 런타임과 내부 생성자
- Produces: `public static SoundSystemConfig ApplyNativeOutput(SoundSystemConfig config, long maximumDecodedBytes, PlanarAcousticOutputs acousticOutputs)`
  - 예외
    - `config`나 `acousticOutputs`가 null이면 `ArgumentNullException`.
    - 바이트가 0 이하면 `ArgumentOutOfRangeException`.
    - `config.CreateVoiceOutput`가 이미 있으면 `InvalidOperationException`.
  - 하는 일
    - `OcclusionChecksPerFrame = 0`으로 둔다.
    - `CreateVoiceOutput`, `PrepareDefinition +=`, `OnDisposed +=`에 비공개 조립 객체를 연결한다.
  - XML 문서에 기존 `Apply`(Valve `SteamAudioSource` 경로)와 다른 경로임을 적는다.

- [ ] **Step 1: 실패하는 테스트 작성**

  픽스처: `PlanarAcousticOutputs` 새 인스턴스, 위치 정의 `fx.3d`와 2D 정의 `fx.2d`. TearDown에서 `Shutdown`한다.

  - `ApplyNativeOutput_WiresConfig`
    - 네 필드(오클루전 0, 출력, 준비 훅, 해제 훅)가 연결된다.
    - `CreateVoiceOutput`가 미리 있으면 `InvalidOperationException`.
  - `InitializedSystem_RegistersOneBindingPerVoice`: `SfxVoices = 3`으로 `Initialize`하면 `acousticOutputs.Items.Count == 3`.
  - `PlayAt_UsesNativeDriver_Play_UsesAuthoredClip`
    - `PlayAt("fx.3d", p)` 뒤 해당 소스 `clip.name == "Bun3 native SFX driver"`.
    - `Play("fx.2d")` 뒤 소스 `clip`이 저작 클립이다.
  - `Shutdown_UnregistersBindingsAndReleasesRuntime`
    - `Items.Count == 0`.
    - 정리 호스트 수가 기준선으로 돌아온다.
  - `ReinitializeWithSameConfig_BuildsFreshRuntime`: `Shutdown` 뒤 같은 설정 객체로 다시 `Initialize`하면 `Items.Count == 3`이고 `PlayAt`이 동작한다.
- [ ] **Step 2: 실패 확인** (`runtests.py PlayMode SteamAudioNativeOutputSetupTests`)
- [ ] **Step 3: 구현**
  - 비공개 조립 객체가 아래를 갖는다.
    - 런타임과 `SteamAudioClipCache(maximumDecodedBytes)`: 첫 `CreateVoiceOutput` 때 늦게 만든다.
    - 바인딩 목록
  - `CreateVoiceOutput` 처리 순서
    1. 런타임과 캐시를 보장한다.
    2. `new SteamAudioSoundOutput(source, cache, runtime)`
    3. `new PlanarAcousticSfxBinding(source, output)`을 만들어 `acousticOutputs.Register`하고 목록에 더한다.
    4. 출력을 돌려준다.
  - 준비 훅: `cache.PrepareDefinition(def)`
  - 해제 훅
    1. 바인딩마다 `acousticOutputs.Unregister`
    2. 캐시와 런타임 `Dispose`
    3. 필드를 null로 돌린다(같은 설정 재사용 대비).
  - 문서: README "Native pooled SFX output"에 `ApplyNativeOutput` → `SoundSystem.Initialize` 예시를 넣고, CHANGELOG [0.6.0] Added에 적는다.
- [ ] **Step 4: 통과 확인** (위 클래스, `Bun3.Unity.Audio.SteamAudio` 전체)
- [ ] **Step 5: 커밋 (kit)**

### Task 6: Steam Audio 장치 변경 (`com.bun3.unity.audio.steamaudio`)

**Files:**
- Modify:
  - `Runtime/SteamAudioSoundRuntime.cs`
  - `Runtime/SteamAudioSoundOutput.cs`: 렌더러 교체, 장치 변경으로 끝내기.
  - `SteamAudioSoundFailure.OutputFormatChanged` XML 문서 수정: "The output rate, DSP frame size or audio device changed while this request was active; the request ended."
  - `CHANGELOG.md`
- Test: `Tests/PlayMode/SteamAudioDeviceChangeTests.cs`

**Interfaces:**
- Produces:
  - `internal void HandleAudioConfigurationChanged(int sampleRate, int frameSize)`
    - 이벤트 핸들러는 현재 `AudioSettings` 값으로 부른다.
    - 테스트는 가짜 형식으로 부른다.
  - `SteamAudioSoundOutput.EndForDeviceChange()` (internal): 활성 요청만 `Fail(OutputFormatChanged)` 후 `Retire()`한다. 그러면 `IsComplete`가 참이 되어 코어가 다음 틱에 슬롯을 끝낸다.
  - `SteamAudioSoundOutput.TryReplaceRenderer(SteamAudioPathRenderer renderer)` (internal, bool): 상태가 0이나 3일 때만 교체한다.
    - 프레임이나 계수 수가 바뀌면 `_mono`·`_stereo`·`_coefficients`를 다시 할당한다.
    - `_rate`·`_frame`을 갱신하고, 생성자처럼 Render 후 Reset으로 예열한 뒤 옛 렌더러를 해제한다.
    - 리더가 있으면 false를 돌려준다.

- [ ] **Step 1: 실패하는 테스트 작성**

  런타임 하나와 출력 둘을 쓴다. 하나는 위치 재생을 시작해 둔다.

  - `SameFormat_IntactDriverClip_ChangesNothing`: 같은 형식으로 부르면 다음이 그대로다.
    - `DriverClip` 인스턴스
    - HRTF 핸들
    - 활성 출력의 `IsComplete == false`
  - `SameFormat_LostDriverClip_RecreatesClipAndEndsActiveOutputs`
    - `DriverClip.SetData`를 0으로 채워 데이터 손실을 흉내 낸다.
    - 결과: 새 클립 인스턴스의 첫 샘플이 1이다.
    - 활성 출력은 `IsComplete`이고 `Failure == OutputFormatChanged`다.
    - HRTF 핸들은 같다.
  - `FormatChange_SwapsHrtfAndRenderersOnly`
    - 다른 샘플레이트(현재가 48000이면 44100, 아니면 48000)로 부른다.
    - 바뀌는 것: 각 출력 렌더러의 `SampleRate`가 가짜 값이 되고 HRTF 핸들이 바뀐다.
    - 그대로인 것: AudioSource 인스턴스, `cache.DecodedBytes`.
  - `FormatRestore_AllowsPositionalPlaybackAgain`: 실제 형식으로 다시 부르면 위치 `TryStart`가 `Started`다.
  - `ConsecutiveFormatChanges_EndOnLatestFormat` (Review Focus 4)
    - 리더를 잡은 상태(`TryClaimReader`)에서 형식 A, 이어서 B로 부른다.
    - `ReleaseReader` 뒤 정리 호스트 `Update` 한 프레임이 지나면 렌더러가 B 형식이다.
  - `RealAudioReset_PositionalPlaybackWorksAfterwards` ([UnityTest])
    1. `ApplyNativeOutput` + `Initialize` 후 `PlayAt`을 부른다.
    2. `AudioSettings.Reset(GetConfiguration())`를 부르고 두 프레임 기다린다.
    3. 다시 `PlayAt`하면 해당 소스가 드라이버 클립으로 `isPlaying`이다.
- [ ] **Step 2: 실패 확인** (`runtests.py PlayMode SteamAudioDeviceChangeTests`)
- [ ] **Step 3: 구현**
  - 런타임 생성자에서 캐시한 델리게이트로 `OnAudioConfigurationChanged`를 구독하고 `Dispose`에서 해제한다.
  - `HandleAudioConfigurationChanged(rate, frame)` 처리 순서
    1. 형식이 바뀌었을 때
       1. 새 HRTF를 만든다.
       2. 출력마다 `EndForDeviceChange()` 후 `TryReplaceRenderer(새 렌더러)`를 부른다. 실패하면 보류 목록에 넣는다.
       3. 옛 HRTF를 놓는다. 렌더러가 HRTF를 retain하므로 안전하다.
       4. 드라이버 클립도 새 형식으로 다시 만든다.
    2. 형식이 같을 때
       - 클립 손실 판정: `DriverClip == null`이거나 미리 할당한 2칸 버퍼로 `GetData` 한 첫 샘플이 `1f`가 아니면 잃은 것이다.
       - 잃었으면 활성 출력을 `EndForDeviceChange()`로 끝낸 뒤 클립을 다시 만든다.
    3. 보류 목록은 정리 호스트 `Update`에서 다시 시도한다. 새 이벤트가 오면 최신 렌더러로 바꿔치기한다.
  - 기존 `ControlUpdate`의 형식 불일치 처리는 안전망으로 남긴다.
  - 문서: CHANGELOG [0.6.0] Added에 장치 변경 처리를 적는다.
- [ ] **Step 4: 통과 확인** (위 클래스, `Bun3.Unity.Audio.SteamAudio` 전체, `Bun3.Unity.Audio.Tests` 전체)
- [ ] **Step 5: 커밋 (kit)**

### Task 7: JP 사운드 조립과 초기화 위치 (JP)

**Files:**
- Create:
  - `Scripts/Audio/SoundSetup.cs`
  - `Tests/PlayMode/SoundSetupPlayModeTests.cs`
- Modify:
  - `Scripts/Core/GameConfig.cs`: `SoundChannelCount` 근처에 필드.
  - `Scripts/Audio/GameAcousticSession.cs`
  - `Scripts/Core/Boot.cs`
  - `Editor/M1EditorSetup.cs`: Game 씬 생성부(지금 SoundManager를 만드는 곳, 약 2348행)에서 `GameConfig.asset`의 카탈로그를 연결하고, `GeneratedAssetVersion`을 806에서 807로 올린다.
  - `Tests/EditMode/Audio/GeneratedAudioAssetsTests.cs`
  - `Data/`·`Resources/GameConfig.asset`: 셋업 실행 결과.

**Interfaces:**
- Consumes: kit `SoundSystem.Initialize/Current/Shutdown`, `SteamAudioSoundSetup.ApplyNativeOutput`
- Produces:
  - `GameConfig`: `[Tooltip("효과음·배경음 문자열 키 카탈로그. Boot와 Menu 시작 시 사운드 초기화에 사용합니다.")] public SoundCatalog SoundCatalog;`
  - `GameAcousticSession`: `internal static PlanarAcousticOutputs OutputRegistry => outputs;`
  - `internal static class SoundSetup` / `internal static void EnsureInitialized(GameConfig config)`
    - `SoundSystem.Current != null`이면 아무것도 하지 않는다.
    - 아니면 스펙 "초기화 위치"의 예시 그대로 설정을 조립하고, `ApplyNativeOutput`과 `SoundSystem.Initialize(config.SoundCatalog, settings)`를 부른다.
    - 설정 값: `SfxVoices = SoundChannelCount`, `GroupGain = PersonalSettings.SoundGain`, `MusicFadeSeconds`, 캐시 `SoundDecodedCacheMiB * 1024 * 1024`.
  - `internal static void InitializeIfPlayStartedFromMenu(string startSceneName, GameConfig config)`: `startSceneName == SceneFlow.Menu`일 때만 `EnsureInitialized(config)`를 부른다.
  - `#if UNITY_EDITOR` 블록의 `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)] static void OnPlayStarted()`: `InitializeIfPlayStartedFromMenu(SceneManager.GetActiveScene().name, GameConfig.I)`를 부른다.

- [ ] **Step 1: 실패하는 테스트 작성**
  - `SoundSetupPlayModeTests`: 모든 테스트는 TearDown에서 `SoundSystem.Shutdown()`을 부른다.
    - `EnsureInitialized_InitializesNativeOutputOnce`
      - 테스트용 `GameConfig`(`CreateInstance`)를 쓴다. 위치 정의 카탈로그, `SoundChannelCount = 2`, `SoundDecodedCacheMiB = 1`.
      - 결과: `Current != null`, `GameAcousticSession.Outputs.Count` 증가분 2.
      - 두 번째 호출은 예외가 없고 `Current`가 같다.
      - `Shutdown` 뒤 증가분이 0이다.
    - `PlayStartedFromMenu_Initializes`: `InitializeIfPlayStartedFromMenu(SceneFlow.Menu, 테스트 GameConfig)` 뒤 `Current != null`.
    - `PlayStartedFromOtherScene_DoesNotInitialize` (Review Focus 1)
      - `InitializeIfPlayStartedFromMenu("InitTestScene", 테스트 GameConfig)` 뒤 `Current == null`.
      - 테스트 러너로 이 테스트에 도달했다는 것 자체가 Play 시작 훅이 아무것도 켜지 않았다는 뜻이다. 그래서 테스트 시작 시점에도 `Current == null`을 단언한다.
    - `BootScene_StartsWithSound` ([UnityTest])
      - `"Boot"` 씬을 불러오고, 활성 씬이 `SceneFlow.Menu`가 될 때까지 최대 30초 기다린 뒤 `Current != null`.
      - 테스트에서는 Play 시작 훅이 동작하지 않으므로, 이 결과는 Boot가 초기화했다는 뜻이다.
      - 출고 `GameConfig`를 쓰는 씬 통합 검사이고, `MapStartupTests`와 같은 방식이다.
  - `GeneratedAudioAssetsTests.GameConfig_ReferencesShippingSoundCatalog`: `GameConfig.asset`의 `SoundCatalog`가 `Assets/_Game/Data/Audio/SoundCatalog.asset`과 같다.
- [ ] **Step 2: 실패 확인** (`runtests.py PlayMode SoundSetupPlayModeTests`, `runtests.py EditMode GeneratedAudioAssetsTests`)
- [ ] **Step 3: 구현**
  1. 위 Interfaces대로 구현한다.
  2. `Boot.Start`에서 `InitializePlatform` 성공 뒤, `SceneFlow.LoadAsync(Menu)` 앞에서 `SoundSetup.EnsureInitialized(GameConfig.I)`를 부른다.
  3. `SoundSetup`에 에디터 전용 Play 시작 훅을 둔다.
  4. `M1EditorSetup`에서 `GameConfig.asset`을 불러 `SoundCatalog`에 `EnsureSoundCatalog()` 결과를 연결하고 SetDirty·저장한다. GAV는 807로 올린다.
  5. 에디터에서 `JP/M1 Setup 실행`을 돌려 산출물을 갱신한다.
  6. 이 시점에는 Game 씬 SoundManager가 남아 있다. 같이 동작하지만 서로 독립이다. Task 8에서 지운다.
- [ ] **Step 4: 통과 확인**
  - 위 테스트가 통과해야 한다.
  - `runtests.py EditMode Audio`, `runtests.py PlayMode MapStartupTests`도 통과해야 한다.
  - 다시 저장된 출고 에셋이 있으면 경로를 지정해 되돌린다.
- [ ] **Step 5: 커밋 (JP)**

### Task 8: SoundManager 제거와 호출부 전환 (JP)

**Files:**
- Delete: `Scripts/Audio/SoundManager.cs`, `Scripts/Audio/Bun3SoundOutput.cs`, `Scripts/Audio/GameAcousticSfxBinding.cs`와 각 `.meta`, `Tests/EditMode/Audio/SoundManagerTests.cs`, `Tests/PlayMode/SoundOutputPlayModeTests.cs`와 각 `.meta`
- Modify (호출부 10개):
  - `Scripts/UI/CctvHudController.cs`, `HudController.cs`, `PrisonView.cs`, `RootBoxHudController.cs`, `SessionSequenceView.cs`
  - `Scripts/View/DinoView.cs`, `FireflyMissionView.cs`, `GateDoorView.cs`, `PlayerView.cs`, `SirenView.cs`
  - 바꿀 내용: `SoundManager.Play(` → `SoundSystem.Play(`, `SoundManager.PlayAt(` → `SoundSystem.PlayAt(`. 필요하면 `using Bun3.Unity.Audio;`를 더한다. `Vector2` 위치는 암시적 변환으로 넘긴다.
- Modify (셋업과 버전):
  - `Editor/M1EditorSetup.cs`: SoundManager 오브젝트 생성 세 줄을 지우고, 카탈로그 연결은 유지한다. GAV는 807에서 808로 올린다.
  - `Editor/CctvEditorSetup.cs:177`: SoundManager 줄을 지운다.
  - `ProjectSettings/ProjectSettings.asset`: `bundleVersion: 0.41.3`
- Create: `Tests/PlayMode/SoundPlaybackRecorder.cs`
- Modify (테스트):
  - `Tests/EditMode/JP.Tests.EditMode.asmdef`: references에 `"JP.Tests.PlayMode"`를 더한다.
  - `Tests/EditMode/Audio/RootBoxSoundTests.cs`
  - `Tests/PlayMode/MapStartupTests.cs`: 사이렌 검사 부분.
  - `Tests/EditMode/Audio/GeneratedAudioAssetsTests.cs`
- Modify (문서): `docs/sound-authoring-guide.md`: SoundManager 설명을 Boot/Menu 초기화, `GameConfig.SoundCatalog`, `SoundSystem.Play/PlayAt`으로 바꾼다.

**Interfaces:**
- Produces (`public sealed class SoundPlaybackRecorder : IDisposable`, 네임스페이스 `JP.Tests`)
  - `public static SoundPlaybackRecorder Install(params (string Key, SpatialMode Spatial)[] keys)`
    1. 키마다 테스트 클립과 정의를 만들어 카탈로그를 구성한다.
    2. 녹음 출력(`ISpatialSoundVoiceOutput`, `Unsupported` 반환)으로 `SoundSystem.Initialize`
  - `public int Count(string key)`, `public int PositionalCount(string key)`
  - `public void Dispose()`: `Shutdown`하고 생성 객체를 파괴한다.

- [ ] **Step 1: 테스트 전환 (실패 상태 작성)**
  - `RootBoxSoundTests`: SetUp을 `recorder = SoundPlaybackRecorder.Install((SoundKeys.BoxOpen, SpatialMode.None))`로, TearDown을 `recorder.Dispose()`로 바꾼다. 단언은 `recorder.Count(SoundKeys.BoxOpen)`으로 같은 값(0 다음 1)을 쓴다.
  - `MapStartupTests` 사이렌 검사
    - 해당 테스트 메서드 시작에 `using var sound = SoundPlaybackRecorder.Install((SoundKeys.SirenBroadcast, SpatialMode.Positional))`를 둔다.
    - `PositionalSoundPlayed` 구독을 지우고 `Assert.That(sound.PositionalCount(SoundKeys.SirenBroadcast), Is.GreaterThan(0))`로 바꾼다.
  - `GeneratedAudioAssetsTests.GeneratedGameScene_HasExactlyOneSoundManager`를 `GeneratedGameScene_HasNoSoundManagerObject`로 바꾼다.
    - 루트·자식 중 이름이 `"SoundManager"`인 오브젝트가 없다.
    - CCTV HUD에 AudioSource가 없다는 기존 단언은 유지한다.
  - 지우는 테스트 두 파일이 맡던 동작은 kit 테스트로 옮겨졌다.
    - 키 동작: Task 1
    - 장치 변경: Task 3·6
    - 준비 실패 격리: Task 1
    - 2D/네이티브 전환: Task 1·5
- [ ] **Step 2: 실패 확인**: 호출부가 아직 SoundManager를 써서 녹음기에 기록이 남지 않는다.
  - `runtests.py EditMode RootBoxSoundTests`: 기록 0으로 실패.
  - `runtests.py EditMode GeneratedAudioAssetsTests`: SoundManager 오브젝트가 있어 실패.
- [ ] **Step 3: 구현**
  1. 호출부 10개를 전환한다.
  2. 파일 3개를 지운다.
  3. 셋업 두 곳에서 SoundManager 생성을 지우고 GAV를 808로 올린다.
  4. `JP/M1 Setup 실행`으로 Game 씬을 다시 만든다.
  5. `CreateCctvTestHud`를 부르는 CCTV 스모크 씬 생성 메뉴도 다시 실행한다.
  6. `bundleVersion`을 0.41.3으로 바꾸고 문서를 갱신한다.
  7. `grep -rn "SoundManager" Scripts Editor Tests --include=*.cs | grep -v MMSoundManager` 결과가 0줄이어야 한다.
- [ ] **Step 4: 통과 확인**
  - `runtests.py EditMode Audio` 전부 통과.
  - `runtests.py EditMode BuildVersion` 통과.
  - `runtests.py PlayMode SoundSetupPlayModeTests`, `MapStartupTests` 통과.
  - 컴파일 경고가 새로 생기지 않는다.
  - 다시 저장된 출고 에셋은 경로를 지정해 되돌린다.
- [ ] **Step 5: 커밋 (JP)**

### Task 9: 통합 마무리 (사용자 확인 후)

**Files:**
- Modify: JP `unity/Packages/manifest.json`, `unity/Packages/packages-lock.json`, 필요하면 `unity/CLAUDE.md`(kit 참조 설명)

**Interfaces:** 없음.

- [ ] **Step 1: 사용자에게 확인을 받는다.** 결과를 보고하고 push와 main 머지 진행 여부를 묻는다. 답을 받기 전에는 아래 단계를 하지 않는다.
- [ ] **Step 2: kit 브랜치를 push하고 main에 머지한다.**
  1. `git push -u origin Bun3/audio-bootstrap`
  2. main에서 `--no-ff`로 머지한다. 메시지: `🔀 정적 진입점과 Steam Audio 조립(com.bun3.unity.audio 0.7.0, com.bun3.unity.audio.steamaudio 0.6.0)을 main에 통합`
  3. main을 push한다.
- [ ] **Step 3: JP를 kit main 참조로 되돌린다.**
  1. 두 항목을 `https://github.com/Bun3/bun3-kit.git?path=unity/Packages/<패키지>`로 되돌린다(브랜치 지정 없음).
  2. 에디터 재해석으로 lock이 새 main 해시를 가리키는지 PackageInfo로 확인한다.
- [ ] **Step 4: 다시 검증한다.**
  - `runtests.py PlayMode Bun3.Unity.Audio`
  - `runtests.py EditMode Audio`
  - `runtests.py PlayMode SoundSetupPlayModeTests`
  - `runtests.py PlayMode MapStartupTests`
- [ ] **Step 5: 커밋 (JP)**: manifest/lock 전환.
