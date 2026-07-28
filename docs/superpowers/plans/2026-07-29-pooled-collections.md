# Pooled Collections Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Allocation-free pooled collections (`PooledList<T>` etc.) with `using`-based auto-return and function-return ownership transfer, in `com.bun3.common`.

**Architecture:** BCL-collection inheritance wrappers (`PooledList<T> : List<T>, IDisposable`) backed by a single `ConcurrentObjectPool<T>` (ConcurrentBag + Interlocked counter). Each wrapper exposes a static `Get()` factory over a per-closed-generic shared pool. A standalone `PriorityQueue<TElement, TPriority>` (binary min-heap) fills the netstandard2.1 gap and is inherited by `PooledPriorityQueue`.

**Tech Stack:** C# 9 / netstandard2.1 library (`common/src/com.bun3.common`), NUnit 4 + net8.0 test project (new).

**Spec:** `docs/superpowers/specs/2026-07-29-pooled-collections-design.md`

## Global Constraints

- Library code targets **netstandard2.1, LangVersion 9** — block-scoped namespaces, no file-scoped namespaces, no `System.Collections.Generic.PriorityQueue`.
- **Nullable annotations OFF** (match existing `CancellationScope.cs` style — no `?` reference annotations, no `null!`).
- Namespaces exactly: `Bun3.Common.Collections` (PriorityQueue), `Bun3.Common.Pooling` (everything else).
- Pool defaults: capacity `Math.Max(32, 2 * Environment.ProcessorCount)`, retained-count threshold `8192`.
- Do **NOT** hand-create Unity `.meta` files — the Unity editor generates them on next open.
- Commit messages use the repo's gitmoji style (`✨`, `✅`, `🔧`, …).
- All commands run from repo root `E:\Projects\orca\workspace\bun3-kit\pooled-collections`.
- **Test isolation trick:** shared pools are static per *closed generic type*, so every test that touches a shared pool must use a unique element-type argument (e.g. `PooledList<Guid>` in one test, `PooledList<double>` in another) to avoid cross-test interference. NUnit runs tests in a fixture sequentially by default; do not enable parallelism.

---

### Task 1: Test project scaffold

**Files:**
- Create: `common/tests/Bun3.Common.Tests/Bun3.Common.Tests.csproj`
- Modify: `Bun3.sln` (via `dotnet sln add`)

**Interfaces:**
- Consumes: `common/src/com.bun3.common/Bun3.Common.csproj` (existing library project)
- Produces: test project all later tasks put their tests in; namespace `Bun3.Common.Tests`

- [ ] **Step 1: Create the csproj**

`common/tests/Bun3.Common.Tests/Bun3.Common.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <RootNamespace>Bun3.Common.Tests</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.10.0" />
    <PackageReference Include="NUnit" Version="4.1.0" />
    <PackageReference Include="NUnit3TestAdapter" Version="4.5.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\com.bun3.common\Bun3.Common.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Register in solution**

Run: `dotnet sln Bun3.sln add common/tests/Bun3.Common.Tests/Bun3.Common.Tests.csproj --solution-folder common/tests`
Expected: `Project ... added to the solution.`

- [ ] **Step 3: Verify the empty test project builds and runs**

Run: `dotnet test common/tests/Bun3.Common.Tests`
Expected: build succeeds, `Passed! - Failed: 0, Passed: 0` (or "No test is available" warning — either is fine, exit code 0).

- [ ] **Step 4: Commit**

```bash
git add common/tests Bun3.sln
git commit -m "🔧 Add Bun3.Common.Tests project (NUnit, net8.0)"
```

---

### Task 2: PriorityQueue (Bun3.Common.Collections)

**Files:**
- Create: `common/src/com.bun3.common/Runtime/Collections/PriorityQueue.cs`
- Test: `common/tests/Bun3.Common.Tests/PriorityQueueTests.cs`

**Interfaces:**
- Consumes: nothing (pure BCL)
- Produces: `Bun3.Common.Collections.PriorityQueue<TElement, TPriority>` with
  `PriorityQueue()`, `PriorityQueue(int initialCapacity)`, `PriorityQueue(IComparer<TPriority> comparer)`, `PriorityQueue(int initialCapacity, IComparer<TPriority> comparer)`,
  `int Count`, `IComparer<TPriority> Comparer`,
  `void Enqueue(TElement, TPriority)`, `TElement Dequeue()`, `bool TryDequeue(out TElement, out TPriority)`, `TElement Peek()`, `bool TryPeek(out TElement, out TPriority)`, `void Clear()`.
  Task 6 inherits this class; `Clear()` and `Count` must be public.

- [ ] **Step 1: Write the failing tests**

`common/tests/Bun3.Common.Tests/PriorityQueueTests.cs` (note the alias — on net8.0 an unqualified `PriorityQueue` is ambiguous with `System.Collections.Generic`):

```csharp
using System;
using System.Collections.Generic;
using NUnit.Framework;
using PQ = Bun3.Common.Collections.PriorityQueue<string, int>;

namespace Bun3.Common.Tests
{
    [TestFixture]
    public class PriorityQueueTests
    {
        [Test]
        public void Dequeue_ReturnsElementsInPriorityOrder()
        {
            var queue = new PQ();
            queue.Enqueue("c", 3);
            queue.Enqueue("a", 1);
            queue.Enqueue("d", 4);
            queue.Enqueue("b", 2);

            Assert.That(queue.Dequeue(), Is.EqualTo("a"));
            Assert.That(queue.Dequeue(), Is.EqualTo("b"));
            Assert.That(queue.Dequeue(), Is.EqualTo("c"));
            Assert.That(queue.Dequeue(), Is.EqualTo("d"));
            Assert.That(queue.Count, Is.EqualTo(0));
        }

        [Test]
        public void Dequeue_ManyRandomItems_ComesOutSorted()
        {
            var queue = new Bun3.Common.Collections.PriorityQueue<int, int>();
            var random = new Random(12345);
            var expected = new List<int>();
            for (var i = 0; i < 1000; i++)
            {
                var value = random.Next(0, 100);
                expected.Add(value);
                queue.Enqueue(value, value);
            }
            expected.Sort();

            foreach (var value in expected)
                Assert.That(queue.Dequeue(), Is.EqualTo(value));
        }

        [Test]
        public void Enqueue_DuplicatePriorities_AllElementsComeOut()
        {
            var queue = new PQ();
            queue.Enqueue("x", 1);
            queue.Enqueue("y", 1);
            queue.Enqueue("z", 1);

            var results = new List<string> { queue.Dequeue(), queue.Dequeue(), queue.Dequeue() };
            Assert.That(results, Is.EquivalentTo(new[] { "x", "y", "z" }));
        }

        [Test]
        public void CustomComparer_ReversesOrder()
        {
            var maxFirst = Comparer<int>.Create((a, b) => b.CompareTo(a));
            var queue = new Bun3.Common.Collections.PriorityQueue<string, int>(maxFirst);

            queue.Enqueue("low", 1);
            queue.Enqueue("high", 10);

            Assert.That(queue.Comparer, Is.SameAs(maxFirst));
            Assert.That(queue.Dequeue(), Is.EqualTo("high"));
            Assert.That(queue.Dequeue(), Is.EqualTo("low"));
        }

        [Test]
        public void DequeueAndPeek_EmptyQueue_Throw()
        {
            var queue = new PQ();
            Assert.Throws<InvalidOperationException>(() => queue.Dequeue());
            Assert.Throws<InvalidOperationException>(() => queue.Peek());
        }

        [Test]
        public void TryDequeueAndTryPeek_EmptyQueue_ReturnFalse()
        {
            var queue = new PQ();
            Assert.That(queue.TryDequeue(out _, out _), Is.False);
            Assert.That(queue.TryPeek(out _, out _), Is.False);
        }

        [Test]
        public void TryPeek_DoesNotRemove_TryDequeueRemoves()
        {
            var queue = new PQ();
            queue.Enqueue("a", 1);

            Assert.That(queue.TryPeek(out var peeked, out var peekedPriority), Is.True);
            Assert.That(peeked, Is.EqualTo("a"));
            Assert.That(peekedPriority, Is.EqualTo(1));
            Assert.That(queue.Count, Is.EqualTo(1));

            Assert.That(queue.TryDequeue(out var dequeued, out var priority), Is.True);
            Assert.That(dequeued, Is.EqualTo("a"));
            Assert.That(priority, Is.EqualTo(1));
            Assert.That(queue.Count, Is.EqualTo(0));
        }

        [Test]
        public void Clear_EmptiesQueue_AndQueueRemainsUsable()
        {
            var queue = new PQ();
            queue.Enqueue("a", 1);
            queue.Enqueue("b", 2);
            queue.Clear();

            Assert.That(queue.Count, Is.EqualTo(0));
            queue.Enqueue("c", 3);
            Assert.That(queue.Dequeue(), Is.EqualTo("c"));
        }

        [Test]
        public void Constructor_NegativeCapacity_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new Bun3.Common.Collections.PriorityQueue<string, int>(-1));
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test common/tests/Bun3.Common.Tests`
Expected: FAIL to compile — `Bun3.Common.Collections` namespace does not exist.

- [ ] **Step 3: Implement PriorityQueue**

`common/src/com.bun3.common/Runtime/Collections/PriorityQueue.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace Bun3.Common.Collections
{
    /// <summary>
    /// Array-backed binary min-heap. Mirrors the .NET 6+
    /// <c>System.Collections.Generic.PriorityQueue</c> API subset so consumers can migrate
    /// mechanically once off netstandard2.1. Elements dequeue in ascending priority order
    /// per the comparer; ties dequeue in unspecified order. Not thread safe.
    /// </summary>
    public class PriorityQueue<TElement, TPriority>
    {
        private (TElement Element, TPriority Priority)[] _nodes;
        private int _count;
        private readonly IComparer<TPriority> _comparer;

        public PriorityQueue() : this(0, null) { }

        public PriorityQueue(int initialCapacity) : this(initialCapacity, null) { }

        public PriorityQueue(IComparer<TPriority> comparer) : this(0, comparer) { }

        public PriorityQueue(int initialCapacity, IComparer<TPriority> comparer)
        {
            if (initialCapacity < 0)
                throw new ArgumentOutOfRangeException(nameof(initialCapacity));
            _nodes = initialCapacity > 0
                ? new (TElement, TPriority)[initialCapacity]
                : Array.Empty<(TElement, TPriority)>();
            _comparer = comparer ?? Comparer<TPriority>.Default;
        }

        public int Count => _count;

        public IComparer<TPriority> Comparer => _comparer;

        public void Enqueue(TElement element, TPriority priority)
        {
            if (_count == _nodes.Length)
                Array.Resize(ref _nodes, _nodes.Length == 0 ? 4 : _nodes.Length * 2);
            var index = _count++;
            _nodes[index] = (element, priority);
            SiftUp(index);
        }

        public TElement Dequeue()
        {
            if (_count == 0)
                throw new InvalidOperationException("The queue is empty.");
            var element = _nodes[0].Element;
            RemoveRoot();
            return element;
        }

        public bool TryDequeue(out TElement element, out TPriority priority)
        {
            if (_count == 0)
            {
                element = default;
                priority = default;
                return false;
            }
            (element, priority) = _nodes[0];
            RemoveRoot();
            return true;
        }

        public TElement Peek()
        {
            if (_count == 0)
                throw new InvalidOperationException("The queue is empty.");
            return _nodes[0].Element;
        }

        public bool TryPeek(out TElement element, out TPriority priority)
        {
            if (_count == 0)
            {
                element = default;
                priority = default;
                return false;
            }
            (element, priority) = _nodes[0];
            return true;
        }

        public void Clear()
        {
            Array.Clear(_nodes, 0, _count);
            _count = 0;
        }

        private void RemoveRoot()
        {
            var lastIndex = --_count;
            _nodes[0] = _nodes[lastIndex];
            _nodes[lastIndex] = default;
            if (_count > 0)
                SiftDown(0);
        }

        private void SiftUp(int index)
        {
            var node = _nodes[index];
            while (index > 0)
            {
                var parentIndex = (index - 1) >> 1;
                var parent = _nodes[parentIndex];
                if (_comparer.Compare(node.Priority, parent.Priority) >= 0)
                    break;
                _nodes[index] = parent;
                index = parentIndex;
            }
            _nodes[index] = node;
        }

        private void SiftDown(int index)
        {
            var node = _nodes[index];
            while (true)
            {
                var childIndex = (index << 1) + 1;
                if (childIndex >= _count)
                    break;
                var rightIndex = childIndex + 1;
                if (rightIndex < _count &&
                    _comparer.Compare(_nodes[rightIndex].Priority, _nodes[childIndex].Priority) < 0)
                    childIndex = rightIndex;
                if (_comparer.Compare(_nodes[childIndex].Priority, node.Priority) >= 0)
                    break;
                _nodes[index] = _nodes[childIndex];
                index = childIndex;
            }
            _nodes[index] = node;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test common/tests/Bun3.Common.Tests`
Expected: PASS, 9 tests.

- [ ] **Step 5: Commit**

```bash
git add common/src/com.bun3.common/Runtime/Collections common/tests
git commit -m "✨ Add PriorityQueue (binary min-heap) to Bun3.Common.Collections"
```

---

### Task 3: Pooling interfaces + ConcurrentObjectPool

**Files:**
- Create: `common/src/com.bun3.common/Runtime/Pooling/IObjectPool.cs`
- Create: `common/src/com.bun3.common/Runtime/Pooling/IPooledObject.cs`
- Create: `common/src/com.bun3.common/Runtime/Pooling/ConcurrentObjectPool.cs`
- Test: `common/tests/Bun3.Common.Tests/ConcurrentObjectPoolTests.cs`

**Interfaces:**
- Consumes: nothing
- Produces:
  - `Bun3.Common.Pooling.IObjectPool<T>` — `int MaxRetainedCount { get; }`, `T Get()`, `void Release(T item)`
  - `Bun3.Common.Pooling.IPooledObject<T> : IDisposable` — `void SetPool(IObjectPool<T> pool)`
  - `Bun3.Common.Pooling.ConcurrentObjectPool<T> : IObjectPool<T> where T : class, IPooledObject<T>, new()` — ctor `(int maxCapacity = 0, int maxRetainedCount = 8192)` (0 → `Math.Max(32, 2 * Environment.ProcessorCount)`), plus `int MaxCapacity { get; }`.
  - Tasks 4–6 store the pool in a field typed `IObjectPool<Pooled…>` and call `Release` from `Dispose`; the pool calls `SetPool(this)` on **every** `Get` (re-arm).

- [ ] **Step 1: Write the failing tests**

`common/tests/Bun3.Common.Tests/ConcurrentObjectPoolTests.cs`:

```csharp
using System;
using Bun3.Common.Pooling;
using NUnit.Framework;

namespace Bun3.Common.Tests
{
    [TestFixture]
    public class ConcurrentObjectPoolTests
    {
        private class TestItem : IPooledObject<TestItem>
        {
            public IObjectPool<TestItem> Pool;
            public int SetPoolCalls;

            public void SetPool(IObjectPool<TestItem> pool)
            {
                Pool = pool;
                SetPoolCalls++;
            }

            public void Dispose() { }
        }

        [Test]
        public void Get_EmptyPool_CreatesNewItemAndArmsIt()
        {
            var pool = new ConcurrentObjectPool<TestItem>();
            var item = pool.Get();

            Assert.That(item, Is.Not.Null);
            Assert.That(item.Pool, Is.SameAs(pool));
        }

        [Test]
        public void Get_AfterRelease_ReturnsSameInstanceAndRearms()
        {
            var pool = new ConcurrentObjectPool<TestItem>();
            var item = pool.Get();
            pool.Release(item);

            var again = pool.Get();
            Assert.That(again, Is.SameAs(item));
            Assert.That(again.SetPoolCalls, Is.EqualTo(2));
        }

        [Test]
        public void Release_BeyondMaxCapacity_DropsExtraItems()
        {
            var pool = new ConcurrentObjectPool<TestItem>(maxCapacity: 1);
            var first = pool.Get();
            var second = pool.Get();
            pool.Release(first);
            pool.Release(second); // over capacity — dropped

            var fromPool = pool.Get();      // the one retained item
            var created = pool.Get();       // pool empty again — freshly created

            Assert.That(fromPool, Is.SameAs(first).Or.SameAs(second));
            Assert.That(created, Is.Not.SameAs(first));
            Assert.That(created, Is.Not.SameAs(second));
        }

        [Test]
        public void Defaults_MatchSpec()
        {
            var pool = new ConcurrentObjectPool<TestItem>();
            Assert.That(pool.MaxCapacity,
                Is.EqualTo(Math.Max(32, 2 * Environment.ProcessorCount)));
            Assert.That(pool.MaxRetainedCount, Is.EqualTo(8192));
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test common/tests/Bun3.Common.Tests`
Expected: FAIL to compile — `Bun3.Common.Pooling` namespace does not exist.

- [ ] **Step 3: Implement the interfaces and pool**

`common/src/com.bun3.common/Runtime/Pooling/IObjectPool.cs`:

```csharp
namespace Bun3.Common.Pooling
{
    /// <summary>An object pool that pooled objects return themselves to on dispose.</summary>
    public interface IObjectPool<T>
    {
        /// <summary>
        /// Items whose element count exceeds this at dispose time are dropped instead of
        /// pooled, so one oversized use cannot pin a large backing array forever. Read by
        /// the pooled wrapper in <c>Dispose</c>, before it clears itself.
        /// </summary>
        int MaxRetainedCount { get; }

        T Get();

        void Release(T item);
    }
}
```

`common/src/com.bun3.common/Runtime/Pooling/IPooledObject.cs`:

```csharp
using System;

namespace Bun3.Common.Pooling
{
    /// <summary>
    /// A poolable object. <see cref="IDisposable.Dispose"/> returns it to its pool;
    /// <see cref="SetPool"/> is called by the pool on every rental and is not for consumers.
    /// </summary>
    public interface IPooledObject<T> : IDisposable
    {
        void SetPool(IObjectPool<T> pool);
    }
}
```

`common/src/com.bun3.common/Runtime/Pooling/ConcurrentObjectPool.cs`:

```csharp
using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Bun3.Common.Pooling
{
    /// <summary>
    /// Thread-safe object pool. Backed by <see cref="ConcurrentBag{T}"/> (thread-local
    /// queues with work stealing), so the common same-thread Get/Release cycle is nearly
    /// lock free. Size is tracked with an <see cref="Interlocked"/> counter instead of
    /// <c>ConcurrentBag.Count</c>, which would lock and walk every thread-local queue.
    /// </summary>
    public class ConcurrentObjectPool<T> : IObjectPool<T> where T : class, IPooledObject<T>, new()
    {
        private readonly ConcurrentBag<T> _items = new ConcurrentBag<T>();
        private int _count;

        public int MaxCapacity { get; }
        public int MaxRetainedCount { get; }

        /// <param name="maxCapacity">
        /// Most items the pool retains; further releases are dropped. Values &lt;= 0 select
        /// the default <c>Math.Max(32, 2 * Environment.ProcessorCount)</c>.
        /// </param>
        /// <param name="maxRetainedCount">See <see cref="IObjectPool{T}.MaxRetainedCount"/>.</param>
        public ConcurrentObjectPool(int maxCapacity = 0, int maxRetainedCount = 8192)
        {
            MaxCapacity = maxCapacity > 0
                ? maxCapacity
                : Math.Max(32, 2 * Environment.ProcessorCount);
            MaxRetainedCount = maxRetainedCount;
        }

        public T Get()
        {
            if (_items.TryTake(out var item))
                Interlocked.Decrement(ref _count);
            else
                item = new T();
            item.SetPool(this);
            return item;
        }

        public void Release(T item)
        {
            if (Interlocked.Increment(ref _count) > MaxCapacity)
            {
                Interlocked.Decrement(ref _count);
                return;
            }
            _items.Add(item);
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test common/tests/Bun3.Common.Tests`
Expected: PASS (13 tests total).

- [ ] **Step 5: Commit**

```bash
git add common/src/com.bun3.common/Runtime/Pooling common/tests
git commit -m "✨ Add ConcurrentObjectPool and pooling interfaces"
```

---

### Task 4: PooledList — wrapper pattern, dispose guard, ownership transfer

**Files:**
- Create: `common/src/com.bun3.common/Runtime/Pooling/PooledList.cs`
- Test: `common/tests/Bun3.Common.Tests/PooledListTests.cs`

**Interfaces:**
- Consumes: `ConcurrentObjectPool<T>`, `IObjectPool<T>`, `IPooledObject<T>` (Task 3)
- Produces: `Bun3.Common.Pooling.PooledList<T> : List<T>, IPooledObject<PooledList<T>>` with `static PooledList<T> Get()`. This file is the canonical wrapper pattern Tasks 5–6 replicate.

- [ ] **Step 1: Write the failing tests**

`common/tests/Bun3.Common.Tests/PooledListTests.cs` (each test uses a distinct element type — shared pools are static per closed generic):

```csharp
using System;
using Bun3.Common.Pooling;
using NUnit.Framework;

namespace Bun3.Common.Tests
{
    [TestFixture]
    public class PooledListTests
    {
        [Test]
        public void Get_AfterDispose_ReusesSameInstanceCleared()
        {
            var list = PooledList<Guid>.Get();
            list.Add(Guid.NewGuid());
            list.Dispose();

            var reused = PooledList<Guid>.Get();
            Assert.That(reused, Is.SameAs(list));
            Assert.That(reused.Count, Is.EqualTo(0));
            reused.Dispose();
        }

        [Test]
        public void DoubleDispose_IsNoOp_InstanceNotPooledTwice()
        {
            var list = PooledList<double>.Get();
            list.Dispose();
            list.Dispose(); // must not enqueue a second time

            var first = PooledList<double>.Get();
            var second = PooledList<double>.Get();
            Assert.That(first, Is.SameAs(list));
            Assert.That(second, Is.Not.SameAs(list));
            first.Dispose();
            second.Dispose();
        }

        [Test]
        public void DirectlyConstructed_DisposeIsNoOp()
        {
            var list = new PooledList<byte> { 1, 2, 3 };
            Assert.DoesNotThrow(() => list.Dispose());
            Assert.DoesNotThrow(() => list.Dispose());
        }

        [Test]
        public void Dispose_CountOverRetainedThreshold_DropsInstance()
        {
            var pool = new ConcurrentObjectPool<PooledList<short>>(maxRetainedCount: 4);
            var list = pool.Get();
            for (short i = 0; i < 5; i++)
                list.Add(i);
            list.Dispose(); // 5 > 4 — dropped, not pooled

            var next = pool.Get();
            Assert.That(next, Is.Not.SameAs(list));
            next.Dispose();
        }

        [Test]
        public void Dispose_CountAtRetainedThreshold_IsPooled()
        {
            var pool = new ConcurrentObjectPool<PooledList<long>>(maxRetainedCount: 4);
            var list = pool.Get();
            for (long i = 0; i < 4; i++)
                list.Add(i);
            list.Dispose(); // 4 <= 4 — pooled

            var next = pool.Get();
            Assert.That(next, Is.SameAs(list));
            next.Dispose();
        }

        private static PooledList<string> MakeGreetings()
        {
            var result = PooledList<string>.Get();
            result.Add("hello");
            result.Add("world");
            return result; // ownership transfers to the caller
        }

        [Test]
        public void ReturnedFromFunction_CallerOwnsAndDisposes()
        {
            string first;
            using (var greetings = MakeGreetings())
            {
                Assert.That(greetings.Count, Is.EqualTo(2));
                first = greetings[0];
            }
            Assert.That(first, Is.EqualTo("hello"));

            var reused = PooledList<string>.Get();
            Assert.That(reused.Count, Is.EqualTo(0));
            reused.Dispose();
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test common/tests/Bun3.Common.Tests`
Expected: FAIL to compile — `PooledList` does not exist.

- [ ] **Step 3: Implement PooledList**

`common/src/com.bun3.common/Runtime/Pooling/PooledList.cs`:

```csharp
using System.Collections.Generic;
using System.Threading;

namespace Bun3.Common.Pooling
{
    /// <summary>
    /// A <see cref="List{T}"/> rented from a shared pool. Dispose returns it to its pool,
    /// so <c>using var list = PooledList&lt;T&gt;.Get();</c> is allocation free after warm-up.
    /// Returning one from a method transfers ownership to the caller, who must dispose it.
    /// Dispose is idempotent; disposing a directly-constructed instance is a no-op.
    /// </summary>
    public class PooledList<T> : List<T>, IPooledObject<PooledList<T>>
    {
        private static readonly ConcurrentObjectPool<PooledList<T>> SharedPool =
            new ConcurrentObjectPool<PooledList<T>>();

        private IObjectPool<PooledList<T>> _pool;

        /// <summary>Rents an empty list from the shared pool.</summary>
        public static PooledList<T> Get()
        {
            return SharedPool.Get();
        }

        void IPooledObject<PooledList<T>>.SetPool(IObjectPool<PooledList<T>> pool)
        {
            _pool = pool;
        }

        public void Dispose()
        {
            var pool = Interlocked.Exchange(ref _pool, null);
            if (pool == null)
                return; // double dispose, or directly-constructed instance
            if (Count > pool.MaxRetainedCount)
                return; // grew too large — let the GC take it
            Clear();
            pool.Release(this);
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test common/tests/Bun3.Common.Tests`
Expected: PASS (19 tests total).

- [ ] **Step 5: Commit**

```bash
git add common/src/com.bun3.common/Runtime/Pooling common/tests
git commit -m "✨ Add PooledList with shared-pool factory and dispose guard"
```

---

### Task 5: Remaining BCL wrappers (Dictionary, HashSet, Queue, Stack, SortedDictionary)

**Files:**
- Create: `common/src/com.bun3.common/Runtime/Pooling/PooledDictionary.cs`
- Create: `common/src/com.bun3.common/Runtime/Pooling/PooledHashSet.cs`
- Create: `common/src/com.bun3.common/Runtime/Pooling/PooledQueue.cs`
- Create: `common/src/com.bun3.common/Runtime/Pooling/PooledStack.cs`
- Create: `common/src/com.bun3.common/Runtime/Pooling/PooledSortedDictionary.cs`
- Test: `common/tests/Bun3.Common.Tests/PooledCollectionTests.cs`

**Interfaces:**
- Consumes: `ConcurrentObjectPool<T>`, `IObjectPool<T>`, `IPooledObject<T>` (Task 3)
- Produces: `PooledDictionary<TKey, TValue>`, `PooledHashSet<T>`, `PooledQueue<T>`, `PooledStack<T>`, `PooledSortedDictionary<TKey, TValue>` — each `: <BCL base>, IPooledObject<self>` with `static Get()`, same contract as `PooledList<T>`.

- [ ] **Step 1: Write the failing tests**

`common/tests/Bun3.Common.Tests/PooledCollectionTests.cs` — one shared contract helper, one test per type (distinct element types per test for pool isolation):

```csharp
using System;
using Bun3.Common.Pooling;
using NUnit.Framework;

namespace Bun3.Common.Tests
{
    [TestFixture]
    public class PooledCollectionTests
    {
        /// <summary>
        /// Shared contract: dispose pools the cleared instance for reuse; double dispose
        /// does not pool it twice.
        /// </summary>
        private static void AssertPoolingContract<TCollection>(
            Func<TCollection> get, Action<TCollection> addOne, Func<TCollection, int> count)
            where TCollection : class, IDisposable
        {
            var first = get();
            addOne(first);
            first.Dispose();

            var reused = get();
            Assert.That(reused, Is.SameAs(first), "dispose should pool the instance");
            Assert.That(count(reused), Is.EqualTo(0), "pooled instance should be cleared");

            reused.Dispose();
            reused.Dispose(); // no-op

            var third = get();
            var fourth = get();
            Assert.That(third, Is.SameAs(reused));
            Assert.That(fourth, Is.Not.SameAs(reused), "double dispose must not pool twice");
            third.Dispose();
            fourth.Dispose();
        }

        [Test]
        public void PooledDictionary_FollowsPoolingContract()
        {
            AssertPoolingContract(
                PooledDictionary<Guid, int>.Get,
                d => d[Guid.NewGuid()] = 1,
                d => d.Count);
        }

        [Test]
        public void PooledHashSet_FollowsPoolingContract()
        {
            AssertPoolingContract(
                PooledHashSet<Guid>.Get,
                s => s.Add(Guid.NewGuid()),
                s => s.Count);
        }

        [Test]
        public void PooledQueue_FollowsPoolingContract()
        {
            AssertPoolingContract(
                PooledQueue<Guid>.Get,
                q => q.Enqueue(Guid.NewGuid()),
                q => q.Count);
        }

        [Test]
        public void PooledStack_FollowsPoolingContract()
        {
            AssertPoolingContract(
                PooledStack<Guid>.Get,
                s => s.Push(Guid.NewGuid()),
                s => s.Count);
        }

        [Test]
        public void PooledSortedDictionary_FollowsPoolingContract()
        {
            AssertPoolingContract(
                PooledSortedDictionary<int, Guid>.Get,
                d => d[7] = Guid.NewGuid(),
                d => d.Count);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test common/tests/Bun3.Common.Tests`
Expected: FAIL to compile — the five types do not exist.

- [ ] **Step 3: Implement the five wrappers**

`common/src/com.bun3.common/Runtime/Pooling/PooledDictionary.cs`:

```csharp
using System.Collections.Generic;
using System.Threading;

namespace Bun3.Common.Pooling
{
    /// <summary>
    /// A <see cref="Dictionary{TKey, TValue}"/> rented from a shared pool. Same contract as
    /// <see cref="PooledList{T}"/>: dispose returns it, dispose is idempotent, ownership
    /// transfers with the reference.
    /// </summary>
    public class PooledDictionary<TKey, TValue>
        : Dictionary<TKey, TValue>, IPooledObject<PooledDictionary<TKey, TValue>>
    {
        private static readonly ConcurrentObjectPool<PooledDictionary<TKey, TValue>> SharedPool =
            new ConcurrentObjectPool<PooledDictionary<TKey, TValue>>();

        private IObjectPool<PooledDictionary<TKey, TValue>> _pool;

        /// <summary>Rents an empty dictionary from the shared pool.</summary>
        public static PooledDictionary<TKey, TValue> Get()
        {
            return SharedPool.Get();
        }

        void IPooledObject<PooledDictionary<TKey, TValue>>.SetPool(
            IObjectPool<PooledDictionary<TKey, TValue>> pool)
        {
            _pool = pool;
        }

        public void Dispose()
        {
            var pool = Interlocked.Exchange(ref _pool, null);
            if (pool == null)
                return;
            if (Count > pool.MaxRetainedCount)
                return;
            Clear();
            pool.Release(this);
        }
    }
}
```

`common/src/com.bun3.common/Runtime/Pooling/PooledHashSet.cs`:

```csharp
using System.Collections.Generic;
using System.Threading;

namespace Bun3.Common.Pooling
{
    /// <summary>
    /// A <see cref="HashSet{T}"/> rented from a shared pool. Same contract as
    /// <see cref="PooledList{T}"/>.
    /// </summary>
    public class PooledHashSet<T> : HashSet<T>, IPooledObject<PooledHashSet<T>>
    {
        private static readonly ConcurrentObjectPool<PooledHashSet<T>> SharedPool =
            new ConcurrentObjectPool<PooledHashSet<T>>();

        private IObjectPool<PooledHashSet<T>> _pool;

        /// <summary>Rents an empty set from the shared pool.</summary>
        public static PooledHashSet<T> Get()
        {
            return SharedPool.Get();
        }

        void IPooledObject<PooledHashSet<T>>.SetPool(IObjectPool<PooledHashSet<T>> pool)
        {
            _pool = pool;
        }

        public void Dispose()
        {
            var pool = Interlocked.Exchange(ref _pool, null);
            if (pool == null)
                return;
            if (Count > pool.MaxRetainedCount)
                return;
            Clear();
            pool.Release(this);
        }
    }
}
```

`common/src/com.bun3.common/Runtime/Pooling/PooledQueue.cs`:

```csharp
using System.Collections.Generic;
using System.Threading;

namespace Bun3.Common.Pooling
{
    /// <summary>
    /// A <see cref="Queue{T}"/> rented from a shared pool. Same contract as
    /// <see cref="PooledList{T}"/>.
    /// </summary>
    public class PooledQueue<T> : Queue<T>, IPooledObject<PooledQueue<T>>
    {
        private static readonly ConcurrentObjectPool<PooledQueue<T>> SharedPool =
            new ConcurrentObjectPool<PooledQueue<T>>();

        private IObjectPool<PooledQueue<T>> _pool;

        /// <summary>Rents an empty queue from the shared pool.</summary>
        public static PooledQueue<T> Get()
        {
            return SharedPool.Get();
        }

        void IPooledObject<PooledQueue<T>>.SetPool(IObjectPool<PooledQueue<T>> pool)
        {
            _pool = pool;
        }

        public void Dispose()
        {
            var pool = Interlocked.Exchange(ref _pool, null);
            if (pool == null)
                return;
            if (Count > pool.MaxRetainedCount)
                return;
            Clear();
            pool.Release(this);
        }
    }
}
```

`common/src/com.bun3.common/Runtime/Pooling/PooledStack.cs`:

```csharp
using System.Collections.Generic;
using System.Threading;

namespace Bun3.Common.Pooling
{
    /// <summary>
    /// A <see cref="Stack{T}"/> rented from a shared pool. Same contract as
    /// <see cref="PooledList{T}"/>.
    /// </summary>
    public class PooledStack<T> : Stack<T>, IPooledObject<PooledStack<T>>
    {
        private static readonly ConcurrentObjectPool<PooledStack<T>> SharedPool =
            new ConcurrentObjectPool<PooledStack<T>>();

        private IObjectPool<PooledStack<T>> _pool;

        /// <summary>Rents an empty stack from the shared pool.</summary>
        public static PooledStack<T> Get()
        {
            return SharedPool.Get();
        }

        void IPooledObject<PooledStack<T>>.SetPool(IObjectPool<PooledStack<T>> pool)
        {
            _pool = pool;
        }

        public void Dispose()
        {
            var pool = Interlocked.Exchange(ref _pool, null);
            if (pool == null)
                return;
            if (Count > pool.MaxRetainedCount)
                return;
            Clear();
            pool.Release(this);
        }
    }
}
```

`common/src/com.bun3.common/Runtime/Pooling/PooledSortedDictionary.cs`:

```csharp
using System.Collections.Generic;
using System.Threading;

namespace Bun3.Common.Pooling
{
    /// <summary>
    /// A <see cref="SortedDictionary{TKey, TValue}"/> rented from a shared pool. Same
    /// contract as <see cref="PooledList{T}"/>.
    /// </summary>
    public class PooledSortedDictionary<TKey, TValue>
        : SortedDictionary<TKey, TValue>, IPooledObject<PooledSortedDictionary<TKey, TValue>>
    {
        private static readonly ConcurrentObjectPool<PooledSortedDictionary<TKey, TValue>> SharedPool =
            new ConcurrentObjectPool<PooledSortedDictionary<TKey, TValue>>();

        private IObjectPool<PooledSortedDictionary<TKey, TValue>> _pool;

        /// <summary>Rents an empty sorted dictionary from the shared pool.</summary>
        public static PooledSortedDictionary<TKey, TValue> Get()
        {
            return SharedPool.Get();
        }

        void IPooledObject<PooledSortedDictionary<TKey, TValue>>.SetPool(
            IObjectPool<PooledSortedDictionary<TKey, TValue>> pool)
        {
            _pool = pool;
        }

        public void Dispose()
        {
            var pool = Interlocked.Exchange(ref _pool, null);
            if (pool == null)
                return;
            if (Count > pool.MaxRetainedCount)
                return;
            Clear();
            pool.Release(this);
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test common/tests/Bun3.Common.Tests`
Expected: PASS (24 tests total).

- [ ] **Step 5: Commit**

```bash
git add common/src/com.bun3.common/Runtime/Pooling common/tests
git commit -m "✨ Add PooledDictionary/HashSet/Queue/Stack/SortedDictionary"
```

---

### Task 6: PooledPriorityQueue

**Files:**
- Create: `common/src/com.bun3.common/Runtime/Pooling/PooledPriorityQueue.cs`
- Test: `common/tests/Bun3.Common.Tests/PooledPriorityQueueTests.cs`

**Interfaces:**
- Consumes: `Bun3.Common.Collections.PriorityQueue<TElement, TPriority>` (Task 2), pooling types (Task 3)
- Produces: `Bun3.Common.Pooling.PooledPriorityQueue<TElement, TPriority> : PriorityQueue<TElement, TPriority>, IPooledObject<self>` with `static Get()`.

- [ ] **Step 1: Write the failing tests**

`common/tests/Bun3.Common.Tests/PooledPriorityQueueTests.cs`:

```csharp
using Bun3.Common.Pooling;
using NUnit.Framework;

namespace Bun3.Common.Tests
{
    [TestFixture]
    public class PooledPriorityQueueTests
    {
        [Test]
        public void Get_AfterDispose_ReusesSameInstanceCleared()
        {
            var queue = PooledPriorityQueue<string, int>.Get();
            queue.Enqueue("b", 2);
            queue.Enqueue("a", 1);
            Assert.That(queue.Peek(), Is.EqualTo("a"));
            queue.Dispose();

            var reused = PooledPriorityQueue<string, int>.Get();
            Assert.That(reused, Is.SameAs(queue));
            Assert.That(reused.Count, Is.EqualTo(0));

            reused.Enqueue("c", 3);
            Assert.That(reused.Dequeue(), Is.EqualTo("c"));
            reused.Dispose();
        }

        [Test]
        public void DoubleDispose_IsNoOp_InstanceNotPooledTwice()
        {
            var queue = PooledPriorityQueue<double, double>.Get();
            queue.Dispose();
            queue.Dispose();

            var first = PooledPriorityQueue<double, double>.Get();
            var second = PooledPriorityQueue<double, double>.Get();
            Assert.That(first, Is.SameAs(queue));
            Assert.That(second, Is.Not.SameAs(queue));
            first.Dispose();
            second.Dispose();
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test common/tests/Bun3.Common.Tests`
Expected: FAIL to compile — `PooledPriorityQueue` does not exist.

- [ ] **Step 3: Implement PooledPriorityQueue**

`common/src/com.bun3.common/Runtime/Pooling/PooledPriorityQueue.cs`:

```csharp
using System.Threading;
using Bun3.Common.Collections;

namespace Bun3.Common.Pooling
{
    /// <summary>
    /// A <see cref="PriorityQueue{TElement, TPriority}"/> rented from a shared pool. Same
    /// contract as <see cref="PooledList{T}"/>.
    /// </summary>
    public class PooledPriorityQueue<TElement, TPriority>
        : PriorityQueue<TElement, TPriority>, IPooledObject<PooledPriorityQueue<TElement, TPriority>>
    {
        private static readonly ConcurrentObjectPool<PooledPriorityQueue<TElement, TPriority>> SharedPool =
            new ConcurrentObjectPool<PooledPriorityQueue<TElement, TPriority>>();

        private IObjectPool<PooledPriorityQueue<TElement, TPriority>> _pool;

        /// <summary>Rents an empty priority queue from the shared pool.</summary>
        public static PooledPriorityQueue<TElement, TPriority> Get()
        {
            return SharedPool.Get();
        }

        void IPooledObject<PooledPriorityQueue<TElement, TPriority>>.SetPool(
            IObjectPool<PooledPriorityQueue<TElement, TPriority>> pool)
        {
            _pool = pool;
        }

        public void Dispose()
        {
            var pool = Interlocked.Exchange(ref _pool, null);
            if (pool == null)
                return;
            if (Count > pool.MaxRetainedCount)
                return;
            Clear();
            pool.Release(this);
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test common/tests/Bun3.Common.Tests`
Expected: PASS (26 tests total).

- [ ] **Step 5: Commit**

```bash
git add common/src/com.bun3.common/Runtime/Pooling common/tests
git commit -m "✨ Add PooledPriorityQueue"
```

---

### Task 7: Multithreaded stress test + full verification

**Files:**
- Test: `common/tests/Bun3.Common.Tests/PoolingStressTests.cs`

**Interfaces:**
- Consumes: `PooledList<T>` (Task 4)
- Produces: nothing new — final safety net proving no instance is ever owned by two threads at once.

- [ ] **Step 1: Write the stress test**

`common/tests/Bun3.Common.Tests/PoolingStressTests.cs`:

```csharp
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Bun3.Common.Pooling;
using NUnit.Framework;

namespace Bun3.Common.Tests
{
    [TestFixture]
    public class PoolingStressTests
    {
        [Test]
        public void ParallelGetAndDispose_NeverHandsOneInstanceToTwoThreads()
        {
            var owned = new ConcurrentDictionary<PooledList<byte>, byte>();
            var doubleRentals = 0;

            Parallel.For(0, 200_000, _ =>
            {
                var list = PooledList<byte>.Get();
                if (!owned.TryAdd(list, 0))
                    Interlocked.Increment(ref doubleRentals);

                list.Add(1);

                // Give up ownership tracking BEFORE dispose — after dispose another
                // thread may legitimately rent this instance.
                owned.TryRemove(list, out var unused);
                list.Dispose();
            });

            Assert.That(doubleRentals, Is.EqualTo(0));
        }
    }
}
```

- [ ] **Step 2: Run the stress test and full suite**

Run: `dotnet test common/tests/Bun3.Common.Tests`
Expected: PASS (27 tests total), stress test included.

- [ ] **Step 3: Verify the library still compiles for netstandard2.1 and the whole solution builds**

Run: `dotnet build Bun3.sln`
Expected: Build succeeded, 0 errors. (Unity-generated csprojs in `unity/` are not part of `Bun3.sln`; if the sln build surfaces unrelated pre-existing failures in `Bun3.Server.Core`, fall back to `dotnet build common/src/com.bun3.common && dotnet build common/tests/Bun3.Common.Tests` and report the unrelated failure.)

- [ ] **Step 4: Commit**

```bash
git add common/tests
git commit -m "✅ Add multithreaded pooling stress test"
```

---

## Post-implementation notes (not tasks)

- Opening the Unity editor will generate `.meta` files for `Runtime/Collections/`, `Runtime/Pooling/`, and their files; commit those in a follow-up `🔧` commit from the Unity side.
- Migrating idlez-server call sites to this library is explicitly out of scope for this plan.
