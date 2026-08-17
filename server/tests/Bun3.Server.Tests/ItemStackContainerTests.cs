using Bun3.Gameplay.Numerics;
using Bun3.Server.Items;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class ItemStackContainerTests
{
    private ItemCatalog<string> _catalog = null!;
    private ItemId _potion;   // maxStack 10
    private ItemId _gold;     // 무제한
    private ItemId _gem;      // 무제한

    [SetUp]
    public void SetUp()
    {
        _catalog = new ItemCatalogBuilder<string>()
            .Register("potion", "물약", maxStack: 10)
            .Register("gold", "골드")
            .Register("gem", "보석")
            .Build();
        _potion = _catalog.GetRequired("potion");
        _gold = _catalog.GetRequired("gold");
        _gem = _catalog.GetRequired("gem");
    }

    // ---- 단건 연산 ----

    [Test]
    public void Add_remove_and_query_roundtrip()
    {
        var container = new ItemStackContainer(_catalog);

        Assert.That(container.TryAdd(_gold, 100), Is.EqualTo(ItemError.None));
        Assert.That(container.TryAdd(_gold, 50), Is.EqualTo(ItemError.None));
        Assert.That(container.GetQuantity(_gold), Is.EqualTo(150));
        Assert.That(container.TryRemove(_gold, 30), Is.EqualTo(ItemError.None));
        Assert.That(container.GetQuantity(_gold), Is.EqualTo(120));
        Assert.That(container.GetQuantity(_gem), Is.EqualTo(0));
        Assert.That(container.Contains(_gem), Is.False);
    }

    [Test]
    public void Remove_to_zero_drops_the_entry()
    {
        var container = new ItemStackContainer(_catalog);
        container.TryAdd(_gold, 5);

        Assert.That(container.TryRemove(_gold, 5), Is.EqualTo(ItemError.None));
        Assert.That(container.Count, Is.EqualTo(0));
        Assert.That(container.Contains(_gold), Is.False);
    }

    [Test]
    public void Single_op_failures_report_reasons_and_leave_state_intact()
    {
        var container = new ItemStackContainer(_catalog);
        container.TryAdd(_potion, 8);

        Assert.That(container.TryAdd(ItemId.None, 1), Is.EqualTo(ItemError.UnknownItem));
        Assert.That(container.TryAdd(_potion, 0), Is.EqualTo(ItemError.InvalidAmount));
        Assert.That(container.TryAdd(_potion, -3), Is.EqualTo(ItemError.InvalidAmount));
        Assert.That(container.TryRemove(_potion, 9), Is.EqualTo(ItemError.Insufficient));
        Assert.That(container.TryAdd(_potion, 3), Is.EqualTo(ItemError.ExceedsMaxStack)); // 8+3 > 10
        Assert.That(container.GetQuantity(_potion), Is.EqualTo(8));
    }

    [Test]
    public void Long_overflow_is_reported_as_exceeds_max_stack()
    {
        var container = new ItemStackContainer(_catalog);
        container.TryAdd(_gold, long.MaxValue - 1);

        Assert.That(container.TryAdd(_gold, 2), Is.EqualTo(ItemError.ExceedsMaxStack));
        Assert.That(container.GetQuantity(_gold), Is.EqualTo(long.MaxValue - 1));
    }

    // ---- 트랜잭션 ----

    [Test]
    public void Apply_mixed_grant_and_consume_atomically()
    {
        var container = new ItemStackContainer(_catalog);
        container.TryAdd(_potion, 5);

        Span<ItemDelta<long>> deltas = stackalloc ItemDelta<long>[3];
        deltas[0] = new ItemDelta<long>(_potion, -5);
        deltas[1] = new ItemDelta<long>(_gold, 100);
        deltas[2] = new ItemDelta<long>(_gem, 3);

        Assert.That(container.TryApply(deltas, out var failedIndex), Is.EqualTo(ItemError.None));
        Assert.That(failedIndex, Is.EqualTo(-1));
        Assert.That(container.Contains(_potion), Is.False);
        Assert.That(container.GetQuantity(_gold), Is.EqualTo(100));
        Assert.That(container.GetQuantity(_gem), Is.EqualTo(3));
    }

    [Test]
    public void Apply_failure_leaves_container_untouched_and_points_at_cause()
    {
        var container = new ItemStackContainer(_catalog);
        container.TryAdd(_gold, 10);

        Span<ItemDelta<long>> deltas = stackalloc ItemDelta<long>[3];
        deltas[0] = new ItemDelta<long>(_gold, -5);
        deltas[1] = new ItemDelta<long>(_gem, 7);
        deltas[2] = new ItemDelta<long>(_gold, -6);   // 잔량 5 < 6

        Assert.That(container.TryApply(deltas, out var failedIndex), Is.EqualTo(ItemError.Insufficient));
        Assert.That(failedIndex, Is.EqualTo(2));
        Assert.That(container.GetQuantity(_gold), Is.EqualTo(10));
        Assert.That(container.Contains(_gem), Is.False);
    }

    [Test]
    public void Apply_accumulates_duplicate_item_deltas_sequentially()
    {
        var container = new ItemStackContainer(_catalog);

        // 순차 의미론 — 지급 전에 소모하는 배치는 실패한다
        Span<ItemDelta<long>> consumeFirst = stackalloc ItemDelta<long>[2];
        consumeFirst[0] = new ItemDelta<long>(_gold, -5);
        consumeFirst[1] = new ItemDelta<long>(_gold, 10);
        Assert.That(container.TryApply(consumeFirst, out var failedIndex), Is.EqualTo(ItemError.Insufficient));
        Assert.That(failedIndex, Is.EqualTo(0));

        // 지급 후 소모는 성공하고 누적이 반영된다
        Span<ItemDelta<long>> grantFirst = stackalloc ItemDelta<long>[3];
        grantFirst[0] = new ItemDelta<long>(_gold, 10);
        grantFirst[1] = new ItemDelta<long>(_gold, -5);
        grantFirst[2] = new ItemDelta<long>(_gold, -5);
        Assert.That(container.TryApply(grantFirst, out _), Is.EqualTo(ItemError.None));
        Assert.That(container.Contains(_gold), Is.False);
    }

    [Test]
    public void Apply_duplicate_deltas_respect_max_stack_accumulation()
    {
        var container = new ItemStackContainer(_catalog);

        Span<ItemDelta<long>> deltas = stackalloc ItemDelta<long>[2];
        deltas[0] = new ItemDelta<long>(_potion, 6);
        deltas[1] = new ItemDelta<long>(_potion, 6);   // 누적 12 > maxStack 10

        Assert.That(container.TryApply(deltas, out var failedIndex), Is.EqualTo(ItemError.ExceedsMaxStack));
        Assert.That(failedIndex, Is.EqualTo(1));
        Assert.That(container.Count, Is.EqualTo(0));
    }

    [Test]
    public void Apply_rejects_zero_delta_and_empty_batch_is_success()
    {
        var container = new ItemStackContainer(_catalog);

        Span<ItemDelta<long>> deltas = stackalloc ItemDelta<long>[1];
        deltas[0] = new ItemDelta<long>(_gold, 0);
        Assert.That(container.TryApply(deltas, out var failedIndex), Is.EqualTo(ItemError.InvalidAmount));
        Assert.That(failedIndex, Is.EqualTo(0));

        Assert.That(container.TryApply(ReadOnlySpan<ItemDelta<long>>.Empty, out _), Is.EqualTo(ItemError.None));
    }

    // ---- 이동 ----

    [Test]
    public void Move_transfers_atomically_between_containers()
    {
        var source = new ItemStackContainer(_catalog);
        var target = new ItemStackContainer(_catalog);
        source.TryAdd(_gold, 100);
        target.TryAdd(_potion, 9);

        Assert.That(source.TryMoveTo(target, _gold, 40), Is.EqualTo(ItemError.None));
        Assert.That(source.GetQuantity(_gold), Is.EqualTo(60));
        Assert.That(target.GetQuantity(_gold), Is.EqualTo(40));

        Assert.That(source.TryMoveTo(target, _gold, 61), Is.EqualTo(ItemError.Insufficient));
        source.TryAdd(_potion, 5);
        Assert.That(source.TryMoveTo(target, _potion, 5), Is.EqualTo(ItemError.ExceedsMaxStack)); // 9+5 > 10
        Assert.That(source.GetQuantity(_potion), Is.EqualTo(5));
        Assert.That(target.GetQuantity(_potion), Is.EqualTo(9));
    }

    [Test]
    public void Move_rejects_catalog_mismatch_and_tolerates_self_move()
    {
        var other = new ItemCatalogBuilder<string>().Register("gold", "골드").Build();
        var source = new ItemStackContainer(_catalog);
        var foreign = new ItemStackContainer(other);
        source.TryAdd(_gold, 10);

        Assert.That(() => source.TryMoveTo(foreign, _gold, 1), Throws.ArgumentException);

        Assert.That(source.TryMoveTo(source, _gold, 10), Is.EqualTo(ItemError.None));
        Assert.That(source.GetQuantity(_gold), Is.EqualTo(10));
    }

    // ---- 로드와 열거 ----

    [Test]
    public void Load_sets_quantities_without_notification_and_checks_max_stack()
    {
        var changed = 0;
        var container = new ItemStackContainer(_catalog, onChanged: () => changed++);

        Assert.That(container.TryLoad(_gold, 500), Is.EqualTo(ItemError.None));
        Assert.That(container.TryLoad(_potion, 11), Is.EqualTo(ItemError.ExceedsMaxStack));
        Assert.That(container.TryLoad(_potion, 0), Is.EqualTo(ItemError.InvalidAmount));
        Assert.That(container.TryLoad(ItemId.None, 1), Is.EqualTo(ItemError.UnknownItem));
        Assert.That(container.GetQuantity(_gold), Is.EqualTo(500));
        Assert.That(changed, Is.EqualTo(0));
    }

    [Test]
    public void Enumeration_yields_only_held_stacks()
    {
        var container = new ItemStackContainer(_catalog);
        container.TryAdd(_gold, 7);
        container.TryAdd(_gem, 2);
        container.TryAdd(_potion, 1);
        container.TryRemove(_potion, 1);

        long total = 0;
        var count = 0;
        foreach (var stack in container)
        {
            Assert.That(stack.Quantity, Is.GreaterThan(0));
            total += stack.Quantity;
            count++;
        }

        Assert.That(count, Is.EqualTo(2));
        Assert.That(total, Is.EqualTo(9));
    }

    // ---- dirty 연계 ----

    [Test]
    public void OnChanged_fires_once_per_successful_mutation_only()
    {
        var changed = 0;
        var container = new ItemStackContainer(_catalog, onChanged: () => changed++);

        container.TryAdd(_gold, 10);                     // +1
        container.TryAdd(_potion, 99);                   // 실패 — 통지 없음
        container.TryRemove(_gold, 3);                   // +1
        Span<ItemDelta<long>> deltas = stackalloc ItemDelta<long>[2];
        deltas[0] = new ItemDelta<long>(_gold, -1);
        deltas[1] = new ItemDelta<long>(_gem, 1);
        container.TryApply(deltas, out _);               // 배치당 +1
        container.Clear();                               // +1
        container.Clear();                               // 빈 상태 — 통지 없음

        Assert.That(changed, Is.EqualTo(4));
    }

    [Test]
    public void Move_notifies_both_containers_once()
    {
        var sourceChanged = 0;
        var targetChanged = 0;
        var source = new ItemStackContainer(_catalog, onChanged: () => sourceChanged++);
        var target = new ItemStackContainer(_catalog, onChanged: () => targetChanged++);
        source.TryAdd(_gold, 10);
        sourceChanged = 0;

        source.TryMoveTo(target, _gold, 4);
        Assert.That(sourceChanged, Is.EqualTo(1));
        Assert.That(targetChanged, Is.EqualTo(1));
    }

    // ---- BigNum 차등 ----

    [Test]
    public void BigNum_container_mirrors_long_semantics()
    {
        var container = new BigNumItemStackContainer(_catalog);

        Assert.That(container.TryAdd(_gold, (BigNum)1_000_000), Is.EqualTo(ItemError.None));
        Assert.That(container.TryRemove(_gold, (BigNum)999_999), Is.EqualTo(ItemError.None));
        Assert.That(container.GetQuantity(_gold), Is.EqualTo((BigNum)1));
        Assert.That(container.TryRemove(_gold, (BigNum)2), Is.EqualTo(ItemError.Insufficient));
        Assert.That(container.TryAdd(_potion, (BigNum)11), Is.EqualTo(ItemError.ExceedsMaxStack));
        Assert.That(container.TryAdd(_gold, BigNum.Zero), Is.EqualTo(ItemError.InvalidAmount));

        // 전량 소모 — 뺄셈이 정확히 Zero를 돌려주고 엔트리가 제거된다
        Assert.That(container.TryRemove(_gold, (BigNum)1), Is.EqualTo(ItemError.None));
        Assert.That(container.Contains(_gold), Is.False);
    }

    [Test]
    public void BigNum_transaction_handles_astronomical_quantities()
    {
        var container = new BigNumItemStackContainer(_catalog);
        var huge = BigNum.FromParts(5, 1000);   // 5e1000

        Span<ItemDelta<BigNum>> deltas = stackalloc ItemDelta<BigNum>[2];
        deltas[0] = new ItemDelta<BigNum>(_gold, huge);
        deltas[1] = new ItemDelta<BigNum>(_gold, -huge);
        Assert.That(container.TryApply(deltas, out _), Is.EqualTo(ItemError.None));
        Assert.That(container.Contains(_gold), Is.False);

        container.TryAdd(_gold, huge);
        Assert.That(container.TryRemove(_gold, BigNum.FromParts(6, 1000)), Is.EqualTo(ItemError.Insufficient));
        Assert.That(container.GetQuantity(_gold), Is.EqualTo(huge));
    }

    // ---- 무할당 ----

    [Test]
    public void Hot_path_operations_do_not_allocate()
    {
        var container = new ItemStackContainer(_catalog, capacity: 8, onChanged: static () => { });
        Span<ItemDelta<long>> deltas = stackalloc ItemDelta<long>[2];
        deltas[0] = new ItemDelta<long>(_gold, 5);
        deltas[1] = new ItemDelta<long>(_gem, 3);

        // 워밍업 — 딕셔너리 엔트리·JIT 정리
        for (var i = 0; i < 3; i++)
        {
            container.TryAdd(_gold, 10);
            container.TryRemove(_gold, 10);
            container.TryApply(deltas, out _);
            container.TryRemove(_gold, 5);
            container.TryRemove(_gem, 3);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            container.TryAdd(_gold, 10);
            container.GetQuantity(_gold);
            container.TryRemove(_gold, 10);
            container.TryApply(deltas, out _);
            container.TryRemove(_gold, 5);
            container.TryRemove(_gem, 3);
            foreach (var stack in container)
            {
                _ = stack.Quantity;
            }
        }

        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.EqualTo(0));
    }
}
