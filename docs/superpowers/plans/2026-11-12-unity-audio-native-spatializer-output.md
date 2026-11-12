# Unity Audio 네이티브 스페이셜라이저 출력 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 오디오 스레드에서 bun3 C# 코드를 없애 GC 동안 배경음·효과음·음성이 끊기지 않게 한다.

**Architecture:** 공간화는 Steam Audio 공식 Unity 스페이셜라이저(`audioplugin_phonon.dll`)가 맡고, bun3 평면 음향 `IPLSource`를 `iplUnityAddSource`로 등록해 경로 출력을 렌더한다. 공식 플러그인에 경로 스테레오 폭 매개변수를 패치로 더한다. 효과음은 실제 클립을 Unity가 재생하고, 음성은 메인 스레드가 반복 클립 링에 `SetData`로 앞서 쓴다.

**Tech Stack:** Unity 6000.3, Steam Audio v4.8.1(패치 빌드), Dissonance, C# netstandard2.1/C#9, C++(MSVC), CMake, Python 빌드 스크립트.

**Spec:** `docs/superpowers/specs/2026-11-12-unity-audio-native-spatializer-output-design.md`

## Global Constraints

- 플랫폼: Windows x64만 패치 빌드. 다른 플랫폼은 원본 SDK 플러그인으로 동작하고 근거리 모노만 빠진다.
- 오디오 스레드에서 실행되는 bun3 C# 코드를 남기지 않는다. 새 `OnAudioFilterRead`, `PCMReaderCallback`을 추가하지 않는다.
- `SetSpatializerFloat`, `iplUnityAddSource`/`iplUnityRemoveSource`, `AudioClip.SetData`는 메인 스레드에서만 호출한다.
- 패치는 기존 매개변수 번호(0~33)와 기본 동작을 바꾸지 않는다. 새 매개변수는 34번 `PATHING_STEREOWIDTH`(0~1, 기본 1)부터 붙인다.
- 폭 계산: mid=(L+R)/2, side=(L−R)/2×w, L'=mid+side, R'=mid−side. 램프 한 샘플당 `1 / max(1, rate × 0.02)`.
- 음성 앞서 쓰기 기본 60ms(설정 가능), 링 클립 길이 1초.
- GC 회귀 기준: 효과음·음성 재생 후 `GC.Collect()` 동안 `AudioSettings.dspTime` 손실 0.05초 미만.
- 버전: `com.bun3.unity.audio.steamaudio` 0.6.1 → 0.7.0, `com.bun3.unity.audio.dissonance.steamaudio` 0.3.1 → 0.4.0. 같은 버전 재퍼블리시 금지.
- 코드·주석·README는 영어, `docs/`는 한국어. 패키지 public 멤버는 영어 XML 문서.
- 컴파일된 DLL은 패키지에 넣지 않는다. Apache-2.0 고지 유지.
- 테스트 실행: `unity/tests/Invoke-SoundFoundationTests.ps1`(기존 검증 프로젝트). 같은 프로젝트를 다른 에디터에서 열지 않는다.

## Review Focus

1. 보이스 반납 직후 오디오 스레드가 해제된 `IPLSource`를 읽는 경우 → 크래시 없이 무음. Task 3에서 반납 직후 즉시 재사용·해제하는 테스트로 고정.
2. 장치 샘플레이트·프레임 크기 변경 후 재생 → 효과음 계속 공간화, 음성 링 클립 재생성. Task 3·4에 `AudioSettings.Reset` 테스트.
3. 긴 프레임(>60ms) 뒤 음성 → 지난 음성이 반복되지 않고 무음 후 재개. Task 4 링 계산 테스트의 따라잡힘 경우.
4. 패치 없는 원본 플러그인 → 위치 소리는 나고 폭 매개변수만 무시, 경고 1회. Task 3 판정 테스트.
5. 피치가 바뀌는 루프 효과음 → Unity 재생으로 피치·루프 유지, 경로 믹스는 매 프레임 갱신. Task 3 PlayMode 테스트.

---

### Task 1: 0단계 스파이크 (버리는 코드, 결정 게이트)

**Files:**
- Create(커밋하지 않음): 검증 프로젝트 `Assets/Spike/NativeSpatializerSpike.cs`
- Modify: `docs/superpowers/specs/2026-11-12-unity-audio-native-spatializer-output-design.md` (끝에 `## 0단계 결과` 추가)

**Interfaces:**
- Produces: 스펙의 `## 0단계 결과` 표(항목·측정값·판정). Task 2~6은 이 표의 "좌표 매개변수 필요 여부", "꼬리 처리", "IPLSource 해제 시점", "Unity 감쇠 곡선 설정"을 따른다.

- [ ] **Step 1: GC 전제** — 검증 프로젝트를 CLI 배치로 새로 실행해, 빈 씬에서 반복 클립 재생 중 `GC.Collect()` 앞뒤 `dspTime`·실제 시간을 기록한다. 같은 프로세스에서 `OnAudioFilterRead` 컴포넌트를 1초 재생한 뒤 다시 측정한다. 기대: 실행 전 손실 < 0.05s, 실행 후 손실 > 0.2s.
- [ ] **Step 2: 경로 렌더·게인 경로** — Project Settings 스페이셜라이저를 Steam Audio Spatializer로 두고, `SteamAudioPathSimulationScope` 소스를 `iplUnityAddSource`로 등록한 `spatialize = true` 소스를 재생한다. 31번 핸들, 6번 `APPLY_PATHING` 1, 27번 경로 믹스를 0과 1로 바꾸며 `AudioListener.GetOutputData`로 RMS를 기록한다. AudioSource `volume`과 mixer group 볼륨이 출력에 반영되는지, Unity 감쇠 곡선(`rolloffMode`)이 경로 출력을 바꾸는지 기록한다.
- [ ] **Step 3: 좌표계** — 소스를 리스너 기준 좌·우·앞·뒤에 두고 기존 `SteamAudioSoundOutput` 출력과 공식 플러그인 출력의 좌/우 RMS 비를 비교한다. 방향이 다르면 "리스너 좌표 매개변수 필요"로 기록한다.
- [ ] **Step 4: 꼬리·해제** — 소스 정지 시 출력 꼬리가 기존 대비 잘리는지 기록한다. 플러그인 v4.8.1 소스(`unity/src/native`의 `SourceManager`, `setSource`, `getLatestSource`)를 읽고 `iplUnityRemoveSource` 후 `IPLSource`를 해제해도 되는 시점을 기록한다.
- [ ] **Step 5: 결과 기록과 게이트** — 스펙 `## 0단계 결과`에 표를 쓴다. 스펙 "실패 조건" 중 하나라도 해당하면 여기서 멈추고 사용자에게 보고한다.
- [ ] **Step 6: Commit** (스펙 변경만)

```bash
git add docs/superpowers/specs/2026-11-12-unity-audio-native-spatializer-output-design.md
git commit  # 📝 네이티브 스페이셜라이저 0단계 검증 결과 기록 (writing-gitmoji-commits 형식)
```

### Task 2: 경로 스테레오 폭 패치와 빌드

**Files:**
- Create: `unity/Packages/com.bun3.unity.audio.steamaudio/Native~/steam-audio-4.8.1-unity-pathing-width.patch`
- Modify: `unity/Packages/com.bun3.unity.audio.steamaudio/Native~/build_diagnostics.py`
- Modify: `unity/Packages/com.bun3.unity.audio.steamaudio/Native~/README.md`
- Create: `unity/Packages/com.bun3.unity.audio.steamaudio/Runtime/SteamAudioSpatializerPatch.cs`
- Test: `unity/Packages/com.bun3.unity.audio.steamaudio/Tests/Editor/SteamAudioSpatializerPatchTests.cs`

**Interfaces:**
- Produces (네이티브 export, `audioplugin_phonon.dll`):
  - `int bun3UnityPluginGetPatchVersion()` → 1
  - `void bun3UnityPluginApplyPathingWidthForTest(float* left, float* right, int frames, float* current, float target, int sampleRate)` — `process()`가 쓰는 것과 같은 함수를 호출한다.
- Produces (C#, `Bun3.Unity.Audio.SteamAudio`):
  - `internal static class SteamAudioSpatializerPatch` — `const int PathingStereoWidthParam = 34;` `static bool IsPatched { get; }`(export 존재와 버전 1 확인, 예외 없이 false 반환), `internal static void ApplyPathingWidthForTest(float[] left, float[] right, ref float current, float target, int sampleRate)`.
  - Task 1에서 좌표 매개변수가 필요하다고 나왔으면 `const int ListenerOverrideFirstParam = 35;`(12개: right xyz, up xyz, ahead xyz, origin xyz)와 패치의 대응 처리를 같은 방식으로 더한다.

- [ ] **Step 1: 실패하는 동등성 테스트**

```csharp
[Test] public void PathingWidthMatchesManagedReference()
{
    Assume.That(SteamAudioSpatializerPatch.IsPatched);
    // Reference: the removed SteamAudioPathRenderer.ApplyStereoWidth + AdvanceSpatialBlend, copied into the test.
    // 48 kHz, 1024 frames, current 1 -> target 0; random L/R in [-1,1] with fixed seed 7.
    // Assert every sample within 1e-6 of reference, and current == reference current at the end.
}
[Test] public void ZeroWidthMakesChannelsEqual() { /* target 0, current 0: L'[i] == R'[i] for all i */ }
[Test] public void FullWidthIsIdentity() { /* target 1, current 1: output == input exactly */ }
```

- [ ] **Step 2: 실행해 실패 확인** — `Invoke-SoundFoundationTests.ps1` EditMode. 기대: `IsPatched` 미구현으로 컴파일 실패 또는 Inconclusive.
- [ ] **Step 3: 패치 작성** — `spatialize_effect.cpp`에 `PATHING_STEREOWIDTH`를 `NUM_PARAMS` 앞에 추가하고, `gParamDefinitions`(범위 0~1, 기본 1), `setParam`/`getParam`, `State`의 `pathingStereoWidth`/`currentPathingStereoWidth`를 더한다. 경로 블록에서 `iplPathEffectApply` 직후, `iplAudioBufferMix` 직전에 `reflectionsSpatializedBuffer` 2채널에 폭을 적용한다. 두 export를 추가한다.
- [ ] **Step 4: 빌드 스크립트** — 기존 phonon 빌드 뒤 같은 체크아웃에서 새 패치를 적용하고 Unity 플러그인(`unity/src/native`)을 Release x64로 빌드해 `audioplugin_phonon.dll` 경로를 출력한다. 진단 패치와 함께 적용돼야 한다.
- [ ] **Step 5: C# 래퍼** — `SteamAudioSpatializerPatch`를 `DllImport("audioplugin_phonon")`로 구현한다. `IsPatched`는 `EntryPointNotFoundException`·`DllNotFoundException`을 잡아 false.
- [ ] **Step 6: 빌드·교체 후 테스트 통과 확인** — README 절차대로 검증 프로젝트 DLL을 교체하고 재시작한 뒤 Step 2 명령. 기대: 3개 PASS.
- [ ] **Step 7: README** — 빌드 명령, 교체 절차(에디터 종료·백업·메타 유지·재시작), 원본 복원, Apache-2.0 고지.
- [ ] **Step 8: Commit** — ✨ 경로 스테레오 폭 스페이셜라이저 패치와 빌드 추가

### Task 3: 효과음 출력을 공식 스페이셜라이저로 전환

**Files:**
- Modify: `Runtime/SteamAudioSoundOutput.cs` (렌더 경로 삭제, 매개변수 쓰기로 축소)
- Modify: `Runtime/SteamAudioSoundRuntime.cs` (드라이버 클립·렌더러 교체 삭제, 소스 등록 관리)
- Modify: `Runtime/SteamAudioSoundSetup.cs` (`ApplyNativeOutput(SoundSystemConfig config, PlanarAcousticOutputs acousticOutputs)`로 시그니처 변경, `PrepareDefinition` 훅 삭제)
- Modify: `Runtime/PlanarAcousticSfxBinding.cs` (`Publish`가 스페이셜라이저 매개변수를 씀)
- Create: `Runtime/SteamAudioSpatializerSupport.cs`
- Delete: `Runtime/SteamAudioSoundDriver.cs`, `Runtime/SteamAudioClipCache.cs`, `PathParameterMailbox.cs`(다른 사용처가 없을 때. `rg PathParameterMailbox`로 확인)
- Modify: `Editor/SteamAudioSetupValidator.cs` (네이티브 출력 사용 시 스페이셜라이저 미지정 경고)
- Test: `Tests/PlayMode/SteamAudioSoundOutputTests.cs`, `Tests/PlayMode/SteamAudioNativeOutputSetupTests.cs`, `Tests/PlayMode/SteamAudioDeviceChangeTests.cs`; Delete `Tests/PlayMode/SteamAudioClipCacheTests.cs`와 삭제된 렌더 경로 전용 테스트

**Interfaces:**
- Consumes: `SteamAudioSpatializerPatch.IsPatched`, `PathingStereoWidthParam`.
- Produces:
  - `public enum SteamAudioSpatializerSupport { Missing, Stock, Patched }`, `public static SteamAudioSpatializerSupport SteamAudioSpatializer.Detect()` — `AudioSettings.GetSpatializerPluginName() == "Steam Audio Spatializer"`와 패치 여부로 판정.
  - `internal static class SteamAudioSpatializerParams` — 공식 번호 상수: `ApplyPathing = 6`, `DirectMixLevel = 23`, `ReflectionsMixLevel = 25`, `PathingBinaural = 26`, `PathingMixLevel = 27`, `SimulationOutputsHandle = 31`, `PathingNormalizeEq = 33`.
  - `SteamAudioSoundOutput`: 보이스 생성 시 `iplUnityAddSource` 1회, 해제 시 `SetSpatializerFloat(31, -1)` → `iplUnityRemoveSource` → Task 1이 정한 시점 이후 `IPLSource` 해제.

- [ ] **Step 1: 실패하는 테스트**

```csharp
[UnityTest] public IEnumerator PoolVoicesRunNoManagedAudioCallback()
{ /* after ApplyNativeOutput + one positional Play: no component on any voice declares OnAudioFilterRead */ }
[UnityTest] public IEnumerator BlockedPathSetsPathingMixZero()
{ /* Publish with uncovered route: GetSpatializerFloat(27) == 0; covered: == path gain */ }
[UnityTest] public IEnumerator WidthFollowsSpatialBlendProfile()
{ /* near 0.5 m: GetSpatializerFloat(34) == 0; far 5 m: == 1 (profile 1 m / 3 m) */ }
[UnityTest] public IEnumerator VoicePlaysAuthoredClipWithPitchAndLoop()
{ /* source.clip == authored clip, spatialize == true, pitch/loop from SoundDef */ }
[UnityTest] public IEnumerator ReleasingVoiceImmediatelyAfterPlayIsSafe()
{ /* Play then Stop+release in the same frame 100 times; no exception, no native error log */ }
[UnityTest] public IEnumerator DeviceResetKeepsSpatializedPlayback()
{ /* AudioSettings.Reset(current config with different dspBufferSize); voice still spatialize, handle param restored */ }
[Test] public void DetectReportsMissingStockPatched() { /* each state from plugin name + IsPatched */ }
```

- [ ] **Step 2: 실행해 실패 확인** — PlayMode. 기대: 새 테스트 FAIL.
- [ ] **Step 3: 구현** — 보이스 AudioSource를 `spatialize = true`, `spatialBlend = 1`로 두고 실제 클립을 재생한다. Unity 감쇠 곡선은 Task 1 결과대로 끈다. 직접음·반사음 믹스 0, 6번 1, 26번 1. `Publish`에서 막힘이면 27번 0, 아니면 `path.Gain`, 34번 `GetSpatialBlend`(`Patched`일 때만), 33번 `NormalizeEq`. `Stock`이면 경고 1회, `Missing`이면 에러 1회. Task 1에서 꼬리가 잘린다고 판정됐으면 보이스 반납을 경로 효과 꼬리 길이(`iplPathEffectGetTailSize` 기준)만큼 늦추고, 그동안 소스는 무음 입력으로 재생을 유지한다.
- [ ] **Step 4: 삭제** — 드라이버·클립 캐시·렌더 경로·`SteamAudioSoundFailure`의 렌더 전용 값. 삭제된 코드 전용 테스트도 함께 삭제한다.
- [ ] **Step 5: 테스트 통과 확인** — PlayMode·EditMode 전체. 기대: 모두 PASS, Skipped 0.
- [ ] **Step 6: Commit** — ♻️ 효과음 공간화를 공식 Steam Audio 스페이셜라이저로 옮기고 C# 오디오 콜백 경로 삭제

### Task 4: 음성 출력을 클립 링으로 전환

**Files:**
- Create: `unity/Packages/com.bun3.unity.audio.dissonance.steamaudio/Runtime/VoiceClipRing.cs`
- Modify: `Runtime/DissonanceSteamAudioPlayback.cs`, `Runtime/DissonancePathPlayback.cs`, `Runtime/DissonancePlanarAcousticOutput.cs`
- Delete: `Runtime/DissonanceOutputMonitor.cs`
- Test: `Tests/PlayMode/VoiceClipRingTests.cs`, `Tests/PlayMode/DissonanceSteamAudioPlaybackTests.cs`, `Tests/PlayMode/DissonancePathPlaybackTests.cs`

**Interfaces:**
- Consumes: Task 3의 `SteamAudioSpatializerParams`, 소스 등록 규칙.
- Produces:
  - `public sealed class VoiceClipRing` — `VoiceClipRing(int sampleRate, float leadSeconds = 0.06f, float lengthSeconds = 1f)`, `int Plan(int playSample, Span<RingWrite> writes)`(이번 프레임에 쓸 음성·무음 구간을 링 인덱스로 반환), `int Underruns { get; }`.
  - `public readonly struct RingWrite { public readonly int Start; public readonly int Count; public readonly bool Silence; }`
  - `DissonanceSteamAudioPlayback.LeadSeconds`(직렬화, 기본 0.06).

- [ ] **Step 1: 실패하는 테스트**

```csharp
[Test] public void FirstPlanWritesLeadThenSilence()
{ /* 48 kHz, play 0: voice [0, 2880), silence [2880, 48000) */ }
[Test] public void SteadyPlanWritesOnlyTheAdvance()
{ /* play 480 after first plan: voice [2880, 3360), silence [3360, 48000)+[0, 480) wraps */ }
[Test] public void OverrunResyncsWithoutRepeatingAudio()
{ /* play jumps 10000 past write cursor: Underruns == 1, voice starts at play + 2880 */ }
[Test] public void WrapAroundSplitsWrites() { /* write range crossing 48000 returns two ranges */ }
[UnityTest] public IEnumerator VoiceRunsNoManagedAudioCallback()
{ /* playback object has no component declaring OnAudioFilterRead; source.spatialize == true */ }
[UnityTest] public IEnumerator DeviceResetRecreatesRingClip() { /* clip.samples restored, playback continues */ }
```

- [ ] **Step 2: 실행해 실패 확인** — PlayMode. 기대: FAIL.
- [ ] **Step 3: 구현** — 메인 스레드 `LateUpdate`에서 `timeSamples` → `Plan` → 음성 구간은 `SpeechSession.Read`로 채워 `SetData`, 무음 구간은 0으로 `SetData`. 공간화 매개변수는 Task 3 규칙. 장치 재설정 감지는 기존 `DriverClipLost` 방식(클립 `samples == 0`)으로 링 클립 재생성. `SpeechSession.Read`가 완료를 반환하면 남은 링을 무음으로 채우고 기존 완료 처리(완료 플래그·우선순위 해제)를 그대로 한다.
- [ ] **Step 4: 삭제** — `DissonanceOutputMonitor`, `DissonancePathPlayback`의 오디오 스레드 읽기·렌더(`ReadInterleaved`, `TryRenderNextFrame`)와 전용 테스트.
- [ ] **Step 5: 테스트 통과 확인** — 기대: 모두 PASS.
- [ ] **Step 6: Commit** — ♻️ 음성 재생을 메인 스레드 클립 링과 공식 스페이셜라이저로 옮김

### Task 5: GC 회귀 테스트, 버전, 문서

**Files:**
- Create: `unity/Packages/com.bun3.unity.audio.steamaudio/Tests/PlayMode/SteamAudioGcStallTests.cs`
- Modify: 두 패키지 `package.json`, `CHANGELOG.md`, `README.md`
- Modify: `unity/tests/README.md` (GC 회귀 테스트는 새 프로세스에서 실행)

**Interfaces:**
- Consumes: Task 3·4 전체.

- [ ] **Step 1: 실패하는 테스트**

```csharp
[UnityTest] public IEnumerator GcDoesNotStallMixerAfterSfxAndVoice()
{
    // Play one positional SFX and one voice for 1 s, then measure real vs dspTime around GC.Collect() 3 times.
    // Assert each (real - dsp) < 0.05.
}
```

- [ ] **Step 2: 실행해 확인** — 새 프로세스 CLI 실행. 기대: Task 3·4 적용 후 PASS. 실패하면 오디오 스레드에서 C#을 부르는 코드를 `rg "OnAudioFilterRead|PCMReaderCallback"`으로 찾아 제거한다.
- [ ] **Step 3: 버전·CHANGELOG** — 0.7.0 / 0.4.0. 마이그레이션: 스페이셜라이저 지정, 패치 DLL 교체, `ApplyNativeOutput` 인자 변경, 삭제된 타입 목록.
- [ ] **Step 4: README** — 네이티브 출력 경로 설명을 "공식 스페이셜라이저 + 패치" 구조로 고치고, 판정 표(`Missing`/`Stock`/`Patched`)를 넣는다.
- [ ] **Step 5: 전체 테스트** — EditMode·PlayMode 전체. 기대: Failed 0, Skipped 0.
- [ ] **Step 6: Commit** — ✅ GC 중 오디오 정지 회귀 테스트와 0.7.0/0.4.0 문서

### Task 6: JP 적용 (JP 저장소)

**Files (jurassic-paradise):**
- Modify: `unity/ProjectSettings/AudioManager.asset` (Spatializer Plugin = Steam Audio Spatializer, 에디터에서 설정)
- Replace: `unity/Assets/Plugins/SteamAudio/Binaries/Windows/x86_64/audioplugin_phonon.dll` (메타 유지)
- Modify: `unity/Assets/_Game/Scripts/Audio/SoundSetup.cs` (`ApplyNativeOutput` 새 시그니처), `unity/Assets/_Game/Scripts/Core/GameConfig.cs`(`SoundDecodedCacheMiB` 삭제)
- Modify: `unity/Packages/packages-lock.json`(패키지 갱신)
- Modify: `docs/sound-authoring-guide.md`, `docs/third-party/`(Steam Audio 수정 빌드 고지), `unity/ProjectSettings/ProjectSettings.asset`(patch 버전)

- [ ] **Step 1: 적용** — 위 변경. JP `unity/CLAUDE.md` 규칙(경고 0, 문자열 규칙)을 따른다.
- [ ] **Step 2: 측정** — 로비→게임 전환과 게임 중 `GC.Collect()`의 `dspTime` 손실을 2026-11-12와 같은 방식으로 측정. 기대: < 0.05s.
- [ ] **Step 3: 청취 확인** — 1m 안 근거리 모노, 벽 너머 차폐, 좌우 방향, 음성 지연·끊김. 결과를 사용자에게 보고한다.
- [ ] **Step 4: JP 테스트** — 사운드·음성 관련 PlayMode 테스트와 컴파일 경고 확인.
- [ ] **Step 5: Commit** — JP 저장소 규칙(writing-gitmoji-commits)으로 커밋한다.
