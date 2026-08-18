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
