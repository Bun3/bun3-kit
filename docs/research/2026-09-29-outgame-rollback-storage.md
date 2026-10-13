# Outgame 메모리 롤백 보관 방식 조사

> 이 문서는 수정 전 조사 기록이다. 후속 변경의 계약과 사용법은 [상태 롤백 설계](../superpowers/specs/2026-09-29-outgame-state-rollback-design.md)와 [롤백 가이드](../outgame-rollback.md)를 따른다.

- 조사일: 2026-09-29
- 대상: 현재 작업 트리의 `ItemInventory<TState>`, `AchievementManager<TDef>`, `Core.Transaction`
- 목적: 대체 게임 패키지 선정이 아니라, 원본 보관·변경 감지·객체 소유권·선택적 트랜잭션 계약을 비교한다.
- 출처: 프로젝트 공식 문서와 공식 저장소 소스만 사용한다. 아래 **사실**은 출처에서 확인한 동작이고, **적용 판단**은 Bun3에 대한 제안이다. 설계 확정이나 패키지 도입 결정은 아니다.
- 조사 도구: Firecrawl CLI 미설치를 확인하고, 환경을 변경하지 않은 채 웹 조회와 공식 GitHub 원문으로 조사했다.

## 1. 결론

현재의 **컨테이너 얕은 스냅샷 + 변경된 인스턴스의 변경 전 값**은 유지할 만한 후보이다. 조사한 사례들도 모든 문제를 하나의 복제 전략으로 해결하지 않는다. 변경 직전의 값을 기록하거나, 작업용 복사본을 만들거나, 불변 상태를 교체하는 식으로 상태와 변경 경계를 제한한다. Bun3에서 먼저 정리할 부분은 컨테이너의 저장 방식보다 **가변 `TState`의 소유권과 복원 계약**이다. 이는 아래 사례를 종합한 적용 판단이다.

`ItemInventory`와 `AchievementManager`는 현재도 `Transaction` 없이 사용할 수 있다. 트랜잭션은 여러 변경을 함께 복원하고 알림을 늦추는 선택 기능이다. 이 독립 사용 경로를 유지하면서, 트랜잭션을 연결한 경우에만 상태 복사·복원 계약을 요구하는 방향이 적절하다. 현재 동작과 한계는 5절에 정리했다.

## 2. 현재 구현과 확인된 경계

### 2.1 무엇을 보관하는가

아이템은 첫 변경 시 `_instances`와 `_regenBasis`를 별도 사전에 얕게 복사한다. 변경되는 인스턴스에는 수량·플래그·만료·dirty 상태·`TState`의 변경 전 값을 한 번 보관한다. 롤백 때 목록을 복원하고 보조 인덱스를 다시 만든 후 각 인스턴스 필드를 복원한다. `TState`의 내부 변경은 `BeginStateChange()`에서 `snapshotState`를 통한 복사 대상으로 삼는다. [현재 ItemInventory 구현](../../server/src/Bun3.Server.Items/ItemInventory.Transaction.cs)

업적은 전체 카탈로그를 복사하지 않고 변경된 ID마다 `AchievementState`를 한 번 보관한다. 동일 ID의 중복 캡처는 배열로 막고, 롤백 때 저장된 상태를 되돌린다. 이 상태는 프레임워크가 정의하므로 사용자 임의의 객체 그래프를 복사해야 하는 아이템 상태 문제와 다르다. [현재 AchievementManager 구현](../../server/src/Bun3.Server.Achievements/AchievementManager.Transaction.cs), [AchievementState](../../server/src/Bun3.Server.Achievements/AchievementState.cs)

**비용에 대한 코드 기반 추론:** 아이템 수 `N`, 재생 기준 수 `R`, 변경 인스턴스 수 `K`일 때 컨테이너 캡처는 `O(N + R)`, 인스턴스 필드 보관은 `O(K)`이다. 여기에 도메인 상태를 복제하는 비용이 추가된다. 버퍼 재사용은 할당을 줄이지만 복사 작업량이나 피크 용량 유지를 없애지는 않는다. 아직 이 방식과 구조 변경 로그 방식의 성능 비교 측정은 하지 않았다.

### 2.2 서로 다른 클래스가 내부 리스트를 공유하는 경우

현재는 기존 상태와 새 상태의 최상위 클래스 참조가 다르면 깊은 복사를 생략할 수 있다. 두 클래스가 동일한 내부 리스트를 공유하면 이 판단은 안전하지 않다. 원래 `Level=1, Values=[10]`인 상태에서 `Level=5`인 새 클래스로 교체하되 리스트를 공유하고, `BeginStateChange()` 안에서 `30`을 추가한 뒤 롤백한 실제 결과는 `Level=1, Values=[10,30]`이었다. 원래 객체 참조를 돌려놓아도 리스트 내용은 복원되지 않았다. [판단 조건과 복원 코드](../../server/src/Bun3.Server.Items/ItemInventory.Transaction.cs)

이 사례는 조사 직전 격리된 임시 콘솔 프로그램으로 확인했다. 이번 조사에서는 production/test 코드를 변경하지 않았다. 임의의 가변 참조를 외부 코드가 보유하거나 여러 아이템이 같은 가변 하위 객체를 공유하는 경우까지 지원하려면, 단순히 클래스/구조체를 구분하는 조건보다 더 명확한 소유권 계약이 필요하다.

## 3. 오픈소스 사례

### 3.1 EF Core: 변경 감지와 스냅샷 정책을 명시한다

**사실:** EF Core는 추적 대상의 스냅샷과 현재 값을 비교해 변경을 감지한다. 사용자 정의 타입에는 `ValueComparer`로 비교·해시·스냅샷 생성 규칙을 제공할 수 있다. 공식 `List<int>` 예제는 내용 비교와 함께 `ToList()`로 별도 리스트를 만든다. 불변 타입이라면 같은 값을 유지해도 원본이 바뀌지 않지만, 내부 변경을 감지하려면 그에 맞는 복사 규칙이 필요하다. [Value Comparers](https://learn.microsoft.com/en-us/ef/core/modeling/value-comparers)

**사실:** `INotifyPropertyChanging`과 `INotifyPropertyChanged`를 모두 사용하는 전략은 속성이 바뀌기 직전에 원래 값을 보관하여, 추적 시작 시 전체 스냅샷을 만드는 필요를 줄인다. 단순히 변경 후 알림만 전달하는 전략과 다르다. [Change Detection and Notifications](https://learn.microsoft.com/en-us/ef/core/change-tracking/change-detection#notification-entities)

**적용 판단:** `snapshotState`처럼 도메인이 복사 방식을 제공하는 훅 자체는 타당하다. 다만 복사 필요 여부를 최상위 참조 동일성으로 추정하면 내부 가변 데이터의 계약과 어긋난다. 프레임워크는 변경 전 캡처 시점을 보장하고, 도메인은 상태 스키마에 맞는 복사 함수를 제공하도록 경계를 정하는 것이 좋다. EF의 변경 감지는 DB 저장용이므로, 이것을 그대로 객체 롤백 기능이라고 해석하거나 EF를 도입하면 Bun3의 메모리 복원이 해결된다고 보아서는 안 된다.

### 3.2 Orleans: 첫 쓰기에서 독립적인 작업 상태를 만든다

**사실:** Orleans 트랜잭션은 선택적으로 활성화하는 분산 영속 상태 기능이며, 상태 접근 API는 `PerformRead`와 `PerformUpdate`로 나뉜다. `ITransactionalState`의 read 함수는 상태를 수정하지 않아야 한다. [Orleans transactions](https://learn.microsoft.com/en-us/dotnet/orleans/grains/transactions), [ITransactionalState 소스](https://github.com/dotnet/orleans/blob/5bebd170b19d996a262ae0dfeda143ebff6cdffe/src/Orleans.Transactions/Abstractions/ITransactionalState.cs)

**사실:** 확인한 소스에서는 해당 트랜잭션의 첫 write에 `copier.DeepCopy(record.State)`를 호출해 작업 상태를 만든다. 반환 결과도 `CopyResult`에서 복사한다. `ITransactionDataCopier<TData>`는 원본과 독립된 깊은 복사본을 반환하는 계약이고, 기본 구현은 Orleans의 복사기를 호출한다. [TransactionalState 소스](https://github.com/dotnet/orleans/blob/5bebd170b19d996a262ae0dfeda143ebff6cdffe/src/Orleans.Transactions/State/TransactionalState.cs#L156), [복사기 계약](https://github.com/dotnet/orleans/blob/5bebd170b19d996a262ae0dfeda143ebff6cdffe/src/Orleans.Transactions/Abstractions/ITransactionDataCopier.cs), [기본 복사기](https://github.com/dotnet/orleans/blob/5bebd170b19d996a262ae0dfeda143ebff6cdffe/src/Orleans.Transactions/Hosting/DefaultTransactionDataCopier.cs)

**적용 판단:** Bun3에 가져올 핵심은 분산 커밋 프로토콜이 아니라, **원본과 작업 상태를 분리하고 쓰기 진입점을 통제한다**는 점이다. `BeginStateChange`를 작업 복사본 편집으로 바꾸는 방향도 검토할 수 있지만, 기존 인스턴스의 즉시 변경·참조 유지 계약과 비용이 달라지므로 별도 설계가 필요하다. Orleans의 트랜잭션 시스템 전체를 도입하는 것은 현재 문제보다 범위가 크다.

### 3.3 Immer: 불변 상태와 명시적인 객체 그래프 제한

**사실:** Immer는 draft의 변경으로 다음 불변 상태를 만들며 변경하지 않은 부분은 이전 상태와 공유한다. `produceWithPatches`는 다음 상태와 정방향·역방향 패치를 반환하여 undo/redo에 활용할 수 있다. 패치 집합이 항상 최소 크기인 것은 아니다. [Introduction](https://immerjs.github.io/immer/), [Patches](https://immerjs.github.io/immer/patches/)

**사실:** 지원 상태는 하나의 객체가 두 위치에 나타나지 않고 순환 참조가 없는 트리여야 한다. 외부에서 가져온 객체는 draft에 넣더라도 자동으로 draft가 되지 않는 경로가 있으며, 그 객체를 수정하면 외부 원본에 영향을 줄 수 있다고 공식 문서가 경고한다. [Pitfalls](https://immerjs.github.io/immer/pitfalls/)

**적용 판단:** 성숙한 라이브러리도 임의의 공유 가변 객체 그래프를 무조건 안전하게 처리하지 않는다. Bun3 역시 `TState`가 소유하는 데이터 범위와 외부 참조 공유 허용 여부를 명시해야 한다. 불변 상태를 교체하는 사용자는 이전 루트 참조만 보관해도 복원할 수 있지만, `record`나 `readonly struct` 선언만으로 내부 리스트까지 불변이 되는 것은 아니다. Immer는 JavaScript 생태계의 설계 사례이며 C# 패키지의 직접 대체재로 추천하는 것은 아니다.

### 3.4 CSLA.NET: 편집 단계마다 상태를 보관하고 하위 객체에 undo를 전파한다

**사실:** CSLA의 `UndoableBase`는 편집 상태를 `Stack<byte[]>`로 보관한다. `CopyState`는 필드를 수집·직렬화해 스택에 넣고, `IUndoableObject`인 하위 객체에는 캡처를 전파한다. `UndoChanges`는 보관 상태를 읽어 필드를 복원하고 하위 객체의 undo를 호출한다. `AcceptChanges`는 해당 단계의 보관 상태를 버리고 하위 객체에도 수락을 전파한다. 인터페이스는 `EditLevel`, `CopyState`, `UndoChanges`, `AcceptChanges`를 명시한다. [UndoableBase 고정 소스](https://github.com/MarimerLLC/csla/blob/408c05eef72c0ffe651fac6565641c71b21d7457/Source/Csla/Core/UndoableBase.cs), [IUndoableObject](https://github.com/MarimerLLC/csla/blob/408c05eef72c0ffe651fac6565641c71b21d7457/Source/Csla/Core/IUndoableObject.cs)

**적용 판단:** .NET 객체의 메모리 undo에 직접 가까운 사례다. 다만 임의 객체가 자동으로 안전해지는 것이 아니라, 보관 대상 필드의 직렬화와 참여하는 하위 객체의 계약을 둔다. Bun3도 복원 가능한 범위를 명확히 정의해야 한다는 근거가 된다. 상속·직렬화·편집 단계별 바이트 버퍼를 그대로 도입하는 것은 현재 패키지의 낮은 할당 목표에 비해 부담이 클 수 있다. 이는 구조상 추론이며 성능 비교 측정 결과는 아니다.

## 4. 참고: SQLite의 변경 전 페이지 보관

**사실:** SQLite rollback journal은 변경할 페이지의 원래 내용과 원래 파일 크기를 먼저 보관한다. 실패 시 이 정보를 이용해 원래 파일을 복원한다. 실제 파일 변경 전 journal을 영속화하는 순서까지 포함하여 프로세스·전원 장애에 대비한다. [Atomic Commit In SQLite](https://www.sqlite.org/atomiccommit.html#creating_a_rollback_journal_file)

**적용 판단:** 변경 전 값을 먼저 확보한다는 원리는 Bun3 인스턴스별 보관 방식과 유사하다. 그러나 페이지 바이트는 DB가 경계를 통제하는 데이터이고, 임의의 C# 객체 그래프와 다르다. SQLite의 내구성·격리 보장을 메모리 undo 기능에 옮겨 설명해서는 안 된다. 이번 조사에서 SQLite 도입을 제안하지 않는다.

## 5. Transaction 없는 독립 사용

**현재 코드에서 확인한 사실:** 두 생성자의 `transaction`은 기본값이 `null`인 선택 인자다. 아이템의 `snapshotState`도 선택 인자다. 활성 트랜잭션이 없으면 롤백 캡처를 수행하지 않고 변경 알림을 즉시 전달한다. 이 경우 `BeginStateChange()`에는 복사 델리게이트가 필요하지 않으며 `MarkChanged()`도 사용할 수 있다. [ItemInventory 생성자](../../server/src/Bun3.Server.Items/ItemInventory.cs), [아이템 캡처](../../server/src/Bun3.Server.Items/ItemInventory.Transaction.cs), [아이템 알림](../../server/src/Bun3.Server.Items/ItemInventory.Notifications.cs), [AchievementManager 생성자](../../server/src/Bun3.Server.Achievements/AchievementManager.cs), [업적 캡처와 알림](../../server/src/Bun3.Server.Achievements/AchievementManager.Transaction.cs)

`ItemInventory.BeginTransaction()`의 `InventoryTransaction`은 단일 인벤토리 작업 묶음을 사전 검증하고 적용하는 별도 API다. 공유 `Core.Transaction`을 연결하지 않아도 사용한다. 다만 업적 보상 수령과 아이템 지급을 별개로 호출했을 때, 나중 작업의 실패가 앞선 작업까지 자동으로 되돌리지는 않는다. [InventoryTransaction](../../server/src/Bun3.Server.Items/InventoryTransaction.cs)

트랜잭션 기능 미사용과 Core 패키지 의존성 제거는 다르다. 두 패키지는 여전히 Core를 참조한다. 또한 롤백용 컬렉션 객체와 업적 `_txCaptured` 배열은 현재 생성 시 준비되므로, 미사용 시 롤백 스냅샷 복사는 없지만 모든 관련 메모리 비용이 0인 것은 아니다. [Items 프로젝트](../../server/src/Bun3.Server.Items/Bun3.Server.Items.csproj), [Achievements 프로젝트](../../server/src/Bun3.Server.Achievements/Bun3.Server.Achievements.csproj), [업적 생성자](../../server/src/Bun3.Server.Achievements/AchievementManager.cs)

조사와 함께 기존 독립 사용 테스트 및 트랜잭션 밖 상태 변경 테스트를 실행하여 Release **70/70 통과**를 확인했다. 이는 해당 사용 경로의 검증이며, 임의의 객체 그래프 롤백 안전성을 입증하는 결과는 아니다.

## 6. 후속 설계에 대한 권고

다음은 조사에 따른 제안이며 아직 구현하거나 확정하지 않았다.

1. **선택적 트랜잭션을 유지한다.** 단독 사용에는 복사 전략을 요구하지 않고, 여러 모듈을 하나의 복원 경계로 묶을 때만 공유 `Transaction`을 연결한다. 미사용 시 보조 버퍼를 지연 생성하는 개선은 별도의 비용 최적화 후보이다.
2. **불변 교체와 가변 편집을 구분한다.** 불변 교체는 이전 상태를 보관하고, 가변 편집은 원본에서 독립된 복사본을 변경 전에 확보해야 한다. 같은 상태에서 두 경로를 섞어 써도 최초 값이 보존되도록 프레임워크가 책임진다.
3. **소유권과 복원 범위를 결정한다.** 롤백 후 값의 복원, 원래 최상위 객체의 참조 유지, 외부 코드가 가진 하위 객체 참조의 내용 복원은 각각 다른 보장이다. 복사본 대입만으로 셋을 모두 약속하지 않는다. 아이템 간 가변 상태 공유를 허용할지, 금지할지, 복사로 분리할지도 명시한다.
4. **현재 컨테이너 전략을 성능 측정 전에 전면 교체하지 않는다.** 큰 인벤토리의 작은 변경이 실제 병목이면 전체 사전 복사와 구조 변경 로그를 비교한다. 정상 커밋·롤백 시간, 할당량, 보관 용량을 함께 측정한다.
5. **회귀 검증은 계약의 경계를 겨냥한다.** 클래스 교체 후 내부 리스트 공유, 참조 필드를 가진 구조체, 반복 교체 후 내부 수정, 아이템 간 상태 공유 정책, 콜백 실패, 트랜잭션 미사용 경로를 검증 대상으로 삼는다.

`TState`를 도메인에 열어 두는 현재 목표를 유지한다면, 우선순위는 범용 트랜잭션 패키지를 추가하는 것보다 **복사와 변경 경계의 계약을 명료하게 만들고 현재 공유 참조 사례를 수정하는 것**이다. 이번 조사에서는 외부 라이브러리 도입이나 추가 리팩터링을 수행하지 않았다.
