# Bun3.Server.Items v0.4 설계 (레거시 잔여 흡수 — 보상 테이블·클램프 지급·만료·적용 통지)

- 날짜: 2026-08-18
- 상태: "최대한 프레임워크 제공" 방침에 따른 레거시 재감사 결과 반영
- 선행 문서: v0.1~v0.3 설계 + idlez/growninja 조사 보고(2026-08-17)

## 1. 재감사 결과 — 흡수 대상 5건

| # | 레거시 실물 | 프레임워크 흡수 |
|---|---|---|
| A | idlez `AddItemGroup`/`PickerGroup`(가중 추첨+prob+min/max 롤), growninja `AddItem` 40필드("게임 달라도 80% 동일") | **RewardTable** — 그룹(발동 확률·전체/1건 추첨) → 항목(가중치·수량 롤). RNG 시섬으로 결정론 지원(idlez의 crypto/IRng 이중 경로 교훈) |
| B | idlez maxCount 초과분 버림/param1 이월, growninja 우편 폴백 — 상한 도달은 실패가 아니라 **부분 지급** | **TryAddUpTo** — 가능한 만큼 지급하고 실제 지급량 반환(잔여는 게임이 우편 등으로) |
| C | 양쪽 다 `until_at` 승격 컬럼 + 조회 필터 + 만료 스윕 + 누적 연장 | **ExpiresAtTicksUtc 프레임워크 필드** + `CollectExpired` — 만료 처리(소모/후속 보상)는 게임 몫, 자동 삭제 안 함(idlez: 만료 ≠ 삭제) |
| D | idlez `GetItemsByDataId` 등 6개 조회 메서드 복붙 | **CollectInstances(ItemId, buffer)** — 정의별 인스턴스 수집 |
| E | idlez `ItemAddedEvent`/`ItemConsumeEvent` → 업적/랭킹/로그 3구독자가 switch 복붙 | **onApplied 통지** — 커밋당 1회, 적용된 순 델타 span 전달(무할당). 업적/퀘스트 카운팅의 원천 |

## 2. 흡수하지 않는 것 (사유 기록)

- **슬롯/정렬/스왑**: growninja 전용(4탭 슬롯맵) — 슬롯 의미론이 게임마다 다름. 게임 몫 유지.
- **우편함/지급 큐**: 반복 패턴 맞으나 별도 도메인(저장소 필요) — 미래 `Bun3.Server.Mail` 후보.
- **리소스 파일 로더 파이프라인**: 호스팅 통합 패키지 후보(GameplayTags 선례).
- **리젠 정산 공식**: 아이템 전용이 아님(스태미나=플레이어 상태) — Players/공용 유틸 후보.
- **천장(pity)·UseItem 카테고리 디스패치·Redis 무효화**: 게임 BM/게임 로직/인프라.
- **initialCreate**: 카탈로그 색인 + 5줄이면 게임에서 끝 — 프레임워크 API 값어치 없음.

## 3. A — RewardTable

```csharp
var table = new RewardTable(new[]
{
    new RewardGroup(probabilityPermyriad: 10000, grantAll: true,        // 확정 그룹
        new RewardEntry(gold, weight: 1, minAmount: 100, maxAmount: 200)),
    new RewardGroup(probabilityPermyriad: 2500, grantAll: false,        // 25% 발동, 가중 1건
        new RewardEntry(sword, weight: 1, minAmount: 1, maxAmount: 1),
        new RewardEntry(gem,   weight: 9, minAmount: 3, maxAmount: 5)),
});
table.Sample(rng, buffer);                          // 미리보기/우편용 — ItemDelta 리스트 적재
inventory.TryGrant(table, rng, out failedIndex);    // 샘플 → 원자 지급 1호출
```

- 확률은 만분율 int(0~10000) — float 비결정성 회피. 수량 롤은 long [min,max] 균등.
- `IRandomSource { long Next(long maxExclusive) }` 시섬 + `SystemRandomSource` 어댑터 —
  서버 권위 RNG든 결정론 시뮬레이션 RNG든 게임이 주입.
- 생성자에서 데이터 오류(가중 0 합, min>max, 만분율 범위 밖) 즉시 던짐 — 기동 검증.
- 게임 몫: 항목 게이트(레벨 요건·배타 목록 — 게임이 테이블을 상황별로 만들거나 필터),
  우편 폴백(B의 잔여량으로), 천장.

## 4. B/C/D/E 계약 요점

- **TryAddUpTo(item, amount, out granted)**: granted = min(amount, 남은 상한). 0 지급도
  성공(변경·통지 없음) — "가득参이라 버림"은 오류가 아니라는 idlez 의미론. 오류는
  UnknownItem/InvalidAmount(비스택형 비정수)뿐.
- **ExpiresAtTicksUtc**: 0 = 무기한. setter가 변경 추적 자동 반영(Flags와 동일).
  `TryLoadInstance`에 선택 인자 추가. `CollectExpired(nowTicksUtc, buffer)`는 수집만 —
  시간은 항상 게임이 주입(프레임워크는 시계를 모름 — 결정론·테스트 용이).
  조회(GetQuantity 등)는 만료를 필터하지 않는다 — 필터 시점·정책은 게임 몫.
- **CollectInstances(item, buffer)**: 정의의 인스턴스 수집, 반환값은 담은 수.
- **onApplied(ReadOnlySpan&lt;ItemDelta&gt;)**: 성공 커밋당 1회, 연산 순서대로의 순 델타
  (지급 +, 소모 −). 내부 배열 재사용 — 무할당. 핸들러 미지정 시 기록 자체를 생략.

## 5. 테스트 계획

- RewardTable: 확정/확률 그룹(경계 0·10000), 가중 추첨 분포(고정 RNG로 결정), 수량 롤
  경계, 생성자 검증, TryGrant 원자성.
- TryAddUpTo: 부분 지급·0 지급 성공·비스택형 정수/상한.
- 만료: 필드 추적(Updated)·로드·CollectExpired 경계(0 제외, ==now 포함 여부 명시).
- CollectInstances / onApplied: 순서·부호·커밋당 1회·실패 시 미호출·무할당.

## 6. 리젠 정산 (v0.4 추가 — 재분류)

§2에서 "아이템 전용 아님"으로 제외했으나 사용자 정정: idlez는 **티켓 아이템을 리젠으로
구현**한다(`CalculateRegenValueFromPeriod`) — 아이템 도메인 실수요가 맞고, 옛 구현이
주석으로 남아 있을 만큼 틀리기 쉬운 공식이라 프레임워크 몫의 전형.

`Regen.SettlePeriodic(current, max, periodTicks, nowTicks, ref lastRefreshTicks)`:
- 소비한 주기만큼만 기준 시각 전진 — 나머지 경과 보존(연속 호출 드리프트 없음).
- 가득이면(도달 포함) 기준을 현재로 재설정 — 가득 상태 경과 적립(악용) 방지.
- 미초기화(0)·시계 역행은 기준을 현재로 놓고 0 지급 — "최초 정산 가득 지급" 버그 방지.
- 시간은 항상 게임 주입(프레임워크는 시계를 모름). 수량 반영·기준 저장(TState)은 게임 몫.
- 초당 r개 연속 리젠은 period = 1초/r 환산으로 동일 공식 사용.

## 7. 리젠 자동 정산 (사용자 요청 확장)

공식만으로는 idlez의 "리젠 아이템들을 돌리는 루프"(RefreshTickets)가 게임마다 남는다 —
카탈로그 메타로 승격해 프레임워크가 스윕을 소유한다:

```csharp
builder.Register("ticket", def, maxStack: 5, regenPeriodTicks: TimeSpan.FromMinutes(30).Ticks);
inventory.SettleRegen(nowTicks);   // 리젠 정의 전체 lazy 정산 — 충전분 원자 지급 1배치
```

- 기준 시각(lastRegenTicksUtc — 파라미터명도 이걸로 개명)은 **정의별 맵**에 둔다:
  전량 소모로 인스턴스가 사라져도(수량 0 행 제거 모델) 리젠이 계속되는 티켓 의미론.
  영속화는 `GetRegenBasis`/`LoadRegenBasis` + `catalog.RegenItems` 순회.
- 제약(Register에서 강제): 스택형만·유한 maxStack 필수·수량 정수 강제(변경 경로 검증).
- 기준 전진은 지급 커밋 성공 후에만 — 실패 시 다음 정산에서 재시도(유실 없음).
- 소모 전 정산이 계약(가득 재설정의 적립 악용 방지가 이 순서에 의존).

## 8. 풀 분리와 우선순위 소모 (growninja count/param3 대응)

growninja가 티켓을 자동 리젠(count)/보상 획득(param3) 두 필드로 쪼갠 이유는 풀별 상한
규칙이 달라서다(리젠 풀만 상한·가득 정지, 보상 풀은 무제한). 우리 답: **풀 = 정의** —
`ticket.regen`(maxStack+regen 메타) + `ticket.bonus`(무제한) 두 정의로 명시하고,
반복될 소모/표시 경로를 흡수:

- `TryRemoveAcross(sources, amount)` — 우선순위 순서로 나눠 소모, 전부-아니면-전무.
  보상 티켓 먼저·무상 재화 먼저(자금결제법류 유/무상 분리)·이벤트 재화 우선이 전부 이 패턴.
- `GetQuantityAcross(items)` — 합산 표시. `GetRemovableQuantity(item)` — 가용(잠금 제외)
  수량 공개(UI·소모 계획용).

## 9. 리젠 목표선 (2026-08-19 — 사용성 지적으로 §8 단순화)

"티켓 종류마다 정의 2개 + 호출부마다 합산/분배"는 호출부 판정 결함의 재생산이라는
사용자 지적 수용. 재분석 결과 **보너스-먼저 소모 정책에서 growninja의 (count, param3)
분리는 단일 총량 + "총량 < 목표선일 때만 리젠"과 관찰상 동치** — 필드 분리는 구현
잔재였다.

결정: **리젠 정의의 maxStack = 하드 상한이 아니라 리젠 목표선.**
- 명시적 지급(TryAdd/TryAddUpTo/로드)은 목표선 초과 허용(리젠 정의 한정).
- SettleRegen은 총량이 목표선 미만일 때만 채우고, 이상이면 기준 재설정(적립 방지).
  목표선 초과 총량은 long 변환 전에 BigNum 비교로 걸러 안전.
- 결과: 던전 티켓 = 정의 1개, 호출부는 GetQuantity/TryRemove 그대로. §8의 두 정의 +
  TryRemoveAcross 패턴은 풀이 관찰상 실제로 다를 때(만료 상이·유/무상 회계·리젠 우선
  소모 정책)만 쓰는 선택지로 강등.

## 10. MaxCount/MaxRegen 분리 (2026-08-19 — 사용자 정식화, §9 대체)

§9의 "maxStack 의미 과적"(비리젠=하드캡, 리젠=목표선)을 사용자 제안대로 명시적 두 노브로
분리하고 Stack 네이밍 제거:

- **maxCount** — 하드 보유 상한(스택 수량/인스턴스 수). `maxStack`에서 개명.
- **maxRegen** — 리젠 목표선. 리젠 정의 필수, 리젠 주기 없이 지정하면 데이터 오류.
- **불변식: maxRegen ≤ maxCount** (위반은 기동 예외).
- **미지정 규칙(사용자 제안)**: maxCount 0(미지정) + maxRegen ≥ 1 → maxCount를
  maxRegen으로 덮음(엄격 기본 — 목표선 초과 적립은 명시적 maxCount 선언으로만 허용).
  미지정 + 리젠 없음 → 무제한.
- 표현력 확장: "목표선 5 + 하드캡 99"(§9로는 불가) 표현 가능. growninja 패턴은
  maxCount: long.MaxValue 명시.
- `ExceedsMaxStack` → `ExceedsMaxCount` 개명. 하드캡 검사는 전 정의 일관 복원
  (리젠 예외 제거), SettleRegen 목표선은 GetMaxRegen 사용.

### §10 수정 (사용자 재결정): maxCount 기본값 = long.MaxValue

"0 = 미지정 → maxRegen 덮어쓰기" 매직 규칙 제거 — 기본값을 문자 그대로 무제한으로.
리젠 정의도 기본은 목표선 초과 적립 허용(growninja 친화)이며, 엄격 상한이 필요한
정의만 maxCount를 maxRegen과 같게 명시한다. maxRegen ≤ maxCount 불변식은 유지.

## 11. v0.5 — reason 원장·인벤토리 간 이동·Growth (2026-08-19, 외부 대조 채택분)

- **reason 사유 코드 + 원장 강화**: 모든 변경 API·BeginTransaction에 선택적
  `long reason`(게임은 `enum ItemReason : long` 권장 — 프레임워크가 enum을 정의하면
  도메인 침범, 제네릭은 서명 오염이라 long 통로). `onApplied(reason, changes)`의
  변경 항목은 `InventoryChange { Item, Delta, Balance }` — **변경 후 잔량** 포함으로
  PlayFab TransactionHistory·Nakama ledger 상당의 CS 원장을 어댑터 하나로 구성 가능.
  "Stack Trace" 요구의 업계 답 = 콜스택이 아니라 (사유, 델타, 잔량) 구조적 원장.
- **인벤토리 간 이동**: `TryTransfer`(정의 단위 — 스택 병합, 비스택형 n개 통째 이동)·
  `TryTransferByInstance`(id·상태·플래그·만료 보존). **같은 플레이어·같은 세션 액터
  한정** — 유저간 거래는 다른 액터라 인메모리 원자화 불가, 에스크로/우편 워크플로
  몫(미래 Trade/Mail 모듈). 출발 Removed·도착 Created 추적(DB 소유 컬럼 UPDATE 매핑).
  잠금은 이동도 차단, 대상 maxCount 검사, 발급자 교차 id 충돌 방어.
- **Growth.SettleExp**: exp 테이블 소진 다단계 레벨업(잔여 보존·만렙 정지·잔여 유지,
  필요치 0 이하는 데이터 오류). 레벨 직접 증가·리셋은 대입 한 줄 — 프레임워크 제외.
- 백로그 기록: 멱등성 id(PlayFab IdempotencyId·Steam requestid 상당)는 영속 dedup이
  필요해 Rpc/핸들러 계층 후보.

### §11 수정 (사용자 피드백): reason 인자 → 로그 스코프 트리

함수마다 사유 인자를 꿰는 방식은 호출 체인 오염 + 문맥 빈약(상품 id·수량 등을 못 담음)
이라는 지적 수용 — reason 파라미터 전면 제거, 스코프 방식으로 대체(분산 트레이싱 span
트리와 동형):

```csharp
using (inventory.BeginLogScope($"BuyItem product={id} x{count}"))   // 핸들러 = 루트
{
    inventory.TryRemove(gold, price);          // 변경(델타+잔량) 자동 첨부
    using (inventory.BeginLogScope("PickReward"))
    {
        inventory.Log($"pity={pity}");         // 자유 노트
        inventory.TryGrant(table, rng, out _);
    }
}   // 루트 닫힘 → onLog 싱크로 완성 트리 전달
```

- 하위 로직은 "뭘로 시작됐는지" 몰라도 됨 — 현재 스코프에 자동 첨부, 중첩 자유.
- 스코프 밖 변경은 즉시 단건 묶음 전달 — 원장에 구멍 없음. 실패 커밋은 Change 미기록.
- 싱크(onLog) 미지정 시 전 경로 no-op, `IsLogging`으로 비싼 문자열 구성 가드.
- 역순 닫기 강제(위반 시 예외). onApplied는 reason 없는 기계 소비(업적)로 존속.

## 12. ActionLog 승격 + 트랜잭션 범용화 분석 (2026-08-19, 사용자 지시)

체인(획득 → 이벤트 → 업적 클리어 → 보상)이 인벤토리 밖에서 시작·경유하므로 인벤토리
내부 기능으로는 부족하다는 지적 반영:

- **ActionLog 독립 승격**: 로그 스코프를 인벤토리에서 분리해 세션/플레이어당 1개의
  범용 `ActionLog`(싱크 필수)로. 어떤 시스템이든 `BeginScope`/`Log`로 참여하고,
  인벤토리는 ctor `log`(+`logLabel`) 참조만 받아 변경(델타·잔량)을 현재 스코프에 자동
  첨부한다. 복수 인벤토리가 한 로그를 공유해 체인 전체가 한 트리에 남는다(라벨로 출처
  구분). Items 패키지 소재는 잠정 — 두 번째 패키지가 쓰게 되면 Core 승격(타입 분리) 후보.
- **트랜잭션 범용화는 보류(분석 기록)**: 레거시 실물에서 체인은 원자적이지 않다 —
  이벤트는 커밋 후 발화하고 업적 보상 실패가 원 행동을 롤백하지 않으며, 단일 액터
  인메모리라 게임 상태+인벤토리는 함께 저장된다. 즉 체인의 실수요는 원자성이 아니라
  추적(ActionLog가 해결). 커밋 코어는 이미 검증/적용 2패스라 참가자 계약으로 승격할
  준비가 돼 있고, 두 번째 프레임워크 시스템(업적/퀘스트 모듈)이 실존할 때 승격한다
  (구현 1개짜리 인터페이스 회피).

### §12 수정 (사용자 지시): ActionLog를 Core로 즉시 승격

다음 작업이 바로 위에 올라온다는 지시로 "두 번째 소비자 대기"를 앞당김 —
`Bun3.Server.Core.ActionLog`(0.5.0)로 이동. Core는 Items 타입을 모르므로 구조화
페이로드를 일반화: 항목 = {Kind(ScopeStart/Note/Data), Depth, Text, Source, Data(object)}.
모듈은 `Append(data, source)`로 자기 타입을 그대로 싣고 싱크가 타입 매칭으로 해석
(`entry.Data is InventoryChange c`). Items는 Core를 참조해 변경을 자동 첨부(저빈도
감사 경로라 박싱 허용 — 문서 명시). 스코프 밖 항목은 항목별 즉시 전달.
