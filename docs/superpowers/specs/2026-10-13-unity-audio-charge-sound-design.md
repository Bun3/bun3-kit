# Unity Audio — 차지 사운드 설계

- 상태: 구현 완료 (0.5.0)
- 작성일: 2026-10-13
- 적용 패키지: `com.bun3.unity.audio` (0.4.0 → 0.5.0)

## 1. 목적과 범위

홀드·차징 인터랙션(1~5초 누르기, 충전 공격, 장전, 제작 게이지)에서 "차오르는 소리"를 SFX처럼
`Play~` 한 줄로 낸다. 게임은 진행도(0~1)만 넘기고, 볼륨·피치 램프와 시작·완료·취소 효과음은
에셋 설정으로 정한다.

합격선: 홀드 사운드 한 세트가 `ChargeSoundDef` 에셋 1개 + 게임 코드 세 줄
(`PlayCharge` / `SetProgress` / `Complete`·`Cancel`)로 동작한다.

**비목표**: §8 참조.

## 2. 결정 요약

| 주제 | 결정 |
|---|---|
| 진행도 구동 | **게임이 주입** — `SetProgress(t)`를 매 프레임 호출. 사운드가 시간으로 자체 진행하지 않는다 |
| 매핑 저작 | **AnimationCurve 2개** — `VolumeByProgress`, `PitchByProgress` (X = 진행도 0~1, Y = 배율) |
| 구조 | **얇은 합성 계층** — 기존 재생 경로(`PlayCore`)와 `SetVolume`/`SetPitch`/`Stop` 위에 얹는다. 코어 Tick·VoiceTable·`PlayCore` 시그니처 무변경 |
| 구성 요소 | `SoundDef` 참조 4칸(Loop 필수, Start·Complete·Cancel 선택). 클립 변주·믹서 그룹·공간 모드·공유 프로필을 각자 그대로 쓴다 |
| 보간 | 없음 — 들어온 진행도를 그 프레임에 바로 반영 |
| 스틸된 루프 | 차지 소멸 — 이후 호출은 전부 무시, 완료음도 없음 |
| 이름 | Charge (Hold보다 범용: 충전 공격·장전·제작) |

기각한 대안:

- **VoiceSlot/Tick 통합(보간 포함)** — 검증된 핫패스를 건드리는 비용 대비 이득이 없다. 보간이
  필요한 게임(서버가 진행도를 보내는 경우)이 생기면 이 계층 위에 얹는다.
- **게임 소유 `ChargeSound` 플레이어 객체** — `Play~` 형태가 아니다.

## 3. 구성 요소

| 파일 | 내용 |
|---|---|
| `Runtime/ChargeSoundDef.cs` | 에셋. `[CreateAssetMenu(menuName = "Bun3/Audio/Charge Sound Def", fileName = "ChargeSoundDef")]` |
| `Runtime/ChargeSoundHandle.cs` | 차지 핸들 readonly struct |
| `Runtime/SoundSystem.Charge.cs` | `SoundSystem` partial — `PlayCharge` 오버로드와 핸들이 위임하는 내부 구현. 머리에 역할 주석 한 줄 |

네임스페이스는 전부 `Bun3.Unity.Audio`(폴더 평평 유지).

## 4. 공개 API

```csharp
public sealed class ChargeSoundDef : ScriptableObject
{
    public SoundDef Loop;       // 필수 · Loop가 켜진 SoundDef
    public SoundDef Start;      // 선택 · 시작 순간 원샷
    public SoundDef Complete;   // 선택 · 완료음
    public SoundDef Cancel;     // 선택 · 취소음

    public AnimationCurve VolumeByProgress = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    public AnimationCurve PitchByProgress  = AnimationCurve.Linear(0f, 0.667f, 1f, 1f);
    public float CompleteFadeOut = 0.05f;   // 완료 시 루프 페이드아웃(초)
    public float CancelFadeOut   = 0.15f;   // 취소 시 루프 페이드아웃(초)
}

// SoundSystem — SFX Play와 같은 배치 오버로드 3종
public ChargeSoundHandle PlayCharge(ChargeSoundDef def);
public ChargeSoundHandle PlayCharge(ChargeSoundDef def, Vector3 position);
public ChargeSoundHandle PlayCharge(ChargeSoundDef def, Transform follow);

public readonly struct ChargeSoundHandle
{
    public static ChargeSoundHandle Invalid { get; }   // default
    public bool IsValid { get; }                       // 루프가 살아 있고 완료·취소 전
    public void SetProgress(float progress);           // 0..1
    public SoundHandle Complete();                     // 루프 정지 + 완료음, 완료음 핸들 반환
    public SoundHandle Cancel();                       // 루프 정지 + 취소음, 취소음 핸들 반환
}
```

핸들 내부 상태는 루프 `SoundHandle`, `ChargeSoundDef`, 롤된 기본 피치 3개다. `SoundSystem`은
루프 핸들의 Owner로 찾는다.

게임 측 사용:

```csharp
_charge = sound.PlayCharge(holdCharge, transform);
_charge.SetProgress(hold.Progress);                 // 매 프레임, 게이지 UI와 같은 값
if (hold.Completed) _charge.Complete(); else _charge.Cancel();
```

## 5. 동작 규칙

### 5.1 곡선 평가

- 곡선이 `null`이거나 키가 0개면 상수 1(해당 축 효과 없음).
- 진행도 정규화: NaN → 0, 그 외 `Mathf.Clamp01`.
- 볼륨 배율 = `Max(0, VolumeByProgress(t))`, 피치 = `Max(0, 기본 피치 × PitchByProgress(t))`.
- 진행도는 감소해도 된다(손을 떼면 게이지가 줄어드는 게임).

### 5.2 시작 — `PlayCharge`

1. `def` 또는 `def.Loop`가 `null`이면 개발 빌드·에디터 경고 후 `ChargeSoundHandle.Invalid`.
   Dispose된 시스템은 `Play`와 같이 `ObjectDisposedException`.
2. `def.Loop.EffectiveLoop`가 false면 개발 빌드·에디터 경고(재생은 진행). 루프가 아니면 클립이
   끝나는 순간 핸들이 소멸해 완료음이 나지 않는다.
3. Loop를 `VolumeByProgress(0)` 볼륨 배율로 재생한다(기존 `PlayCore`의 `volumeScale` 인자 —
   재생 전 적용이라 첫 오디오 블록부터 0 지점 음량). 실패(클립 없음·쿨다운·출력 거부)하면
   `Invalid`를 반환하고 시작음도 내지 않는다.
4. 슬롯의 롤된 피치를 기본 피치로 저장하고, 같은 호출 안에서 `기본 피치 × PitchByProgress(0)`을
   기존 `SetSourcePitch` 경로로 적용한다.
5. Start가 있으면 같은 위치·추적 대상으로 재생한다. 루프가 먼저 할당되므로 Start가 루프를
   스틸하는 경우는 풀 포화 극단(`SfxVoices = 1` 등)뿐이고, 그때는 §5.4 스틸 규칙을 따른다.
6. 차지 핸들을 반환한다.

### 5.3 진행도 — `SetProgress`

- 무효 핸들이면 무시.
- 루프 `VolumeScale` ← 볼륨 배율. 롤된 기본 볼륨에 곱해지므로 루프 에셋은 "완료 직전" 음량으로
  맞춘다. 페이드 계수와도 곱으로 합성된다.
- 루프 피치 ← `기본 피치 × PitchByProgress(t)`. 덮어쓰지 않고 매번 기본 피치에서 계산하므로
  루프 def의 피치 변주가 유지된다. `PitchWithTimescale`·출력 소유자(Steam Audio) 처리는 기존
  `SetPitch` 경로 그대로다. 루프는 수명이 끝나지 않으므로 피치에 따른 수명 진행 변화는 무관하다.

### 5.4 완료·취소 — `Complete` / `Cancel`

- 무효 핸들이면 아무 것도 하지 않고 `SoundHandle.Invalid`.
- 유효하면:
  1. 루프의 현재 배치를 읽는다 — 추적 대상이 살아 있으면 그 Transform, 아니면 소스의 마지막 위치.
  2. 루프를 `CompleteFadeOut`(취소면 `CancelFadeOut`)으로 정지한다. 0 이하는 다음 틱 즉시 정지.
  3. Complete(취소면 Cancel) def가 있으면 그 배치로 재생해 핸들을 반환하고, 없으면
     `SoundHandle.Invalid`. 2D·3D는 각 def의 `Spatial`이 정한다.
- 첫 호출 뒤 루프는 FadingOut이므로 `IsValid`가 false가 된다. `Complete` 2회, `Complete` 후
  `Cancel`에서 효과음은 한 번만 난다. 기존 `Stop`은 페이드 중 재호출 시 페이드를 재시작하므로
  이 가드가 필요하다.

### 5.5 유효성과 무효 처리

- `IsValid` = 루프 핸들 유효(`TryGetSlot` — Dispose·세대 검사 포함) **그리고** 슬롯 상태가
  FadingOut이 아님.
- 루프가 스틸·자연 종료·Dispose되면 `SetProgress`/`Complete`/`Cancel`은 조용히 무시된다.
  `SoundHandle`의 stale 규칙과 같다. 루프를 잃은 차지에는 완료음도 없다.
- 같은 차지 연타: 루프 def가 `MaxInstances = 1`이면 새 `PlayCharge`가 페이드아웃 중인 이전
  루프를 스틸하고, 이전 핸들은 stale이 된다.
- 권장 루프 def 설정: `Loop` 켬, `Pitch` 1–1, `Cooldown = 0`, `MaxInstances` = 동시에 진행될 수
  있는 차지 수. 플레이어 1명이 소유하는 차지면 1(연타 시 이전 루프 교체). 여러 적이 같은 def를
  공유하면 동시 차지 수만큼 둔다 — 부족하면 새 차지가 기존 차지의 루프를 스틸해 그 차지는
  완료음 없이 끝난다.

### 5.6 핫패스

- `ChargeSoundHandle`은 readonly struct, `SetProgress`는 곡선 평가 2회 + 슬롯 조회 — 무할당.
- 경고 로그는 오설정(def/Loop null, Loop 꺼짐) 경로에서만 난다.

## 6. 테스트

`Tests/Runtime/ChargeSoundTests.cs` (PlayMode). 클립은 `AudioClip.Create` 절차 생성,
검증은 `SourceForTest`·`SourcePitchForTest`·`Table.Slots`.

| # | 검증 |
|---|---|
| 1 | `SetProgress(t)` → 루프 `VolumeScale = VolumeByProgress(t)`, 소스 피치 = 롤된 피치 × `PitchByProgress(t)` (루프 def 피치 1.2–1.2로 곱셈 확인) |
| 2 | NaN → 0, 범위 밖 → 0~1 고정. 빈 곡선·`null` 곡선 → 상수 1 |
| 3 | `PlayCharge` → 루프·시작음 재생, 반환 직후 루프 `VolumeScale`과 소스 피치가 곡선 0 지점 값 |
| 4 | 루프 실패(클립 없음, 쿨다운) → `Invalid`, 시작음 보이스 없음. `def`/`def.Loop` null → 경고 + `Invalid` |
| 5 | `Complete` → 루프 FadingOut, 완료음이 루프 위치(Positional)에서 재생 / 같은 Transform 추적(Follow), 반환 핸들 유효, 차지 `IsValid` false |
| 6 | `Complete` ×2, `Complete`→`Cancel` → 효과음 보이스 1개 |
| 7 | Cancel def 없음 → 루프만 정지, `SoundHandle.Invalid` 반환 |
| 8 | `MaxInstances = 1`에서 재`PlayCharge` → 이전 핸들 전부 무시, 새 핸들 정상 |
| 9 | `SoundSystem` Dispose 후 → 전부 무시, 예외 없음 |
| 10 | Loop def `Loop` 꺼짐 → 경고 1회(`LogAssert.Expect`), 재생은 진행 |
| 11 | 준비된 def로 `PlayCharge`→`SetProgress`→`Complete` 한 사이클 `Is.Not.AllocatingGCMemory()` (기존 `AllocationTests` 패턴) |

실행: `unity test "E:\Projects\bun3-kit\unity" --filter "Bun3.Unity.Audio.Tests"` — 패키지 전체로
기존 테스트 회귀를 함께 확인한다. 통과여도 total="0"이면 필터 미매칭이다.

## 7. 문서와 버전

- **README** `### Charge sounds` (Usage 아래, Music 앞):
  - 사용 예(§4 게임 측 코드).
  - 루프 에셋 저작: 진행감을 굽지 않은 1~2초 심리스 루프, "완료 직전" 상태를 피치 1.0으로
    저작. 텍스처에 두드러진 이벤트가 있으면 2~3초.
  - 피치 곡선 시작값 예: 5도 0.667, 옥타브 0.5, 단3도 0.84.
  - 완료음은 즉시 어택·같은 키, 시작음 0.1~0.3초, 완료음 0.3~1초.
  - 권장 def 설정(§5.5) + WAV 소스·Decompress On Load.
  - 제약: 루프가 스틸되면 완료음이 나지 않는다.
- **API 레퍼런스 표**: `PlayCharge`, `ChargeSoundDef`, `ChargeSoundHandle` 행 추가.
- **XML 문서**: 모든 public 멤버, 영어.
- **package.json**: 0.4.0 → **0.5.0** (public API 추가).
- **CHANGELOG**: 현재 맨 위가 0.3.0이고 0.3.1~0.4.0이 빠져 있다. git 이력 기준으로
  `[0.4.0]` 따라잡기 항목 1개(사운드 카탈로그·공유 프로필·외부 보이스 출력, 감쇠 반영 수정,
  공통 음향 설정·인스펙터, **레거시 Acoustics API 제거 — breaking**)를 넣고 그 위에 `[0.5.0]`
  Added 항목을 쓴다. 0.3.x 문서 버전들은 0.4.0 항목에 묶는다.
- 의존 패키지(dissonance 등)의 audio 하한 핀은 손대지 않는다.

## 8. 비목표·백로그

- **보이스 우선순위·스틸 불가 플래그** — 루프 보호와 BGM 레이어링 둘 다에 필요. 별도 스펙.
- **진행도 보간** — 서버 구동 진행도 게임이 생기면 이 계층 위에 추가.
- **루프 핸들 노출**(차지 도중 위치·추적 대상 변경) — 필요해지면.
- **재생 전 피치 적용** — §5.2-4는 `PlayCore` 직후 같은 호출에서 피치를 적용한다. 0 지점 볼륨이
  0이 아닌 곡선에서 첫 블록 피치 차이가 들리면 `PlayCore`에 피치 배율 인자를 추가한다.
- **`SoundCatalog` 등록**(문자열 키로 차지 재생) — 카탈로그 사용 게임에서 요구가 생기면.
- **샘플·에디터 프리뷰** — `Samples~/AudioDemo` 차지 데모, `ChargeSoundDef` 인스펙터 프리뷰.
- **태그·퍼블리시** — `com.bun3.unity.audio/v0.5.0` 태그는 구현 완료 후 별도 확인.
