# Unity 사운드 매니저 지형 조사 — 1차 사료 기준

- 조사일: 2026-08-31
- 방법: GitHub 저장소 실제 소스(raw 파일)·package.json·LICENSE, Asset Store 상품 페이지(2026-08-31 스크랩), FMOD/Wwise 공식 라이선스 페이지, Unity 공식 문서를 직접 확인. 2차 요약 블로그는 근거로 쓰지 않음.
- 목적: `Bun3.*` 사운드 매니저 패키지(AudioSource pooling, fade in/out, 무할당 핫패스)를 직접 만들기 전에 기존 솔루션의 기능·아키텍처·라이선스를 검증.

---

## 요약

1. **오픈소스에는 "프레임워크급" 후보가 사실상 없다.** GitHub에서 `audio manager unity language:C# stars:>100` 검색 결과는 단 2건 — JSAM(757★, 활발)과 Microsoft Audio Manager(193★, **archived**)뿐이다. 200★를 넘는 실전 사운드 매니저는 JSAM 하나. 모두 MIT라 라이선스 문제는 없지만, **전부 coroutine 기반 fade + per-play 할당 구조**라 이 레포의 무할당 규율과 충돌한다.
2. **유료 자산(Master Audio $150, Sonity $99, Audio Toolkit $49)은 기능은 완성돼 있으나 Asset Store EULA(Extension Asset, per-seat) 때문에 Bun3 패키지에 재배포할 수 없다.** 프레임워크 구성 요소가 될 수 없고, 게임별 구매 선택지일 뿐이다.
3. **미들웨어(FMOD/Wwise)는 인디 무료 티어가 넉넉하다**(FMOD: 개발예산 $600k 미만 + 연매출 $200k 미만 무료·로고 필수 / Wwise: 제작예산 $250k 미만 무료·에셋 무제한). 그러나 Unity AudioSource 체계를 통째로 대체하는 별도 파이프라인이라, 재사용 프레임워크의 기본 의존성으로는 부적합. **게임 단위 opt-in 선택지로 남긴다.**
4. **Unity 내장 기능은 여전히 pooling·per-voice fade를 제공하지 않는다.** Unity 6에 Audio Random Container(클립 랜덤 재생)가 추가됐고, mixer snapshot 전환(`TransitionToSnapshots`)이 그룹 단위 fade를 대신할 뿐이다. DSPGraph는 0.1.0-preview 상태로 방치돼 있다.
5. **결론: 직접 만든다.** 단, 기존 구현들이 반복 증명한 패턴(사전 워밍 풀 + 보이스 훔치기, ScriptableObject 사운드 정의, per-sound 인스턴스 상한, 중앙 Update 루프)은 그대로 채택하고, 이들이 전부 놓친 것(세대 검증 struct 핸들, 무-coroutine fade, 무할당 완료 콜백)을 차별점으로 삼는다.

---

## 기능 매트릭스

| | JSAM | MathewHDYT UAM | prime31 SoundKit | MS Audio Manager | Master Audio 2024 | Sonity | Audio Toolkit | FMOD | Wwise |
|---|---|---|---|---|---|---|---|---|---|
| 형태 | OSS·UPM | OSS·unitypackage | OSS·단일 파일 | OSS·UPM | $150 | $99 | $49 | 미들웨어 | 미들웨어 |
| 라이선스 | MIT | MIT | 파일 없음(미확인) | MIT | AS EULA | AS EULA | AS EULA | 독자(무료 티어) | 독자(무료 티어) |
| 유지보수(2026-08) | 활발 (v3.1.1) | 저활동 (2026-05) | 사실상 중단(2014년작) | **archived** | 활발 (2026-06-24) | 활발 (2026-07-09) | 활발 (2026-07-02) | 활발 | 활발 |
| AudioSource pooling | ✅ 리스트 스캔+동적 증설 | ❌ (등록된 named source) | ✅ Stack 풀+상한 | (미확인) | ✅ "no instantiating during play" | ✅ voice 시스템 | ✅ "audio object pools" | 자체 voice mgmt | 자체 voice mgmt |
| Fade in/out | ✅ coroutine per fade | ✅ coroutine(LerpVolume) | ✅ coroutine(fadeOut만) | (미확인) | ✅ crossfade/gapless | ✅ (SoundEvent 설정) | ✅ cross-fading | ✅ | ✅ |
| 3D/공간화 | ✅ target 추적(중앙 루프) | ✅ 3D 위치 재생 | ❌ (매니저 GO에 부착) | (미확인) | ✅ +occlusion | ✅ +Steam Audio 연동 | ✅ | ✅ | ✅ |
| 인스턴스 상한/보이스 제한 | ✅ maxPlayingInstances | ❌ | ❌ (풀 상한만) | (미확인) | ✅ voice limit+재트리거 제한 | ✅ | ✅ 대체 사운드 선택 | ✅ | ✅ |
| Mixer 통합 | ✅ group 지정 | ✅ mixer group lerp | ❌ | (미확인) | 자체 bus+ducking | ✅ addressable mixer | ✅ 카테고리 볼륨 | 자체 DSP | 자체 DSP |
| async/Addressables 로딩 | ❌ | ❌ | ❌ | ❌ | Resources 자동 로드/언로드 | ✅ Addressables 지원 명시 | ❌ | 뱅크 시스템 | 뱅크 시스템 |
| per-play 할당 규율 | ❌ (coroutine·string·List) | ❌ (closure·WaitUntil) | △ (재생은 무할당, fade는 closure) | (미확인) | "super-low allocation" 광고 | "highly optimized·IL2CPP" 광고 | (미확인) | 네이티브 | 네이티브 |
| 핸들 반환 | MonoBehaviour helper ref | error enum (string key) | 풀링된 class ref | (미확인) | PlaySoundResult | SoundEvent 기반 | PlayAudioObject ref | 핸들/이벤트 인스턴스 | 이벤트 ID |

---

## 후보별 상세

### 1. JSAM — Jacky's Simple Audio Manager (오픈소스 중 1위)

- 저장소: https://github.com/jackyyang09/Simple-Unity-Audio-Manager — 757★, MIT([LICENSE.md](https://github.com/jackyyang09/Simple-Unity-Audio-Manager/blob/master/LICENSE.md)), 2026-08 시점 push 있음.
- 패키지: UPM `com.brogrammist.jsam` v3.1.1, `"unity": "2021.3"` ([package.json](https://github.com/jackyyang09/Simple-Unity-Audio-Manager/blob/master/package.json)).
- 아키텍처(소스 직접 확인 — [AudioManagerInternal.cs](https://github.com/jackyyang09/Simple-Unity-Audio-Manager/blob/master/Runtime/Scripts/AudioManagerInternal.cs), [BaseAudioChannelHelper.cs](https://github.com/jackyyang09/Simple-Unity-Audio-Manager/blob/master/Runtime/Scripts/BaseAudioChannelHelper.cs)):
  - **풀**: `List<SoundChannelHelper>`/`List<MusicChannelHelper>` — 채널당 GameObject+AudioSource+MonoBehaviour helper. 기동 시 `StartingSoundChannels`개 생성, 빈 채널은 선형 스캔(`IsFree` = `!Reserved && !enabled`)으로 찾고, 없으면 `DynamicSourceAllocation` 설정에 따라 증설 또는 에러.
  - **핸들**: `PlaySoundInternal(...)`이 `SoundChannelHelper`(MonoBehaviour 참조)를 그대로 반환. 채널이 재사용되면 이전 핸들이 다른 사운드를 가리키는 **stale handle 위험**이 API에 내장돼 있다.
  - **fade**: helper별 `StartCoroutine(FadeIn/FadeOut)` — fade마다 coroutine 할당. 파일 설정 기반 fadeInOut은 helper의 per-frame `Update()`에서 `Mathf.Lerp`.
  - **완료 감지**: 콜백이 아니라 helper `Update()`에서 `enabled = AudioSource.isPlaying`으로 자기 비활성화(= 풀 반환). 완료 이벤트 API 없음.
  - **인스턴스 상한**: `maxPlayingInstances` 초과 시 가장 오래된 helper 재사용(voice stealing) — `Dictionary<BaseAudioFileObject, List<helper>>`로 추적.
  - **공간화**: 중앙 매니저 Update/FixedUpdate/LateUpdate에서 `List<IAudioHelperEvents>`를 순회해 target 위치 추적. timescale 변화 감지·pitch 보정도 같은 중앙 루프.
  - **볼륨**: mixer 스냅샷이 아니라 Master/Music/Sound/Voice 채널 float 곱 + 변경 이벤트 브로드캐스트, PlayerPrefs 저장.
  - **ID 체계**: AudioLibrary(ScriptableObject)에서 enum을 **Assembly-CSharp에 codegen**하고 `Type.GetType(name + ", Assembly-CSharp")` + 문자열 dictionary로 역참조 — asmdef 분리 프로젝트·패키지화와 상성이 나쁘다.
- 판정: 기능 스코프는 참고서로 최적. 그러나 MonoBehaviour helper 반환, coroutine fade, 문자열/enum codegen 결합, 채널당 GameObject 구조 모두 우리 규율과 충돌. **의존이 아니라 설계 레퍼런스.**

### 2. MathewHDYT/Unity-Audio-Manager

- 저장소: https://github.com/MathewHDYT/Unity-Audio-Manager — 88★, MIT, 최근 갱신 2026-05.
- 아키텍처([DefaultAudioManager.cs](https://github.com/MathewHDYT/Unity-Audio-Manager/blob/main/Example_Project/Assets/Scripts/AudioManager/Service/DefaultAudioManager.cs) 확인): 서비스 로케이터 구조. 사운드를 **이름(string)으로 등록**해 `IDictionary<string, AudioSourceWrapper>`에 보관 — **풀링 없음**, 사운드당 AudioSource 1개(3D 재생 시 자식 source 동적 생성/`Object.Destroy`). `Play(string name)`은 `AudioError` enum 반환. fade는 `LerpVolume(name, endValue, duration)` coroutine + callback delegate. 진행률 콜백은 `SubscribeProgressCoroutine`이 `new WaitUntil(closure)`로 폴링 — per-call 할당 다수(closure, WaitUntil, Dictionary).
- 판정: 문서화는 좋지만 "등록된 named sound" 모델이라 다발성 SFX 풀링 시나리오 자체가 없다. 에러 enum 반환 API(핸들 없음)도 제어에 불리. 제외.

### 3. prime31/SoundKit

- 저장소: https://github.com/prime31/SoundKit — 44★, 2014년작, LICENSE 파일 없음(라이선스 **미확인** — 재사용 불가로 취급).
- 아키텍처([SoundKit.cs](https://github.com/prime31/SoundKit/blob/master/Assets/SoundKit/SoundKit.cs) 전문 확인): 단일 파일. `Stack<SKSound> _availableSounds` + `List<SKSound> _playingSounds`, `maxCapacity` 초과 시 반납 대신 Destroy. **완료 감지가 돋보인다**: coroutine 없이 매니저 `Update()`에서 `_elapsedTime += Time.deltaTime`을 누적해 `clip.length` 초과 시 `stop()` → 저장해 둔 `Action _completionHandler` 호출 → 풀 반납. fade-out만 지원(매니저 coroutine + closure). AudioSource를 전부 매니저 GameObject에 붙여 **3D 위치 재생 불가**.
- 판정: "중앙 Update 루프 + 시간 누적으로 완료 감지"라는 가장 오래되고 검증된 무-coroutine 패턴의 원형. 코드 자체는 낡았고 3D 미지원.

### 4. Microsoft Audio-Manager-for-Unity

- 저장소: https://github.com/microsoft/Audio-Manager-for-Unity — 193★, MIT, 노드 기반 에디터. **2020년 이후 방치, 현재 archived(read-only)**. 제외.

### 5. CarterGames/AudioManager

- 저장소: https://github.com/CarterGames/AudioManager — 41★, MIT, Unity 2020.3+, UPM Git URL 설치. README 기준 강점은 **오디오 클립 자동 스캔 라이브러리 관리**(시작 무음 트리밍 포함)와 Inspector 플레이어. README에는 pooling·fade가 명시돼 있지 않음(기능 존재 여부 미확인).

### 6. 기타

- Mukarillo/UnitySoundManager (58★, https://github.com/Mukarillo/UnitySoundManager): 설명상 pooling+fade 지원 주장, 2018년작 소규모. 소스 미검토.
- keijiro/Lasp(1.7k★) 등은 신호 처리/시각화용이지 사운드 매니저가 아님.
- Eazy Sound Manager(무료, Asset Store: https://assetstore.unity.com/packages/tools/audio/eazy-sound-manager-71142): 입문용 단순 매니저.
- **검색 근거**: GitHub `audio manager unity language:C# stars:>100 in:name,description` → 2건, `topic:audio-manager language:C# stars:>30` → Unity용 1건(MathewHDYT). 300★↑ 사운드 매니저는 JSAM이 유일.

### 7. Sonity — Audio Middleware (Asset Store, 오픈소스 아님)

- 스토어: https://assetstore.unity.com/packages/tools/audio/sonity-audio-middleware-229857 — **$99**, 퍼블리셔 Sonigon, v1.2.0, 최종 릴리스 2026-07-09, Unity 2018~6.7 테스트·IL2CPP 완전 지원·WebGL(DSP 필터 제외)·domain reload 비활성 지원·**Addressables 지원 명시**. 무료 트라이얼 별도(244369).
- 특징: SoundEvent/SoundParameter ScriptableObject 기반 live-edit, 음악 playlists/stems/intros, per-voice 커스텀 DSP, 씬 뷰 라이브 디버그, "Fully documented source code provided"(구매 시 소스 포함).
- **검증 실패 항목**: 과제에서 언급된 "github.com/JohanLarsson의 Sonity 저장소"는 존재하지 않음 — GitHub `sonity in:name` 검색에서 Unity 오디오 미들웨어 저장소 0건. Sonity는 Asset Store 전용이다.

### 8. Master Audio 2024: AAA Sound (Asset Store)

- 스토어: https://assetstore.unity.com/packages/tools/audio/master-audio-2024-aaa-sound-287785 — **$150**, Dark Tonic, v1.0.5, 최종 릴리스 2026-06-24, Unity 2022+, 리뷰 890건(카테고리 최다).
- 특징(스토어 페이지 원문): Real-Time Parameter Commands(RTPC — 파라미터 값으로 클립 crossfade·pitch·필터 제어), 오디오 occlusion, 가중치 랜덤 variation, **카테고리별 voice limit + 시간제한 재트리거**, per-sound ducking, crossfade/gapless 플레이리스트, Resources 자동 로드/언로드, **"No Instantiating during play! ... super-low allocation on all platforms"**, 멀티플레이어 브로드캐스트(FishNet/Mirror/Photon). 구버전 Master Audio 2022($99.99)도 병행 판매.
- 판정: C# 매니저 카테고리의 기능 상한선. 우리가 v1에서 안 만들어도 되는 것(occlusion, RTPC, 멀티플레이어)과 만들어야 하는 것(voice limit, ducking, 무할당)의 경계를 보여준다.

### 9. Audio Toolkit (ClockStone, Asset Store)

- 스토어: https://assetstore.unity.com/packages/tools/audio/audio-toolkit-2647 — **$49**, v12.5, 최종 릴리스 2026-07-02, Unity 2022/2023/6. `AudioController.Play("MySoundID")` 문자열 API, 카테고리 볼륨, 대체 사운드 선택 모드, **audio object pool**, random pitch/volume, cross-fading, gapless stitching. 문자열 키 API는 우리 규율(런타임 문자열 최소화)과 정면 충돌.

### 10. Soundy (Doozy) — 현황 불명

- 2026-08-31 검색 기준 **독립 Asset Store 상품 페이지를 찾을 수 없음**(검색 결과는 제3자 아카이브 뿐). Soundy는 Doozy UI Manager(패키지 203601)의 내장 모듈로 배포된다는 것이 Unity 공식 포럼 스레드에서 확인됨: https://discussions.unity.com/t/doozyui-complete-ui-management-system/623889 ("Soundy, the sound manager that comes with DoozyUI"). **독립 자산으로는 사실상 단종으로 판단** — 미검증 표기.

---

## 미들웨어: buy-vs-build 상한선

### FMOD (https://www.fmod.com/licensing — 2026-08-31 확인)

| 티어 | 조건 | 비용 |
|---|---|---|
| Free Indie | 개발예산 **$600k 미만** + 연매출 **$200k 미만** | 무료 (게임당 등록 필요) |
| Indie | 개발예산 $600k 미만 | $2,000/게임 |
| Basic | 예산 $600k–$1.8M | $6,000/게임 |
| Premium | 예산 $1.8M 초과 | $18,000/게임 |

- 전 티어 **FMOD 로고 표기 필수**(로고 면제는 Basic $6k/Premium $12k 추가 구매). 배포권은 lifetime, 전 기능·전 플랫폼 동일. Unity 공식 통합 플러그인 무료(https://www.fmod.com/unity).

### Wwise (https://www.audiokinetic.com/en/wwise/pricing/ — 2026-08-31 확인)

| 티어 | 조건 | 비용 |
|---|---|---|
| Indie | 제작예산 **$250K 미만** | 무료, **사운드 에셋 무제한**, 전 엔진 기능, 로열티 0% |
| Pro | 예산 $250K–$2M | $8,000부터 |
| Premium / Platinum | 예산 $2M 초과 | $25,000 / $45,000부터 |

- 비상업/학술 프로젝트는 별도 무료 등록.

### C# 매니저 대비 미들웨어가 주는 것 / 뺏는 것

- **주는 것**: 전용 authoring tool(사운드 디자이너 워크플로), 실제 버스/스테이트/RTPC 그래프, 런타임 프로파일러, 뱅크 단위 메모리 관리·스트리밍, 플랫폼별 코덱 최적화. 이건 C# 레이어로는 도달 불가능한 상한선이다.
- **뺏는 것**: Unity AudioSource/AudioMixer/AudioClip 파이프라인 전체(기존 에셋·mixer 셋업 폐기), 빌드 파이프라인 복잡도(뱅크 빌드), 학습 곡선. 그리고 **프레임워크 관점의 결정타**: Bun3 패키지가 FMOD/Wwise에 의존하면 모든 게임이 그 미들웨어와 그 라이선스 절차에 묶인다.
- 결론: 솔로 개발 다작 체제에서 게임 대부분이 인디 무료 티어에 들어가므로 "특정 게임이 사운드 디자인 중심이면 FMOD 채택"은 항상 열려 있는 선택지다. **프레임워크의 기본값은 Unity 내장 위의 얇은 레이어**로 하되, 게임이 미들웨어로 갈아탈 때 게임 코드 호출부가 안 바뀌도록 재생 API를 좁게 유지한다.

---

## Unity 내장 현황 (Unity 6 기준)

- **AudioSource에는 fade API가 없다.** 재생 계열은 `Play`/`PlayOneShot`/`PlayDelayed`/`PlayScheduled`뿐 — 페이드는 volume을 매 프레임 직접 조작해야 한다. (https://docs.unity3d.com/6000.2/Documentation/ScriptReference/AudioSource.html)
- **AudioSource pooling도 없다.** 내장 voice management는 `AudioSource.priority` 기반 가상화(virtualization)뿐이고, source GameObject 생성/재사용은 사용자 몫.
- **Mixer 스냅샷 전환이 유일한 내장 fade 메커니즘**: `AudioMixer.TransitionToSnapshots(AudioMixerSnapshot[] snapshots, float[] weights, float timeToReach)` — 그룹(버스) 단위 가중 블렌드. per-voice fade에는 못 쓴다. (https://docs.unity3d.com/6000.2/Documentation/ScriptReference/Audio.AudioMixer.TransitionToSnapshots.html)
- **Audio Random Container**(Unity 6 매뉴얼): 발소리·타격음 등 SFX용 클립 랜덤 재생 리스트 에셋. variation 셋업은 흡수하지만 pooling/fade/핸들 관리와는 무관. (https://docs.unity3d.com/6000.2/Documentation/Manual/AudioRandomContainer.html)
- **DSPGraph는 죽은 상태**: `com.unity.audio.dspgraph` 0.1.0-preview.22에서 멈춘 preview 패키지. production 언급 없음. DOTS 오디오 로드맵은 사실상 공백. (https://docs.unity3d.com/Packages/com.unity.audio.dspgraph@0.1/manual/index.html)
- 요컨대 **우리가 필요한 레이어(풀링, per-voice fade, 핸들, 완료 통지)는 Unity 로드맵이 채워줄 기미가 없다** — 직접 만드는 것이 낭비가 아니다.

---

## Buy vs Build 판정

| 선택지 | 판정 | 이유 |
|---|---|---|
| 오픈소스 채택(JSAM 등) | ❌ | MIT라 법적 문제는 없으나, coroutine fade·MonoBehaviour 핸들·문자열/codegen ID·per-play 할당이 레포 규율과 충돌. 포크 후 재작성하면 결국 새로 짜는 것과 같다. |
| 유료 자산 채택 | ❌ (프레임워크로서) | Asset Store EULA(Extension Asset) — **Bun3 패키지에 포함해 재배포 불가**. 게임 레포가 개별 구매하는 건 자유지만 프레임워크 구성 요소가 못 된다. |
| FMOD/Wwise 기본 채택 | ❌ (기본값으로서) | 인디 무료 티어는 충분하지만 게임 전부를 미들웨어·로고 의무·뱅크 파이프라인에 묶는다. 게임별 opt-in으로 유지. |
| **직접 구축 (Bun3.Unity.Audio)** | ✅ | 필요한 스코프(풀링+fade+핸들+채널 볼륨)는 작고, 기존 구현들이 설계 패턴을 충분히 검증해 놨으며, 무할당 규율을 지키는 구현은 시장에 없다. |

---

## 우리 구현을 위한 아키텍처 교훈

실소스에서 확인한 패턴과, 전원이 놓친 지점:

1. **핸들은 세대 검증 struct로.** 조사한 어떤 OSS도 안전한 핸들이 없다 — JSAM은 재사용되는 MonoBehaviour 참조를, SoundKit은 재활용되는 class 인스턴스를 반환하고(둘 다 stale 참조가 소리 없이 다른 사운드를 조작), MathewHDYT는 아예 에러 enum만 반환한다. `readonly struct SoundHandle { int slot; uint generation; }` + 슬롯별 generation 카운터로 stale 접근을 no-op 처리하는 것이 정답이고, 이 차별점 하나로 기존 전부를 이긴다.
2. **fade는 coroutine이 아니라 보이스 상태 + 중앙 루프.** JSAM·SoundKit·MathewHDYT 전원이 fade마다 coroutine(+closure)을 할당한다. 보이스 슬롯에 `(startVol, targetVol, elapsed, duration)`을 박아 두고 매니저의 단일 `Update()`에서 활성 보이스 배열만 순회하면 할당 0으로 같은 결과가 나온다. 그룹 전체 fade/덕킹/일시정지는 Unity 내장 `TransitionToSnapshots`에 위임.
3. **완료 감지는 SoundKit 방식(중앙 루프 + 경과 시간)이 원형.** `isPlaying` 폴링은 일시정지와 구분이 안 되므로 경과 시간 누적이 낫다. 콜백이 필요하면 per-play closure 대신 (a) 핸들 `IsDone` 폴링을 기본으로, (b) 콜백은 `Action<SoundHandle>` 정적/캐시 델리게이트 + userData 슬롯으로 제한.
4. **풀은 사전 워밍 + 상한 + voice stealing.** SoundKit(Stack, 초과 시 Destroy), JSAM(선형 스캔 + 동적 증설), Master Audio(재생 중 인스턴스화 금지 광고), Audio Toolkit(object pool 광고) — 전부 같은 결론. 상한 도달 시 JSAM/Master Audio처럼 **per-sound `maxPlayingInstances` + 가장 오래된 보이스 훔치기**가 무한 증설보다 낫다. 풀 크기는 수십 개 수준이라 free-list 없이 선형 스캔도 실측상 충분(JSAM 방식).
5. **사운드 정의는 ScriptableObject.** JSAM `SoundFileObject`, Sonity `SoundEvent`, Master Audio 그룹 — 승자 패턴이 일치한다: 클립 배열 + pitch/volume 랜덤 범위 + loop 설정 + 인스턴스 상한 + mixer group을 디자이너가 에셋으로 튜닝. **ID는 문자열/enum codegen(JSAM의 Assembly-CSharp 결합은 반면교사) 대신 ScriptableObject 참조 자체를 키로** 쓰면 런타임 문자열이 0이 된다. Unity 6 프로젝트라면 클립 랜덤 부분을 Audio Random Container로 대체하는 통합 지점도 열어 둘 것.
6. **3D 재생은 풀 소스의 위치 갱신을 fade와 같은 중앙 루프에서.** JSAM은 추적 대상 리스트를 매니저 Update에서 순회(또는 parenting 옵션), SoundKit은 이걸 생략해서 3D를 포기했다. 위치 추적 옵션(고정 위치 / Transform 추적)은 보이스 슬롯 필드로 흡수한다.
7. **채널 볼륨(Master/Music/SFX/Voice) + 저장은 전원이 재구현하는 table-stakes.** JSAM은 float 곱 + PlayerPrefs, 유료 자산은 mixer 파라미터. mixer `SetFloat`(dB 변환) 쪽이 스냅샷·덕킹과 합성이 자연스러우므로 mixer 기반을 기본으로.
8. **스코프 밖으로 밀어낼 것**: occlusion, RTPC, 멀티플레이어 브로드캐스트(Master Audio 영역), per-voice DSP(Sonity 영역), 노드 에디터(MS 영역). 두 번째 게임에서도 필요해지면 그때 내린다 — 이 기능들이 필요한 게임은 애초에 FMOD로 가는 게 맞다.

---

## 계층 분석 — Unity 내장 vs 오픈소스 vs 직접 구축의 경계

### Unity 내장의 구멍 (오픈소스가 존재하는 이유)

Unity가 주는 것은 "보이스 하나"의 원시 API뿐이다. 오픈소스 매니저들이 하나같이 재구현하는 목록이 곧 Unity의 구멍 목록: per-voice fade 없음, 풀링 없음, 재생 핸들 없음(`Play()`는 void), 완료 통지 없음, 인스턴스 상한 없음, 채널 볼륨 조립은 사용자 몫. DSPGraph 방치·ARC의 좁은 스코프가 보여주듯 이 층은 Unity 로드맵이 영구히 비워 둔 서드파티 몫이다.

### 오픈소스의 한계 (스코프는 맞고 구현은 틀렸다)

기능 체크박스는 채워져 있으나 공통 결함: ① per-play 할당(coroutine+closure fade 전원 공통), ② 안전한 핸들 부재(stale 참조가 조용히 다른 사운드를 조작), ③ 패키지화 상성 나쁨(Assembly-CSharp codegen 등), ④ 버스 팩터 1(300★↑는 JSAM 유일, 2위는 archived). 오픈소스는 "무엇을 만들까"(스코프 검증)의 답이고 "어떻게 만들까"의 반면교사다.

### 직접 구축이 효과적으로 채우는 것

시장 공백과 레포 규율이 정확히 겹치는 지점: 세대 검증 struct 핸들(전 제품 부재), 무-coroutine fade(할당 0), 무할당 완료 통지, 런타임 문자열 0인 ScriptableObject 참조 키, 재배포 가능성(EULA상 유료 에셋은 원천 불가), 그리고 얇은 재생 API가 주는 미들웨어 마이그레이션 보험(게임이 FMOD로 갈아타도 호출부 불변).

### 직접 구축이 채울 수 없는 것 (FMOD/Wwise의 존재 이유)

C# 매니저 층에서 원리적으로 도달 불가: 사운드 디자이너 authoring tool, DSP 층 접근(진짜 RTPC·per-voice DSP·occlusion), 뱅크 단위 메모리 관리/스트리밍/코덱 최적화, 런타임 오디오 프로파일러. 실용적 경계선: **사운드가 콘텐츠인 게임(리듬·호러·사운드 디자인 중심)은 FMOD, 사운드가 피드백인 게임(대부분)은 이 레이어로 충분.** 아키텍처 교훈 8의 스코프 컷이 필요해지는 시점 = 그 게임이 FMOD 인디 무료 티어로 갈아탈 시점.

> 용어: **occlusion** = 리스너-음원 사이 지오메트리 차폐 시 감쇠 + low-pass filter로 먹먹하게 처리(보통 raycast 감지). 완전 차폐(벽 너머)와 **obstruction**(같은 공간에서 직접 경로만 막힘 — 직접음만 감쇠, 반사음 유지)을 구분한다. Unity 내장 없음, Master Audio는 raycast 기반 제공, FMOD/Wwise는 룸/포탈 모델까지 지원.

---

## 미검증/한계 항목

- prime31/SoundKit 라이선스: 저장소에 LICENSE 파일 없음 → 코드 재사용 불가로 취급.
- CarterGames AudioManager의 pooling/fade 여부: README에 명시 없음(소스 미검토).
- Soundy 독립 상품의 현재 판매 여부: 스토어 검색으로 확인 불가(단종 추정).
- Master Audio/Sonity의 "저할당" 광고 문구: 스토어 페이지 주장일 뿐 소스 미검증(소스는 구매자에게만 제공).
- Asset Store 자산의 세부 내부 아키텍처는 소스 비공개로 스토어 페이지 기술 수준까지만 확인.
