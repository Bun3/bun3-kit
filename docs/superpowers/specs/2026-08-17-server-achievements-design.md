# Bun3.Server.Achievements 설계 (서버 업적 프레임워크)

- 날짜: 2026-08-17
- 상태: 승인 대기 (합리적 기본값으로 진행 — 결정 사항은 워크트리 코멘트로 공유)
- 범위: `server/src/Bun3.Server.Achievements` 신설 — 정의 카탈로그 계약, 진행도
  추적, 달성/보상 클레임 상태, Players 저장 패턴 연계
- 참고: idlez-server Achievement 구조(실수요 파악용, 코드 미복사),
  `2026-08-06-server-players-design.md` (dirty/저장 계약)

## 1. 실수요 분석 (idlez 기준)

idlez의 업적 모델에서 실제로 쓰인 것: 정의(id, 목표치, 반복 가능), 플레이어 상태
(진행도, InProgress/Completed/Rewarded), 진행도 증가 시 목표 도달 판정, 반복
업적의 다회 달성(진행도/목표 몫), 달성↔보상 수령 분리, 부모/자식 체인(티어).

프레임워크/게임 경계:

| 항목 | 소속 | 근거 |
|---|---|---|
| 조건(Condition) 분류·판정 | **게임** | idlez의 Condition enum은 100+ 항목의 순수 도메인. 브리프 제약: 태그/스탯 직접 참조 금지 — 게임이 자기 이벤트를 업적 인덱스로 라우팅한다 |
| 진행도 카운터·목표 판정·중복 달성 방지 | 프레임워크 | 두 번째 게임에서도 똑같이 짠다 |
| 달성/클레임 카운트 추적 | 프레임워크 | 동상 — 중복 수령 방지는 매 게임 재구현 대상 |
| 보상 지급 | **게임** (훅) | Items 등 도메인 의존 금지(병렬 작업 중) |
| 티어(다단계) | **게임** (체인) | idlez도 parent/child + 완료 시 개방을 게임 로직으로 구성. 프레임워크 primitive는 Repeatable + OnCompleted 훅으로 충분 — YAGNI |
| 정의 콘텐츠(이름·보상·기간·조건값) | **게임** (TDef 파생) | 게임마다 다름 |
| DB 스키마/저장 | **게임** (상태 열람/복원 API 제공) | Players와 동일 원칙 |

## 2. 패키지 구조

```
server/src/Bun3.Server.Achievements/   netstandard2.1 · 의존성 0 (순수)
├── AchievementDefinition.cs   게임이 상속하는 정의 베이스 (Id, Target, Repeatable)
├── AchievementCatalog.cs      AchievementCatalog<TDef> — 기동 시 로드/검증, 불변
├── AchievementState.cs        업적 1개의 플레이어 상태 struct
└── AchievementTracker.cs      AchievementTracker<TDef> — 플레이어당 1개, 진행/달성/클레임
```

- **의존성 0**: Players를 참조하지 않는다. dirty 연계는 `onDirty` 델리게이트로
  게임이 `player.MarkDirty`를 넘긴다(§5). 제네릭 `TDef : AchievementDefinition`은
  Players의 `TPlayer : Player` 패턴 반복 — 게임 훅이 캐스팅 없이 파생 정의를 받는다.

## 3. 카탈로그 — id 인터닝과 기동 시 검증

```csharp
var catalog = new AchievementCatalog<MyAchievementDef>(defs, validator: ValidateRewards);
int killIdx = catalog.GetIndex("kill_monsters");   // 기동 시 1회 — 이후 int 인덱스만 사용
```

- 생성자에서 전량 검증 후 동결(불변). 실패는 즉시 예외 = 기동 실패.
  - 프레임워크 검증: null 정의, 빈/중복 Id(ordinal), `Target <= 0`, 정의 수 상한
    65,536 초과.
  - 게임 검증: `Action<TDef>? validator` — 정의별 호출, 도메인 불변식은 게임 몫.
- **id 인터닝** = 문자열 id → 조밀한 int 인덱스(0..Count-1) 1회 변환. 런타임
  식별자는 인덱스이며 핫패스에 문자열이 등장하지 않는다(레포 규율).
  `GetIndex(string)`(없으면 예외)·`TryGetIndex(string, out int)`·`GetDefinition(int)`·`Count`.
- 로드 델리게이트를 별도 타입으로 파지 않는다 — "기동 시 게임 로더가 정의 목록을
  만들어 생성자에 넘긴다"가 계약 전부다(호스팅 통합은 실수요 발생 시).

## 4. 진행도/달성/클레임 — AchievementTracker<TDef>

플레이어당 1개, 게임 Player 파생 클래스가 소유. 세션 액터 안에서만 접근하는
전제(플레이어 상태와 동일)로 락 없음.

상태(struct, 카탈로그 인덱스 순 배열):

```csharp
public struct AchievementState
{
    public long Progress;                // 누적 진행도 (Repeatable은 무한 누적)
    public int  CompletedCount;          // 달성 횟수 — 단조 증가, 감소 없음
    public int  ClaimedCount;            // 보상 수령 횟수 (≤ CompletedCount)
    public long LastCompletedAtUtcTicks; // 마지막 달성 시각 (0 = 미달성)
}
```

API (핫패스는 전부 인덱스 기반·무할당):

| 메서드 | 동작 |
|---|---|
| `int Add(int index, long amount)` | 진행도 증가(amount ≥ 0, 0이면 재평가만). 신규 달성 수 반환 |
| `int Set(int index, long value)` | 진행도 설정(value ≥ 0). 달성 수는 감소하지 않는다(중복 달성 방지의 단조 규칙) |
| `bool TryClaim(int index)` | `ClaimedCount < CompletedCount`면 +1 후 true. 지급은 게임이 true 반환 후 수행 |
| `int GetClaimableCount(int index)` | 수령 가능 횟수 |
| `ref readonly AchievementState GetState(int index)` | 상태 열람(복사 없음) — 저장 직렬화용 |
| `void Restore(int index, in AchievementState state)` | 로드 복원 — 훅·dirty 없음. 불변식 위반(음수, Claimed>Completed)은 예외, 비반복 초과 진행도는 Target으로 클램프(밸런스 패치로 목표 하향 대응) |

달성 판정(단조 — 같은 달성이 두 번 발화하지 않음):

- 비반복: `Progress = min(Target, Progress + amount)`; `Progress == Target`이고
  `CompletedCount == 0`일 때만 달성 1회.
- 반복: `Progress += amount`(long.MaxValue 클램프); 신규 달성 수 =
  `Progress / Target - CompletedCount`(0 미만이면 0, int.MaxValue 클램프) —
  큰 점프로 여러 번 교차하면 한 번에 몰아 발화(idlez clearCount와 동일 산식).
- 달성 시 `LastCompletedAtUtcTicks` 기록(시각원은 생성자 주입 가능, 기본 UtcNow).

훅:

```csharp
tracker.OnCompleted = (index, def, newCompletions) => { /* 알림·체인·자동지급 등 게임 몫 */ };
```

- 상태 갱신 완료 **후** 호출 — 훅 안에서 다른 업적에 Add(체인/티어) 가능.
- 보상 훅은 따로 두지 않는다: 자동 지급은 OnCompleted에서 `TryClaim` 후 지급,
  수동 지급은 클라 요청 핸들러에서 `TryClaim` — 프레임워크는 수령 횟수만 지킨다.

## 5. 저장 연계 — Players dirty 계약

```csharp
public sealed class MyPlayer : Player
{
    public readonly AchievementTracker<MyAchievementDef> Achievements;
    public MyPlayer(AchievementCatalog<MyAchievementDef> catalog)
        => Achievements = new AchievementTracker<MyAchievementDef>(catalog, MarkDirty);
}
```

- 상태 변경(Add/Set의 실제 변경, TryClaim 성공) 시 `onDirty` 1회 호출 → Players의
  버전 카운터가 저장 스윕을 잡는다. 변경 없으면 호출 없음. `Restore`는 호출 없음.
- 저장 자체는 게임 몫: `OnSaveAsync`에서 `GetState(i)` 순회 직렬화, 로더에서
  `Restore`. 프레임워크는 DB를 모른다(Players와 동일 원칙).

## 6. 무할당 규율

- 핫패스(`Add`/`Set`/`TryClaim`): 배열 인덱싱 + 정수 연산 + 캐시된 델리게이트
  호출뿐 — 클로저·LINQ·문자열·박싱 없음. 상태는 `AchievementState[]` 1개(생성 시
  1회 할당).
- 문자열 → 인덱스 변환은 기동 시 1회(§3). 예외 메시지 등 문자열은 오류 경로에만.

## 7. 검증 (완료 조건)

| 종류 | 대상 |
|---|---|
| 단위 | 카탈로그 — 중복/빈 id, Target ≤ 0, 상한 초과, 게임 validator 전파, GetIndex/TryGetIndex |
| 단위 | 비반복 — 도달 시 1회 달성, 초과 Add 클램프·재달성 없음(중복 방지), LastCompletedAt 기록 |
| 단위 | 반복 — 다회 달성, 큰 점프 몰아 발화, 진행도 누적 유지 |
| 단위 | Set — 상향 달성, 하향 시 달성 수 유지(단조) |
| 단위 | 클레임 — Completed 전 false, 횟수만큼 true 후 false, 반복 업적 다회 클레임 |
| 단위 | Restore — 훅·dirty 미발화, 불변식 위반 예외, 목표 하향 클램프 |
| 단위 | dirty — 실제 변경 시에만 onDirty, Add(0)·변경 없는 Set은 미호출 |
| 단위 | OnCompleted 훅 안에서 타 업적 Add(체인) 동작 |
| 단위 | 오버플로 — long.MaxValue 근처 Add 클램프 |

## 8. 비범위 (예약)

- 티어 내장 모델(체인 훅으로 충분 — 실수요 재발 시 재검토), 조건 판정(게임/향후
  Gameplay 어댑터), 보상 지급(게임/Items), 호스팅 DI 통합(AddAchievements — 카탈로그
  생성자 호출로 충분), 기간 한정(StartAt/UntilAt — 게임 TDef + 라우팅에서 거름),
  일간/주간 리셋(게임 몫 — 필요 시 `Set(i, 0)` + 게임 상태로 구성), BigNum 진행도
  (long 상한 9.2e18로 방치형 카운터 충분 — 부족 실증 시 재검토).
