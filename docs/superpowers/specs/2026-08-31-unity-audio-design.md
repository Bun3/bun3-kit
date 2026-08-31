# Bun3.Unity.Audio 사운드 매니저 설계

날짜: 2026-08-31
상태: 승인
선행 리서치: [2026-08-31-unity-sound-manager-landscape.md](../../research/2026-08-31-unity-sound-manager-landscape.md)

## 목표

Unity 내장이 영구히 비워 둔 사운드 관리 레이어(AudioSource 풀링, per-voice fade,
안전한 재생 핸들, 완료 통지, 보이스 제한)를 **무할당 핫패스 규율**로 채우는 UPM 패키지.
기존 오픈소스(JSAM 등)는 전부 coroutine fade + stale 핸들 + per-play 할당 구조라
채택 불가(리서치 문서 참조). 유료 에셋은 EULA상 재배포 불가.

포지셔닝: FMOD 축소판이 아니라 **Unity 내장 위의 얇은 관리 레이어**.
재생 API를 좁게 유지해 특정 게임이 FMOD로 갈아타도 호출부가 안 바뀌게 한다.
사운드가 콘텐츠인 게임(리듬·호러)은 FMOD로 — 마디 동기화 음악·룸/포탈 오클루전·
파라미터 DSP는 스코프 밖.

## 확정 결정 (브레인스토밍)

| 항목 | 결정 |
|---|---|
| 첫 소비자 | 3D 공간음향 게임 + 2D/UI 중심 게임 **양쪽 동시** — 2D 경로 무설정, 3D+오클루전 1급 |
| v1 스코프 | 코어(풀·fade·핸들·보이스 제한·채널 볼륨·쿨다운·variation) + 음악 + 오클루전 훅/raycast 기본 + Steam Audio 어댑터 + 타임스케일·스냅샷 헬퍼 |
| 진입점 | **인스턴스 서비스** `new SoundSystem(config)` (DI 친화, 정적 파사드 아님) |
| 클립 로딩 | 직접 AudioClip 참조 기본 + **Addressables 조건부 지원**(versionDefines) |
| 믹서 | **기본 AudioMixer 에셋 동봉**(Master/Music/SFX/VoiceOver + duck + Normal/Paused 스냅샷), 게임 믹서로 교체 가능 |
| 내부 구조 | 보이스 struct 배열 + PlayerLoopSystemHelper 단일 틱. MonoBehaviour 0개, coroutine 0개 |
| 비동기 | 모든 진입 경로에 **UniTask 쌍** (PlayAsync/WaitAsync/StopAsync/CrossfadeAsync) |

## 패키지 배치

```
unity/Packages/com.bun3.unity.audio/            v0.1.0, Unity 6000.3
  Runtime/Bun3.Unity.Audio.asmdef               core·UniTask 의존
                                                versionDefines: com.unity.addressables → BUN3_ADDRESSABLES
  Runtime/   SoundSystem.cs, SoundSystem.Tick.cs, SoundSystem.Music.cs (partial=역할),
             SoundHandle.cs, SoundDef.cs, MusicDef.cs, VoiceSlot.cs,
             IOcclusionProvider.cs, RaycastOcclusionProvider.cs
  Runtime/Assets/  기본 AudioMixer
  Editor/    SoundDef 인스펙터 프리뷰 버튼
  Tests/, Samples/

unity/Packages/com.bun3.unity.audio.steamaudio/ v0.1.0
  Runtime/   SteamAudioVoiceBinder 등 (audio + Steam Audio UPM 의존)
  Editor/    spatializer 프로젝트 설정 검증
```

네임스페이스 `Bun3.Unity.Audio` / `Bun3.Unity.Audio.SteamAudio`. 폴더=네임스페이스, 평평하게.

## 핸들 — 세대 검증 struct

```csharp
public readonly struct SoundHandle
{
    readonly SoundSystem _owner; readonly int _slot; readonly uint _generation;

    public bool IsValid { get; }
    public bool IsPlaying { get; }
    public void Stop(float fadeOut = 0f);
    public void SetVolume(float volume);   // 정의 볼륨에 곱하는 스케일
    public void SetPitch(float pitch);
    public void SetPosition(Vector3 position);
    public void Follow(Transform target);
    public UniTask WaitAsync(CancellationToken ct = default);
    public UniTask StopAsync(float fadeOut, CancellationToken ct = default);
}
```

- 슬롯 재사용 시 generation 증가 → 이전 핸들의 모든 호출은 **조용히 no-op**
  (사운드는 fire-and-forget이 기본, stale 제어는 에러가 아니다). 예외 없음.
- 완료 통지 기본은 `IsPlaying` 폴링/`WaitAsync`. 콜백은
  `SoundSystem.SetCompletionCallback(handle, Action<SoundHandle>)` — 델리게이트 캐시는
  호출자 책임(XML 문서에 명시).

## 사운드 정의 — ScriptableObject 참조가 곧 키 (런타임 문자열 0)

```csharp
public sealed class SoundDef : ScriptableObject
{
    AudioClip[] clips;              // 라운드로빈 셔플, 직전 클립 회피
    // BUN3_ADDRESSABLES: AssetReferenceT<AudioClip>[] 대체 경로
    MinMax volume, pitch;           // variation 범위
    bool loop;
    AudioMixerGroup mixerGroup;     // 미지정 시 SFX
    int maxInstances;               // 0=무제한, 초과 시 같은 def 최고참 스틸
    float cooldown;                 // 재트리거 최소 간격(초)
    SpatialMode spatial;            // None(2D) / Positional / Follow
    MinMax distance;                // 감쇠 거리
    OcclusionMode occlusion;        // Off / Raycast
}
```

- **MusicDef 별도 SO**: intro 클립(선택) + loop 클립 + 크로스페이드 기본값.
- Unity 6 Audio Random Container 통합은 스코프 아웃(clips+variation으로 동일 효과).

## SoundSystem 코어

```csharp
var sound = new SoundSystem(new SoundSystemConfig
{
    Mixer = null,          // null → 동봉 기본 믹서
    SfxVoices = 24, MusicVoices = 2,
    OcclusionProvider = null,   // null → RaycastOcclusionProvider
    PitchWithTimescale = false,
});
// IDisposable — 풀 GameObject·PlayerLoop 훅 해제
```

- 생성 시 `DontDestroyOnLoad` 루트 아래 AudioSource 전량 워밍(+`AudioLowPassFilter`
  비활성 부착). **재생 중 Instantiate 0, 증설 없음.**
- `PlayerLoopSystemHelper`(core)로 Update 페이즈 틱 등록.

보이스 슬롯(AoS — 수십 개 규모라 SoA 불요):

```csharp
struct VoiceSlot {
    uint generation; VoiceState state;   // Idle/Playing/FadingIn/FadingOut
    SoundDef def; float elapsed, clipLength;
    float fadeElapsed, fadeDuration, fadeFrom, fadeTo;
    float baseVolume, volumeScale;
    Transform follow;                    // null=고정 위치
    float occlusionCurrent, occlusionTarget;
    AutoResetUniTaskCompletionSource completion;  // WaitAsync용, UniTask 풀링
}
// cooldown 스탬프는 Dictionary<SoundDef, float> (기동 후 def 수만큼만 성장)
```

재생 경로 `Play(def, position)` — 전 경로 무할당:

1. cooldown 검사 — 걸리면 무효 핸들 반환
2. `maxInstances` 초과 시 같은 def 최고참 보이스 스틸
3. 빈 슬롯 선형 스캔, 없으면 전역 최고참 스틸
4. variation 롤(볼륨·피치) + 클립 라운드로빈 선택
5. generation++ 후 핸들 반환

틱(단일 루프, 활성 보이스만 순회): fade 보간 → **경과 시간 누적으로 완료 감지**
(`isPlaying` 폴링 금지 — 일시정지 오판) → `follow` 위치 갱신 → 오클루전 라운드로빈
(프레임당 N개, 설정값) → 타임스케일 변화 시 옵트인 보이스 pitch 재적용 →
완료 보이스 `completion.TrySetResult` + 풀 반환.

채널 볼륨: `SetChannelVolume(Channel, float)` → 믹서 `SetFloat`(선형→dB).
저장(PlayerPrefs 등)은 게임 몫 — get/set만 제공.

### UniTask 경로

```csharp
await sound.PlayAsync(def, position, ct);  // ct 취소 = 사운드 정지 + await 취소
await handle.WaitAsync(ct);                // 완료·스틸·Stop 전부 "완료"로 신호(예외 아님)
await handle.StopAsync(0.5f);              // fade-out 종료 시점 완료
await sound.CrossfadeAsync(musicDef, 2f);  // 전환 완료 시점
```

무효 핸들의 `WaitAsync`는 즉시 완료. `AutoResetUniTaskCompletionSource`로 per-await 할당 없음.

## 음악 서브시스템 (SoundSystem.Music.cs)

- 뮤직 보이스 2개 전용(A/B 스왑). SFX와 분리 이유: **DSP 클록 스케줄링**이 핵심.
- **인트로+루프**: intro `Play()` + loop 소스 `PlayScheduled(dspTime + intro.length)`,
  loop는 `loop=true`. 샘플 정확한 이음새. intro 없으면 바로 루프.
- **크로스페이드**: 현재 곡 fade-out + 새 곡 fade-in 병행. 크로스페이드 중 재호출 시
  fade-out 중인 보이스를 즉시 뺏어 최신 요청 우선.
- **일시정지/재개**: `AudioSource.Pause` — pause 시점에 loop 예약이 미도달이면
  재개 시 스케줄 재계산. 이 재계산이 최난이도 지점, 집중 테스트 대상.
- 스코프 아웃: 플레이리스트, 셔플, 마디 동기화 전환(= FMOD 후보 게임).

## 오클루전

```csharp
public interface IOcclusionProvider
{
    // 0 = 완전 개방, 1 = 완전 차폐
    float Evaluate(in Vector3 listenerPos, in Vector3 sourcePos);
}
```

- 기본 구현 `RaycastOcclusionProvider`: LayerMask + 단일 raycast 이진 판정.
- **적용은 코어 책임**: occlusion 값 → 볼륨 감쇠 + LowPassFilter cutoff 커브 보간.
  값 변화는 틱에서 스무딩(딸깍임 방지). `occlusionCurrent → occlusionTarget`.
- 비용 제어: occlusion 켠 def만 + 프레임당 N개 라운드로빈.

## Steam Audio 어댑터

Steam Audio(Apache 2.0, spatializer 플러그인)는 오클루전·HRTF를 자체 처리하므로
어댑터의 일은 계산이 아니라 **배선**:

1. 풀 워밍 시 각 소스에 `SteamAudioSource` 부착, `SoundDef.occlusion`을
   SteamAudioSource 플래그로 매핑.
2. 코어 raycast provider + LowPassFilter 경로 **비활성화**(이중 적용 방지).
3. Editor: 프로젝트 spatializer 설정 검증.

게임 코드는 `SoundDef.occlusion` 하나만 만지고, 어댑터 유무로 품질만 달라진다.
코어 API는 어댑터의 존재를 모른다.

## 부가 헬퍼

- 타임스케일 연동: `PitchWithTimescale` 옵트인, 틱에서 `Time.timeScale` 변화 감지 →
  보이스 pitch 곱(음악 기본 제외).
- 스냅샷: `TransitionTo(snapshot, seconds)` 래퍼 + 동봉 믹서 Normal/Paused
  (Paused = low-pass) 프리셋.

## 에러 처리

사운드는 게임을 못 죽인다:

- 재생 실패(클립 null·풀 고갈·cooldown) → **무효 핸들 반환** + 개발 빌드 한정 경고 로그.
- Addressables 로드 실패 → 무음 스킵 + 경고.
- 예외는 구성 오류에만: Config 검증 실패, Dispose 후 사용.

## 테스트 전략

- **EditMode 순수 로직이 주력**: 스틸 우선순위, generation 무효화, cooldown,
  fade 보간, 크로스페이드 상태 전이, pause 스케줄 재계산 — 틱에 deltaTime을 주입해
  AudioSource 없이 상태 기계만 검증(보이스 상태 조작과 AudioSource 반영을 partial로 분리).
- PlayMode는 스모크(생성→재생→Dispose 누수 0). DSP 이음새는 Samples 씬 귀 검증.
- **무할당 어설션**: Play/틱 경로 GC.Alloc 0 (diagnostics 패키지 전례 방식).

## 구현 슬라이스

1. 코어 — 풀·핸들·fade·보이스 제한·cooldown·variation·채널 볼륨·UniTask
2. 음악 — 인트로+루프·크로스페이드·pause 재계산
3. 오클루전 + 기본 믹서 에셋 + 헬퍼 2종
4. Steam Audio 어댑터 패키지
5. Addressables 조건부 경로
