# 아웃게임 트랜잭션·정합 구현 플랜

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `Bun3.Server.Core`에 즉시 적용 + 되돌리기 트랜잭션을 신설하고, Items·Achievements가 참가자로 동참하며 ActionLog·API 관례·인스턴스 색인을 정합한다.

**Architecture:** 트랜잭션은 세션당 1개 `Transaction` 객체이고 모듈은 이를 **생성자 멤버로 보유**한다(ActionLog와 동형). 모듈은 트랜잭션 안에서 처음 변경될 때 자기를 `Enlist`하고 최소 스냅샷을 뜬 뒤 **즉시 적용**하며, 롤백은 스냅샷 복원이다. 트랜잭션이 열려 있는 동안 모든 알림(`onDirty`/`onApplied`/ActionLog 첨부/`OnCompleted`)은 버퍼링되어 커밋 시 발화하고 롤백 시 폐기된다.

**Tech Stack:** C# 9 / netstandard2.1 (패키지), net10.0 + NUnit 4 (테스트), BigNum(`Bun3.Gameplay.Numerics`)

**Spec:** `docs/superpowers/specs/2026-08-20-server-outgame-transactions-design.md`

## Global Constraints

- 패키지 코드는 **netstandard2.1 + C# 9 블록 네임스페이스**. 테스트는 net10.0 파일 스코프 네임스페이스(`namespace Bun3.Server.Tests;`).
- **모든 public 멤버에 영어 XML 문서**. 빌드 경고 0.
- **코드·주석·문자열은 영어 전용** — 한글 금지. 레거시(idlez/growninja) 언급 금지, 타 패키지 참고 언급 금지. 한국어 설명은 `docs/`에만.
- **최소화된 필요한 주석만** — 코드가 못 보여주는 제약·계약만. 구현 경위·정당화 주석 금지.
- 라이브러리 `await`에는 `ConfigureAwait(false)` (이 플랜에는 async 경로 없음).
- **핫패스 무할당 규율** — 클로저·LINQ·문자열 할당 금지. 트랜잭션 스크래치는 생성자에서 1회 할당하고 재사용.
- 무할당 어서션은 `GC.GetAllocatedBytesForCurrentThread()` 델타를 **로컬 변수로 즉시 받고 분기 비교**(`if (delta != 0) Assert.Fail(...)`). `Assert.That(..., Is.EqualTo(0))`에 직접 넣으면 Release JIT 재배치로 NUnit 제약 할당(24B)을 세는 오탐이 난다.
- 제네릭이 단독 파일을 가지면 파일명은 `타입{T}.cs` (백틱 금지). partial은 `타입.역할.cs`.
- 커밋은 gitmoji + `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>` 트레일러. 서브에이전트는 `git commit -m "<제목>" -m "<트레일러>"` 이중 플래그.
- **퍼블리시·main 머지 금지.** 이 워크트리 브랜치(`Bun3/server-outgame-items-achievements`)에서 완결.

**테스트 실행:** `dotnet test server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj`
**필터 실행:** `dotnet test server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj --filter "FullyQualifiedName~TransactionTests"`

---

## File Structure

**신규 (Core)**
- `server/src/Bun3.Server.Core/Transaction.cs` — `ITransactionParticipant`, `Transaction`, `TransactionScope` (밀접한 형제 타입 3개, 한 파일)

**신규 (Items)**
- `server/src/Bun3.Server.Items/ItemStateScope{TState}.cs` — TState 제자리 변경 스코프
- `server/src/Bun3.Server.Items/ItemInventory.Transaction.cs` — partial: 참가자 구현·스냅샷·알림 버퍼링

**신규 (Achievements)**
- `server/src/Bun3.Server.Achievements/AchievementId.cs`
- `server/src/Bun3.Server.Achievements/AchievementTag.cs`
- `server/src/Bun3.Server.Achievements/AchievementCatalogException.cs`
- `server/src/Bun3.Server.Achievements/AchievementChange.cs` — `AchievementChangeKind` + `AchievementChange`
- `server/src/Bun3.Server.Achievements/AchievementManager.Transaction.cs` — partial: 참가자 구현·스냅샷·알림 버퍼링

**수정**
- `server/src/Bun3.Server.Items/ItemInstance.cs` — `State` setter, `BeginStateChange`, `MarkChanged` 가드
- `server/src/Bun3.Server.Items/ItemInventory.cs` — `_onDirty` 개명, `_instancesByItem` 색인, ctor 인자 추가
- `server/src/Bun3.Server.Items/ItemInventory.Operations.cs` / `.Tracking.cs` / `.Transfer.cs` / `.Regen.cs` — 색인·알림 경유
- `server/src/Bun3.Server.Achievements/AchievementCatalog.cs` — 식별자 타입·조회 개명·예외
- `server/src/Bun3.Server.Achievements/AchievementManager.cs` — 식별자 타입, partial 전환
- `server/src/Bun3.Server.Achievements/Bun3.Server.Achievements.csproj` — Core 참조, 버전
- `server/tests/Bun3.Server.Tests/*` — 무회귀 수정 + 신규 테스트 파일

**신규 테스트**
- `TransactionTests.cs` · `ItemInventoryTransactionTests.cs` · `ItemStateRollbackTests.cs` · `AchievementTransactionTests.cs` · `OutgameLedgerIntegrationTests.cs`

---

## Task 1: Core 트랜잭션 코어

**Files:**
- Create: `server/src/Bun3.Server.Core/Transaction.cs`
- Test: `server/tests/Bun3.Server.Tests/TransactionTests.cs`

**Interfaces:**
- Consumes: 없음
- Produces:
  - `interface Bun3.Server.Core.ITransactionParticipant { void OnCommit(); void OnRollback(); }`
  - `sealed class Bun3.Server.Core.Transaction { bool IsActive { get; } TransactionScope Begin(); void Enlist(ITransactionParticipant participant); }`
  - `readonly struct Bun3.Server.Core.TransactionScope : IDisposable { void Commit(); void Dispose(); }`

- [ ] **Step 1: 실패하는 테스트 작성**

`server/tests/Bun3.Server.Tests/TransactionTests.cs`:

```csharp
using Bun3.Server.Core;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class TransactionTests
{
    private sealed class Participant : ITransactionParticipant
    {
        private readonly List<string> _trace;
        private readonly string _name;

        public Participant(List<string> trace, string name)
        {
            _trace = trace;
            _name = name;
        }

        public void OnCommit() => _trace.Add($"commit:{_name}");

        public void OnRollback() => _trace.Add($"rollback:{_name}");
    }

    [Test]
    public void Commit_notifies_participants_in_enlist_order()
    {
        var trace = new List<string>();
        var transaction = new Transaction();
        var a = new Participant(trace, "a");
        var b = new Participant(trace, "b");

        using (var scope = transaction.Begin())
        {
            transaction.Enlist(a);
            transaction.Enlist(b);
            scope.Commit();
        }

        Assert.That(trace, Is.EqualTo(new[] { "commit:a", "commit:b" }));
        Assert.That(transaction.IsActive, Is.False);
    }

    [Test]
    public void Dispose_without_commit_rolls_back()
    {
        var trace = new List<string>();
        var transaction = new Transaction();

        using (transaction.Begin())
        {
            transaction.Enlist(new Participant(trace, "a"));
        }

        Assert.That(trace, Is.EqualTo(new[] { "rollback:a" }));
        Assert.That(transaction.IsActive, Is.False);
    }

    [Test]
    public void Enlist_is_idempotent_per_transaction()
    {
        var trace = new List<string>();
        var transaction = new Transaction();
        var a = new Participant(trace, "a");

        using (var scope = transaction.Begin())
        {
            transaction.Enlist(a);
            transaction.Enlist(a);
            scope.Commit();
        }

        Assert.That(trace, Is.EqualTo(new[] { "commit:a" }));
    }

    [Test]
    public void Nested_begin_throws()
    {
        var transaction = new Transaction();

        using (transaction.Begin())
        {
            Assert.That(() => transaction.Begin(), Throws.TypeOf<InvalidOperationException>());
        }
    }

    [Test]
    public void Enlist_without_active_transaction_throws()
    {
        var transaction = new Transaction();

        Assert.That(
            () => transaction.Enlist(new Participant(new List<string>(), "a")),
            Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void Transaction_is_inactive_while_notifying_so_hooks_may_mutate()
    {
        var transaction = new Transaction();
        var observed = true;
        var participant = new DelegateParticipant(() => observed = transaction.IsActive, () => { });

        using (var scope = transaction.Begin())
        {
            transaction.Enlist(participant);
            scope.Commit();
        }

        Assert.That(observed, Is.False);
    }

    [Test]
    public void Empty_transaction_commits_cleanly()
    {
        var transaction = new Transaction();

        using (var scope = transaction.Begin())
        {
            scope.Commit();
        }

        Assert.That(transaction.IsActive, Is.False);
    }

    [Test]
    public void Reusing_a_stale_scope_after_commit_is_a_no_op()
    {
        var trace = new List<string>();
        var transaction = new Transaction();
        var scope = transaction.Begin();
        transaction.Enlist(new Participant(trace, "a"));
        scope.Commit();

        scope.Dispose();   // already committed — must not roll back

        Assert.That(trace, Is.EqualTo(new[] { "commit:a" }));
    }

    private sealed class DelegateParticipant : ITransactionParticipant
    {
        private readonly Action _commit;
        private readonly Action _rollback;

        public DelegateParticipant(Action commit, Action rollback)
        {
            _commit = commit;
            _rollback = rollback;
        }

        public void OnCommit() => _commit();

        public void OnRollback() => _rollback();
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj --filter "FullyQualifiedName~TransactionTests"`
Expected: 컴파일 실패 — `Transaction`, `ITransactionParticipant` 미정의

- [ ] **Step 3: 구현**

`server/src/Bun3.Server.Core/Transaction.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace Bun3.Server.Core
{
    /// <summary>
    /// A module that can take part in a transaction. Participants apply their changes immediately
    /// and keep a snapshot taken at first touch, so a rollback is a restore rather than a deferred
    /// apply — later steps of a chain can read the results of earlier ones.
    /// </summary>
    public interface ITransactionParticipant
    {
        /// <summary>Discards the snapshot and flushes any notifications buffered during the transaction.</summary>
        void OnCommit();

        /// <summary>Restores the snapshot, discards it, and drops any buffered notifications.</summary>
        void OnRollback();
    }

    /// <summary>Transaction handle — close with using. Without a <see cref="Commit"/> call the
    /// transaction rolls back on dispose.</summary>
    public readonly struct TransactionScope : IDisposable
    {
        private readonly Transaction? _transaction;
        private readonly int _token;

        internal TransactionScope(Transaction transaction, int token)
        {
            _transaction = transaction;
            _token = token;
        }

        /// <summary>Commits — participants are notified in enlist order.</summary>
        public void Commit() => _transaction?.Commit(_token);

        /// <summary>Rolls back unless <see cref="Commit"/> was called.</summary>
        public void Dispose() => _transaction?.Rollback(_token);
    }

    /// <summary>
    /// Multi-operation transaction, one per session. Modules hold it as a constructor member and
    /// enlist themselves the first time they change while it is active; changes apply immediately
    /// and a rollback restores the snapshot each participant took at that first touch.
    /// Notifications a participant would raise are buffered for the duration and fire on commit,
    /// so a rolled-back change never reaches an audit ledger or a hook.
    /// Single level — a nested <see cref="Begin"/> throws. Use only inside a single session actor
    /// (no locks). When no transaction is active, participants skip snapshotting entirely, so a
    /// game that never rolls back pays nothing.
    /// </summary>
    public sealed class Transaction
    {
        private readonly List<ITransactionParticipant> _participants = new List<ITransactionParticipant>();
        private int _token;
        private bool _active;

        /// <summary>Whether a transaction is currently open. False while participants are being
        /// notified, so commit hooks may mutate freely.</summary>
        public bool IsActive => _active;

        /// <summary>Opens the transaction.</summary>
        /// <exception cref="InvalidOperationException">When a transaction is already open.</exception>
        public TransactionScope Begin()
        {
            if (_active)
            {
                throw new InvalidOperationException("Nested transactions are not supported.");
            }

            _active = true;
            _participants.Clear();
            _token++;
            return new TransactionScope(this, _token);
        }

        /// <summary>Registers a participant. Idempotent within one transaction — a module calls
        /// this on its first change.</summary>
        /// <exception cref="InvalidOperationException">When no transaction is open.</exception>
        public void Enlist(ITransactionParticipant participant)
        {
            if (participant == null)
            {
                throw new ArgumentNullException(nameof(participant));
            }

            if (!_active)
            {
                throw new InvalidOperationException("Enlist requires an active transaction.");
            }

            for (var i = 0; i < _participants.Count; i++)
            {
                if (ReferenceEquals(_participants[i], participant))
                {
                    return;
                }
            }

            _participants.Add(participant);
        }

        internal void Commit(int token)
        {
            if (token != _token || !_active)
            {
                return;
            }

            _active = false;
            for (var i = 0; i < _participants.Count; i++)
            {
                _participants[i].OnCommit();
            }

            _participants.Clear();
        }

        internal void Rollback(int token)
        {
            if (token != _token || !_active)
            {
                return;
            }

            _active = false;
            // Reverse order; participants restore independent state, so the order is immaterial.
            for (var i = _participants.Count - 1; i >= 0; i--)
            {
                _participants[i].OnRollback();
            }

            _participants.Clear();
        }
    }
}
```

- [ ] **Step 4: 통과 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj --filter "FullyQualifiedName~TransactionTests"`
Expected: PASS (8건)

- [ ] **Step 5: 커밋**

```bash
git add server/src/Bun3.Server.Core/Transaction.cs server/tests/Bun3.Server.Tests/TransactionTests.cs
git commit -m "✨ Core 트랜잭션 코어: 즉시 적용 + 되돌리기 참가자 계약" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Task 2: Items — `onChanged` → `onDirty` 개명

**Files:**
- Modify: `server/src/Bun3.Server.Items/ItemInventory.cs` (ctor 인자·필드·XML 문서)
- Modify: `server/src/Bun3.Server.Items/ItemInventory.Operations.cs`, `.Tracking.cs`, `.Transfer.cs` (`_onChanged` 참조 4곳)
- Modify: `server/src/Bun3.Server.Items/ItemInstance.cs` (XML 문서의 onChanged 언급)
- Modify: `server/tests/Bun3.Server.Tests/ItemInventoryTests.cs` (`onChanged:` 명명 인자 1곳)

**Interfaces:**
- Consumes: 없음
- Produces: `ItemInventory<TState>` 생성자의 `Action? onDirty` 인자 (기존 `onChanged` 자리·순서 그대로)

- [ ] **Step 1: 테스트를 새 이름으로 바꿔 실패시키기**

`ItemInventoryTests.cs`의 `SetUp`에서 명명 인자를 바꾼다:

```csharp
        _inventory = new ItemInventory<ItemState>(
            _catalog,
            instanceIdIssuer: () => ++_nextId,
            stateFactory: _ => new ItemState(),
            onDirty: () => _changed++,
            removeBlockingFlags: FlagInUse | FlagUserLocked);
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj --filter "FullyQualifiedName~ItemInventoryTests"`
Expected: 컴파일 실패 — `onDirty`라는 이름의 인자 없음

- [ ] **Step 3: 구현**

`ItemInventory.cs`에서 필드와 생성자 인자를 개명하고 XML 문서를 갱신한다:

```csharp
        private readonly Action? _onDirty;
```

```csharp
        /// <param name="onDirty">Called once per successful mutating operation plus once per
        /// <see cref="ItemInstance{TState}.MarkChanged"/> — games pass Player.MarkDirty to tie
        /// into the save cadence. Inside an active transaction the call is buffered and fires on
        /// commit (dropped on rollback).</param>
```

```csharp
            Action? onDirty = null,
```

```csharp
            _onDirty = onDirty;
```

나머지 파일의 `_onChanged?.Invoke()` 4곳을 `_onDirty?.Invoke()`로 바꾼다:
- `ItemInventory.Operations.cs` — `CommitOps` 성공 경로
- `ItemInventory.Tracking.cs` — `OnInstanceChanged`
- `ItemInventory.Transfer.cs` — 2곳(출발·도착 인벤토리)

`ItemInstance.cs`의 `MarkChanged` XML 문서에서 "the inventory's onChanged" → "the inventory's onDirty".

- [ ] **Step 4: 전체 테스트 통과 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj`
Expected: 전건 PASS (기존 265건 무회귀), 경고 0

- [ ] **Step 5: 커밋**

```bash
git add server/src/Bun3.Server.Items server/tests/Bun3.Server.Tests/ItemInventoryTests.cs
git commit -m "♻️ Items dirty 훅 이름을 onDirty로 통일 (Players MarkDirty 계약어)" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Task 3: Items — 정의별 인스턴스 색인

**Files:**
- Modify: `server/src/Bun3.Server.Items/ItemInventory.cs` (`_stackSingletons` → `_instancesByItem`, 조회 3곳)
- Modify: `server/src/Bun3.Server.Items/ItemInventory.Operations.cs` (조회·소모 7곳)
- Modify: `server/src/Bun3.Server.Items/ItemInventory.Tracking.cs` (로드 2곳)
- Modify: `server/src/Bun3.Server.Items/ItemInventory.Transfer.cs` (이동 1곳)
- Test: `server/tests/Bun3.Server.Tests/ItemInventoryTests.cs` (색인 정합 테스트 추가)

**Interfaces:**
- Consumes: 없음
- Produces: 내부 헬퍼 `bool TryGetSingleton(ItemId item, out ItemInstance<TState> singleton)`, `List<ItemInstance<TState>>? GetHeld(ItemId item)`. 공개 API 변경 없음.

- [ ] **Step 1: 실패하는 테스트 작성**

`ItemInventoryTests.cs` 끝에 추가:

```csharp
    // ---- Per-definition instance index ----

    [Test]
    public void Index_stays_consistent_across_create_and_remove()
    {
        Assert.That(_inventory.TryAdd(_sword, 3), Is.EqualTo(InventoryError.None));
        Assert.That(_inventory.GetQuantity(_sword).ToString(), Is.EqualTo(((BigNum)3).ToString()));

        Assert.That(_inventory.TryRemove(_sword, 2), Is.EqualTo(InventoryError.None));
        Assert.That(_inventory.GetQuantity(_sword).ToString(), Is.EqualTo(BigNum.One.ToString()));

        Assert.That(_inventory.TryRemove(_sword, 1), Is.EqualTo(InventoryError.None));
        Assert.That(_inventory.GetQuantity(_sword).Sign, Is.Zero);
        Assert.That(_inventory.InstanceCount, Is.Zero);

        // Refilling after the index entry drained must still work.
        Assert.That(_inventory.TryAdd(_sword, 1), Is.EqualTo(InventoryError.None));
        Assert.That(_inventory.GetQuantity(_sword).ToString(), Is.EqualTo(BigNum.One.ToString()));
    }

    [Test]
    public void Index_isolates_definitions_with_many_instances()
    {
        Assert.That(_inventory.TryAdd(_relic, 50), Is.EqualTo(InventoryError.None));
        Assert.That(_inventory.TryAdd(_sword, 3), Is.EqualTo(InventoryError.None));

        var buffer = new List<ItemInstance<ItemState>>();
        Assert.That(_inventory.CollectInstances(_sword, buffer), Is.EqualTo(3));
        Assert.That(buffer, Has.All.Matches<ItemInstance<ItemState>>(i => i.Item == _sword));
        Assert.That(_inventory.GetQuantity(_relic).ToString(), Is.EqualTo(((BigNum)50).ToString()));
    }

    [Test]
    public void Stack_singleton_is_the_sole_index_entry()
    {
        Assert.That(_inventory.TryAdd(_gold, 100), Is.EqualTo(InventoryError.None));
        Assert.That(_inventory.TryAdd(_gold, 50), Is.EqualTo(InventoryError.None));

        Assert.That(_inventory.InstanceCount, Is.EqualTo(1));
        var buffer = new List<ItemInstance<ItemState>>();
        Assert.That(_inventory.CollectInstances(_gold, buffer), Is.EqualTo(1));
        Assert.That(buffer[0].Quantity.ToString(), Is.EqualTo(((BigNum)150).ToString()));
    }

    [Test]
    public void Loading_a_second_stackable_instance_is_rejected_via_the_index()
    {
        Assert.That(
            _inventory.TryLoadInstance(10, _gold, 100, 0, new ItemState()),
            Is.EqualTo(InventoryError.None));
        Assert.That(
            _inventory.TryLoadInstance(11, _gold, 50, 0, new ItemState()),
            Is.EqualTo(InventoryError.DuplicateInstance));
    }
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj --filter "FullyQualifiedName~ItemInventoryTests"`
Expected: 신규 4건 중 일부 PASS(기존 동작으로도 성립), `Index_stays_consistent_across_create_and_remove`까지 전부 PASS일 수 있다. **이 태스크는 리팩터이므로 테스트는 무회귀 그물이다** — 4건이 모두 PASS인 것을 먼저 확인하고 구현에 들어간다(구현 후에도 PASS여야 한다).

- [ ] **Step 3: 구현 — 색인 도입과 싱글턴 흡수**

`ItemInventory.cs`에서 `_stackSingletons` 필드를 교체한다:

```csharp
        private readonly Dictionary<ItemId, List<ItemInstance<TState>>> _instancesByItem;
```

생성자에서:

```csharp
            _instancesByItem = new Dictionary<ItemId, List<ItemInstance<TState>>>();
```

헬퍼 두 개를 `ItemInventory.cs`에 추가한다:

```csharp
        /// <summary>Held instances of the definition, or null when none are held. The list is
        /// reused across refills, so an empty list is treated as "not held".</summary>
        private List<ItemInstance<TState>>? GetHeld(ItemId item) =>
            _instancesByItem.TryGetValue(item, out var held) && held.Count > 0 ? held : null;

        /// <summary>The stack singleton of a stackable definition. False for unstackables and for
        /// definitions with nothing held.</summary>
        private bool TryGetSingleton(ItemId item, out ItemInstance<TState> singleton)
        {
            if (!_catalog.IsUnstackable(item))
            {
                var held = GetHeld(item);
                if (held != null)
                {
                    singleton = held[0];
                    return true;
                }
            }

            singleton = null!;
            return false;
        }
```

`CreateInstance`(`ItemInventory.Operations.cs`)에서 색인에 등록한다:

```csharp
        private ItemInstance<TState> CreateInstance(ItemId item, BigNum quantity)
        {
            var instance = new ItemInstance<TState>(
                this, _instanceIdIssuer(), item, quantity, 0, 0, _stateFactory(item))
            {
                IsNew = true,
            };
            _instances.Add(instance.InstanceId, instance);
            Index(instance);
            return instance;
        }

        private void Index(ItemInstance<TState> instance)
        {
            if (!_instancesByItem.TryGetValue(instance.Item, out var held))
            {
                held = new List<ItemInstance<TState>>();
                _instancesByItem.Add(instance.Item, held);
            }

            held.Add(instance);
        }

        private void Unindex(ItemInstance<TState> instance)
        {
            if (_instancesByItem.TryGetValue(instance.Item, out var held))
            {
                held.Remove(instance);
            }
        }
```

`RemoveInstance`에서 `_stackSingletons` 정리를 `Unindex`로 대체한다:

```csharp
        private void RemoveInstance(ItemInstance<TState> instance)
        {
            _instances.Remove(instance.InstanceId);
            Unindex(instance);

            if (!instance.IsNew)
            {
                _removed.Add(instance.InstanceId);
            }

            instance._owner = null;
        }
```

`ApplyGrant`의 싱글턴 병합 분기를 바꾼다:

```csharp
            if (TryGetSingleton(item, out var singleton))
            {
                singleton.Quantity += amount;
                MarkChangedNoNotify(singleton);
            }
            else
            {
                var instance = CreateInstance(item, amount);
                created?.Add(instance);
            }
```

(`_stackSingletons.Add(item, instance.InstanceId)` 줄은 `CreateInstance` 안의 `Index`가 대신하므로 삭제한다.)

`GetQuantity` / `GetRemovableQuantity` / `GetRemovablePoolBase` / `CollectInstances` / `ApplyConsume` / `TryTransfer`의 전수 스캔을 색인 순회로 바꾼다. 예 — `GetQuantity`:

```csharp
        public BigNum GetQuantity(ItemId item)
        {
            if (TryGetSingleton(item, out var singleton))
            {
                return singleton.Quantity;
            }

            var held = GetHeld(item);
            return held == null ? BigNum.Zero : (BigNum)(long)held.Count;
        }
```

`GetRemovableQuantity`:

```csharp
        public BigNum GetRemovableQuantity(ItemId item)
        {
            if (TryGetSingleton(item, out var singleton))
            {
                return (singleton.Flags & _removeBlockingFlags) != 0 ? BigNum.Zero : singleton.Quantity;
            }

            var held = GetHeld(item);
            if (held == null)
            {
                return BigNum.Zero;
            }

            long count = 0;
            for (var i = 0; i < held.Count; i++)
            {
                if ((held[i].Flags & _removeBlockingFlags) == 0)
                {
                    count++;
                }
            }

            return count;
        }
```

`GetRemovablePoolBase`는 같은 형태에 `&& !IsUnstackableTarget(held[i].InstanceId)` 조건을 더한다.

`CollectInstances`:

```csharp
        public int CollectInstances(ItemId item, List<ItemInstance<TState>> buffer)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            var held = GetHeld(item);
            if (held == null)
            {
                return 0;
            }

            for (var i = 0; i < held.Count; i++)
            {
                buffer.Add(held[i]);
            }

            return held.Count;
        }
```

`ApplyConsume`의 비스택형 수집과 `TryTransfer`의 대상 수집도 `GetHeld(item)` 순회로 바꾼다. **제거 중 리스트 변형을 피하려고 기존과 같이 `_removeScratch`에 먼저 담고 나서 제거한다.**

`ItemInventory.Tracking.cs`의 `TryLoadInstance`에서:
- 스택형 중복 판정 `_stackSingletons.ContainsKey(item)` → `GetHeld(item) != null`
- `_stackSingletons.Add(item, instanceId)` 줄 삭제
- 마지막 `_instances.Add(...)` 뒤에 `Index(instance)` 호출을 위해 인스턴스를 지역 변수로 받는다:

```csharp
            var instance = new ItemInstance<TState>(this, instanceId, item, quantity, flags, expiresAtTicksUtc, state);
            _instances.Add(instanceId, instance);
            Index(instance);
            return InventoryError.None;
```

`CollectExpired`와 `DrainChanges`는 전수 스캔을 유지한다(시간 키·본질적 전수).

- [ ] **Step 4: 전체 테스트 통과 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj`
Expected: 전건 PASS. `_stackSingletons` 참조가 0인지 확인: `grep -rn "_stackSingletons" server/src/Bun3.Server.Items/` → 결과 없음

- [ ] **Step 5: 커밋**

```bash
git add server/src/Bun3.Server.Items server/tests/Bun3.Server.Tests/ItemInventoryTests.cs
git commit -m "⚡ Items 정의별 인스턴스 색인 도입, 스택 싱글턴 맵 흡수" -m "ItemId 키 전수 스캔 6곳을 색인 순회로 대체. _stackSingletons는 색인의 유일 원소이므로 중복 자료구조라 제거 — 동기화 지점이 둘에서 하나로." -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Task 4: Items — 트랜잭션 참가 + 알림 버퍼링

**Files:**
- Create: `server/src/Bun3.Server.Items/ItemInventory.Transaction.cs`
- Modify: `server/src/Bun3.Server.Items/ItemInventory.cs` (ctor `transaction` 인자, 스크래치 필드)
- Modify: `server/src/Bun3.Server.Items/ItemInstance.cs` (`TxToken`, `TxSnapshotIndex` 내부 필드)
- Modify: `server/src/Bun3.Server.Items/ItemInventory.Operations.cs` (변경 전 `Capture`, 알림 경유)
- Modify: `server/src/Bun3.Server.Items/ItemInventory.Tracking.cs` (`OnInstanceChanged` 알림 경유)
- Modify: `server/src/Bun3.Server.Items/ItemInventory.Regen.cs` (리젠 기준 변경 전 `Capture`)
- Modify: `server/src/Bun3.Server.Items/Bun3.Server.Items.csproj` (이미 Core 참조 있음 — 변경 없음)
- Test: `server/tests/Bun3.Server.Tests/ItemInventoryTransactionTests.cs`

**Interfaces:**
- Consumes: `Bun3.Server.Core.Transaction`, `ITransactionParticipant` (Task 1)
- Produces: `ItemInventory<TState>` 생성자 말미 인자 `Transaction? transaction = null`. `ItemInventory<TState> : ITransactionParticipant`.

- [ ] **Step 1: 실패하는 테스트 작성**

`server/tests/Bun3.Server.Tests/ItemInventoryTransactionTests.cs`:

```csharp
using Bun3.Gameplay.Numerics;
using Bun3.Server.Core;
using Bun3.Server.Items;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class ItemInventoryTransactionTests
{
    private sealed class ItemState
    {
        public int Level = 1;
    }

    private ItemCatalog<string> _catalog = null!;
    private ItemId _gold;
    private ItemId _sword;
    private long _nextId;
    private Transaction _transaction = null!;
    private ItemInventory<ItemState> _inventory = null!;
    private int _dirty;
    private readonly List<InventoryChange> _applied = new();

    [SetUp]
    public void SetUp()
    {
        _catalog = new ItemCatalogBuilder<string>()
            .Register("gold", "Gold")
            .Register("sword", "Sword", unstackable: true)
            .Build();
        _gold = _catalog.GetRequired("gold");
        _sword = _catalog.GetRequired("sword");

        _nextId = 0;
        _dirty = 0;
        _applied.Clear();
        _transaction = new Transaction();
        _inventory = new ItemInventory<ItemState>(
            _catalog,
            instanceIdIssuer: () => ++_nextId,
            stateFactory: _ => new ItemState(),
            onDirty: () => _dirty++,
            onApplied: applied =>
            {
                for (var i = 0; i < applied.Length; i++)
                {
                    _applied.Add(applied[i]);
                }
            },
            transaction: _transaction);
    }

    private static string Q(BigNum value) => value.ToString();

    [Test]
    public void Rollback_undoes_a_grant_of_a_new_stack()
    {
        using (_transaction.Begin())
        {
            Assert.That(_inventory.TryAdd(_gold, 100), Is.EqualTo(InventoryError.None));
        }

        Assert.That(_inventory.GetQuantity(_gold).Sign, Is.Zero);
        Assert.That(_inventory.InstanceCount, Is.Zero);
    }

    [Test]
    public void Rollback_restores_a_partially_consumed_stack()
    {
        _inventory.TryAdd(_gold, 100);

        using (_transaction.Begin())
        {
            Assert.That(_inventory.TryRemove(_gold, 30), Is.EqualTo(InventoryError.None));
            Assert.That(Q(_inventory.GetQuantity(_gold)), Is.EqualTo(Q(70)));
        }

        Assert.That(Q(_inventory.GetQuantity(_gold)), Is.EqualTo(Q(100)));
    }

    [Test]
    public void Rollback_restores_a_fully_consumed_stack_instance()
    {
        _inventory.TryAdd(_gold, 100);
        var before = _inventory.InstanceCount;

        using (_transaction.Begin())
        {
            Assert.That(_inventory.TryRemove(_gold, 100), Is.EqualTo(InventoryError.None));
            Assert.That(_inventory.InstanceCount, Is.Zero);
        }

        Assert.That(_inventory.InstanceCount, Is.EqualTo(before));
        Assert.That(Q(_inventory.GetQuantity(_gold)), Is.EqualTo(Q(100)));
    }

    [Test]
    public void Rollback_restores_removed_unstackable_instances()
    {
        _inventory.TryAdd(_sword, 3);

        using (_transaction.Begin())
        {
            Assert.That(_inventory.TryRemove(_sword, 2), Is.EqualTo(InventoryError.None));
        }

        Assert.That(Q(_inventory.GetQuantity(_sword)), Is.EqualTo(Q(3)));
    }

    [Test]
    public void Rollback_restores_flags_and_expiry()
    {
        _inventory.TryAdd(_sword, 1);
        var buffer = new List<ItemInstance<ItemState>>();
        _inventory.CollectInstances(_sword, buffer);
        var instance = buffer[0];

        using (_transaction.Begin())
        {
            instance.Flags = 4u;
            instance.ExpiresAtTicksUtc = 12345;
        }

        Assert.That(instance.Flags, Is.Zero);
        Assert.That(instance.ExpiresAtTicksUtc, Is.Zero);
    }

    [Test]
    public void Commit_keeps_changes()
    {
        using (var scope = _transaction.Begin())
        {
            _inventory.TryAdd(_gold, 100);
            _inventory.TryRemove(_gold, 40);
            scope.Commit();
        }

        Assert.That(Q(_inventory.GetQuantity(_gold)), Is.EqualTo(Q(60)));
    }

    [Test]
    public void Notifications_are_dropped_on_rollback()
    {
        using (_transaction.Begin())
        {
            _inventory.TryAdd(_gold, 100);
            Assert.That(_dirty, Is.Zero, "onDirty must be buffered inside a transaction");
            Assert.That(_applied, Is.Empty, "onApplied must be buffered inside a transaction");
        }

        Assert.That(_dirty, Is.Zero);
        Assert.That(_applied, Is.Empty);
    }

    [Test]
    public void Notifications_fire_once_on_commit_with_all_changes()
    {
        using (var scope = _transaction.Begin())
        {
            _inventory.TryAdd(_gold, 100);
            _inventory.TryRemove(_gold, 40);
            scope.Commit();
        }

        Assert.That(_dirty, Is.EqualTo(1));
        Assert.That(_applied.Count, Is.EqualTo(2));
        Assert.That(Q(_applied[0].Delta), Is.EqualTo(Q(100)));
        Assert.That(Q(_applied[1].Delta), Is.EqualTo(Q(-40)));
        Assert.That(Q(_applied[1].Balance), Is.EqualTo(Q(60)));
    }

    [Test]
    public void Outside_a_transaction_notifications_stay_immediate()
    {
        _inventory.TryAdd(_gold, 100);

        Assert.That(_dirty, Is.EqualTo(1));
        Assert.That(_applied.Count, Is.EqualTo(1));
    }

    [Test]
    public void Rollback_reverts_pending_removals_for_change_tracking()
    {
        _inventory.TryAdd(_gold, 100);
        var drained = new List<ItemChange<ItemState>>();
        _inventory.DrainChanges(drained);
        drained.Clear();

        using (_transaction.Begin())
        {
            _inventory.TryRemove(_gold, 100);
        }

        _inventory.DrainChanges(drained);
        Assert.That(drained, Is.Empty, "a rolled-back removal must not surface as a Removed change");
    }

    [Test]
    public void Rollback_restores_the_regen_basis()
    {
        var start = TimeSpan.TicksPerMinute * 100;
        _inventory.SettleRegen(start);                       // initializes the basis
        var basisBefore = _inventory.GetRegenBasis(_ticket);
        var quantityBefore = Q(_inventory.GetQuantity(_ticket));

        using (_transaction.Begin())
        {
            _inventory.SettleRegen(start + TimeSpan.TicksPerMinute * 30);
            Assert.That(_inventory.GetRegenBasis(_ticket), Is.Not.EqualTo(basisBefore));
        }

        Assert.That(_inventory.GetRegenBasis(_ticket), Is.EqualTo(basisBefore));
        Assert.That(Q(_inventory.GetQuantity(_ticket)), Is.EqualTo(quantityBefore));
    }

    [Test]
    public void Rollback_undoes_a_mixed_batch_atomically()
    {
        _inventory.TryAdd(_gold, 500);
        _inventory.TryAdd(_sword, 2);

        using (_transaction.Begin())
        {
            var tx = _inventory.BeginTransaction();
            tx.Remove(_gold, 200);
            tx.Add(_sword, 1);
            Assert.That(tx.Commit(out _), Is.EqualTo(InventoryError.None));
            Assert.That(Q(_inventory.GetQuantity(_sword)), Is.EqualTo(Q(3)));
        }

        Assert.That(Q(_inventory.GetQuantity(_gold)), Is.EqualTo(Q(500)));
        Assert.That(Q(_inventory.GetQuantity(_sword)), Is.EqualTo(Q(2)));
        Assert.That(_inventory.InstanceCount, Is.EqualTo(3));   // gold singleton + 2 swords
    }
}
```

`SetUp`의 카탈로그에 리젠 정의를 더하고 필드를 선언한다:

```csharp
    private ItemId _ticket;
```

```csharp
        _catalog = new ItemCatalogBuilder<string>()
            .Register("gold", "Gold")
            .Register("sword", "Sword", unstackable: true)
            .Register("ticket", "Ticket", maxCount: 5, regenPeriodTicks: TimeSpan.TicksPerMinute, maxRegen: 5)
            .Build();
        _gold = _catalog.GetRequired("gold");
        _sword = _catalog.GetRequired("sword");
        _ticket = _catalog.GetRequired("ticket");
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj --filter "FullyQualifiedName~ItemInventoryTransactionTests"`
Expected: 컴파일 실패 — `transaction:` 인자 없음

- [ ] **Step 3: 인스턴스에 스냅샷 토큰 추가**

`ItemInstance.cs`의 내부 필드 옆에 추가:

```csharp
        internal int TxToken;
        internal int TxSnapshotIndex = -1;
```

- [ ] **Step 4: 참가자 partial 구현**

`server/src/Bun3.Server.Items/ItemInventory.Transaction.cs`:

```csharp
// ItemInventory partial — transaction participation (snapshot, rollback) and notification buffering.
using System.Collections.Generic;
using Bun3.Gameplay.Numerics;
using Bun3.Server.Core;

namespace Bun3.Server.Items
{
    // Changes apply immediately; a snapshot taken at first touch makes them reversible.
    // While a transaction is active every notification is buffered and flushed on commit.
    public sealed partial class ItemInventory<TState> : ITransactionParticipant
    {
        private readonly struct InstanceSnapshot
        {
            internal InstanceSnapshot(
                ItemInstance<TState> instance, BigNum quantity, uint flags, long expiresAt,
                bool isNew, bool changed, TState state, bool stateSnapshotted)
            {
                Instance = instance;
                Quantity = quantity;
                Flags = flags;
                ExpiresAt = expiresAt;
                IsNew = isNew;
                Changed = changed;
                State = state;
                StateSnapshotted = stateSnapshotted;
            }

            internal ItemInstance<TState> Instance { get; }
            internal BigNum Quantity { get; }
            internal uint Flags { get; }
            internal long ExpiresAt { get; }
            internal bool IsNew { get; }
            internal bool Changed { get; }
            internal TState State { get; }
            internal bool StateSnapshotted { get; }

            internal InstanceSnapshot WithState(TState state) =>
                new InstanceSnapshot(Instance, Quantity, Flags, ExpiresAt, IsNew, Changed, state, true);
        }

        private readonly List<ItemInstance<TState>> _txCreated = new List<ItemInstance<TState>>();
        private readonly List<ItemInstance<TState>> _txRemoved = new List<ItemInstance<TState>>();
        private readonly List<InstanceSnapshot> _txSnapshots = new List<InstanceSnapshot>();
        private readonly List<ItemId> _txRegenItems = new List<ItemId>();
        private readonly List<long> _txRegenBasis = new List<long>();
        private readonly List<bool> _txRegenExisted = new List<bool>();
        private int _txSnapshotToken;
        private int _txRemovedMark;
        private bool _txHasChangesMark;
        private bool _txEnlisted;
        private bool _txDirtyPending;

        /// <summary>Whether this inventory is currently taking part in a transaction.</summary>
        private bool InTransaction => _transaction != null && _transaction.IsActive;

        /// <summary>Enlists on the first change of a transaction and marks the snapshot baseline.</summary>
        private void EnsureEnlisted()
        {
            if (_txEnlisted || !InTransaction)
            {
                return;
            }

            _transaction!.Enlist(this);
            _txEnlisted = true;
            _txSnapshotToken++;
            _txCreated.Clear();
            _txRemoved.Clear();
            _txSnapshots.Clear();
            _txRegenItems.Clear();
            _txRegenBasis.Clear();
            _txRegenExisted.Clear();
            _txRemovedMark = _removed.Count;
            _txHasChangesMark = _hasChanges;
            _txDirtyPending = false;
        }

        /// <summary>Snapshots an instance before its first change in the current transaction.</summary>
        private void Capture(ItemInstance<TState> instance)
        {
            EnsureEnlisted();
            if (!_txEnlisted || instance.TxToken == _txSnapshotToken)
            {
                return;
            }

            instance.TxToken = _txSnapshotToken;
            instance.TxSnapshotIndex = _txSnapshots.Count;
            _txSnapshots.Add(new InstanceSnapshot(
                instance, instance.Quantity, instance.Flags, instance.ExpiresAtTicksUtc,
                instance.IsNew, instance.Changed, instance.State, false));
        }

        /// <summary>Records a newly created instance so a rollback can drop it.</summary>
        private void CaptureCreated(ItemInstance<TState> instance)
        {
            EnsureEnlisted();
            if (!_txEnlisted)
            {
                return;
            }

            instance.TxToken = _txSnapshotToken;   // Created instances are dropped wholesale, never restored.
            _txCreated.Add(instance);
        }

        /// <summary>Records a removed instance so a rollback can re-index it.</summary>
        private void CaptureRemoved(ItemInstance<TState> instance)
        {
            EnsureEnlisted();
            if (_txEnlisted)
            {
                _txRemoved.Add(instance);
            }
        }

        /// <summary>Snapshots a regen basis entry before it changes.</summary>
        private void CaptureRegenBasis(ItemId item)
        {
            EnsureEnlisted();
            if (!_txEnlisted)
            {
                return;
            }

            for (var i = 0; i < _txRegenItems.Count; i++)
            {
                if (_txRegenItems[i] == item)
                {
                    return;
                }
            }

            var existed = _regenBasis.TryGetValue(item, out var basis);
            _txRegenItems.Add(item);
            _txRegenBasis.Add(basis);
            _txRegenExisted.Add(existed);
        }

        /// <summary>Raises onDirty, or buffers it until commit while a transaction is active.</summary>
        private void NotifyDirty()
        {
            if (_txEnlisted)
            {
                _txDirtyPending = true;
                return;
            }

            _onDirty?.Invoke();
        }

        /// <inheritdoc />
        void ITransactionParticipant.OnCommit()
        {
            _txEnlisted = false;
            if (_txDirtyPending)
            {
                _txDirtyPending = false;
                _onDirty?.Invoke();
            }

            FlushApplied();
        }

        /// <inheritdoc />
        void ITransactionParticipant.OnRollback()
        {
            _txEnlisted = false;
            _txDirtyPending = false;
            _appliedCount = 0;

            for (var i = _txCreated.Count - 1; i >= 0; i--)
            {
                var created = _txCreated[i];
                _instances.Remove(created.InstanceId);
                Unindex(created);
                created._owner = null;
            }

            for (var i = _txSnapshots.Count - 1; i >= 0; i--)
            {
                var snapshot = _txSnapshots[i];
                var instance = snapshot.Instance;
                instance.RestoreForRollback(
                    snapshot.Quantity, snapshot.Flags, snapshot.ExpiresAt,
                    snapshot.IsNew, snapshot.Changed, snapshot.State);
                instance.TxSnapshotIndex = -1;
            }

            for (var i = _txRemoved.Count - 1; i >= 0; i--)
            {
                var removed = _txRemoved[i];
                removed._owner = this;
                _instances[removed.InstanceId] = removed;
                Index(removed);
            }

            for (var i = 0; i < _txRegenItems.Count; i++)
            {
                if (_txRegenExisted[i])
                {
                    _regenBasis[_txRegenItems[i]] = _txRegenBasis[i];
                }
                else
                {
                    _regenBasis.Remove(_txRegenItems[i]);
                }
            }

            if (_removed.Count > _txRemovedMark)
            {
                _removed.RemoveRange(_txRemovedMark, _removed.Count - _txRemovedMark);
            }

            _hasChanges = _txHasChangesMark;
            _txCreated.Clear();
            _txRemoved.Clear();
            _txSnapshots.Clear();
            _txRegenItems.Clear();
            _txRegenBasis.Clear();
            _txRegenExisted.Clear();
        }
    }
}
```

`ItemInstance.cs`에서 `State`를 백킹 필드로 전환한다 — 롤백 복원이 대입해야 하기 때문이다. 다른 필드 선언 옆에:

```csharp
        private TState _state;
```

자동 프로퍼티를 읽기 전용 프로퍼티로 바꾼다(setter는 Task 5에서 추가한다):

```csharp
        /// <summary>Game-owned state — the framework does not interpret it.</summary>
        public TState State => _state;
```

생성자의 `State = state;`를 `_state = state;`로 바꾼다.

그리고 롤백 복원 메서드를 추가한다(프레임워크 전용):

```csharp
        internal void RestoreForRollback(BigNum quantity, uint flags, long expiresAt, bool isNew, bool changed, TState state)
        {
            Quantity = quantity;
            _flags = flags;
            _expiresAtTicksUtc = expiresAt;
            IsNew = isNew;
            Changed = changed;
            _state = state;
        }
```

- [ ] **Step 5: 생성자와 변경 경로 배선**

`ItemInventory.cs` 생성자에 인자와 필드를 추가한다(`log`, `logLabel` 다음):

```csharp
        private readonly Transaction? _transaction;
```

```csharp
        /// <param name="transaction">Session-wide transaction (<see cref="Transaction"/>) — when
        /// set, changes made while it is open are reversible and notifications are buffered until
        /// commit. Nothing is snapshotted when no transaction is open.</param>
        public ItemInventory(
            ItemCatalog catalog,
            Func<long> instanceIdIssuer,
            Func<ItemId, TState> stateFactory,
            int capacity = 0,
            Action? onDirty = null,
            uint removeBlockingFlags = 0,
            InventoryAppliedHandler? onApplied = null,
            ActionLog? log = null,
            string? logLabel = null,
            Transaction? transaction = null)
```

```csharp
            _transaction = transaction;
```

변경 경로에 캡처를 끼운다:

- `CreateInstance` — `_instances.Add` 뒤, `Index(instance)` 뒤에 `CaptureCreated(instance);`
- `RemoveInstance` — 메서드 첫 줄에 `CaptureRemoved(instance);`
- `ApplyGrant`의 싱글턴 병합 — `singleton.Quantity += amount;` **앞에** `Capture(singleton);`
- `ApplyConsume`의 싱글턴 부분 소모 — `singleton.Quantity -= amount;` **앞에** `Capture(singleton);`
- `ApplyOp`의 인스턴스 지정 부분 소모 — `target.Quantity -= resolved;` **앞에** `Capture(target);`
- `ItemInstance.Flags` / `ExpiresAtTicksUtc` setter — 값 대입 **앞에** `_owner?.CaptureForRollback(this);` (아래 내부 통로 추가)
- `ItemInventory.Regen.cs` — `_regenBasis[item] = ...` 대입 **앞에** `CaptureRegenBasis(item);` (3곳: 목표선 도달 재설정, 지급 성공 후 전진, 미지급 갱신)

`ItemInventory.Transaction.cs`에 인스턴스에서 부를 내부 통로를 추가한다:

```csharp
        internal void CaptureForRollback(ItemInstance<TState> instance) => Capture(instance);
```

알림 경유: `CommitOps` 성공 경로의 `_onDirty?.Invoke();`와 `OnInstanceChanged`의 `_onDirty?.Invoke();`를 `NotifyDirty();`로 바꾼다. `TryTransfer`의 양쪽 알림도 각 인벤토리의 `NotifyDirty()`로 바꾼다.

`onApplied`·ActionLog 플러시를 커밋으로 미룬다. `ItemInventory.Operations.cs`의 `CommitOps` 말미를 바꾼다:

```csharp
            _hasChanges = true;
            NotifyDirty();
            if (!_txEnlisted)
            {
                FlushApplied();
            }

            return InventoryError.None;
```

그리고 `_appliedCount = 0;` 초기화를 적용 패스 앞에서 **트랜잭션 밖일 때만** 하도록 바꾼다:

```csharp
            // Apply pass — applied in order, in a state where failure is impossible.
            if (!_txEnlisted)
            {
                _appliedCount = 0;
            }
```

`FlushApplied`를 `ItemInventory.Operations.cs`에 추가하고 기존 발화 코드를 옮긴다:

```csharp
        /// <summary>Delivers the accumulated applied changes to onApplied and the action log,
        /// then resets the buffer. Deferred to commit while a transaction is active.</summary>
        private void FlushApplied()
        {
            if (_appliedCount == 0)
            {
                return;
            }

            _onApplied?.Invoke(new ReadOnlySpan<InventoryChange>(_applied, 0, _appliedCount));
            LogAppliedChanges();
            _appliedCount = 0;
        }
```

`RecordApplied`의 조기 반환 조건은 그대로 둔다(`_onApplied == null && _log == null`).

- [ ] **Step 6: 통과 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj`
Expected: 신규 12건 + 기존 전건 PASS, 경고 0

- [ ] **Step 7: 커밋**

```bash
git add server/src/Bun3.Server.Items server/tests/Bun3.Server.Tests/ItemInventoryTransactionTests.cs
git commit -m "✨ Items 트랜잭션 참가: 즉시 적용 + 스냅샷 롤백, 알림 커밋 시 발화" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Task 5: Items — TState 스냅샷 2경로와 `MarkChanged` 가드

**Files:**
- Create: `server/src/Bun3.Server.Items/ItemStateScope{TState}.cs`
- Modify: `server/src/Bun3.Server.Items/ItemInstance.cs` (`State` setter, `BeginStateChange`, `MarkChanged` 가드)
- Modify: `server/src/Bun3.Server.Items/ItemInventory.cs` (ctor `snapshotState` 인자)
- Modify: `server/src/Bun3.Server.Items/ItemInventory.Transaction.cs` (깊은 스냅샷 승격)
- Test: `server/tests/Bun3.Server.Tests/ItemStateRollbackTests.cs`

**Interfaces:**
- Consumes: `Capture(ItemInstance<TState>)`, `InstanceSnapshot.WithState` (Task 4)
- Produces:
  - `ItemInstance<TState>.State { get; set; }`
  - `ItemStateScope<TState> ItemInstance<TState>.BeginStateChange()`
  - `readonly struct ItemStateScope<TState> : IDisposable { TState State { get; } void Dispose(); }`
  - `ItemInventory<TState>` 생성자 인자 `Func<TState, TState>? snapshotState = null`

- [ ] **Step 1: 실패하는 테스트 작성**

`server/tests/Bun3.Server.Tests/ItemStateRollbackTests.cs`:

```csharp
using Bun3.Server.Core;
using Bun3.Server.Items;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class ItemStateRollbackTests
{
    private sealed class MutableState
    {
        public int Level;
        public List<int> Options = new();

        public MutableState Clone() => new MutableState { Level = Level, Options = new List<int>(Options) };
    }

    private readonly record struct ValueState(int Level);

    private ItemCatalog<string> _catalog = null!;
    private ItemId _sword;
    private Transaction _transaction = null!;

    [SetUp]
    public void SetUp()
    {
        _catalog = new ItemCatalogBuilder<string>()
            .Register("sword", "Sword", unstackable: true)
            .Build();
        _sword = _catalog.GetRequired("sword");
        _transaction = new Transaction();
    }

    private ItemInventory<T> Inventory<T>(Func<ItemId, T> factory, Func<T, T>? snapshot = null)
    {
        long next = 0;
        return new ItemInventory<T>(
            _catalog,
            instanceIdIssuer: () => ++next,
            stateFactory: factory,
            transaction: _transaction,
            snapshotState: snapshot);
    }

    private static ItemInstance<T> Single<T>(ItemInventory<T> inventory, ItemId item)
    {
        var buffer = new List<ItemInstance<T>>();
        inventory.CollectInstances(item, buffer);
        return buffer[0];
    }

    [Test]
    public void Value_state_rolls_back_through_the_setter()
    {
        var inventory = Inventory<ValueState>(_ => new ValueState(1));
        inventory.TryAdd(_sword, 1);
        var instance = Single(inventory, _sword);

        using (_transaction.Begin())
        {
            instance.State = instance.State with { Level = 9 };
            Assert.That(instance.State.Level, Is.EqualTo(9));
        }

        Assert.That(instance.State.Level, Is.EqualTo(1));
    }

    [Test]
    public void Value_state_setter_commits()
    {
        var inventory = Inventory<ValueState>(_ => new ValueState(1));
        inventory.TryAdd(_sword, 1);
        var instance = Single(inventory, _sword);

        using (var scope = _transaction.Begin())
        {
            instance.State = instance.State with { Level = 9 };
            scope.Commit();
        }

        Assert.That(instance.State.Level, Is.EqualTo(9));
    }

    [Test]
    public void Mutable_state_rolls_back_through_the_change_scope()
    {
        var inventory = Inventory<MutableState>(_ => new MutableState { Level = 1 }, s => s.Clone());
        inventory.TryAdd(_sword, 1);
        var instance = Single(inventory, _sword);

        using (_transaction.Begin())
        {
            using (var scope = instance.BeginStateChange())
            {
                scope.State.Level = 9;
                scope.State.Options.Add(42);
            }

            Assert.That(instance.State.Level, Is.EqualTo(9));
        }

        Assert.That(instance.State.Level, Is.EqualTo(1));
        Assert.That(instance.State.Options, Is.Empty);
    }

    [Test]
    public void Change_scope_marks_the_instance_changed()
    {
        var inventory = Inventory<MutableState>(_ => new MutableState(), s => s.Clone());
        inventory.TryAdd(_sword, 1);
        var drained = new List<ItemChange<MutableState>>();
        inventory.DrainChanges(drained);
        drained.Clear();
        var instance = Single(inventory, _sword);

        using (var scope = instance.BeginStateChange())
        {
            scope.State.Level = 3;
        }

        inventory.DrainChanges(drained);
        Assert.That(drained.Count, Is.EqualTo(1));
        Assert.That(drained[0].Kind, Is.EqualTo(ItemChangeKind.Updated));
    }

    [Test]
    public void Change_scope_without_snapshot_delegate_throws_inside_a_transaction()
    {
        var inventory = Inventory<MutableState>(_ => new MutableState());
        inventory.TryAdd(_sword, 1);
        var instance = Single(inventory, _sword);

        using (_transaction.Begin())
        {
            Assert.That(() => instance.BeginStateChange(), Throws.TypeOf<InvalidOperationException>());
        }
    }

    [Test]
    public void Change_scope_without_snapshot_delegate_works_outside_a_transaction()
    {
        var inventory = Inventory<MutableState>(_ => new MutableState());
        inventory.TryAdd(_sword, 1);
        var instance = Single(inventory, _sword);

        using (var scope = instance.BeginStateChange())
        {
            scope.State.Level = 5;
        }

        Assert.That(instance.State.Level, Is.EqualTo(5));
    }

    [Test]
    public void MarkChanged_inside_a_transaction_throws()
    {
        var inventory = Inventory<MutableState>(_ => new MutableState(), s => s.Clone());
        inventory.TryAdd(_sword, 1);
        var instance = Single(inventory, _sword);

        using (_transaction.Begin())
        {
            Assert.That(() => instance.MarkChanged(), Throws.TypeOf<InvalidOperationException>());
        }
    }

    [Test]
    public void MarkChanged_outside_a_transaction_still_works()
    {
        var inventory = Inventory<MutableState>(_ => new MutableState());
        inventory.TryAdd(_sword, 1);
        var drained = new List<ItemChange<MutableState>>();
        inventory.DrainChanges(drained);
        drained.Clear();
        var instance = Single(inventory, _sword);

        instance.State.Level = 7;
        instance.MarkChanged();

        inventory.DrainChanges(drained);
        Assert.That(drained.Count, Is.EqualTo(1));
        Assert.That(drained[0].Kind, Is.EqualTo(ItemChangeKind.Updated));
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj --filter "FullyQualifiedName~ItemStateRollbackTests"`
Expected: 컴파일 실패 — `snapshotState` 인자, `BeginStateChange`, `State` setter 미정의

- [ ] **Step 3: `ItemStateScope<TState>` 작성**

`server/src/Bun3.Server.Items/ItemStateScope{TState}.cs`:

```csharp
using System;

namespace Bun3.Server.Items
{
    /// <summary>
    /// Mutation scope for game-owned instance state. Entering the scope is the moment right before
    /// the change, which is where a transaction takes its rollback snapshot; disposing marks the
    /// instance changed. Use this instead of mutating <see cref="ItemInstance{TState}.State"/> in
    /// place when the state type is mutable — games treating state as a value assign
    /// <see cref="ItemInstance{TState}.State"/> directly instead.
    /// </summary>
    /// <typeparam name="TState">Game-defined instance state type.</typeparam>
    public readonly struct ItemStateScope<TState> : IDisposable
    {
        private readonly ItemInstance<TState>? _instance;

        internal ItemStateScope(ItemInstance<TState> instance)
        {
            _instance = instance;
        }

        /// <summary>The state to mutate.</summary>
        public TState State => _instance!.State;

        /// <summary>Marks the instance changed, feeding change tracking and onDirty.</summary>
        public void Dispose() => _instance?.MarkChangedInternal();
    }
}
```

- [ ] **Step 4: `ItemInstance` 배선**

`ItemInstance.cs`:

```csharp
        /// <summary>Game-owned state — the framework does not interpret it. Assign a replacement
        /// rather than mutating in place: the previous value is what a transaction restores on
        /// rollback. For mutable state types use <see cref="BeginStateChange"/> instead.
        /// The setter feeds change tracking automatically.</summary>
        public TState State
        {
            get => _state;
            set
            {
                _owner?.CaptureForRollback(this);
                _state = value;
                MarkChangedInternal();
            }
        }

        /// <summary>Opens a mutation scope for a mutable state type. Inside a transaction the
        /// framework deep-copies the state through the inventory's snapshotState delegate here, so
        /// the change is reversible.</summary>
        /// <exception cref="InvalidOperationException">Inside a transaction when the inventory was
        /// created without a snapshotState delegate.</exception>
        public ItemStateScope<TState> BeginStateChange()
        {
            _owner?.CaptureStateForRollback(this);
            return new ItemStateScope<TState>(this);
        }

        /// <summary>Call after mutating <see cref="State"/> in place outside a transaction — feeds
        /// change tracking (Updated) and the inventory's onDirty. Ignored after removal from the
        /// inventory.</summary>
        /// <exception cref="InvalidOperationException">Inside an active transaction — in-place
        /// mutation is not rollback-safe.</exception>
        public void MarkChanged()
        {
            _owner?.ThrowIfTransactionActive();
            MarkChangedInternal();
        }

        internal void MarkChangedInternal()
        {
            Changed = true;
            _owner?.OnInstanceChanged();
        }
```

`Flags`·`ExpiresAtTicksUtc` setter는 `MarkChanged()` 대신 캡처 후 `MarkChangedInternal()`을 부른다:

```csharp
        public uint Flags
        {
            get => _flags;
            set
            {
                if (_flags != value)
                {
                    _owner?.CaptureForRollback(this);
                    _flags = value;
                    MarkChangedInternal();
                }
            }
        }
```

`ExpiresAtTicksUtc`도 같은 형태로 바꾼다.

- [ ] **Step 5: 인벤토리 배선**

`ItemInventory.cs` 생성자에 인자와 필드를 추가한다(`transaction` 다음):

```csharp
        private readonly Func<TState, TState>? _snapshotState;
```

```csharp
        /// <param name="snapshotState">Deep-copy delegate for mutable state types — called when a
        /// <see cref="ItemInstance{TState}.BeginStateChange"/> scope opens inside a transaction.
        /// Leave null when state is treated as a value (assigned rather than mutated).</param>
            Transaction? transaction = null,
            Func<TState, TState>? snapshotState = null)
```

```csharp
            _snapshotState = snapshotState;
```

`ItemInventory.Transaction.cs`에 깊은 스냅샷 승격과 가드를 추가한다:

```csharp
        internal void CaptureStateForRollback(ItemInstance<TState> instance)
        {
            if (!InTransaction)
            {
                return;
            }

            if (_snapshotState == null)
            {
                throw new InvalidOperationException(
                    "BeginStateChange inside a transaction requires a snapshotState delegate — pass one to the inventory, or assign State instead of mutating in place.");
            }

            Capture(instance);
            var index = instance.TxSnapshotIndex;
            if (index >= 0 && !_txSnapshots[index].StateSnapshotted)
            {
                _txSnapshots[index] = _txSnapshots[index].WithState(_snapshotState(instance.State));
            }
        }

        internal void ThrowIfTransactionActive()
        {
            if (InTransaction)
            {
                throw new InvalidOperationException(
                    "MarkChanged is not available inside a transaction — assign State or use BeginStateChange so the change can be rolled back.");
            }
        }
```

- [ ] **Step 6: 통과 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj`
Expected: 신규 8건 + 기존 전건 PASS, 경고 0

- [ ] **Step 7: 커밋**

```bash
git add server/src/Bun3.Server.Items server/tests/Bun3.Server.Tests/ItemStateRollbackTests.cs
git commit -m "✨ Items TState 롤백 2경로: State setter + BeginStateChange 스코프, 제자리 변경 가드" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Task 6: Achievements — `AchievementId`/`AchievementTag` 도입과 조회·예외 정합

**Files:**
- Create: `server/src/Bun3.Server.Achievements/AchievementId.cs`
- Create: `server/src/Bun3.Server.Achievements/AchievementTag.cs`
- Create: `server/src/Bun3.Server.Achievements/AchievementCatalogException.cs`
- Modify: `server/src/Bun3.Server.Achievements/AchievementCatalog.cs`
- Modify: `server/src/Bun3.Server.Achievements/AchievementManager.cs`
- Modify: `server/tests/Bun3.Server.Tests/AchievementTests.cs`

**Interfaces:**
- Consumes: 없음
- Produces:
  - `readonly struct AchievementId : IEquatable<AchievementId> { static readonly AchievementId None; bool IsNone { get; } }`
  - `readonly struct AchievementTag : IEquatable<AchievementTag> { static readonly AchievementTag None; bool IsNone { get; } }`
  - `class AchievementCatalogException : Exception`
  - `AchievementCatalog<TDef>`: `bool TryGet(string, out AchievementId)`, `AchievementId GetRequired(string)`, `bool TryGetTag(string, out AchievementTag)`, `AchievementTag GetRequiredTag(string)`, `TDef GetDefinition(AchievementId)`, `ReadOnlySpan<AchievementId> GetIndicesByTag(AchievementTag)`, `bool Contains(AchievementId)`
  - `AchievementManager<TDef>`: 전 메서드가 `int index` → `AchievementId id`, `int tagIndex` → `AchievementTag tag`

- [ ] **Step 1: 실패하는 테스트 작성**

`AchievementTests.cs`의 카탈로그 테스트를 새 API로 바꾼다:

```csharp
    [Test]
    public void Catalog_interning_id_and_definition_lookup()
    {
        var catalog = Catalog(new GameDef("a", 10), new GameDef("b", 5, repeatable: true));

        Assert.That(catalog.Count, Is.EqualTo(2));
        Assert.That(catalog.TryGet("a", out var a), Is.True);
        Assert.That(catalog.GetDefinition(a).Id, Is.EqualTo("a"));
        Assert.That(catalog.TryGet("missing", out var missing), Is.False);
        Assert.That(missing.IsNone, Is.True);
        Assert.That(() => catalog.GetRequired("missing"), Throws.TypeOf<AchievementCatalogException>());
        Assert.That(catalog.GetDefinition(catalog.GetRequired("b")).Id, Is.EqualTo("b"));
    }

    [Test]
    public void Catalog_none_and_out_of_range_identifiers_are_rejected()
    {
        var catalog = Catalog(new GameDef("a", 10));

        Assert.That(catalog.Contains(AchievementId.None), Is.False);
        Assert.That(() => catalog.GetDefinition(AchievementId.None), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Tag_identifier_is_distinct_from_achievement_identifier()
    {
        var catalog = Catalog(new GameDef("a", 10, tags: new[] { "daily" }));

        Assert.That(catalog.TryGetTag("daily", out var tag), Is.True);
        Assert.That(catalog.GetIndicesByTag(tag).Length, Is.EqualTo(1));
        Assert.That(catalog.GetIndicesByTag(tag)[0], Is.EqualTo(catalog.GetRequired("a")));
        Assert.That(() => catalog.GetRequiredTag("missing"), Throws.TypeOf<AchievementCatalogException>());
    }
```

나머지 테스트의 `catalog.GetIndex("x")` 호출은 전부 `catalog.GetRequired("x")`로, 로컬 변수 타입은 `AchievementId`로 바꾼다(14곳).

- [ ] **Step 2: 실패 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj --filter "FullyQualifiedName~AchievementTests"`
Expected: 컴파일 실패 — `AchievementId`, `TryGet`, `AchievementCatalogException` 미정의

- [ ] **Step 3: 식별자 구조체와 예외 작성**

`server/src/Bun3.Server.Achievements/AchievementId.cs`:

```csharp
using System;

namespace Bun3.Server.Achievements
{
    /// <summary>
    /// Achievement identifier — a dense index interned by the catalog at startup. Games cache these
    /// once at startup and use only identifiers on hot paths; the string id never appears at
    /// runtime. Stored as index + 1 so <c>default</c> is <see cref="None"/>. Distinct from
    /// <see cref="AchievementTag"/> so the two can never be passed interchangeably.
    /// </summary>
    public readonly struct AchievementId : IEquatable<AchievementId>
    {
        private readonly int _indexPlusOne;

        internal AchievementId(int index)
        {
            _indexPlusOne = index + 1;
        }

        /// <summary>The absent identifier.</summary>
        public static readonly AchievementId None = default;

        /// <summary>Whether this is <see cref="None"/>.</summary>
        public bool IsNone => _indexPlusOne == 0;

        /// <summary>Catalog-internal index. -1 for None.</summary>
        internal int Index => _indexPlusOne - 1;

        /// <inheritdoc />
        public bool Equals(AchievementId other) => _indexPlusOne == other._indexPlusOne;

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is AchievementId other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => _indexPlusOne;

        /// <summary>Equality operator.</summary>
        public static bool operator ==(AchievementId a, AchievementId b) => a.Equals(b);

        /// <summary>Inequality operator.</summary>
        public static bool operator !=(AchievementId a, AchievementId b) => !a.Equals(b);

        /// <inheritdoc />
        public override string ToString() => IsNone ? "AchievementId.None" : $"AchievementId({Index})";
    }
}
```

`AchievementTag.cs`는 같은 형태로 이름만 `AchievementTag`, 문서는 *"Routing tag identifier — interned by the catalog at startup"*, `ToString`은 `AchievementTag(...)`로 작성한다.

`AchievementCatalogException.cs`:

```csharp
using System;

namespace Bun3.Server.Achievements
{
    /// <summary>Catalog build or lookup failure — a startup-blocking data error.</summary>
    public class AchievementCatalogException : Exception
    {
        /// <summary>Creates the exception with a message.</summary>
        public AchievementCatalogException(string message) : base(message)
        {
        }

        /// <summary>Creates the exception with a message and an inner exception.</summary>
        public AchievementCatalogException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
```

- [ ] **Step 4: 카탈로그 개명**

`AchievementCatalog.cs`:

- `_indicesByTag` 타입을 `AchievementId[][]`로 바꾸고 빌드 루프에서 `new AchievementId(i)`를 담는다.
- `GetIndex`/`TryGetIndex` → `GetRequired`/`TryGet`, `GetTagIndex`/`TryGetTagIndex` → `GetRequiredTag`/`TryGetTag`. 실패는 `AchievementCatalogException`.
- `GetDefinition(int)` → `GetDefinition(AchievementId)`, 범위 검사 추가.
- `Contains(AchievementId)` 추가.
- `GetIndicesByTag(AchievementTag)`로 시그니처 변경 + 범위 검사.

```csharp
        /// <summary>Returns the definition. Throws on an identifier from another catalog or None.</summary>
        /// <exception cref="ArgumentOutOfRangeException">When the identifier is not valid here.</exception>
        public TDef GetDefinition(AchievementId id)
        {
            if (!Contains(id))
            {
                throw new ArgumentOutOfRangeException(nameof(id), "Not an identifier of this catalog.");
            }

            return _definitions[id.Index];
        }

        /// <summary>Whether the identifier is valid for this catalog (None or out of range is false).</summary>
        public bool Contains(AchievementId id) => (uint)id.Index < (uint)_definitions.Length;

        /// <summary>Looks up by string id. Returns false if absent; <paramref name="id"/> is None.</summary>
        public bool TryGet(string id, out AchievementId achievement)
        {
            if (id != null && _indexById.TryGetValue(id, out var index))
            {
                achievement = new AchievementId(index);
                return true;
            }

            achievement = AchievementId.None;
            return false;
        }

        /// <summary>Looks up by string id — call once at startup and cache.</summary>
        /// <exception cref="AchievementCatalogException">When the id is not in the catalog.</exception>
        public AchievementId GetRequired(string id)
        {
            if (!TryGet(id, out var achievement))
            {
                throw new AchievementCatalogException($"Achievement id not in catalog: '{id}'");
            }

            return achievement;
        }
```

`TryGetTag`/`GetRequiredTag`도 같은 형태(`AchievementTag`, `_tagIndexByName`, 메시지 `"Achievement tag not in catalog: '{tag}'"`).

생성자의 검증 실패도 `ArgumentException` → `AchievementCatalogException`으로 바꾼다(빈/중복 id, `Target <= 0`, 상한 초과, 가용성 범위, 빈/중복 태그, null 정의). `definitions == null`은 `ArgumentNullException` 유지.

- [ ] **Step 5: 매니저 개명**

`AchievementManager.cs`의 모든 `int index` → `AchievementId id`, `int tagIndex` → `AchievementTag tag`. 내부 배열 접근은 `_states[id.Index]`. 진입점마다 범위 검사를 넣는다:

```csharp
        private ref AchievementState StateOf(AchievementId id)
        {
            if (!_catalog.Contains(id))
            {
                throw new ArgumentOutOfRangeException(nameof(id), "Not an identifier of this catalog.");
            }

            return ref _states[id.Index];
        }
```

`OnCompleted`의 시그니처는 `Action<AchievementId, TDef, int>?`가 된다.
`IncreaseByTag`의 `indices` 지역 변수 타입은 `ReadOnlySpan<AchievementId>`가 되고, 비트마스크 루프는 그대로다(`indices[i]`가 `AchievementId`).

- [ ] **Step 6: 통과 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj`
Expected: 전건 PASS, 경고 0

- [ ] **Step 7: 커밋**

```bash
git add server/src/Bun3.Server.Achievements server/tests/Bun3.Server.Tests/AchievementTests.cs
git commit -m "♻️ Achievements 식별자 타입화: AchievementId·AchievementTag, 조회·예외를 Items 관례로" -m "생 int 두 종류가 서로 바꿔 넣어도 컴파일되던 오호출 계열을 타입으로 차단." -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Task 7: Achievements — Core 참조와 트랜잭션 참가

**Files:**
- Modify: `server/src/Bun3.Server.Achievements/Bun3.Server.Achievements.csproj` (Core `ProjectReference`)
- Modify: `server/src/Bun3.Server.Achievements/AchievementManager.cs` (partial 전환, ctor 인자, 알림 경유)
- Create: `server/src/Bun3.Server.Achievements/AchievementManager.Transaction.cs`
- Test: `server/tests/Bun3.Server.Tests/AchievementTransactionTests.cs`

**Interfaces:**
- Consumes: `Bun3.Server.Core.Transaction`, `ITransactionParticipant` (Task 1); `AchievementId` (Task 6)
- Produces: `AchievementManager<TDef>` 생성자 인자 `Transaction? transaction = null`. `AchievementManager<TDef> : ITransactionParticipant`.

- [ ] **Step 1: 실패하는 테스트 작성**

`server/tests/Bun3.Server.Tests/AchievementTransactionTests.cs`:

```csharp
using Bun3.Server.Achievements;
using Bun3.Server.Core;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class AchievementTransactionTests
{
    private AchievementCatalog<AchievementDefinition> _catalog = null!;
    private AchievementId _kill;
    private AchievementId _daily;
    private Transaction _transaction = null!;
    private AchievementManager<AchievementDefinition> _achievements = null!;
    private int _dirty;
    private readonly List<string> _completed = new();

    [SetUp]
    public void SetUp()
    {
        _catalog = new AchievementCatalog<AchievementDefinition>(new[]
        {
            new AchievementDefinition("kill", 10),
            new AchievementDefinition("daily", 5, repeatable: true),
        });
        _kill = _catalog.GetRequired("kill");
        _daily = _catalog.GetRequired("daily");

        _dirty = 0;
        _completed.Clear();
        _transaction = new Transaction();
        _achievements = new AchievementManager<AchievementDefinition>(
            _catalog,
            onDirty: () => _dirty++,
            transaction: _transaction)
        {
            OnCompleted = (id, def, count) => _completed.Add($"{def.Id}:{count}"),
        };
    }

    [Test]
    public void Rollback_restores_progress_and_completion_counters()
    {
        _achievements.Increase(_kill, 4);

        using (_transaction.Begin())
        {
            Assert.That(_achievements.Increase(_kill, 6), Is.EqualTo(1));
            Assert.That(_achievements.GetStatus(_kill), Is.EqualTo(AchievementStatus.Completed));
        }

        Assert.That(_achievements.GetState(_kill).Progress, Is.EqualTo(4));
        Assert.That(_achievements.GetState(_kill).CompletedCount, Is.Zero);
        Assert.That(_achievements.GetStatus(_kill), Is.EqualTo(AchievementStatus.Active));
    }

    [Test]
    public void Rollback_restores_a_claim()
    {
        _achievements.Increase(_daily, 5);
        var before = _achievements.GetState(_daily);

        using (_transaction.Begin())
        {
            Assert.That(_achievements.TryClaim(_daily), Is.True);
        }

        var after = _achievements.GetState(_daily);
        Assert.That(after.ClaimedCount, Is.EqualTo(before.ClaimedCount));
        Assert.That(after.Progress, Is.EqualTo(before.Progress));
        Assert.That(_achievements.GetClaimableCount(_daily), Is.EqualTo(1));
    }

    [Test]
    public void Rollback_restores_availability_transitions()
    {
        using (_transaction.Begin())
        {
            Assert.That(_achievements.Lock(_kill), Is.True);
        }

        Assert.That(_achievements.GetStatus(_kill), Is.EqualTo(AchievementStatus.Active));
    }

    [Test]
    public void Commit_keeps_changes_and_fires_hooks_once()
    {
        using (var scope = _transaction.Begin())
        {
            _achievements.Increase(_kill, 10);
            Assert.That(_dirty, Is.Zero, "onDirty must be buffered");
            Assert.That(_completed, Is.Empty, "OnCompleted must be buffered");
            scope.Commit();
        }

        Assert.That(_dirty, Is.EqualTo(1));
        Assert.That(_completed, Is.EqualTo(new[] { "kill:1" }));
        Assert.That(_achievements.GetState(_kill).CompletedCount, Is.EqualTo(1));
    }

    [Test]
    public void Rollback_drops_buffered_hooks()
    {
        using (_transaction.Begin())
        {
            _achievements.Increase(_kill, 10);
        }

        Assert.That(_dirty, Is.Zero);
        Assert.That(_completed, Is.Empty);
    }

    [Test]
    public void Outside_a_transaction_hooks_stay_immediate()
    {
        _achievements.Increase(_kill, 10);

        Assert.That(_dirty, Is.EqualTo(1));
        Assert.That(_completed, Is.EqualTo(new[] { "kill:1" }));
    }

    [Test]
    public void Multiple_achievements_roll_back_together()
    {
        using (_transaction.Begin())
        {
            _achievements.Increase(_kill, 3);
            _achievements.Increase(_daily, 2);
        }

        Assert.That(_achievements.GetState(_kill).Progress, Is.Zero);
        Assert.That(_achievements.GetState(_daily).Progress, Is.Zero);
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj --filter "FullyQualifiedName~AchievementTransactionTests"`
Expected: 컴파일 실패 — `transaction:` 인자 없음

- [ ] **Step 3: Core 참조 추가**

`server/src/Bun3.Server.Achievements/Bun3.Server.Achievements.csproj`의 `</PropertyGroup>` 뒤에:

```xml
  <ItemGroup>
    <ProjectReference Include="..\Bun3.Server.Core\Bun3.Server.Core.csproj" />
  </ItemGroup>
```

- [ ] **Step 4: 참가자 partial 구현**

`AchievementManager.cs`의 클래스 선언을 `public sealed partial class AchievementManager<TDef>`로 바꾼다.

`server/src/Bun3.Server.Achievements/AchievementManager.Transaction.cs`:

```csharp
// AchievementManager partial — transaction participation (snapshot, rollback) and notification buffering.
using System.Collections.Generic;
using Bun3.Server.Core;

namespace Bun3.Server.Achievements
{
    // State changes apply immediately; a per-achievement snapshot taken at first touch makes them
    // reversible. While a transaction is active, dirty and completion hooks are buffered and fire
    // on commit.
    public sealed partial class AchievementManager<TDef> : ITransactionParticipant
    {
        private readonly bool[] _txCaptured;
        private readonly List<AchievementId> _txIds = new List<AchievementId>();
        private readonly List<AchievementState> _txStates = new List<AchievementState>();
        private readonly List<AchievementId> _txCompletedIds = new List<AchievementId>();
        private readonly List<int> _txCompletedCounts = new List<int>();
        private bool _txEnlisted;
        private bool _txDirtyPending;

        private bool InTransaction => _transaction != null && _transaction.IsActive;

        /// <summary>Snapshots an achievement before its first change in the current transaction.</summary>
        private void Capture(AchievementId id)
        {
            if (!InTransaction)
            {
                return;
            }

            if (!_txEnlisted)
            {
                _transaction!.Enlist(this);
                _txEnlisted = true;
            }

            if (_txCaptured[id.Index])
            {
                return;
            }

            _txCaptured[id.Index] = true;
            _txIds.Add(id);
            _txStates.Add(_states[id.Index]);
        }

        /// <summary>Raises onDirty, or buffers it until commit while a transaction is active.</summary>
        private void NotifyDirty()
        {
            if (_txEnlisted)
            {
                _txDirtyPending = true;
                return;
            }

            _onDirty?.Invoke();
        }

        /// <summary>Fires the completion hook, or buffers it until commit.</summary>
        private void NotifyCompleted(AchievementId id, int newCompletions)
        {
            if (_txEnlisted)
            {
                _txCompletedIds.Add(id);
                _txCompletedCounts.Add(newCompletions);
                return;
            }

            OnCompleted?.Invoke(id, _catalog.GetDefinition(id), newCompletions);
        }

        /// <inheritdoc />
        void ITransactionParticipant.OnCommit()
        {
            _txEnlisted = false;
            ClearSnapshots();

            if (_txDirtyPending)
            {
                _txDirtyPending = false;
                _onDirty?.Invoke();
            }

            for (var i = 0; i < _txCompletedIds.Count; i++)
            {
                var id = _txCompletedIds[i];
                OnCompleted?.Invoke(id, _catalog.GetDefinition(id), _txCompletedCounts[i]);
            }

            _txCompletedIds.Clear();
            _txCompletedCounts.Clear();
        }

        /// <inheritdoc />
        void ITransactionParticipant.OnRollback()
        {
            _txEnlisted = false;
            _txDirtyPending = false;
            _txCompletedIds.Clear();
            _txCompletedCounts.Clear();

            for (var i = _txIds.Count - 1; i >= 0; i--)
            {
                _states[_txIds[i].Index] = _txStates[i];
            }

            ClearSnapshots();
        }

        private void ClearSnapshots()
        {
            for (var i = 0; i < _txIds.Count; i++)
            {
                _txCaptured[_txIds[i].Index] = false;
            }

            _txIds.Clear();
            _txStates.Clear();
        }
    }
}
```

- [ ] **Step 5: 매니저 배선**

`AchievementManager.cs` 생성자에 인자와 필드를 추가한다:

```csharp
        private readonly Transaction? _transaction;
```

```csharp
        /// <param name="transaction">Session-wide transaction (<see cref="Transaction"/>) — when
        /// set, changes made while it is open are reversible and dirty/completion notifications are
        /// buffered until commit. Nothing is snapshotted when no transaction is open.</param>
        public AchievementManager(
            AchievementCatalog<TDef> catalog,
            Action? onDirty = null,
            Func<long>? utcNowTicks = null,
            Transaction? transaction = null)
```

```csharp
            _transaction = transaction;
            _txCaptured = new bool[catalog.Count];
```

변경 경로에 캡처와 알림 경유를 끼운다:

- `ApplyProgress` 진입 직후 `Capture(id);`
- `TryClaim`의 `state.ClaimedCount++;` 앞 `Capture(id);`
- `Unlock`/`Activate`/`Lock`의 `state.Availability = ...` 앞 `Capture(id);`
- `Reset`의 되감기 앞 `Capture(id);`
- 모든 `_onDirty?.Invoke();` → `NotifyDirty();`
- `OnCompleted?.Invoke(index, def, newCompletions);` → `NotifyCompleted(id, newCompletions);`

`Restore`는 로드 경로라 캡처·알림 모두 없음(기존 계약 유지).

- [ ] **Step 6: 통과 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj`
Expected: 신규 7건 + 기존 전건 PASS, 경고 0

- [ ] **Step 7: 커밋**

```bash
git add server/src/Bun3.Server.Achievements server/tests/Bun3.Server.Tests/AchievementTransactionTests.cs
git commit -m "✨ Achievements 트랜잭션 참가: 상태 스냅샷 롤백, dirty·완료 훅 커밋 시 발화" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Task 8: Achievements — ActionLog 자동 첨부

**Files:**
- Create: `server/src/Bun3.Server.Achievements/AchievementChange.cs`
- Modify: `server/src/Bun3.Server.Achievements/AchievementManager.cs` (ctor `log`/`logLabel`)
- Modify: `server/src/Bun3.Server.Achievements/AchievementManager.Transaction.cs` (첨부 버퍼링)
- Test: `server/tests/Bun3.Server.Tests/AchievementTransactionTests.cs` (첨부 테스트 추가)

**Interfaces:**
- Consumes: `Bun3.Server.Core.ActionLog` (기존), `AchievementId` (Task 6), `Capture`/`NotifyDirty` (Task 7)
- Produces:
  - `enum AchievementChangeKind : byte { Completed, Claimed, AvailabilityChanged }`
  - `readonly struct AchievementChange { AchievementId Achievement; AchievementChangeKind Kind; int CompletedCount; int ClaimedCount; AchievementStatus Availability; }`
  - `AchievementManager<TDef>` 생성자 인자 `ActionLog? log = null, string? logLabel = null` (`utcNowTicks` 다음, `transaction` 앞)

- [ ] **Step 1: 실패하는 테스트 작성**

`AchievementTransactionTests.cs`에 추가(`SetUp`에 `_log`/`_entries` 준비 필요):

```csharp
    private readonly List<ActionLogEntry> _entries = new();
    private ActionLog _log = null!;
```

`SetUp`에서 매니저 생성 전에:

```csharp
        _entries.Clear();
        _log = new ActionLog(batch =>
        {
            for (var i = 0; i < batch.Length; i++)
            {
                _entries.Add(batch[i]);
            }
        });
```

그리고 매니저 생성에 `log: _log, logLabel: "ach",`를 더한다.

```csharp
    [Test]
    public void Completion_and_claim_are_attached_to_the_action_log()
    {
        using (_log.BeginScope("DailyClear"))
        using (var scope = _transaction.Begin())
        {
            _achievements.Increase(_daily, 5);
            _achievements.TryClaim(_daily);
            scope.Commit();
        }

        var data = _entries.Where(e => e.Kind == ActionLogEntryKind.Data).ToList();
        Assert.That(data.Count, Is.EqualTo(2));
        Assert.That(data[0].Source, Is.EqualTo("ach"));

        var completed = (AchievementChange)data[0].Data!;
        Assert.That(completed.Kind, Is.EqualTo(AchievementChangeKind.Completed));
        Assert.That(completed.Achievement, Is.EqualTo(_daily));
        Assert.That(completed.CompletedCount, Is.EqualTo(1));

        var claimed = (AchievementChange)data[1].Data!;
        Assert.That(claimed.Kind, Is.EqualTo(AchievementChangeKind.Claimed));
        Assert.That(claimed.ClaimedCount, Is.EqualTo(1));
    }

    [Test]
    public void Rolled_back_changes_never_reach_the_action_log()
    {
        using (_log.BeginScope("DailyClear"))
        using (_transaction.Begin())
        {
            _achievements.Increase(_daily, 5);
            _achievements.TryClaim(_daily);
        }

        Assert.That(_entries.Where(e => e.Kind == ActionLogEntryKind.Data), Is.Empty);
    }

    [Test]
    public void Availability_transitions_are_attached()
    {
        using (_log.BeginScope("Rotate"))
        {
            _achievements.Lock(_kill);
        }

        var data = _entries.Where(e => e.Kind == ActionLogEntryKind.Data).ToList();
        Assert.That(data.Count, Is.EqualTo(1));
        var change = (AchievementChange)data[0].Data!;
        Assert.That(change.Kind, Is.EqualTo(AchievementChangeKind.AvailabilityChanged));
        Assert.That(change.Availability, Is.EqualTo(AchievementStatus.Locked));
    }
```

파일 상단에 `using System.Linq;`와 `using Bun3.Server.Core;`를 확인한다.

- [ ] **Step 2: 실패 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj --filter "FullyQualifiedName~AchievementTransactionTests"`
Expected: 컴파일 실패 — `AchievementChange`, `log:` 인자 미정의

- [ ] **Step 3: 페이로드 타입 작성**

`server/src/Bun3.Server.Achievements/AchievementChange.cs`:

```csharp
namespace Bun3.Server.Achievements
{
    /// <summary>What changed on an achievement.</summary>
    public enum AchievementChangeKind : byte
    {
        /// <summary>The achievement completed one or more times.</summary>
        Completed,

        /// <summary>A pending completion was claimed.</summary>
        Claimed,

        /// <summary>Availability moved between Locked/Ready/Active.</summary>
        AvailabilityChanged,
    }

    /// <summary>One achievement change, carrying the post-change counters so it can serve as an
    /// audit ledger row. Shipped through the action log's structured payload channel; sinks
    /// interpret it by type matching.</summary>
    public readonly struct AchievementChange
    {
        internal AchievementChange(
            AchievementId achievement, AchievementChangeKind kind,
            int completedCount, int claimedCount, AchievementStatus availability)
        {
            Achievement = achievement;
            Kind = kind;
            CompletedCount = completedCount;
            ClaimedCount = claimedCount;
            Availability = availability;
        }

        /// <summary>Target achievement.</summary>
        public AchievementId Achievement { get; }

        /// <summary>What changed.</summary>
        public AchievementChangeKind Kind { get; }

        /// <summary>Completion count immediately after the change.</summary>
        public int CompletedCount { get; }

        /// <summary>Claim count immediately after the change.</summary>
        public int ClaimedCount { get; }

        /// <summary>Availability immediately after the change.</summary>
        public AchievementStatus Availability { get; }
    }
}
```

- [ ] **Step 4: 첨부 배선**

`AchievementManager.cs` 생성자에 `ActionLog? log = null, string? logLabel = null`을 `utcNowTicks` 다음·`transaction` 앞에 넣고 필드를 저장한다.

`AchievementManager.Transaction.cs`에 첨부 버퍼를 추가한다:

```csharp
        private readonly List<AchievementChange> _txChanges = new List<AchievementChange>();

        /// <summary>Attaches a change to the action log, or buffers it until commit.
        /// Low-frequency audit path, so boxing is acceptable.</summary>
        private void NotifyChange(AchievementId id, AchievementChangeKind kind)
        {
            if (_log == null)
            {
                return;
            }

            ref readonly var state = ref _states[id.Index];
            var change = new AchievementChange(
                id, kind, state.CompletedCount, state.ClaimedCount, state.Availability);
            if (_txEnlisted)
            {
                _txChanges.Add(change);
                return;
            }

            _log.Append(change, _logLabel);
        }
```

`OnCommit`에서 `_txChanges`를 순서대로 `_log.Append`하고 비운다(`OnCompleted` 발화 **앞에** — 원장에는 변경이 먼저 남는다). `OnRollback`에서는 `_txChanges.Clear()`만 한다.

호출 지점:
- `ApplyProgress`에서 `newCompletions > 0`일 때 `NotifyChange(id, AchievementChangeKind.Completed);` (`NotifyCompleted` 앞)
- `TryClaim`의 `NotifyDirty()` 앞 `NotifyChange(id, AchievementChangeKind.Claimed);`
- `Unlock`/`Activate`/`Lock`의 `NotifyDirty()` 앞 `NotifyChange(id, AchievementChangeKind.AvailabilityChanged);`

`Increase` 자체(달성 없는 진행도 증가)는 고빈도라 첨부하지 않는다.

- [ ] **Step 5: 통과 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj`
Expected: 신규 3건 + 기존 전건 PASS, 경고 0

- [ ] **Step 6: 커밋**

```bash
git add server/src/Bun3.Server.Achievements server/tests/Bun3.Server.Tests/AchievementTransactionTests.cs
git commit -m "✨ Achievements ActionLog 자동 첨부: 달성·수령·전이를 구조화 항목으로" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Task 9: 통합 — 한 원장 트리와 교차 참가자 시나리오

**Files:**
- Create: `server/tests/Bun3.Server.Tests/OutgameLedgerIntegrationTests.cs`

**Interfaces:**
- Consumes: Task 1~8의 전 공개 API
- Produces: 없음 (검증 전용)

- [ ] **Step 1: 통합 테스트 작성**

`server/tests/Bun3.Server.Tests/OutgameLedgerIntegrationTests.cs`:

```csharp
using Bun3.Gameplay.Numerics;
using Bun3.Server.Achievements;
using Bun3.Server.Core;
using Bun3.Server.Items;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class OutgameLedgerIntegrationTests
{
    private sealed class ItemState
    {
    }

    private readonly List<ActionLogEntry> _entries = new();
    private ActionLog _log = null!;
    private Transaction _transaction = null!;
    private ItemCatalog<string> _items = null!;
    private ItemId _gold;
    private ItemId _reward;
    private ItemId _capped;
    private ItemInventory<ItemState> _inventory = null!;
    private AchievementCatalog<AchievementDefinition> _achievementCatalog = null!;
    private AchievementId _firstPurchase;
    private AchievementManager<AchievementDefinition> _achievements = null!;

    [SetUp]
    public void SetUp()
    {
        _entries.Clear();
        _log = new ActionLog(batch =>
        {
            for (var i = 0; i < batch.Length; i++)
            {
                _entries.Add(batch[i]);
            }
        });
        _transaction = new Transaction();

        _items = new ItemCatalogBuilder<string>()
            .Register("gold", "Gold")
            .Register("reward", "Reward")
            .Register("capped", "Capped", maxCount: 1)
            .Build();
        _gold = _items.GetRequired("gold");
        _reward = _items.GetRequired("reward");
        _capped = _items.GetRequired("capped");

        long next = 0;
        _inventory = new ItemInventory<ItemState>(
            _items,
            instanceIdIssuer: () => ++next,
            stateFactory: _ => new ItemState(),
            log: _log,
            logLabel: "bag",
            transaction: _transaction);

        _achievementCatalog = new AchievementCatalog<AchievementDefinition>(new[]
        {
            new AchievementDefinition("first_purchase", 1),
        });
        _firstPurchase = _achievementCatalog.GetRequired("first_purchase");
        _achievements = new AchievementManager<AchievementDefinition>(
            _achievementCatalog,
            log: _log,
            logLabel: "ach",
            transaction: _transaction);
    }

    [Test]
    public void One_scope_holds_the_whole_chain_from_both_modules()
    {
        _inventory.TryAdd(_gold, 1000);
        _entries.Clear();

        using (_log.BeginScope("BuyProduct id=1021 x1"))
        using (var scope = _transaction.Begin())
        {
            Assert.That(_inventory.TryRemove(_gold, 300), Is.EqualTo(InventoryError.None));
            Assert.That(_inventory.TryAdd(_reward, 1), Is.EqualTo(InventoryError.None));
            Assert.That(_achievements.Increase(_firstPurchase, 1), Is.EqualTo(1));
            scope.Commit();
        }

        Assert.That(_entries[0].Kind, Is.EqualTo(ActionLogEntryKind.ScopeStart));
        Assert.That(_entries[0].Text, Is.EqualTo("BuyProduct id=1021 x1"));

        var data = _entries.Where(e => e.Kind == ActionLogEntryKind.Data).ToList();
        Assert.That(data.Count, Is.EqualTo(3));
        Assert.That(data[0].Source, Is.EqualTo("bag"));
        Assert.That(data[1].Source, Is.EqualTo("bag"));
        Assert.That(data[2].Source, Is.EqualTo("ach"));

        var spend = (InventoryChange)data[0].Data!;
        Assert.That(spend.Delta.ToString(), Is.EqualTo(((BigNum)(-300)).ToString()));
        Assert.That(spend.Balance.ToString(), Is.EqualTo(((BigNum)700).ToString()));

        var achievement = (AchievementChange)data[2].Data!;
        Assert.That(achievement.Kind, Is.EqualTo(AchievementChangeKind.Completed));
        Assert.That(data.All(e => e.Depth == 1), Is.True);
    }

    [Test]
    public void Claim_rolls_back_when_the_reward_grant_fails()
    {
        _inventory.TryAdd(_capped, 1);            // capped definition is now full
        _achievements.Increase(_firstPurchase, 1);
        Assert.That(_achievements.GetClaimableCount(_firstPurchase), Is.EqualTo(1));
        _entries.Clear();

        var granted = false;
        using (_log.BeginScope("ClaimReward"))
        using (var scope = _transaction.Begin())
        {
            Assert.That(_achievements.TryClaim(_firstPurchase), Is.True);
            if (_inventory.TryAdd(_capped, 1) == InventoryError.None)
            {
                granted = true;
                scope.Commit();
            }
        }

        Assert.That(granted, Is.False);
        Assert.That(_achievements.GetClaimableCount(_firstPurchase), Is.EqualTo(1), "the claim must be restored");
        Assert.That(_achievements.GetState(_firstPurchase).ClaimedCount, Is.Zero);
        Assert.That(_entries.Where(e => e.Kind == ActionLogEntryKind.Data), Is.Empty, "a rolled-back change must not reach the ledger");
    }

    [Test]
    public void Claim_and_grant_commit_together_on_success()
    {
        _achievements.Increase(_firstPurchase, 1);

        using (var scope = _transaction.Begin())
        {
            Assert.That(_achievements.TryClaim(_firstPurchase), Is.True);
            Assert.That(_inventory.TryAdd(_reward, 10), Is.EqualTo(InventoryError.None));
            scope.Commit();
        }

        Assert.That(_achievements.GetState(_firstPurchase).ClaimedCount, Is.EqualTo(1));
        Assert.That(_inventory.GetQuantity(_reward).ToString(), Is.EqualTo(((BigNum)10).ToString()));
    }

    [Test]
    public void Hot_path_outside_a_transaction_stays_allocation_free()
    {
        var bare = new ItemInventory<ItemState>(
            _items,
            instanceIdIssuer: () => 1,
            stateFactory: _ => new ItemState());
        bare.TryAdd(_gold, 1_000_000);

        for (var i = 0; i < 100; i++)
        {
            bare.TryAdd(_gold, 1);
            bare.TryRemove(_gold, 1);
            _ = bare.GetQuantity(_gold);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            bare.TryAdd(_gold, 1);
            bare.TryRemove(_gold, 1);
            _ = bare.GetQuantity(_gold);
        }

        var delta = GC.GetAllocatedBytesForCurrentThread() - before;
        if (delta != 0)
        {
            Assert.Fail($"Expected no allocation on the transaction-free hot path, got {delta} bytes.");
        }
    }
}
```

파일 상단에 `using System.Linq;`를 넣는다.

- [ ] **Step 2: 실행하고 실패 원인 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj --filter "FullyQualifiedName~OutgameLedgerIntegrationTests"`
Expected: Task 1~8이 끝났다면 PASS. 실패하면 앞 태스크의 배선 누락이므로 **여기서 새 기능을 만들지 말고 해당 태스크를 고친다.**

- [ ] **Step 3: Release 구성으로 무할당 재확인**

Run: `dotnet test server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj -c Release --filter "FullyQualifiedName~Hot_path_outside_a_transaction_stays_allocation_free"`
Expected: PASS

- [ ] **Step 4: 전체 테스트**

Run: `dotnet test server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj`
Expected: 전건 PASS, 경고 0

- [ ] **Step 5: 커밋**

```bash
git add server/tests/Bun3.Server.Tests/OutgameLedgerIntegrationTests.cs
git commit -m "✅ 아웃게임 통합 검증: 한 원장 트리·교차 참가자 롤백·무할당 무회귀" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Task 10: 계약 문서화와 버전 범프

**Files:**
- Modify: `server/src/Bun3.Server.Core/Bun3.Server.Core.csproj` (Version, Description)
- Modify: `server/src/Bun3.Server.Items/Bun3.Server.Items.csproj` (Version, Description)
- Modify: `server/src/Bun3.Server.Achievements/Bun3.Server.Achievements.csproj` (Version, Description)
- Modify: `server/src/Bun3.Server.Items/ItemInventory.cs` (클래스 XML 문서에 실패 보고 규칙·멱등성)
- Modify: `server/src/Bun3.Server.Achievements/AchievementManager.cs` (클래스 XML 문서에 실패 보고 규칙)

**Interfaces:**
- Consumes: 없음
- Produces: 없음

- [ ] **Step 1: 실패 보고 규칙을 XML 문서에 명문화**

`ItemInventory.cs`의 클래스 요약 말미에 한 문단을 더한다:

```
    /// Failure reporting follows one rule across the outgame packages: domain failures the caller
    /// must handle are returned (<see cref="InventoryError"/>), while programmer errors and save
    /// data corruption throw.
    /// The transaction is in-memory only, so a database commit cannot take part in it — claim
    /// idempotency across the in-memory and persisted boundaries is the game's responsibility.
```

`AchievementManager.cs`의 클래스 요약 말미에:

```
    /// Failure reporting follows one rule across the outgame packages: domain failures the caller
    /// must handle are returned (the claim bool, the completion count), while programmer errors and
    /// save data corruption throw.
```

- [ ] **Step 2: 버전과 Description 범프**

`Bun3.Server.Core.csproj`:

```xml
    <Version>0.6.0</Version>
```
```xml
    <Description>Session-actor game server core — Session/ServerBase, single consume loop per connection, ActionLog action ledger, multi-operation transaction with rollback</Description>
```

`Bun3.Server.Items.csproj`: `<Version>0.2.0</Version>`
`Bun3.Server.Achievements.csproj`: `<Version>0.3.0</Version>`

두 Description에 각각 `transaction rollback`을 언급하는 문구를 더한다(기존 문장 끝에 `, transaction rollback participation`).

- [ ] **Step 3: 빌드·테스트 확인**

Run: `dotnet build Bun3.sln -c Release` 다음 `dotnet test server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj`
Expected: 경고 0, 전건 PASS

- [ ] **Step 4: 한글 잔존 확인**

Run: `grep -rnP "[\x{AC00}-\x{D7A3}]" server/src/Bun3.Server.Core server/src/Bun3.Server.Items server/src/Bun3.Server.Achievements`
Expected: 결과 없음

- [ ] **Step 5: 커밋**

```bash
git add server/src/Bun3.Server.Core server/src/Bun3.Server.Items server/src/Bun3.Server.Achievements
git commit -m "🔖 아웃게임 3패키지 버전 범프 + 실패 보고·멱등성 계약 문서화" -m "Core 0.6.0(트랜잭션 신설)·Items 0.2.0·Achievements 0.3.0. 퍼블리시는 사용자 확인 후." -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## 완료 조건

- [ ] `dotnet build Bun3.sln -c Release` 경고 0
- [ ] `dotnet test server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj` 전건 PASS (기존 265건 + 신규 약 40건)
- [ ] `grep -rn "_stackSingletons" server/src/Bun3.Server.Items/` 결과 없음
- [ ] `grep -rn "onChanged" server/src/Bun3.Server.Items/` 결과 없음
- [ ] 패키지 소스에 한글 없음
- [ ] 퍼블리시·main 머지 하지 않음 — 브랜치 `Bun3/server-outgame-items-achievements`에서 완결
