# Unity Audio 네이티브 스페이셜라이저 출력 설계

- 상태: 설계 승인 대기 (2026-11-12)
- 적용 패키지: `com.bun3.unity.audio.steamaudio` 0.6.1 → 0.7.0, `com.bun3.unity.audio.dissonance.steamaudio` 0.3.1 → 0.4.0
- 첫 적용 게임: 쥬라기 낙원(JP)

## 의도

GC가 돌 때 Unity 오디오 믹서 전체가 멈추고, 그 순간 나오던 배경음·효과음·음성이 "드드득" 끊긴다. 원인은 bun3 출력이 오디오 스레드에서 C# 코드를 실행한다는 데 있다. 오디오 스레드가 C#에 한 번 들어가면 런타임에 등록되고, 그 뒤로는 모든 stop-the-world GC 동안 함께 멈춘다.

이번 작업으로 오디오 스레드에서 C# 코드를 없앤다. 공간화는 Steam Audio 공식 Unity 스페이셜라이저(네이티브)가 맡는다. 공식 플러그인에 없는 근거리 모노(경로 출력의 스테레오 폭)는 플러그인에 작은 패치로 더한다. bun3가 오디오 스레드에서 하던 PCM 디코딩·리샘플·경로 렌더는 삭제한다.

### 측정 근거 (JP, Unity 6000.3 에디터, 2026-11-12)

로그 원본: JP `Logs/review-2026-11-12/bgm/` (Git 제외).

| 실험 | 실제 시간 | 오디오 DSP 시계 손실 |
|---|---|---|
| 메인 스레드 CPU 점유 0.4~0.6초 | 0.40~0.60s | 0.00~0.02s |
| 메인 스레드 sleep 0.6초 | 0.60s | 0.02s |
| `GC.Collect()` (타이틀 음악 재생 중) | 0.38s | 0.32~0.36s |
| `SteamAudioSoundDriver` 64개 비활성화 후 `GC.Collect()` | 0.39s | 0.34s |
| 사운드 시스템 없는 빈 씬, Steam Audio 종료 후 `GC.Collect()` | 0.37s | 0.33~0.37s |
| 로비→게임 씬 로드 프레임(`Application.LoadLevelAsync Integrate` 안 `GC.Collect` 412ms) | 0.90s | 0.43s |

- 메인 스레드가 멈추는 것만으로는 믹서가 멈추지 않는다. GC만 멈춘다.
- 콜백을 꺼도, 빈 씬이어도 손실이 났다. 해당 에디터 프로세스가 이미 C# 오디오 콜백을 실행한 적이 있어 오디오 스레드가 등록된 상태였기 때문으로 본다(도메인 리로드 비활성). 콜백을 한 번도 실행하지 않은 새 프로세스에서 GC가 믹서를 멈추지 않는다는 것은 아직 확인하지 않았다. 아래 0단계에서 검증한다.

### 오디오 스레드에서 C#을 실행하는 bun3 코드 (현재)

- `com.bun3.unity.audio.steamaudio` `SteamAudioSoundDriver.OnAudioFilterRead` → `SteamAudioSoundOutput.Process`: 보이스마다 PCM 리샘플, `iplPathEffectApply`(P/Invoke), 스테레오 폭, 게인 곱.
- `com.bun3.unity.audio.dissonance.steamaudio` `DissonanceOutputMonitor.OnAudioFilterRead` → `DissonancePathPlayback.ReadInterleaved`: Dissonance `SpeechSession.Read`(디코더·지터 버퍼)와 경로 렌더.

JP는 Dissonance 기본 재생 프리팹 대신 bun3 경로(`AcousticVoicePlayback`)를 쓰므로 위 두 곳이 전부다. JP의 스페이셜라이저 슬롯(`m_SpatializerPlugin`)은 비어 있고 SDK `SteamAudioSource`도 쓰지 않는다.

## 사용자 결정 (원문)

- 수정 방향 선택지(로딩 페이드 / 씬 로드 구조 변경 / bun3-kit 근본 수정)에서: "bun3-kit에서 근본 수정". 이후 Claude가 로딩 페이드를 지금 적용하고 근본 수정을 별도 과제로 하는 안을 내자 "2번으로 가보자".
- 플랫폼: "Windows x64만 (Recommended)".
- 지연 허용: "추가 지연 없음 (Recommended)".
- 기성 플러그인 검토 요청: "그냥 이미 구현된 플러그인 쓰거나 하는 건 안되나?"
- 근거리 모노: "있어야 되긴해. 게임에서 꽤 보편적으로 쓰이는 기능일텐데? 이걸 지원하는 다른 플러그인 리서치라도 좀 해봐" → 조사 후 공식 플러그인 + 패치(E안)에 "ㄱㄱ".
- C# 경로 삭제와 음성 앞서 쓰기 60ms: "아하 다 이해했어. 둘다 괜찮을듯 ㄱㄱ".

## 목표

- 오디오 스레드에서 bun3 C# 코드가 실행되지 않는다(효과음·음성 모두).
- 효과음·음성이 재생된 뒤 `GC.Collect()`를 해도 오디오 DSP 시계 손실이 0.05초 미만이다.
- 기존 평면 음향 결과(경로 계수·EQ·차폐 게인·커버리지 차단)와 근거리 모노(경로 출력 스테레오 폭, 기본 1m/3m)가 Windows x64에서 유지된다.
- 효과음에 추가 지연이 없다.

## 비목표

- Windows x64 외 플랫폼의 패치 빌드. 그 플랫폼은 원본 SDK 플러그인으로 동작하고 근거리 모노만 빠진다. 필요해지면 빌드 스크립트에 플랫폼을 더한다.
- 씬 로드 GC 자체를 줄이는 구조 변경.
- SDK 스페이셜라이저 바인더 경로(`SteamAudioSoundSetup.Apply`) 변경. 다른 게임용으로 그대로 둔다.
- 반사음(reflections)·직접음(direct) 사용. 지금처럼 경로 출력만 쓴다.

## 실패 조건 (0단계에서 하나라도 나오면 멈추고 재논의)

1. 콜백을 한 번도 실행하지 않은 새 프로세스에서도 `GC.Collect()`가 오디오를 멈춘다.
2. 공식 플러그인이 bun3 시뮬레이터의 `IPLSource` 경로 출력을 렌더하지 못하거나, bun3 렌더와 들리는 결과가 크게 다르다.
3. 리스너 좌표 불일치를 패치 매개변수로도 맞출 수 없다.

## 0단계: 스파이크 (버리는 코드)

| 확인 | 방법 | 통과 기준 |
|---|---|---|
| GC 전제 | 새 Unity 프로세스(CLI 배치 실행)에서 음악 재생 중 `GC.Collect()`; 같은 프로세스에서 `OnAudioFilterRead` 컴포넌트를 한 번 실행한 뒤 다시 측정 | 실행 전 손실 0.05초 미만, 실행 후 손실 발생 |
| 경로 렌더 | bun3 `SteamAudioPathSimulationScope` 소스를 `iplUnityAddSource`로 등록하고 `spatialize = true` AudioSource 재생 | 경로 믹스 0이면 무음, 경로가 있으면 소리 |
| 좌표계 | 같은 위치의 소스를 좌·우·앞·뒤에 두고 기존 C# 렌더와 패치 플러그인의 좌우 에너지 비교 | 방향이 일치 |
| 폭 동등성 | 같은 입력·폭 값으로 기존 `SteamAudioPathRenderer`와 패치 플러그인 출력 비교 | 허용 오차 안 |
| 꼬리 | AudioSource 정지 시 경로 효과 꼬리가 잘리는지 청취·출력 비교 | 들리는 차이 없음, 있으면 반납 지연으로 해결 |
| 해제 시점 | 플러그인 소스(`unity/src/native`)의 `iplUnityRemoveSource`·`getLatestSource` 수명 규칙 확인 | `IPLSource` 해제 가능 시점 확정 |
| 게인 경로 | AudioSource volume·mixer group이 스페이셜라이저 입력에 반영되는지, Unity 감쇠 곡선이 경로 출력에 겹치지 않는지 | 기존과 같은 음량 규칙 |

## 결정: 패치 플러그인 (`Native~`)

- 기존 `Native~/build_diagnostics.py`(Steam Audio v4.8.1 고정)에 Unity 스페이셜라이저(`unity/src/native`, `audioplugin_phonon.dll`) 빌드를 더한다. 진단 패치와 함께 적용할 수 있어야 한다.
- 새 패치 `steam-audio-4.8.1-unity-pathing-width.patch`:
  - 스페이셜라이저 매개변수 목록 맨 뒤에 "경로 스테레오 폭"(0~1, 기본 1)을 더한다. 기존 매개변수 번호는 바꾸지 않는다.
  - 경로 출력(`iplPathEffectApply` 결과)을 최종 버퍼에 섞기 직전에 미드/사이드 폭을 적용한다. 계산은 현재 `SteamAudioPathRenderer.ApplyStereoWidth`와 같다(mid=(L+R)/2, side=(L−R)/2×w). 값이 바뀌면 최대 20ms 램프.
  - 0단계에서 좌표 불일치가 확인되면 리스너 좌표를 bun3가 넘기는 매개변수를 같은 방식으로 더한다.
  - 패치 식별 함수 `bun3UnityPluginGetPatchVersion`을 내보낸다.
- 컴파일된 DLL은 패키지에 넣지 않는다(기존 원칙). README에 교체 절차를 기존 `phonon.dll` 절차와 같은 형식으로 적는다. Apache-2.0 고지 유지.

## 결정: 효과음 출력 (`com.bun3.unity.audio.steamaudio`)

- 보이스 AudioSource가 실제 클립을 Unity로 재생한다(`spatialize = true`, `spatialBlend = 1`). 피치·루프·리샘플은 Unity가 맡는다.
- 보이스마다 있는 평면 음향 소스(`PlanarAcousticSfxBinding`)를 보이스 생성 때 한 번 `iplUnityAddSource`로 등록하고 핸들을 `SetSpatializerFloat`로 넘긴다.
- `Publish`가 메일박스 대신 스페이셜라이저 매개변수를 쓴다(메인 스레드).
  - 경로 믹스 레벨: 막힘·오래된 결과면 0, 아니면 지금의 경로 게인(`ObstructedPathGain` 보간).
  - 경로 스테레오 폭: 지금의 `GetSpatialBlend` 값.
  - 경로 EQ 정규화: 지금의 `NormalizeEq`.
  - 직접음·반사음 믹스 0, 플래그 off. 경로 HRTF on.
- 거리 감쇠는 지금처럼 경로 계수 안에 있다. Unity 감쇠 곡선은 겹치지 않게 끈다(0단계 게인 경로 확인 결과로 확정).
- 삭제: `SteamAudioSoundDriver`, `SteamAudioSoundOutput`의 렌더 경로(`Process`, `MixRenderedFrames`, `RenderNextFrame`, 피치 리샘플), `SteamAudioClipCache`, 드라이버 클립, 이 경로 전용 `SteamAudioPathRenderer` 사용처와 `PathParameterMailbox`(다른 사용처가 없으면).
- `SteamAudioSoundSetup.ApplyNativeOutput`의 `maximumDecodedBytes` 인자와 `PrepareDefinition` 디코딩 훅을 없앤다.

## 결정: 음성 출력 (`com.bun3.unity.audio.dissonance.steamaudio`)

- 보이스마다 1초 반복 재생 모노 AudioClip을 링으로 쓴다.
- 메인 스레드가 매 프레임:
  1. `timeSamples`로 재생 위치를 읽는다.
  2. 재생 위치 + 앞서 쓰기(기본 60ms, 설정 가능)까지 `SpeechSession.Read`로 꺼내 `SetData`한다.
  3. 그 뒤 구간을 무음으로 덮어 메인 스레드 정지 시 지난 음성이 반복되지 않게 한다.
  4. 쓰기 위치가 재생 위치에 따라잡혔으면 놓친 구간을 버리고 재생 위치 + 앞서 쓰기부터 다시 쓴다. 횟수를 언더런 카운터로 기록한다.
- 공간화는 효과음과 같은 패치 플러그인과 매개변수 규칙을 쓴다.
- 장치 재설정으로 클립 데이터가 비면(기존 `DriverClipLost` 감지 방식) 클립을 다시 만든다.
- 삭제: `DissonanceOutputMonitor`의 `OnAudioFilterRead`, `DissonancePathPlayback`의 오디오 스레드 읽기·렌더 경로.
- 메인 스레드가 멈추는 동안(GC·긴 로딩 프레임) 음성만 무음이 된다. 음악·효과음은 계속 나온다.

## 결정: 시작 판정과 오류

| 상태 | 동작 |
|---|---|
| 스페이셜라이저 = Steam Audio Spatializer, 패치 식별 함수 있음 | 전체 기능 |
| 스페이셜라이저 = Steam Audio Spatializer, 패치 없음 | 근거리 모노만 빠짐. 경고 로그 1회 |
| 스페이셜라이저 미지정 | 위치 소리가 공간화 없이 나옴. 에러 로그와 에디터 경고(`SteamAudioSetupValidator` 확장) |

- 시뮬레이션 실패·NaN·오래된 결과는 지금 판정(`HasReadableOutput`, `GeometryRevision`, `IsRouteCovered`)을 그대로 쓰고 경로 믹스를 0으로 둔다.
- `SteamAudioSoundFailure`에서 삭제된 렌더 경로 전용 항목을 정리한다.
- 해제 순서: `SetSpatializerFloat(핸들, -1)` → `iplUnityRemoveSource` → 0단계에서 확정한 시점 이후 `IPLSource` 해제. 모든 해제는 메인 스레드.

## 게임 적용 (JP)

- Project Settings > Audio > Spatializer Plugin = Steam Audio Spatializer.
- 빌드한 패치 `audioplugin_phonon.dll`을 `Assets/Plugins/SteamAudio/Binaries/Windows/x86_64/`에 교체(메타 유지).
- `SoundSetup.EnsureInitialized`의 디코딩 캐시 인자와 `GameConfig.SoundDecodedCacheMiB` 정리.
- 문서: `docs/sound-authoring-guide.md`(디코딩 캐시 설명 정리, 스페이셜라이저 설정 안내), `docs/third-party/`(Steam Audio 수정 빌드 고지).
- 2026-11-12에 넣은 로딩 화면 BGM 페이드아웃(`LoadingOverlay`, `GameConfig.LoadingMusicFadeSeconds`)은 안전장치로 유지한다.
- 빌드 버전은 patch만 올린다(네트워크 데이터 불변).

## 작업 순서

1. 0단계 스파이크. 실패 조건이면 멈춤.
2. 플러그인 패치와 빌드 스크립트.
3. 효과음 경로 전환과 삭제.
4. 음성 경로 전환.
5. bun3-kit 테스트.
6. JP 적용, 측정, 문서.

## 제약

- 오디오 스레드에서 실행되는 bun3 C# 코드가 남지 않는다. 새 `OnAudioFilterRead`, `PCMReaderCallback`을 추가하지 않는다.
- 스페이셜라이저 매개변수 설정(`SetSpatializerFloat`), 소스 등록·해제, `SetData`는 메인 스레드에서만 한다.
- 패치는 기존 매개변수 번호와 기본 동작을 바꾸지 않는다. 폭 기본값 1이면 원본과 같은 출력이다.

## Claude가 정한 것 (사용자 확인 필요)

- 음성 링 길이 1초, 무음 덮기 구간 길이(앞서 쓰기 이후 남은 링 전체).
- 폭 램프 20ms를 패치에서도 유지.
- 0.x 관례에 따른 minor 버전 상승.
- `PathParameterMailbox`는 다른 사용처가 없으면 삭제.

## 검증

- bun3-kit 자동 테스트
  - 폭 동등성(EditMode): 기존 C# 계산 기준값 대비 패치 플러그인 출력 오차.
  - 경로 렌더(PlayMode 대표 1개): 경로 믹스 0이면 무음, 폭 0이면 좌우 동일.
  - GC 회귀(PlayMode, CLI 새 프로세스): 효과음·음성 재생 후 `GC.Collect()` 손실 0.05초 미만.
  - 삭제된 렌더 경로 전용 테스트는 함께 삭제하고, 공간화 결과 테스트는 새 경로로 옮긴다.
- JP 수동 확인
  - DSP 시계 손실 측정: 로비→게임 전환, 게임 중 `GC.Collect()`.
  - 청취: 1m 안 근거리 모노, 벽 너머 차폐, 좌우 방향, 음성 지연·끊김.

## 0단계 결과 (2026-11-12)

환경: Unity 6000.5.5f1, Windows x64, 48 kHz, DSP 버퍼 1024, Mono. 스파이크 코드는 버렸다.

| 확인 | 방법 | 결과 | 판정 |
|---|---|---|---|
| GC 전제 | 패키지 없는 최소 프로젝트 스탠드얼론 플레이어(새 프로세스), 2,500만 객체 힙, `GC.Collect()` 3회씩 | C# 오디오 콜백 실행 전: GC 0.13s, 손실 0.004s 이하. `OnAudioFilterRead` 1회 실행 후: 손실 0.09~0.12s. 그 컴포넌트를 제거한 뒤에도 손실 0.07~0.09s | 통과. 콜백을 한 번이라도 실행하면 프로세스가 끝날 때까지 GC가 믹서를 멈춘다 |
| 경로 렌더 | bun3 개발 프로젝트 PlayMode(배치), `PlanarAcousticWorld` 소스의 `IPLSource`를 `iplUnityAddSource`로 등록, 6번 1·27번 1·23·25번 0 | 소리가 나고, 27번 0이면 완전 무음 | 통과 |
| 좌표계 | 소스 x=−2, x=+2, 리스너 원점(회전 없음). 공식 플러그인과 `SteamAudioPathRenderer` 좌/우 RMS 비 | x=−2: 1.212 대 1.212, x=+2: 1.142 대 1.143 | 통과. 리스너 좌표 매개변수 불필요 |
| 음량 | 같은 모노 300 Hz 0.2 진폭 입력 | 공식 플러그인 출력이 bun3 렌더러의 0.708배(−3 dB). Steam Audio 다운믹스는 채널 평균이므로, Unity가 모노 클립을 스페이셜라이저 입력에 채널당 1/√2로 넣는 것이 원인 | 보정 필요: 모노 클립은 경로 믹스에 √2를 곱한다(Task 3에서 테스트로 고정) |
| 게인 경로 | AudioSource `volume` 0.5, Unity 로그 감쇠 곡선으로 60 u 이동 | 볼륨 0.5 → 출력 정확히 절반. 위치 이동은 출력 불변 | 통과. Unity 감쇠 곡선은 경로 출력에 영향 없음(따로 끌 필요 없음). mixer group 라우팅은 미확인 |
| 꼬리 | 정지 100 ms 뒤 출력 | 무음. 차이는 bun3 렌더러의 꼬리(프레임/4 = 256샘플, 약 5 ms) | 처리하지 않는다 |
| 해제 시점 | 플러그인 v4.8.1 `SourceManager`·`setSource`·`getLatestSource` | 플러그인이 `iplSourceRetain`으로 참조를 따로 잡고 교체 시 해제한다 | bun3는 `iplUnityRemoveSource` 직후 자기 참조를 해제해도 된다 |
