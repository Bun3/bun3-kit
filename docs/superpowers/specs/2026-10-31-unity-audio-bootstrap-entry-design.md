# Unity Audio 정적 진입점과 Steam Audio 조립 설계

- 상태: 설계 승인 (2026-11-01)
- 적용 패키지: `com.bun3.unity.audio` 0.6.0 → 0.7.0, `com.bun3.unity.audio.steamaudio` 0.5.0 → 0.6.0
- 첫 적용 게임: 쥬라기 낙원(JP), 브랜치 `🔈/bind-sfx-keys`

## 의도

지금은 사운드를 쓰려면 게임마다 래퍼를 짜야 한다. JP에는 래퍼가 두 개 있다.

- `JP.SoundManager`: Game 씬에만 있는 MonoBehaviour다.
  - 정적 `I`를 둔다.
  - 문자열 키로 재생하고, 없는 키는 무시한다.
  - 배경음 키 상태를 갖는다.
  - 오디오 장치가 바뀌면 출력 전체를 다시 만든다.
  - 테스트 전용 seam을 갖는다.
- `JP.Bun3SoundOutput`:
  - `SoundSystemConfig`를 조립한다.
  - Steam Audio 디코딩 캐시와 음성 출력을 만든다.
  - 맵 음향에 등록한다(`GameAcousticSfxBinding`).
  - 정의별 준비 오류를 기록한다.

이 코드는 두 번째 게임에서도 똑같이 짜게 된다. 그래서 kit 제1 원칙에 따라 프레임워크 몫이다. 게임에는 그 게임만의 값만 남긴다. 채널 수, 볼륨 설정 연결, 맵 음향 세션이 여기에 해당한다.

## 사용자 결정 (원문)

- "1번 2번 옮기고, 씬에 붙지 않고 카탈로그 로드하는 식으로 할거야. 그래서 각 게임 Bootstrap 단계에서 로드하게끔 / 3번은 우리 게임에서 지정이 필요한 것들인 건 알겠는데, 그걸 연결하기 위해서 왜 우리 게임 SoundManager가 필요한지 모르겠어. bun3-kit 쪽에 연결 API들을 추가하고, 우리가 직접 그쪽 System/Manager 통해서 연결 해야지"
  - 번호는 Claude가 앞서 설명한 SoundManager의 역할 목록을 가리킨다. 1은 수명 관리, 2는 키 기반 재생, 3은 게임별 설정 연결이다.
- 진입 모양: "그냥 뭐 SoundSystem.Initialize(~Catalog, ~Setting, ~); 이런 식?"
- Steam Audio 조립:
  - Claude의 질문: "Bootstrap에서 설정을 만든 뒤 Steam Audio 패키지 함수에 한 번 통과시키는 방식이면 괜찮아요?"
  - 답: "ㅇㅇ 그렇게 하자."
- 장치 변경:
  - 사용자 질문: "사운드 장치의 샘플레이트가 바뀔 때마다 출력을 다시 만드는 형태로만 대응이 가능한거야? 할당 비용이 많이 발생할 것 같은데"
  - Claude는 HRTF를 공유하고 장치가 바뀌면 Steam Audio 부분만 교체하는 개선을 이번 범위에 넣자고 제안했다. 답: "ㅇㅇ"
- 따라가기 재생:
  - 사용자는 대상 하위에 붙이지 않는 이유를 물었다.
  - Claude는 매 프레임 위치를 복사하는 지금 방식을 유지하자고 제안했다. 답: "ㄱㄱ"
- 장치 변경 후 배경음:
  - Claude는 처음에 지금 JP처럼 처음부터 다시 트는 방식을 정했다.
  - 사용자: "처음부터 재생하면 갑자기 뚝 끊겨 들려서 너무 어색하지 않을까? 다른 게임들은 장치 바꾸거나 해도 재생한 부분에서 이어 들리던데."
- 장치 변경 처리 범위:
  - Claude가 다른 엔진·게임의 처리 방식을 조사한 뒤 "알림이 오면 실제로 멈춘 것만 복구"하자고 제안했다.
  - 답: "ㅇㅇ 반영해줘"
- Boot를 거치지 않는 실행:
  - Claude의 질문: "에디터에서 Menu 씬부터 Play할 때도 소리가 나야 할까요? 그렇다면 사운드 초기화를 Boot와 그 경로가 같이 부르는 함수로 빼고, 카탈로그 참조도 Boot 필드 대신 `GameConfig`로 옮길게요."
  - 답: "ㅇㅇ"
- 테스트와 사운드 초기화:
  - 계획 초안에서는 Menu 씬을 열면 `MenuController.Awake`가 사운드를 초기화하게 했다. 그래서 테스트마다 실제 사운드가 켜졌다.
  - 사용자: "테스트마다 사운드 시스템이 켜질 필요는 없지 않을까?"

## 목표

1. 게임은 Bootstrap 코드에서 설정을 만들고 `SoundSystem.Initialize(catalog, config)`를 한 번 부른다. 씬에 두는 사운드 컴포넌트는 없다. JP는 에디터에서 Menu 씬부터 Play해도 같은 초기화를 거친다.
2. 게임 코드 어디서나 `SoundSystem.Play(key)`, `SoundSystem.PlayAt(key, position)`, `SoundSystem.PlayMusic(key)`, `SoundSystem.StopMusic()`로 재생한다.
3. 게임별 값은 `SoundSystemConfig`의 값과 델리게이트로만 넘긴다.
4. Steam Audio 공간음은 Steam Audio 패키지의 조립 함수를 한 번 불러서 붙인다. 디코딩 캐시, 음성 출력, 맵 음향 등록, 장치 변경 대응, 종료 정리는 패키지가 맡는다.
5. 장치 변경 비용을 줄인다.
   - 모든 음성이 HRTF 하나를 같이 쓴다.
   - 장치가 바뀌면 Steam Audio 렌더러만 바꾼다. 렌더러는 샘플레이트나 버퍼 크기가 바뀔 때만 교체한다.
   - 음성 풀과 클립 캐시는 그대로 둔다.
6. JP의 `SoundManager`, `Bun3SoundOutput`, `GameAcousticSfxBinding`을 지운다. 호출부는 kit을 직접 부른다.

## 비목표

- 효과음 키 연결 작업(`switch.ready` 분리, 결과음 기준 수정). 이 작업이 끝난 뒤 따로 한다.
- 따라가기 재생 방식 변경. 대상 하위에 붙이지 않고 지금처럼 위치를 복사한다.
- 음성 채팅(Dissonance) 경로. 이미 장치 변경을 스스로 처리한다.
- 기존 1인자 `SteamAudioSoundSetup.Apply(config)`의 동작 변경. 이 함수는 Valve `SteamAudioSource` 컴포넌트를 쓰는 경로다.

## 실패 조건

기술적으로 동작해도 아래 중 하나라도 해당하면 실패다.

- 게임 레포에 사운드 래퍼나 매니저 클래스가 다시 생긴다.
- Steam Audio 사용 여부를 설정 값 스위치(`UseSteamAudio` 같은 bool)로 고른다. 사용 여부는 Bootstrap 코드가 조립 함수를 부르는지로 정한다.
- 재생·틱 경로에서 관리 힙 할당이 생긴다. 키 조회도 이 경로에 포함된다.
- 별칭 API(`PlayFx` 등)나 두 번째 카탈로그가 생긴다.
- 장치가 바뀔 때 클립을 다시 디코딩하거나 `SoundSystem` 전체를 다시 만든다.

## 결정: `com.bun3.unity.audio`

### 정적 진입점

기존 `SoundSystem` 클래스에 정적 멤버를 더한다. 인스턴스 API는 그대로 두며, kit 테스트와 샘플은 계속 인스턴스를 쓴다. 인스턴스 API는 `new SoundSystem(config)`, 핸들, 차지 사운드, 따라가기, 믹서 볼륨을 말한다.

| 멤버 | 동작 |
|---|---|
| `Initialize(SoundCatalog catalog, SoundSystemConfig config)` | 카탈로그를 검증(`ValidateOrThrow`)하고 인스턴스를 만든 뒤 카탈로그 전체를 준비한다. 이미 초기화돼 있으면 `InvalidOperationException`을 던진다. |
| `Shutdown()` | 인스턴스를 해제한다. 초기화 전이면 아무것도 하지 않는다. 앱 종료(`Application.quitting`) 때 자동으로 불린다. |
| `Current` | 초기화된 인스턴스를 돌려준다. 없으면 null이다. 정적 API에 없는 기능(핸들, 차지, 따라가기, 채널 볼륨)을 쓸 때 쓴다. |
| `Play(string key)` | 2D로 재생한다. 정의가 위치 재생이어도 2D로 낸다. JP는 자기 캐릭터 소리를 이렇게 낸다. |
| `PlayAt(string key, Vector3 position)` | 정의의 `EffectiveSpatial`이 `None`이 아니면 위치 재생, `None`이면 2D로 재생한다. JP의 `Vector2` 인자는 암시적 변환으로 받는다. |
| `PlayMusic(string key)` | 지금 키와 같으면 다시 시작하지 않는다. 카탈로그에 없는 키는 배경음을 끈다. 요청은 "지금 들려야 할 음악"을 뜻하기 때문이다. 페이드는 설정의 음악 페이드 값을 쓴다. |
| `StopMusic()` | 같은 페이드로 배경음을 끈다. |

- 없는 키는 조용히 무시하고 로그도 남기지 않는다. 데이터보다 키 연결을 먼저 하는 흐름을 허용하기 위해서다.
- 초기화 전 호출도 무시한다. 다만 에디터에서는 처음 한 번만 경고를 남긴다. 초기화를 거치지 않은 경로(JP의 캡처 씬 등)에서 소리가 안 나는 원인을 알 수 있게 하기 위해서다.
- 키 조회는 `SoundCatalog.TryGet`의 사전 조회라 할당이 없다.
- 도메인 리로드를 끈 Play Mode 진입에 대비해 `SubsystemRegistration`에서 정적 상태를 비운다.

### 설정에 더하는 것

- **음악 페이드 기본값:** 정적 `PlayMusic`과 `StopMusic`이 쓴다. JP는 `GameConfig.MusicFadeSeconds`를 넣는다.
- **정의 준비 훅:** 코어가 정의를 준비할 때 불린다. 초기화 때는 카탈로그 전체에 대해, 에디터에서는 클립이 바뀐 정의에 대해 불린다. Steam Audio 조립 함수가 여기에 클립 캐시 디코딩을 연결한다.
- **준비 실패 기록:** 준비 시점에 정의별로 한 번 `Debug.LogException`으로 남긴다.
  - JP의 `LastPreparationError`, `GetPreparationError`, `LastPlaybackFailure`는 옮기지 않는다. 테스트 실패 메시지 말고는 쓰는 곳이 없다.

### 에디터 클립 교체

- JP는 에디터 Play 중에 `SoundDef`의 클립이나 공간 모드가 바뀌면 그 정의를 다시 준비한다. `Bun3SoundOutput.RefreshPreparedDefinitions`가 매 프레임 비교한다.
- 이 동작을 코어로 옮기고, `UNITY_EDITOR`에서만 돌게 한다.

### 장치 변경 (코어)

- 오디오 설정이 바뀌면(출력 장치 교체 포함) Unity는 모든 소스의 재생 상태와 스크립트로 만든 클립을 잃는다. Unity 매뉴얼(AudioSettings)은 이를 `OnAudioConfigurationChanged`에서 다시 만들라고 안내한다.
- 그래서 코어 `SoundSystem`이 이 이벤트를 직접 구독한다. Steam Audio를 쓰지 않는 게임도 함께 복구된다.
- 다른 엔진과의 차이:
  - FMOD와 Unreal은 엔진이 장치를 바꾸고 재생 상태를 유지한다.
  - Unity 기본 오디오는 이 일을 게임에 맡긴다. 찾아본 Unity 게임(Fly Dangerous)은 콜백에서 곡을 처음부터 다시 튼다.
  - 이 설계는 위치까지 되살려 FMOD·Unreal 게임과 같은 경험을 목표로 한다.
- **실제로 멈춘 것만 복구한다.** 알림이 왔다고 무조건 정리하지 않는다.
  - 이유: Unity 이슈 UUM-83238의 검색 요약에는 Windows에서 5.1 채널 모드일 때만 장치 전환으로 재생이 멈추고, 스테레오에서는 재현되지 않았다고 나온다. 원문은 확인하지 못했다.
  - 이 말이 맞다면 무조건 정리하는 코드가 오히려 소리를 끊는다. 멈춘 것만 복구하면 Unity가 상태를 잃든 안 잃든 맞게 동작한다.
- 이벤트가 오면 샘플레이트와 상관없이 아래를 처리한다.
  - 효과음: 소스가 멈춘 음성만 끝난 것으로 처리하고 슬롯을 비운다. 계속 재생 중인 음성은 그대로 둔다. 이 처리가 없으면 출력 완료 신호를 기다리는 음성이 영원히 "재생 중"으로 남는다(`VoiceTable`이 출력 완료로만 끝내는 경로). 일시정지한 음성은 멈춘 것으로 보지 않는다.
  - 배경음: 소스가 멈췄을 때만 끊긴 위치에서 이어서 튼다. 계속 재생 중이면 그대로 둔다. 일시정지 상태였으면 그 상태를 유지한다.
    - 일시정지 중에는 소스만 보고 상태를 잃었는지 알 수 없다. 판별 방법은 빌더가 정한다. 예를 들어 재개할 때 소스가 실제로 재생되지 않으면 기억한 위치에서 다시 튼다.
- 이벤트가 왔을 때는 Unity가 이미 재생 위치를 잃은 뒤다. 그래서 음악 틱에서 재생 중인 인트로·루프 소스의 `timeSamples`를 매 프레임 기억해 둔다. 정수 하나를 읽으므로 할당이 없다.
  - 복구할 때 기억한 위치로 `timeSamples`를 맞추고 다시 재생한다. 인트로 중이었으면 루프 시작은 `ResumeMusic`처럼 인트로의 남은 시간으로 다시 예약한다.
  - 장치 전환 자체의 짧은 공백은 피할 수 없다. 무음에서 파형 중간으로 바로 들어가면 딸깍 소리가 날 수 있어, 복구할 때 아주 짧은 페이드인을 건다. 길이는 빌더가 정한다.
- 효과음은 이어서 틀지 않는다. JP 효과음은 모두 짧은 단발음이고 반복 재생(`Loop`) 정의가 없다.
- 음성 풀(AudioSource)과 준비된 정의는 다시 만들지 않는다.

## 결정: `com.bun3.unity.audio.steamaudio`

### 조립 함수

`SteamAudioSoundSetup`에 기존 1인자 `Apply`와 별개인 조립 함수를 더한다.

받는 값:

- `SoundSystemConfig config`
- 디코딩 캐시 상한(바이트)
- 맵 음향 등록 목록 `PlanarAcousticOutputs`. 게임의 맵 음향 세션이 읽는 목록이다.

하는 일:

- 코어 오클루전을 끈다(`OcclusionChecksPerFrame = 0`).
- 클립 캐시(`SteamAudioClipCache`)를 만들고, 정의 준비 훅에 디코딩을 연결한다.
- `CreateVoiceOutput`에 공간음 출력(`SteamAudioSoundOutput`) 생성과 맵 음향 바인딩(`PlanarAcousticSfxBinding`) 생성·등록을 연결한다.
- 위 자원과 장치 변경 구독은 `SoundSystem`이 해제될 때 함께 정리한다.

이름은 빌더가 정한다. 다만 기존 1인자 `Apply`와 다른 경로라는 점이 이름이나 XML 문서에서 드러나야 한다.

### HRTF 공유

- 지금은 음성마다 `SteamAudioPathRenderer.CreateDefault`가 Steam Audio 컨텍스트와 HRTF를 따로 만든다. 음성이 64개면 각각 64개씩이다.
- 앞으로는 조립 함수가 만든 런타임이 컨텍스트 하나와 HRTF 하나를 갖고, 모든 음성 렌더러가 이를 공유한다. 렌더러 생성자는 이미 컨텍스트와 HRTF를 인자로 받는다.
- 음성마다 하나씩 생기는 숨김 정리 오브젝트(`Bun3 native sound retirement`)도 하나로 합친다.

### 드라이버 클립 공유

- 공간음 출력은 1.0으로 채운 스테레오 클립(`Bun3 native SFX driver`)을 반복 재생해 `OnAudioFilterRead`를 돌린다. 지금은 음성마다 같은 내용의 클립을 하나씩 만든다.
- 모든 음성이 클립 하나를 같이 쓴다.

### 장치 변경 (Steam Audio)

- 조립 함수의 런타임이 `AudioSettings.OnAudioConfigurationChanged`를 구독한다. 코어의 처리(음성 정리, 배경음 이어 재생)와 함께 동작한다.
- 매 이벤트마다 코어와 같은 원칙으로 실제로 잃은 것만 복구한다.
  - 공유 드라이버 클립을 잃었을 때만 하나를 다시 만들고, 멈춘 출력만 정리한다.
  - 클립을 잃었는지 판별하는 방법은 빌더가 정한다. 실제 `AudioSettings.Reset` PlayMode 테스트로 확인한 결과를 근거로 한다. 후보는 클립 참조 파괴와 드라이버 소스 정지다.
- 샘플레이트나 DSP 버퍼 크기가 바뀐 경우에만: HRTF 하나를 새로 만들고 각 음성의 렌더러(효과 객체와 프레임 크기 버퍼)를 교체한다.
- 아래는 그대로 둔다.
  - 음성 풀(AudioSource)
  - 클립 캐시. 원본 샘플레이트로 저장하고 재생할 때 변환하므로 장치와 무관하다.
- 드물게 일어나는 이벤트라 이 경로에서는 할당을 허용한다. 재생·틱 경로는 계속 무할당이다.

### 맵 밖 위치 효과음

- 공간음 출력은 맵 음향 시뮬레이션이 풀어 줄 때까지 막힌 상태로 시작한다(`StartBlocked`). 그래서 맵 음향 세션이 없는 메뉴에서는 위치 효과음이 들리지 않고 2D 효과음만 들린다.
- 이 동작은 바꾸지 않는다. 메뉴 효과음은 2D로 저작한다.

## 게임 적용 (JP)

### 초기화 위치

사운드 설정을 조립하는 정적 함수 하나를 JP에 둔다. 두 곳에서 부른다.

- `Boot.Start`: Menu를 불러오기 전에 부른다.
- 에디터 Play 시작 훅: `UNITY_EDITOR`에서만 동작하는 `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` 훅이다. Play를 시작한 첫 씬이 Menu일 때만 부른다.
  - 테스트 러너는 자체 테스트 씬으로 Play를 시작하므로 이 훅이 초기화하지 않는다. 그래서 Menu 씬을 여는 테스트마다 사운드가 켜지지 않는다.
  - 출시 빌드는 항상 Boot에서 시작하므로 이 훅이 필요 없다.

이 함수는 설정을 조립해 kit에 넘기기만 한다. 재생 API를 감싸지 않으므로 실패 조건의 "래퍼"가 아니다.

Boot에서는 Loading 오버레이가 보이지 않는다(`SceneFlow`는 Game 씬 전환에만 오버레이를 띄운다). 카탈로그는 짧은 효과음 22개라 디코딩 시간이 짧을 것으로 보지만, 계획 단계에서 측정해 눈에 띄면 다시 다룬다. 아래 코드의 필드·함수 이름은 예시다.

```csharp
var config = GameConfig.I;
var catalog = config.SoundCatalog;
var settings = new SoundSystemConfig
{
    SfxVoices = config.SoundChannelCount,
    GroupGain = group => PersonalSettings.SoundGain(config, group),
    MusicFadeSeconds = config.MusicFadeSeconds,
};
SteamAudioSoundSetup.ApplyNativeOutput(settings, (long)config.SoundDecodedCacheMiB * 1024 * 1024, GameAcousticSession.OutputRegistry);
SoundSystem.Initialize(catalog, settings);
```

- **카탈로그 연결:** `GameConfig`에 `SoundCatalog` 참조 필드를 더한다. `GameConfig`는 `Resources`에서 읽으므로 어느 씬에서 시작해도 카탈로그를 찾을 수 있다.
  - `M1EditorSetup`은 `GameConfig.asset`이 이미 있으면 그대로 두므로, 이 필드는 따로 연결해야 한다. 지금 Game 씬 SoundManager에 연결하던 `EnsureSoundCatalog()` 결과를 여기에 넣는다.
  - 저작 결과가 바뀌므로 `GeneratedAssetVersion`을 올린다.
- **맵 음향 등록 목록:** `GameAcousticSession`은 정적 `PlanarAcousticOutputs`를 내부에 갖고 있다. 이를 조립 함수에 넘길 수 있게 노출한다.
  - 세션이 내려가도 등록은 유지된다. `PlanarAcousticSessionScope.Dispose`는 `Detach`만 하기 때문이다.
  - 그래서 Boot에서 한 번 등록한 음성을 다음 판에서도 그대로 쓴다.

### 지우는 것

- `SoundManager.cs`, `Bun3SoundOutput.cs`, `GameAcousticSfxBinding.cs`.
- Game 씬의 SoundManager 오브젝트(`M1EditorSetup`의 생성 코드)와 CCTV 스모크 씬의 SoundManager(`CctvEditorSetup`).
- 벽 linecast 폴백(`WallOcclusion`). `GameConfig`가 없을 때만 쓰는 경로라 출고 빌드에서는 쓰지 않는다.
- `OnSourceCreated`의 Linear rolloff. Steam Audio 출력은 위치 재생 때 `spatialBlend = 0`으로 두고 직접 렌더링하며, 2D 재생에서는 rolloff가 의미가 없다.

### 호출부

10개 파일의 `SoundManager.Play`와 `PlayAt`을 `SoundSystem.Play`와 `PlayAt`으로 바꾼다. 키와 재생 위치는 그대로다.

### 버전과 문서

- `PlayerSettings.bundleVersion`의 patch를 올린다. 네트워크나 동기화 데이터는 바뀌지 않는다.
- `unity/CLAUDE.md`, `docs/sound-authoring-guide.md` 등 SoundManager를 설명하는 문서를 같은 작업에서 고친다.

### 작업 순서

1. kit 워크트리(`Bun3/audio-bootstrap`)에서 작업한다.
2. JP가 `file:`로 로컬 참조해 확인한다.
3. 확인이 끝나면 브랜치를 푸시하고 kit main에 `--no-ff`로 머지한다.
4. JP는 main 참조로 되돌린다.

## 제약

- **kit:**
  - 코드, XML 문서, README는 영어로 쓴다.
  - 모든 public 멤버에 XML 문서를 단다.
  - 경고 0, 최소 주석.
  - 재생·틱 경로는 무할당.
  - 내용이 바뀌면 버전을 올린다.
- **JP:**
  - 새 컴파일 경고 금지.
  - 모든 변경에 자동 테스트를 둔다.
  - 테스트는 출고 콘텐츠(`SoundCatalog.asset`) 대신 테스트 전용 카탈로그를 만든다.
  - 저작 결과가 바뀌면 `GeneratedAssetVersion`을 올린다.
- **정적 상태:** 정적 `SoundSystem`은 싱글톤이다. JP 테스트가 이를 초기화하면 TearDown에서 반드시 `Shutdown()`으로 되돌린다.

## 빌더에게 맡기는 것

- Steam Audio 조립 함수의 이름과 런타임 객체 구조.
- 정의 준비 훅의 이름과 시그니처, 음악 페이드 설정 필드의 이름.
- 렌더러 교체를 음성 출력 안에서 할지, 출력 자체를 교체할지. 단, AudioSource와 클립 캐시는 유지한다.
- 장치 변경을 테스트에서 흉내 내는 방식.
- JP 테스트 재구성 방식.
  - 지금 `SoundManagerTests`, `RootBoxSoundTests`, `MapStartupTests`(`PositionalSoundPlayed`), `SoundOutputPlayModeTests`가 `SoundManager`의 테스트 seam을 쓴다.
  - 이미 있는 `SoundSystemConfig.CreateVoiceOutput`에 가짜 출력을 넣으면 어떤 정의가 재생됐는지 관측할 수 있다.
- JP 조립 함수의 이름과 둘 곳. Boot 안에서 부르는 정확한 위치는 Menu를 불러오기 전이라는 조건만 지킨다.

## Claude가 정한 것 (사용자 확인 필요)

- 정적 멤버를 별도 클래스가 아니라 기존 `SoundSystem` 클래스에 둔다. 사용자가 예로 든 이름을 그대로 따랐다.
- `Play(key)`는 항상 2D로, `PlayAt`은 정의를 따라 재생한다. 지금 JP 규칙과 같다.
- 없는 키는 로그 없이 무시한다. 초기화 전 호출은 에디터에서만 처음 한 번 경고한다.
- 장치가 바뀐 뒤 배경음을 이어서 틀 때 아주 짧은 페이드인을 건다.
- 장치가 바뀌어 멈춘 효과음은 이어서 틀지 않고 끝낸다.
- 장치 변경 처리는 Steam Audio 패키지만이 아니라 코어도 맡는다.
- 공간음 드라이버 클립을 모든 음성이 공유한다.
- Boot에 로딩 화면을 새로 띄우지 않는다.
- 두 번째 `Initialize`는 예외를 던진다. 다시 초기화하려면 `Shutdown` 후 부른다.
- 카탈로그 검증에 실패하면 `Initialize`가 예외를 던지고, Boot는 이를 잡지 않는다. 카탈로그 오류는 기존 `GeneratedAudioAssetsTests`가 먼저 잡는다.
- 준비 오류 API는 옮기지 않고 로그로 바꾼다.
- 에디터에서 Menu 씬부터 Play할 때는 Play 시작 훅이 초기화한다. 첫 씬이 Menu일 때만 동작한다.
- 캡처 씬과 CCTV 스모크 빌드는 초기화하지 않아 소리가 나지 않는다. 질문이 Menu 씬 시작만 다뤘기 때문이다.
- 맵 밖 위치 효과음은 지금처럼 들리지 않는다.
- 벽 linecast 폴백과 Linear rolloff를 지운다.
- kit은 minor, JP는 patch를 올린다.

## 검증

kit (`com.bun3.unity.audio`):

- 정적 API:
  - 초기화 전 호출과 없는 키를 무시한다.
  - `PlayAt`은 정의에 따라 2D와 위치 재생으로 나뉘고, `Play`는 항상 2D다.
  - 같은 음악 키로는 다시 시작하지 않고, 없는 음악 키는 배경음을 끈다.
  - 두 번째 `Initialize`는 예외를 던지고, `Shutdown` 후에는 다시 초기화할 수 있다.
- 정적 `Play(key)`와 `PlayAt(key)`에서 할당이 없다. 기존 `AllocationTests` 방식으로 확인한다.
- 정의 준비 훅이 초기화 때는 카탈로그 전체에 대해, 에디터 클립 교체 때는 해당 정의에 대해 불린다.
- 장치 변경:
  - 멈춘 음성만 끝난 것으로 처리되고, 이어서 새 효과음을 재생할 수 있다. 반복 음성도 남지 않는다.
  - 상태를 잃지 않은 채 이벤트만 오면(소스가 계속 재생 중) 효과음과 배경음을 건드리지 않는다.
  - 재생 중이던 배경음이 끊긴 위치 근처(한 프레임 이내)에서 이어서 재생되고, 일시정지 상태는 유지된다.
  - 인트로 중에 바뀌면 인트로가 끝나는 시점에 루프가 이어진다.
  - 정적 `PlayMusic` 이후 매 프레임 위치 기억에서 할당이 없다.
  - 실제 `AudioSettings.Reset` 호출로 Unity가 재생 상태와 재생 위치를 잃는 것을 먼저 PlayMode 테스트로 확인한다.

kit (`com.bun3.unity.audio.steamaudio`):

- 모든 음성 렌더러가 HRTF 하나를, 모든 음성이 드라이버 클립 하나를 공유한다.
- 샘플레이트가 같은 장치 변경: 드라이버 클립을 잃었으면 다시 만들고 멈춘 출력만 정리한다. 잃지 않았으면 아무것도 바꾸지 않는다. HRTF와 렌더러는 그대로다.
- 샘플레이트나 버퍼 크기가 바뀐 장치 변경: HRTF와 렌더러도 교체한다.
- 두 경우 모두 AudioSource와 클립 캐시의 디코딩 바이트는 그대로고, 이어서 위치 효과음을 다시 재생할 수 있다.
- `SoundSystem`이 해제되면 HRTF, 캐시, 드라이버 클립, 구독이 정리된다.

JP:

- 기존 사운드 테스트를 새 구조로 옮겨 같은 동작을 확인한다. 키별 재생 횟수, 사이렌 위치 재생, 장치 변경 후 재생이 여기에 해당한다.
- Boot가 사운드를 초기화하는지, Game 씬에 SoundManager가 남아 있지 않은지 확인한다.
- Play 시작 씬이 Menu면 초기화하고, 다른 씬(테스트 씬 포함)이면 초기화하지 않는다.
- Boot 씬에서 시작하면 Boot가 초기화한다.

수동 보조 확인 (자동 테스트를 대신하지 않음):

- 실제 Windows 기본 출력 장치를 이어폰↔스피커로 바꿔, 배경음이 이어지고 위치 효과음이 다시 나는지 듣는다.
- 사용자 PC의 소리 설정을 바꾸는 일이라 진행 전에 사용자에게 허락을 받는다.
- `GameConfig.asset`의 카탈로그 참조가 출고 카탈로그를 가리킨다.
