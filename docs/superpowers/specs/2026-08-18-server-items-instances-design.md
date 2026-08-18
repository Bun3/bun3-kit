# Bun3.Server.Items v0.2 설계 (인스턴스 인벤토리)

- 날짜: 2026-08-18
- 상태: 승인 대기 (사용자 "ㄱㄱ" — 방향 승인, 세부는 합리적 기본값)
- 범위: 스택/인스턴스 통합 인벤토리(`ItemInventory<TState>`), 카탈로그 unstackable
  메타, 인스턴스 id 발급 시섬, per-instance 변경 추적(저장/전송 연계)
- 선행 문서: `2026-08-17-server-items-design.md` (v0.1 — 카탈로그·스택 컨테이너·수량 시섬)

## 1. 근거 — 세 시스템의 수렴

idlez(`CashItemManager`)·growninja(`player_items`)·Steam Inventory Service가 독립적으로
같은 모델에 수렴했다: **"고유 id와 수량을 가진 인스턴스, 스택 여부는 정의 메타"**.

| | idlez | growninja | Steam |
|---|---|---|---|
| 인스턴스 id | long (DB IDENTITY) | long (DB IDENTITY) | uint64 itemid |
| 스택 판정 | `ResourceItem.Unstackable` | `Tag.UNSTACKABLE_ITEM` | itemdef 메타 |
| 판정 위치 | 지급/삭제/조회에 분산 | **7곳+ 재판정 (복사 버그 온상)** | 플랫폼 내부 |
| 상태 플래그 | InUse/UserLocked (uint) | 장착/NFT 태그 | NoTrade/Removed (uint16) |
| 가변 상태 | option json + param1-4 | option blob + param1-4 | dynamic properties |
| id=0 함정 | `AddedItemStuff` 우회 | 동일 | 없음 (플랫폼 발급) |

프레임워크 결론: 판정은 **컨테이너 내부에서 정확히 한 번**, 상태는 불투명 `TState`,
id는 발급 시섬(외부 권위 id 수용).

## 2. 입력 결정

| 질문 | 결정 |
|---|---|
| 스택·인스턴스 통합 방식 | **단일 `ItemInventory<TState>`** — 스택형 정의는 정의당 싱글턴 인스턴스로 자동 병합(idlez 의미론), 비스택형은 수량 1 인스턴스 N개. 지급/소모/트랜잭션 경로가 하나 |
| 인벤토리 수량 타입 | **long 고정** — idlez(long)·growninja(int)·Steam(uint16) 전부 포함. BigNum 대수량은 v0.1 스택 컨테이너 몫(재화·경량 카운터 용도로 존속) |
| maxStack 의미 확장 | **정의당 최대 보유량** — 스택형이면 스택 상한, 비스택형이면 최대 인스턴스 수(idlez maxCount와 동일 역할) |
| TState 생성 | 게임이 `Func<ItemId, TState>` 팩토리 제공(생성자 1회 할당). 프레임워크는 해석 안 함 |
| 인스턴스 id | 게임이 `Func<long>` 발급자 제공(스노플레이크/하이로우/시퀀스는 게임 선택). 로드는 기존 id 그대로 수용(DB·Steam 권위 id). **저장 전 id=0 함정 원천 제거** |
| 소모 잠금 | 생성자 `removeBlockingFlags`(uint 마스크) — idlez NotConsumable·Steam NoTrade가 전부 비트마스크라는 수렴. 델리게이트 아님(무할당·선언적) |
| 변경 추적 | per-instance Created/Updated/Removed 플래그 + `DrainChanges(buffer)` 1채널. idlez의 Dirty(DB)/Updated(클라) 2플래그는 게임이 드레인 1회 후 팬아웃으로 대체(더 단순) |
| level/exp 성장·만료 정책 | **v0.2 제외** — TState + MarkChanged로 표현 가능. 반복 증거가 더 쌓이면 v0.3 |
| 인벤토리 간 이동 | **v0.2 제외** — idlez는 플레이어당 단일 인벤토리. 소비자 생기면 추가 |

## 3. 카탈로그 확장

```csharp
builder.Register("sword.iron", def, maxStack: 10, externalId: 2001, unstackable: true);
catalog.IsUnstackable(itemId);   // bool[] 조회 — 무할당
```

v0.1 `ItemStackContainer`는 비스택형 정의를 **`ItemError.NotStackable`로 거부**한다
(장비를 수량 병합하는 오용 차단 — "단일 카탈로그 계약을 변경 경로에서 강제" 선례).

## 4. 모델

```csharp
public sealed class ItemInstance<TState>
{
    public long InstanceId { get; }          // 발급자 또는 로드 id
    public ItemId Item { get; }              // 불변
    public long Quantity { get; internal set; }  // 비스택형은 항상 1
    public uint Flags { get; set; }          // 의미는 게임/플랫폼 몫, setter가 MarkChanged
    public TState State { get; }             // 불투명 — 게임이 변경 후 MarkChanged()
    public void MarkChanged();               // Updated 마킹 + 컨테이너 onChanged
}

public class ItemInventory<TState>
{
    // ctor(catalog, instanceIdIssuer, stateFactory, capacity = 0, onChanged = null, removeBlockingFlags = 0)
    long GetQuantity(ItemId item);                       // 스택형=싱글턴 수량, 비스택형=인스턴스 수 합
    bool TryGetInstance(long instanceId, out ItemInstance<TState> instance);
    int InstanceCount;  Enumerator GetEnumerator();      // struct 열거자

    ItemError TryAdd(ItemId item, long amount, List<ItemInstance<TState>>? created = null);
    ItemError TryRemove(ItemId item, long amount);       // 비스택형: 잠금 아닌 인스턴스 n개 제거
    ItemError TryRemoveByInstance(long instanceId, long amount);
    ItemError TryApply(ReadOnlySpan<ItemDelta<long>> deltas, out int failedIndex,
                       List<ItemInstance<TState>>? created = null);   // 전부-아니면-전무
    ItemError TryLoadInstance(long instanceId, ItemId item, long quantity, uint flags, TState state);

    bool HasChanges;  void DrainChanges(List<ItemChange<TState>> buffer);
}

public readonly struct ItemChange<TState> { ItemChangeKind Kind; long InstanceId; ItemInstance<TState>? Instance; }
public enum ItemChangeKind { Created, Updated, Removed }
```

### 의미론

- **지급**: 스택형 → 싱글턴 병합(v0.1과 동일한 maxStack/오버플로 검증, `LongQuantityOps`
  재사용). 비스택형 → amount개 인스턴스 생성(각 Quantity=1, `stateFactory` 호출).
  보유 합이 maxStack을 넘으면 ExceedsMaxStack. 발급자는 검증 통과 후에만 호출.
- **소모**: `removeBlockingFlags`에 걸린 인스턴스는 소모 후보에서 제외 — 가용 수량
  부족이면 Insufficient, 특정 잠긴 인스턴스 직접 제거 시도는 `Locked`. 비스택형
  제거 순서는 미보장(특정 인스턴스는 TryRemoveByInstance로).
- **트랜잭션**: v0.1과 동일 계약(순차 판정·같은 id 누적·실패 시 완전 무변경·
  failedIndex). 스택형·비스택형 델타 혼합 가능 — 판정은 내부에서 1회.
- **변경 추적**: 성공 변경마다 인스턴스에 Created/Updated 마킹, 제거는 id 목록
  적재(생성 후 드레인 전 제거는 상쇄 — DB에 안 나감). `DrainChanges`가 호출자
  버퍼에 채우고 초기화. `onChanged`(→`Player.MarkDirty`)는 v0.1과 동일하게
  성공 연산당 1회 + `MarkChanged`당 1회.
- **로드**: 추적·통지 없음. 중복 instanceId는 `DuplicateInstance`, 스택형 정의의
  두 번째 인스턴스도 `DuplicateInstance`, maxStack 검사 수행.
- **무할당**: 조회·열거·스택 경로·드레인(버퍼 재사용 시) 무할당. 인스턴스 생성은
  본질적 할당(저빈도) — 예외로 문서화.

### ItemError 추가

`NotStackable`(스택 컨테이너에 비스택형), `UnknownInstance`, `DuplicateInstance`(로드),
`Locked`(잠금 인스턴스 직접 제거).

## 5. 역할 분담 요약

| | v0.1 `ItemStackContainer` | v0.2 `ItemInventory<TState>` |
|---|---|---|
| 용도 | 재화·경량 카운터 (BigNum 포함) | 아이템 가방 (장비+소모품 혼재) |
| 엔트리 | ItemId → 수량 | 인스턴스 (id·수량·플래그·TState) |
| 저장 연계 | onChanged + 전체 열거 | onChanged + DrainChanges(행 단위 upsert/delete) |

## 6. 테스트 계획

- 카탈로그: unstackable 메타·스택 컨테이너의 NotStackable 거부.
- 스택형: 싱글턴 병합·maxStack·플래그 잠금 소모 차단.
- 비스택형: n개 생성(수량 1)·보유 상한·잠금 제외 소모·Locked·TryRemoveByInstance·
  created 버퍼.
- 트랜잭션: 혼합 델타 원자성·중간 실패 무변경·같은 id 누적·발급자 호출 시점
  (실패 시 미호출).
- 변경 추적: Created/Updated/Removed 드레인·생성-제거 상쇄·로드 무추적·
  MarkChanged/Flags setter.
- 로드: 기존 id 수용·중복 거부·스택형 이중 로드 거부.
- 무할당: 조회·열거·스택 델타 TryApply 워밍업 후 할당 0.
