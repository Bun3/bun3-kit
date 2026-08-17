# Bun3.Server.Items v0.3 설계 (일원화·트랜잭션·카탈로그 색인)

- 날짜: 2026-08-18
- 상태: 사용자 결정 반영 (네이밍·트랜잭션·색인·일원화 4건 확정)
- 선행 문서: `2026-08-17-server-items-design.md`(v0.1), `2026-08-18-server-items-instances-design.md`(v0.2)

## 1. 확정 결정

| 질문 | 결정 |
|---|---|
| 실패 enum 네이밍 | **`InventoryError`** — 주어(인벤토리 조작)를 이름에 노출. `Item`Error는 대상이 모호, Process/Behaviour는 정보 없는 접미어라 기각 |
| 컨테이너 일원화 | **재화도 아이템 행(스택형 정의)으로 처리, count는 BigNum** — idlez가 골드를 아이템 행으로 다루는 실물 구조 채택. `ItemStackContainer` 계열·수량 제네릭 시섬(`IQuantityOps`) 전체 삭제 |
| 트랜잭션 | **`BeginTransaction()` 빌더 도입** — 정의 단위 + **인스턴스 지정** 연산 혼합 원자 배치(기존 `TryApply`로는 "특정 검 파괴+골드 지급"이 원자 불가). 프레임워크 전역 트랜잭션 승격은 두 번째 소비자 모듈이 나올 때 |
| 카탈로그 쿼리 | **선언적 보조 색인 `CreateIndex`/`CreateMultiIndex`** — idlez 7개·growninja 4개 손색인의 프레임워크 흡수. `GetRequired("...")` 하드코딩은 네이티브 아이템 예외 경로로 강등 |

## 2. 수량 = BigNum

`ItemInstance.Quantity`·델타·조회가 전부 `BigNum`. long은 implicit 변환으로 그대로 사용
가능하므로 일반 게임 코드는 변화 없음(`TryAdd(gold, 100)`).

- **손실 의미론**: BigNum 덧셈은 유효 18~19자리 밖 항을 흡수한다(1e30 골드 + 1 = 1e30).
  방치형 수량 계약으로 수용, XML 문서 명시. 전량 소모는 정확히 Zero → 엔트리 제거.
- **비스택형 정수 강제**: 인스턴스 수는 정수여야 하므로 비스택형 델타 크기는
  "정수(Exponent ≥ 0) 그리고 ≤ MaxInstancesPerOperation(1000)"을 지급·소모 양방향에
  요구(위반 시 InvalidAmount). 스택형은 BigNum 자유.
- maxStack(long)은 비교 시 BigNum 변환(무제한 sentinel은 비교 자체 생략).
- 산술 오버플로(지수 1e8 초과)는 기존 BigNumQuantityOps처럼 catch → ExceedsMaxStack.

삭제: `ItemStackContainer(OfT)`, `BigNumItemStackContainer`, `IQuantityOps`,
`Long/BigNumQuantityOps`, `ItemStack`, `ItemError.NotStackable`(스택 컨테이너 소멸로 무의미).

## 3. InventoryTransaction — 인스턴스 지정 원자 배치

```csharp
var tx = inventory.BeginTransaction();          // 인벤토리 소유 스크래치 재사용 — 무할당
tx.Remove(potionId, 5);                         // 정의 단위
tx.RemoveInstance(swordInstanceId);             // 인스턴스 전량
tx.RemoveInstance(goldSingletonId, 150);        // 스택 싱글턴 부분 소모
tx.Add(gemId, 3);
InventoryError r = tx.Commit(out int failedIndex, created);   // 전부-아니면-전무
```

- `BeginTransaction`은 이전 미커밋 배치를 버리고 새로 시작(동시 배치 1개 계약).
  낡은 빌더 오용은 토큰 검사로 즉시 던진다.
- `TryApply(span)`/`TryAdd`/`TryRemove`는 내부적으로 같은 커밋 코어를 타는 편의 API로 존속.
- **검증-적용 일관성 규칙**: 배치 내 `RemoveInstance`가 지정한 인스턴스는 정의 단위
  소모의 후보 풀에서 **배치 전체에 걸쳐 제외**된다(순서 무관). 검증은 수량(풀 크기)
  기준 시뮬레이션 — 적용이 어떤 비지정 인스턴스를 집어도 개수는 보장된다.
- 스택 싱글턴 대상 `RemoveInstance`는 의미상 정의 단위 소모와 동일 풀로 정산.
- 같은 인스턴스 중복 지정 시 두 번째는 Insufficient. 잠금 인스턴스 지정은 Locked.
- 실패 시 완전 무변경, `failedIndex`는 tx 연산 순번. id 발급자·상태 팩토리는 검증 통과
  후에만 호출.

## 4. 카탈로그 보조 색인

```csharp
var builder = new ItemCatalogBuilder<MyDef>();
var byType = builder.CreateIndex(def => def.Type);            // 단일 키
var byTag  = builder.CreateMultiIndex(def => def.Tags);       // 다중 키
// ... Register 후
var catalog = builder.Build();                                 // 색인도 이때 1회 구축
ReadOnlySpan<ItemId> potions = byType.Get(ItemType.Potion);   // 무할당 조회, 미등록 키는 빈 span
```

- 정의 내 정의 참조(materialId류)는 기존 검증 델리게이트에서 빌드 시 1회 해석해
  TDefinition에 ItemId로 캐시 — 색인과 함께 "런타임 문자열 조회 제로"를 완성.
- Build 전 `Get`은 InvalidOperationException(기동 순서 오류를 즉시 노출).

## 5. DrainChanges 소유권 계약 (문서화 보강)

드레인은 파괴적(추적 초기화)이므로 **버퍼 = 변경 집합의 소유권 이전**. 게임은 영속화
성공 전까지 버퍼를 유지하고 실패 시 같은 버퍼로 재시도한다(Created/Updated는 인스턴스
참조라 재시도 시점 최신 상태가 나감 — 안전). 드레인 자체는 인메모리 O(n) 스냅샷이며
DB I/O는 게임의 `OnSaveAsync`(async) 몫 — 프레임워크 인벤토리 조작은 동기 무할당 유지.

## 6. 테스트 계획

- 리네임 반영 전체 + BigNum 수량: long implicit 경로 무변화·천문학적 수량·손실 흡수
  문서 동작·전량 소모 Zero·비스택형 정수/상한 강제(소수·1000 초과 거부).
- 트랜잭션: 정의+인스턴스 혼합 원자성·지정 인스턴스의 정의 소모 풀 제외·싱글턴 부분
  소모 정산·중복 지정 Insufficient·잠금 Locked·실패 무변경·낡은 빌더 토큰 거부.
- 색인: 단일/다중 키 조회·미등록 키 빈 span·Build 전 사용 거부.
- 무할당: 조회·열거·tx 커밋(인스턴스 미생성 배치) 워밍업 후 0.

## 7. 레시피 (v0.3 추가)

idlez `materialItemGroups`·growninja/Steam `exchange`의 공통 코어만: **재료 목록 →
결과 목록, 전부-아니면-전무**. 커밋 코어 위의 얇은 조합이다.

```csharp
var recipe = new Recipe(
    new[] { new RecipeEntry(gold, 30), new RecipeEntry(ore, 3) },
    new[] { new RecipeEntry(sword, 1) });
inventory.TryCraft(recipe, out failedIndex, created, count: 4);   // count = 반복 제작 배수
```

- 재료 소모가 결과 지급보다 먼저 정산 — 같은 정의가 양쪽에 있는 합성(검 3→1)도 정확.
- 레시피 데이터는 게임 몫(보통 TDefinition 안) — 카탈로그 검증 델리게이트에서 빌드 시
  해석·검증. RecipeEntry 생성자가 None·0 이하를 즉시 던져 데이터 오류를 기동에서 잡는다.
- 게임 몫으로 남긴 것: 대체 재료 분기(OR — 게임이 분기 선택 후 호출), 재료 유효성
  (레벨 요건), 특정 인스턴스 재료 지정(BeginTransaction 직접 조합), 확률 결과(보상
  테이블 영역).
