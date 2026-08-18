# Bun3.Server.Achievements 설계 (서버 업적 프레임워크)

- 날짜: 2026-08-17 (v2: 2026-08-18 — 상태 머신·태그 라우팅·수령 차감 모델 사용자 합의 반영)
- 상태: 승인 (v2 방향은 사용자와 문답으로 확정)
- 범위: `server/src/Bun3.Server.Achievements` — 정의 카탈로그(id·태그 인터닝), 상태
  머신, 진행도/달성/수령, 저장 연계. 업적은 진행 확인·컨텐츠 진입 제한·기능 제한까지
  범용으로 쓰인다 — **인스턴스에 대한 모든 동작은 프레임워크가 구현하고, 게임은 저장
  훅과 이벤트 라우팅(Increase 호출)만 작성한다.**
- 참고: idlez-server / growninja Achievement 구조(실수요 파악용, 코드 미복사),
  `2026-08-06-server-players-design.md` (dirty/저장 계약)

## 1. 실수요 분석과 경계 (idlez / growninja 대조)

| 항목 | 소속 | 근거 |
|---|---|---|
| 진행 카운터·달성 판정·중복 방지·수령 카운트 | 프레임워크 | 두 게임이 공유하던 코어 — 산식까지 동일(누적 + 수령 시 차감) |
| 상태 머신(잠김/열림/진행/완료/수령) | 프레임워크 (v2) | 두 게임 모두 보유(READY/DISABLED/DOING/COMPLETED/GOT_REWARD ↔ Disabled/InProgress/Completed/Rewarded). 범용 게이트 용도라 매 게임 재구현 대상 |
| 태그 라우팅·그룹 순회 | 프레임워크 (v2) | 두 게임 모두 특정 업적 지정이 아니라 태그/조건으로 브로드캐스트(idlez Condition, growninja Tag+AchievementCondition) |
| 조건 항목 정의·판정 | **게임** | 조건 체계가 게임마다 완전히 다름(idlez enum 100+, growninja Tag×CompareType). 태그 인덱스로 라우팅하고 조건값 비교는 필터 델리게이트 |
| 보상 지급·우편·시즌 EXP | **게임** (훅) | Items 등 도메인 의존 금지. TryClaim true 이후의 세계 |
| 리셋 로테이션·선발 정책 | **게임** | 무엇을 언제 리셋/선발할지는 게임 몫, 되감기 프리미티브(Reset)만 프레임워크 |
| 정의 콘텐츠(이름·보상·조건값·기간) | **게임** (TDef 파생) | 게임마다 다름 |
| DB 스키마/저장 | **게임** (GetState/Restore) | Players와 동일 원칙 — 저장 불가지론이라 클라 호스트(로컬 파일/Steam Cloud)에서도 동작 |

## 2. 패키지 구조

```
server/src/Bun3.Server.Achievements/   netstandard2.1 · 의존성 0 (순수)
├── AchievementStatus.cs       수명주기 enum (Locked/Ready/Active/Completed/Claimed)
├── AchievementDefinition.cs   게임이 상속하는 정의 베이스 (Id, Target, Repeatable, Tags, InitialAvailability)
├── AchievementCatalog.cs      AchievementCatalog<TDef> — 기동 시 로드/검증, id·태그 인터닝, 불변
├── AchievementState.cs        업적 1개의 플레이어 상태 struct (저장 단위)
└── AchievementManager.cs      AchievementManager<TDef> — 플레이어당 1개, 전 인스턴스 동작 소유
```

- **의존성 0**: Players를 참조하지 않는다. dirty 연계는 `onDirty` 델리게이트로 게임이
  `player.MarkDirty`를 넘긴다. `TDef : AchievementDefinition` 제네릭은 Players의
  `TPlayer : Player` 패턴 반복. 순수 netstandard2.1이라 클라 호스트 프로세스에서도 동작.

## 3. 카탈로그 — id·태그 인터닝과 기동 시 검증

- 생성자에서 전량 검증 후 동결(불변). 실패는 즉시 예외 = 기동 실패.
  - 프레임워크 검증: null 정의, 빈/중복 Id(ordinal), `Target ≤ 0`, 정의 수 상한 65,536,
    InitialAvailability 범위(Locked/Ready/Active만), 빈 태그/같은 정의 내 중복 태그.
  - 게임 검증: `Action<TDef>? validator` — 도메인 불변식은 게임이 여기서 던진다.
- **인터닝** = 문자열 → 조밀한 int 인덱스 1회 변환. id와 태그 모두 대상이며, 런타임
  핫패스에 문자열이 등장하지 않는다. 기동 시 `GetIndex`/`GetTagIndex`로 캐시(오타는
  기동 실패로 표면화), 게임이 태그를 enum으로 관리하고 싶으면 `nameof`로 채운다 —
  프레임워크는 문자열→인덱스만 알고, enum은 게임의 표기법 선택이다(GameplayTags 무관).
- `GetIndicesByTag(tagIndex)` → 동결된 인덱스 배열(`ReadOnlySpan<int>`) — 라우팅 외에
  리셋 스윕·로테이션 선발 같은 그룹 순회도 같은 메커니즘으로 커버한다.

## 4. 상태 모델 — 저장은 가용성만, Completed/Claimed는 파생

`AchievementStatus`: `Locked(0) / Ready(1) / Active(2) / Completed(3) / Claimed(4)`
(숫자는 저장 포맷 — 불변).

- **저장 필드는 `AchievementState.Availability` 하나이고 Locked/Ready/Active만 담는다.**
  Completed/Claimed는 `GetStatus(i)`가 카운터에서 파생한다 — 상태·카운터 불일치 버그가
  원리적으로 없다(두 게임의 state/param 불일치 방어 코드 계열이 통째로 사라짐).

```
GetStatus: Availability != Active → 그대로
           CompletedCount > ClaimedCount → Completed        (수령 가능분 있음)
           비반복 && CompletedCount > 0  → Claimed          (종결)
           그 외 → Active                                    (반복은 수령 후 자동 복귀)
```

- 전이 API: `Unlock`(Locked→Ready) · `Activate`(→Active) · `Lock`(→Locked, 카운터 유지).
  변경 시에만 dirty, 반환값은 변경 여부. "완료/수령 시 자식 열림"은 게임이 OnCompleted나
  수령 핸들러에서 `Unlock`/`Activate` 호출로 조립(idlez OpenChildrenOnComplete,
  growninja childID 대응).
- 진행(`Increase`/`SetProgress`)은 **Active에서만** 쌓인다 — 비활성 업적에 라우팅이
  도달하면 조용히 no-op(0), 두 게임과 동일. 초기 가용성은 Def의 `InitialAvailability`
  (기본 Active).
- `TryClaim`은 가용성과 무관 — 로테이션 아웃된 업적의 보상 정산(growninja 우편) 지원.

## 5. 진행/달성/수령 — 누적 + 수령 시 차감 (두 게임과 동일 모델)

상태(struct, 카탈로그 인덱스 순 배열 1개):

```csharp
public struct AchievementState
{
    public long Progress;                 // 누적 진행. 반복: 수령 시 Target 차감. 비반복: Target 클램프
    public int  CompletedCount;           // 달성 횟수 — 단조 증가. 반복 불변식: = ClaimedCount + Progress/Target
    public int  ClaimedCount;             // 수령 횟수 (≤ CompletedCount)
    public long LastCompletedAtUtcTicks;  // 마지막 달성 시각 (0 = 미달성)
    public AchievementStatus Availability; // Locked/Ready/Active만 유효
}
```

API (핫패스 전부 인덱스 기반·무할당):

| 메서드 | 동작 |
|---|---|
| `int Increase(int index, long amount)` | 진행 증가(≥0, 0은 재평가만). 신규 달성 수 반환. Active 아니면 0 |
| `int IncreaseByTag(int tagIndex, long amount)` | 태그의 Active 업적 전부에 각각 적용(독립 카운터 — 공유 원장 없음), 합계 반환. **호출 시점 스냅샷**: 훅에서 Activate된 체인 티어는 이번 배치를 받지 않는다(idlez 스냅샷 순회와 동일) |
| `int IncreaseByTag<TArg>(tag, amount, arg, filter)` | 조건값 비교 게이트(growninja AchievementCondition 대응). static 람다 + TArg로 무할당 |
| `int SetProgress(int index, long value)` | 진행 설정 — 파생값 동기화(로그인 역산)·거대 재화 포화 변환용. 달성 수는 감소하지 않음(단조) |
| `bool TryClaim(int index)` | 수령 가능분 있으면 수령 +1, **반복은 Progress에서 Target 차감**. 일괄 수령은 `while (TryClaim(i))` |
| `int GetClaimableCount(int index)` | CompletedCount − ClaimedCount |
| `AchievementStatus GetStatus(int index)` | §4 파생 조회 |
| `ref readonly AchievementState GetState(int index)` | 상태 열람(복사 없음) — 저장 직렬화용 |
| `void Restore(int index, in state)` | 로드 복원 — 훅·dirty 없음. 불변식 위반(음수, 수령>달성, 비반복 다회, Availability에 파생 상태) 예외, 비반복 초과 진행도는 클램프(목표 하향 대응, 재판정은 `Increase(i,0)`). 달성 수가 모자라면 다음 Increase에서 몰아 발화(at-least-once) |
| `void Reset(int index)` | 카운터 전체 0(가용성은 유지 — 전이는 별도). 일간/주간 사이클 교체용. 미수령 정산은 게임이 Reset 전에 |

- 반복 달성 판정: `신규 달성 = Progress/Target − (CompletedCount − ClaimedCount)`
  (0 미만이면 0, int 상한 클램프) — 큰 점프는 몰아 발화. 수령 대기 중 진행도는 Target
  이상으로 유지되어 **UI "10/10 [보상받기]"가 성립**하고(표시는 `min(Progress, Target)`),
  수령 차감 후 남은 누적분이 다음 사이클 진행도가 된다. idlez `ClaimReward`
  (`progress -= count × target`)·growninja REPEAT와 동일 모델, 수령 대기 N건은 상위 호환.
- 달성 시 `LastCompletedAtUtcTicks` 기록(시각원 생성자 주입 가능, 기본 UtcNow).
- 훅 `OnCompleted(index, def, newCompletions)`: 상태 갱신 후 호출 — 안에서 타 업적
  Increase/Activate(체인·메타) 가능. 자동 보상 = 훅에서 TryClaim 후 게임이 지급.
- Reset이 프레임워크 몫인 이유: 달성 수는 단조라 `SetProgress(0)`으로는 재달성 불가 —
  카운터를 함께 되감는 유일한 지점. 일간/주간은 두 게임 공통 핵심 실수요.

### 티어 조립 패턴 (프레임워크 내장 아님 — 가용성 배치로 표현)

- **포함형** (루비 소모 1/10/100 — 이전 소모 포함): 티어 전부 처음부터 Active + 같은
  태그. 한 번에 100 소모 → 각 티어가 독립적으로 100씩 받아 전부 완료. 패치로 추가된
  티어는 로그인 시 평생 누계에서 `SetProgress` 역산.
- **신규누적형** (킬 1/10/100 — 티어마다 새로): 다음 티어 `InitialAvailability=Locked`,
  OnCompleted에서 `Activate(next)`. 스냅샷 의미론 덕에 같은 배치는 이월되지 않는다.

## 6. 플랫폼 업적(Steam 등)과의 관계 — 원본 → 프로젝션

**매니저가 항상 원본(source of truth), 플랫폼 업적은 단방향 미러다.** Steam 모델은
1회성 언락 + 진행도 스탯뿐이라(반복·클레임·리셋 없음) 왕복 변환이 안 되고, Steam
없는 타깃에서도 프레임워크는 동작해야 한다. 코어는 플랫폼을 모른다 — 접합점은
기존 seam 둘로 충분하다:

- **언락 미러**: `OnCompleted` 훅에서 토폴로지별 Steam 쓰기 경로 호출 —
  클라 호스트는 클라 API(`SetAchievement`, 오프라인 캐시 공짜), 데디 서버는
  `ISteamGameServerStats`(업적을 "Set By: GS"로 잠가 클라 위조 차단), 백엔드는
  Web API `SetUserStatsForGame`(퍼블리셔 키). 재발화는 무해(재언락 no-op).
- **진행도 스탯 미러**: 저장 경로(`OnSaveAsync`의 `GetState` 순회)에 피기백.

미러 대상 선택(Steam 초기 100개 제한, 반복/일간 업적은 표현 불가)은 게임이 TDef
파생 필드로 고른다. 어댑터 패키지(백엔드 Web API는 `server/`, 클라 API는
`unity/` — Auth.Steam 패턴)는 실수요 시점에 별도 신설, 지금은 비범위.

## 7. 무할당 규율

- 핫패스(`Increase`/`IncreaseByTag`/`SetProgress`/`TryClaim`/전이): 배열 인덱싱 + 정수
  연산 + 캐시된 델리게이트 호출뿐. 상태는 `AchievementState[]` 1개(생성 시 1회 할당).
  IncreaseByTag 스냅샷은 stackalloc 비트마스크(무할당·재진입 안전). 필터는 static
  람다 + `TArg` 제네릭으로 클로저/박싱 없음.
- 문자열 → 인덱스 변환은 기동 시 1회(§3). 예외 메시지 등 문자열은 오류 경로에만.

## 8. 진행도 타입 — long (BigNum/BigInteger/확장 정수 기각)

무한대급 범위·정확한 정수 산술·무할당 고정 크기는 셋 중 둘만 가능하다:

- **BigNum**(Bun3.Gameplay.Numerics): 19자리 초과 근사 → `progress -= target` 차감이
  target ≪ progress일 때 소실되어 수령 산식·중복 방지가 조용히 붕괴. 원장 부적합.
  Gameplay 패키지 결합 문제도 별개로 존재.
- **BigInteger**: 정확·무한이나 int 초과 값은 연산마다 힙 배열 할당 — 핫패스 탈락.
- **Int128/Int256 수제**: 정확·무할당이나 여전히 유한(e38/e76) — 천장을 옮길 뿐이고
  방치형 재화 상한(BigNum 지수 10^1e8)을 못 덮는다. 저장 스키마 비용은 전 게임 영구 부과.

**해법은 타입이 아니라 역할 분리**: 업적 카운터가 정확해야 하는 구간은 target 근방뿐.

- long 범위 재화(루비 등 유료/희소): 업적 진행도가 곧 원장 — `Increase` 직접.
- BigNum 범위 재화(골드 등 인플레): 원장은 게임 필드(재화와 같은 BigNum, UI 표시용으로
  어차피 보유), 업적은 포화 동기화 `SetProgress(i, (long)min(누계, target))` — 비반복은
  target 클램프라 포화 변환에 정보 손실 0. target 자체가 초과하면 TDef 단위(scale)
  필드로 단위 수 라우팅.
- 실증(idlez 출시작 업적 진행도 = int32)과 일치. 탈출구: "target > 9.2e18인 반복
  업적" 실수요가 실증되면 그때 Int128 확장(저장 포맷 변경 수반이라 선제 도입은 손해).

## 9. 검증 (완료 조건)

| 종류 | 대상 |
|---|---|
| 단위 | 카탈로그 — 중복/빈 id, Target ≤ 0, 상한, validator 전파, 가용성 범위, 태그 인터닝/그룹 조회/빈·중복 태그 |
| 단위 | 비반복 — 도달 1회·클램프·중복 방지·LastCompletedAt, 수령 후 Claimed 종결 |
| 단위 | 반복 — 다회·큰 점프 몰아 발화, 수령 차감·10/10 UI 시나리오·차감 후 불변식 유지, 오버플로 클램프 |
| 단위 | SetProgress — 상향 달성/하향 단조, 비반복 포화 클램프 |
| 단위 | 가용성 — 초기값 반영, Locked/Ready 진행 무시(no dirty), Unlock/Activate/Lock 전이·dirty, Locked 수령 정산 |
| 단위 | 태그 — Active만 각각 적용, 필터 게이트, 포함형 티어 일괄, 신규누적형 체인 스냅샷(배치 미이월) |
| 단위 | 수령 — 횟수 제한, 일괄 수령 패턴 |
| 단위 | Reset — 카운터 0·가용성 유지·재달성, 변경 시에만 dirty·훅 없음 |
| 단위 | Restore — 훅·dirty 미발화, 불변식/파생 상태 저장 예외, 목표 하향 클램프+재판정 |
| 단위 | dirty — 실제 변경 시에만 |
| 단위 | OnCompleted — 체인 Increase, (index, def, count) 전달 |

## 10. 비범위 (예약)

- 조건 항목 정의·판정(게임/향후 어댑터), 보상 지급(게임/Items), 리셋 로테이션·선발
  정책(프레임워크는 Reset/Lock/Activate 프리미티브만), 2축 진행(growninja
  completeParam1+2 — 실데이터 대부분 1축, 태그 병렬 업적 + 필터 게이트로 커버),
  업적별 게임 페이로드 슬롯(굴린 보상 id 등 — 게임 병렬 배열로), 호스팅 DI 통합,
  기간 한정(StartAt/UntilAt — 게임 TDef + 라우팅/전이에서 거름), Steam 어댑터
  패키지(§6 — seam만 확보), 진행도 확장 정수(§8 탈출구 조건 명시).
