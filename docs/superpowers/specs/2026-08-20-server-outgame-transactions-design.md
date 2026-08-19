# Bun3.Server 아웃게임 고도화 설계 (트랜잭션·ActionLog 검증·API 정합·인스턴스 색인)

- 날짜: 2026-08-20
- 상태: 사용자와 문답으로 확정 (①~⑤ 전 섹션 승인)
- 범위: `Bun3.Server.Core`(트랜잭션 신설) · `Bun3.Server.Items` · `Bun3.Server.Achievements`
- 선행 문서: `2026-08-17-server-items-design.md`(v0.1) ~ `2026-08-18-server-items-v04-design.md`
  (§5~§12 후속 결정 누적), `2026-08-17-server-achievements-design.md`(v2),
  `2026-08-06-server-players-design.md`(dirty/저장 계약)

## 0. 트리거와 요약

v0.4 §12는 트랜잭션 참가자 계약 승격을 **"두 번째 프레임워크 시스템 모듈이 실존할 때"** 로
이연했다. Achievements가 등장해 조건이 충족됐다.

재분석 결과 승격의 형태가 §12의 예상과 다르다. §12는 "커밋 코어를 검증/적용 2패스로
쪼개 참가자로 삼는다"(지연 적용, 2PC)를 상정했으나, **실제 체인은 앞 단계의 결과를 읽어야
다음 단계가 판단하므로 지연 적용으로는 표현이 안 된다.** 따라서 **즉시 적용 + 되돌리기**
모델을 채택한다. §12의 "체인은 원자성이 아니라 추적 문제" 결론 자체는 유효하며, 이번
트랜잭션은 체인을 묶는 장치가 아니라 **각 단계 안에서의 다단계 원자성**을 준다.

| # | 항목 | 결정 |
|---|---|---|
| ① | 트랜잭션 참가자 계약 | 승격 — Core에 `Transaction`/`ITransactionParticipant`. 즉시 적용 + 롤백. 모듈은 멤버로 보유(인자 전달 아님) |
| ② | ActionLog 실사용 검증 | Achievements → Core 참조 + 자동 첨부. 트랜잭션 안에서는 알림·첨부를 버퍼링해 커밋 시 발화 |
| ③ | API·네이밍 정합 | `AchievementId`/`AchievementTag` 도입, 조회·예외·훅 이름 통일. 타입명은 현행 유지 |
| ④ | 우편/지급 큐 | **신설하지 않음** — 프레임워크 몫은 이미 전부 존재. 트리거 조건 명시 후 백로그 |
| ⑤ | 보유 인스턴스 색인 | 정의별 인스턴스 색인 신설(내부), `_stackSingletons` 흡수. 공개 API 신규 0건 |

## 1. 트랜잭션 — 즉시 적용 + 되돌리기

### 1.1 왜 지연 적용이 아닌가

체인의 실물(아이템 획득 → 획득 업적 → 업적 보상 지급)에서 2단계는 1단계의 결과를 읽어
유효성을 판단한다. 지연 적용에서는 1단계가 아직 반영되지 않아 읽을 것이 없다.
또한 이 셋은 **서로 묶이지 않는다** — 각자 자기만 성립하면 되는 독립 트랜잭션이다.
트랜잭션은 "모든 기능이 Commit/Rollback을 갖는다"는 계약이지 전부를 하나로 묶는 장치가
아니다.

### 1.2 계약 (Bun3.Server.Core)

```csharp
public interface ITransactionParticipant
{
    void OnCommit();      // discard the snapshot taken at first touch
    void OnRollback();    // restore the snapshot, then discard
}

public sealed class Transaction
{
    public bool IsActive { get; }
    public TransactionScope Begin();                            // nested Begin throws
    public void Enlist(ITransactionParticipant participant);    // idempotent per transaction
}

public readonly struct TransactionScope : IDisposable
{
    public void Commit();
    public void Dispose();   // rolls back unless Commit was called
}
```

- **세션당 1개**, 모듈 생성자에 `Transaction? transaction`으로 주입 — ActionLog와 동형.
  열려 있지 않으면 스냅샷을 뜨지 않으므로 **롤백을 안 쓰는 게임의 비용은 0**이다.
- 모듈은 트랜잭션 안에서 **처음 변경될 때** `_transaction?.Enlist(this)`로 자기를 등록하고
  최소 스냅샷을 뜬다. 참가자별 복원은 서로 독립이라 순서 무관.
- 중첩은 예외(단일 레벨). 마크 기반 중첩은 실수요 실증 시 확장(백로그).
- 커밋/롤백 후 트랜잭션은 비활성 — 커밋 시 발화하는 훅이 다시 변경해도 스냅샷 대상이 아니다.

### 1.3 알림·훅은 커밋 시 발화한다

트랜잭션이 열려 있는 동안 모듈의 알림은 **버퍼링**되고, 커밋 시 enlist 순서로 발화,
**롤백 시 폐기**된다. 대상은 넷 전부다:

| 알림 | 대상 모듈 |
|---|---|
| `onDirty` | Items, Achievements |
| `onApplied(ReadOnlySpan<InventoryChange>)` | Items |
| ActionLog 자동 첨부 | Items, Achievements |
| `OnCompleted(id, def, count)` | Achievements |

이 규칙이 세 문제를 한꺼번에 푼다.

1. **원장 오염 방지** — 롤백된 변경이 CS 원장에 실제 변경으로 남지 않는다. ActionLog에
   되돌리기 API를 추가할 필요가 없다.
2. **`Increase`의 롤백 안전성** — `OnCompleted` 훅은 임의 게임 코드(체인 티어 활성화 등)를
   부르므로 즉시 발화하면 되돌릴 수 없다. 커밋 시 발화로 미루면 예외 없이 전 API가
   롤백 안전해진다.
3. **반응 레인 의미론과 일치** — 반응은 원래 커밋 후에 일어나야 한다(§1.5).

트랜잭션 밖에서는 지금과 동일하게 즉시 발화한다. ActionLog 트리에서 인벤토리 변경은
같은 트랜잭션 안에서 게임이 남긴 노트보다 뒤에 놓인다 — 트랜잭션의 결과로 읽히므로
의도된 순서다.

### 1.4 참가자 구현

**Items** — 첫 변경 시 스냅샷: 생성된 인스턴스 id 목록, 제거된 인스턴스 객체, 변경된
인스턴스의 프레임워크 필드(`Quantity`/`Flags`/`ExpiresAtTicksUtc`), TState 스냅샷(§1.6),
리젠 기준 맵의 변경 항목. 롤백은 역순 복원. 발급된 인스턴스 id는 회수하지 않는다
(유일성만 계약이고 연속성은 아니므로 버려도 무해).

**Achievements** — 터치된 인덱스의 `AchievementState` 구조체 복사본 보관. 5필드 값
복사라 사실상 공짜. 롤백은 되쓰기.

기존 `ItemInventory.BeginTransaction()`(인벤토리 내부 배치)은 **연산 1건의 원자성**이고
이 트랜잭션은 **연산 여러 건의 원자성**이다. 층이 달라 그대로 공존한다.

### 1.5 두 레인 — 무엇이 참가자이고 무엇이 아닌가

| 레인 | 대상 | 근거 |
|---|---|---|
| **원자** (Transaction 참가자) | 인벤토리 조작, 업적 수령·전이, 미래 시즌패스/퀘스트 | 같은 세션 액터, 인메모리, 완전 가역 |
| **반응** (`onApplied` / ActionLog / `OnCompleted`) | 랭킹, 분석, 업적 진행도 라우팅, 외부 저장소 | 되돌릴 수 없거나 되돌리면 안 되는 것 |

- **랭킹은 참가자가 될 수 없다** — 네트워크 호출이라 인메모리 롤백이 불가능하고, 랭킹
  갱신 실패로 구매를 되돌리는 것은 틀린 동작이다.
- **업적 진행도(`Increase`)는 참가자로 쓰지 않는다** — 기술적으로는 롤백 가능하지만(§1.3),
  실패 개념이 없고 at-least-once가 정답인 영역이다. 원자 레인에 넣을 이유가 없다.

### 1.6 TState 스냅샷 — 두 경로

`ItemInstance<TState>.State`는 게임 소유 불투명 타입이라 프레임워크가 임의로 되돌릴 수
없다. **`where TState : struct` 제약은 걸지 않는다** — 실물 조사에서 레거시의 아이템 상태는
직렬화 라이브러리가 생성한 class이고 안에 가변 리스트가 넷 있어 struct 제약이면 사용 자체가
불가능하다. 또한 struct여도 내부 참조 필드는 공유되므로 제약이 보장을 주지도 못한다.

**경로 1 — 값처럼 다루는 게임 (struct / immutable record):**

```csharp
instance.State = instance.State with { Level = lv + 1 };   // setter — old reference is the snapshot
```

`State`에 setter를 추가한다(기존 `Flags`·`ExpiresAtTicksUtc` setter가 이미 변경 추적을
자동 반영하는 것과 같은 패턴). 프레임워크는 옛 값/참조를 그대로 보관 — 복제 비용 0.

**경로 2 — 가변 TState:**

```csharp
using (var scope = instance.BeginStateChange())
{
    scope.State.Options[i] = newOption;
}   // Dispose → Updated 마킹 + onDirty
```

```csharp
new ItemInventory<MyItemState>(catalog, idIssuer, stateFactory,
    snapshotState: static s => s.Clone());
```

스코프 **진입 시점**이 곧 "변경 직전"이라 스냅샷 타이밍 문제가 해결된다. `snapshotState`
미지정 상태에서 트랜잭션 안에 진입하면 예외(롤백 불가를 조용히 넘기지 않는다).
레포의 `~Scope` 네이밍 컨벤션(`CancellationScope`, `PopupCloseScope`)을 따른다.

**가드**: 트랜잭션이 열린 상태에서 `MarkChanged()`가 불렸는데 그 인스턴스의 State
스냅샷이 없으면 예외 — *"assign State or use BeginStateChange — in-place mutation is not
rollback-safe"*. 조용한 데이터 유실을 테스트에서 잡히는 실패로 바꾼다. 트랜잭션 밖에서는
`MarkChanged()`가 지금과 동일하게 동작한다.

### 1.7 실증 — 보상 수령의 재작성

롤백이 없는 구현은 **순서로 원자성을 흉내낸다**: 먼저 지급하고 성공하면 진행도를 차감한다.
그래서 배수 지급이 부분 성공하면 진행도만 전량 차감되는 위험이 남고, 사전 검사가 본체에
중복 서술된다.

```csharp
using (log.BeginScope($"ClaimReward achievement={def.Id} x{count}"))
using (var tx = transaction.Begin())
{
    for (var i = 0; i < count; i++)
        if (!achievements.TryClaim(id)) break;

    if (inventory.TryGrant(def.RewardTable, rng, out _) != InventoryError.None)
        return SendRewardMail(def, count);      // no Commit → Dispose rolls the claims back

    tx.Commit();
}
achievements.Increase(claimRewardConditionId, 1);   // reaction lane — after commit
```

차감을 먼저 해도 안전해지고(롤백이 있으므로), 사전 검사 중복이 사라진다.

## 2. ActionLog 실사용 검증 — Achievements 연계

`Bun3.Server.Achievements`가 `Bun3.Server.Core`를 참조한다(전이 의존:
Core → Abstractions → Microsoft.Extensions.Logging.Abstractions). 트랜잭션 계약이 Core에
있으므로 참조는 어차피 필수이며, ActionLog 자동 첨부를 막을 이유가 사라진다.

- 생성자에 `ActionLog? log, string? logLabel` 추가 — Items와 동일한 이름·순서.
- 자동 첨부 대상: **달성**(`OnCompleted` 시점), **수령**, **전이**(Unlock/Activate/Lock).
  진행도 증가 자체는 고빈도라 제외 — 필요하면 게임이 노트로 남긴다.
- 페이로드는 Core의 `Data(object)` 통로에 `AchievementChange` 구조체를 그대로 싣고
  싱크가 타입 매칭으로 해석한다(`entry.Data is AchievementChange c`). Items의
  `InventoryChange`와 같은 방식.

**검증되는 계약**: 복수 모듈이 하나의 `ActionLog`를 공유해 체인 전체가 한 트리에 남는다.
이것이 ActionLog를 Core로 승격한 이유이며, Achievements가 두 번째 소비자로서 이를 실사용
증명한다. 통합 테스트로 트리 1건을 검증한다(§7).

```
ScopeStart  "BuyProduct id=1021 x1"
  Data      InventoryChange   { gold,  -300, balance 700 }   source="bag"
  Data      InventoryChange   { sword,   +1, balance 1   }   source="bag"
  Data      AchievementChange { first_purchase, Claimed 0→1 }
```

## 3. API·네이밍 정합

### 3.1 고칠 것

| # | 항목 | 현재 | 변경 후 |
|---|---|---|---|
| 1 | 식별자 타입 | `Increase(int index)` / `IncreaseByTag(int tagIndex)` — 둘 다 생 `int` | `AchievementId` + `AchievementTag` (`ItemId` 패턴: index+1, `default = None`) |
| 2 | 문자열 조회 | `TryGetIndex` / `GetIndex` | `TryGet` / `GetRequired`, 태그는 `TryGetTag` / `GetRequiredTag` |
| 3 | 카탈로그 실패 예외 | `ArgumentException` / `KeyNotFoundException` | `AchievementCatalogException` (`ItemCatalogException`과 동형) |
| 4 | dirty 훅 이름 | `onDirty`(Ach) ↔ `onChanged`(Items) | 양쪽 **`onDirty`** — Players 계약어가 `MarkDirty` |
| 5 | 세션 스코프 서비스 | Ach에 없음 | 양쪽 생성자 말미에 `ActionLog? log, string? logLabel, Transaction? transaction` 동일 이름·순서 |

**#1이 이번 정합의 핵심이다.** 현재는 아래가 컴파일되고 조용히 엉뚱한 업적을 올린다:

```csharp
var tag = catalog.GetTagIndex("daily");
achievements.Increase(tag, 1);          // tag index used as achievement index
```

`GetIndicesByTag`의 반환도 `ReadOnlySpan<int>` → `ReadOnlySpan<AchievementId>`가 되어
그룹 순회까지 타입이 붙는다.

### 3.2 이미 맞는 것 — 실패 보고 규칙

두 패키지가 이미 같은 규칙을 따르고 있다. 규칙을 XML 문서에 명문화만 한다.

- **도메인 실패**(호출자가 처리해야 하는 것) → 반환값. Items는 `InventoryError`,
  Achievements는 `TryClaim`의 bool·`Increase`의 달성 수. Achievements에 오류 enum이 없는
  것은 도메인 실패가 없기 때문이지 불일치가 아니다.
- **프로그래머 오류 / 데이터 손상** → 예외. Items는 낡은 tx 빌더·잘못된 `ItemId`,
  Achievements는 음수 amount·`Restore` 불변식 위반.

### 3.3 일부러 다른 것 (사유 기록)

| 항목 | Items | Achievements | 사유 |
|---|---|---|---|
| 카탈로그 생성 | 빌더 | 생성자 + 리스트 | Items는 등록 시점 메타(maxCount·externalId·unstackable·regen)와 보조 색인 선언이 필요. Achievements는 메타가 전부 정의 베이스에 있어 빌더가 빈 껍데기가 된다 — **대칭을 위한 타입은 만들지 않는다** |
| 검증기 | `AddValidator(Action<카탈로그>)` 다중 | 생성자 `Action<TDef>` 단일 | Items는 정의 간 상호 참조(재료 id 해석)를 완성 카탈로그에서 검증해야 함 |
| 정의 타입 | 불투명 `TDefinition` | `TDef : AchievementDefinition` | Achievements는 Target·Repeatable·Tags를 프레임워크가 읽어야 함. Items는 아무것도 읽지 않음 |
| 보조 색인 | `CreateIndex`/`CreateMultiIndex` 범용 | 태그 전용 | 업적 라우팅은 태그가 유일한 실수요 |
| 영속화 | `DrainChanges`(동적 행) | `GetState`/`Restore`(고정 배열) | 데이터 성격이 다름 |

### 3.4 타입명 — 현행 유지

`ItemInventory` / `AchievementManager`를 그대로 둔다. `ItemManager`로의 개명은 기각:
복수 컨테이너 실수요가 생겼을 때 **`ItemManager`를 위에 얹는 것은 순수 추가**지만,
`ItemManager`로 먼저 명명한 뒤 `ItemInventory`를 아래로 추출하는 것은 클래스 부피의
대부분(Operations/Crafting/Rewards/Transfer/Tracking)을 옮기고 전 호출부를 바꾸는 리팩터다.
작고 확정적인 비용을 택한다.

오늘 기준으로도 이 타입은 **컨테이너에 매니저 성격 필드 몇 개가 얹힌 형태**다(부피의
대부분이 컨테이너 일). 업그레이드 경로는 §8에 기록.

## 4. 보유 인스턴스 색인

현재 인스턴스 전수 스캔 지점 8곳 중 **6곳이 `ItemId` 키**다: `GetQuantity`,
`CollectInstances`, `GetRemovableQuantity`, `GetRemovablePoolBase`, `ApplyConsume`,
`TryTransfer`. (나머지 둘: `CollectExpired`는 시간 키, `DrainChanges`는 본질적으로 전수.)

```csharp
private readonly Dictionary<ItemId, List<ItemInstance<TState>>> _instancesByItem;
```

- 유지 지점은 `CreateInstance` / `RemoveInstance` 둘뿐.
- **기존 `_stackSingletons`(ItemId → instanceId)를 흡수한다** — 스택형 정의의 싱글턴은
  이 색인의 유일 원소이므로 중복 자료구조다. 둘에서 하나로 줄어 동기화 지점도 하나 준다.
- 열거 순서 비보장 계약은 유지(비스택형 소모 순서 미보장).

**게임 키별 인스턴스 색인은 만들지 않는다.** 이미 있는 둘의 조합으로 커버된다:

```csharp
foreach (var item in byCategory.Get(Category.Equipment))   // catalog definition index
    inventory.CollectInstances(item, buffer);              // per-definition instance index
```

게임 키별 색인을 프레임워크가 들면 게임이 키를 선언하고 프레임워크가 생성/삭제마다 N개
색인을 갱신해야 한다. 위 조합은 그 비용 없이 같은 결과를 낸다(정의 수만큼의 딕셔너리
조회). **공개 API 신규 0건.**

만료 색인은 미룬다 — 스캔 지점 1곳이고 주기적 스윕(저빈도)이라 `ExpiresAtTicksUtc`
setter에 동기화 지점을 늘릴 값어치가 없다(§8 백로그).

## 5. 우편/지급 큐 — 신설하지 않음

레거시 우편 실물(테이블·발송·수령 핸들러)을 조사한 결과, **우편에서 프레임워크 몫으로
남는 것은 이미 전부 존재한다**:

| 우편의 동작 | 이미 있는 것 |
|---|---|
| 첨부 보상 확정(발송 시점에 굴려 저장) | `RewardTable.Sample(rng, buffer)` — v0.4 §3에서 "미리보기/우편용"으로 이미 도입 |
| 만료 판정 | `CollectExpired` 패턴, 시간은 게임 주입 |
| 수령 = 첨부 지급 + 읽음 마킹의 원자화 | **§1의 `Transaction`** |
| 가방 상한 시 부분 수령 | `TryAddUpTo(item, amount, out granted)` |
| 첨부 페이로드 표현 | `ItemDelta` 목록 |

우편 고유로 남는 것은 **저장소**(테이블·조회·마킹 SQL)와 **세션 밖 발신**(운영툴 지급,
타 플레이어 발신, 오프라인 도달)이며 전부 게임/인프라 몫이다. 인메모리 프레임워크가
닿지 않는 영역이다.

**지급 큐(인메모리 보류 큐)도 기각** — 레거시 증거가 없다. 한쪽은 상한 초과분을 버리고
다른 쪽은 우편으로 보낸다. 둘 다 대기 큐를 두지 않는다.

**트리거 조건**: 우편 모듈은 **저장소 계약이 필요한 두 번째 시스템**이 등장할 때 재검토한다.
그때의 논점은 우편이 아니라 "세션 밖 영속 큐"의 공통 계약이다.

### 5.1 우편 폴백 레시피 (문서만, 코드 0줄)

1. 지급 불가/실패 시 `table.Sample(rng, buffer)`로 보상을 **확정**해 델타 목록을 얻는다.
2. 델타 목록을 게임 저장소(우편 행)에 넣는다 — 지급은 하지 않는다.
3. 수령 시 `Transaction` 안에서 인벤토리 지급 + 읽음 마킹을 함께 처리한다.
4. 가방 상한이면 `TryAddUpTo`의 잔량으로 다시 1번.

### 5.2 게임 몫으로 명문화 — 수령 멱등성

프레임워크 트랜잭션은 **인메모리 전용**이라 DB 커밋을 참가시킬 수 없다. 인메모리 지급과
DB 마킹의 durability 경계가 다르므로, 그 사이의 크래시로 인한 **이중 지급은 프레임워크가
막지 못한다**. 멱등 키는 게임 몫이며, v0.5 백로그의 "멱등성 id → Rpc/핸들러 계층 후보"와
같은 문제다. 계약 표와 XML 문서에 명시한다.

## 6. 패키지·버전 영향

| 패키지 | 변경 | 버전 |
|---|---|---|
| `Bun3.Server.Core` 0.5.0 | `Transaction`/`TransactionScope`/`ITransactionParticipant` 신설 (순수 추가) | → 0.6.0 |
| `Bun3.Server.Items` 0.1.0 | `onChanged`→`onDirty`, `State` setter + `BeginStateChange` + `snapshotState`, `transaction` 인자, `MarkChanged` 가드, 알림 버퍼링, 인스턴스 색인 | → 0.2.0 |
| `Bun3.Server.Achievements` 0.2.0 | Core 참조, `AchievementId`/`AchievementTag`, 조회·예외 개명, 세션 서비스 인자, 알림 버퍼링, ActionLog 첨부 | → 0.3.0 |

Items·Achievements 모두 파괴적 변경이나 실사용 게임이 아직 없어 비용은 사실상 0이다.
퍼블리시는 이번 작업 범위 밖(사용자 확인 후).

## 7. 테스트 계획

`server/tests/Bun3.Server.Tests`(net10, NUnit)에 추가:

| 종류 | 대상 |
|---|---|
| 트랜잭션 코어 | Begin/Commit/Rollback, Dispose 미커밋 시 자동 롤백, 중첩 Begin 예외, Enlist 멱등, 참가자 0개 커밋 |
| Items 롤백 | 지급 롤백(생성 인스턴스 소멸), 소모 롤백(제거 인스턴스 복원), 수량·Flags·ExpiresAt 복원, 리젠 기준 맵 복원, 혼합 배치 롤백 |
| Achievements 롤백 | 수령 롤백(ClaimedCount·Progress 복원), 전이 롤백, 진행도 롤백, 복수 인덱스 |
| 교차 참가자 | 수령 + 지급 실패 → 양쪽 무변경(§1.7 시나리오), 커밋 시 양쪽 반영 |
| 알림 버퍼링 | 롤백 시 onDirty/onApplied/OnCompleted/ActionLog **미발화**, 커밋 시 enlist 순서로 1회, 트랜잭션 밖은 즉시 발화(기존 동작 무회귀) |
| TState | setter 경로 롤백, `BeginStateChange` + `snapshotState` 경로 롤백, `snapshotState` 미지정 시 스코프 진입 예외, 트랜잭션 안 `MarkChanged` 가드 예외, 트랜잭션 밖 `MarkChanged` 정상 |
| ActionLog 통합 | 복수 모듈이 한 로그 공유 → 스코프 1개에 InventoryChange + AchievementChange가 순서대로 (§2 트리) |
| API 정합 | `AchievementId`/`AchievementTag` None·범위 밖 거부, `GetRequired` 예외 타입, `onDirty` 개명 무회귀 |
| 인스턴스 색인 | 6개 스캔 지점 동작 무회귀, 싱글턴 흡수 후 스택형 경로 무회귀, 생성/삭제 후 색인 정합, 대량 인스턴스에서 조회 정확성 |
| 무할당 | 트랜잭션 밖 기존 핫패스 무할당 무회귀. 측정 창 안에 NUnit 식이 끼지 않게 델타를 로컬로 받아 분기 비교(기존 교훈) |

## 8. 비범위·백로그

- **`ItemManager` 승격** — 복수 컨테이너 실수요가 생기면 카탈로그·id 발급자·상태 팩토리·
  리젠 기준·ActionLog·Transaction을 소유하고 자식 `ItemInventory` N개 + 크로스 컨테이너
  조회 + 로드 라우팅을 맡는 타입을 **위에 얹는다**. `ItemInventory`는 이미 컨테이너 단위
  타입이라 파괴적 변경 없음.
- **중첩 트랜잭션** — 마크 기반 부분 롤백. 단일 레벨로 부족한 실수요가 실증될 때.
- **만료 인스턴스 색인** — `CollectExpired` 스윕이 측정 가능한 문제가 될 때(§4).
- **가변 TState 깊은 스냅샷 델리게이트의 일반화** — `BeginStateChange` + `snapshotState`로
  충분하지 않은 실수요가 나올 때(§1.6).
- **우편/영속 큐 모듈** — §5의 트리거 조건.
- **멱등성 id** — Rpc/핸들러 계층 후보(§5.2).
- **외부 저장소 참가자** — 랭킹·DB를 트랜잭션에 넣는 것은 범위 밖(2PC/사가가 필요하며
  인메모리 액터 모델의 전제를 벗어난다).
