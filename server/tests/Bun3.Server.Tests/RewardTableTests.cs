using Bun3.Gameplay.Numerics;
using Bun3.Server.Items;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class RewardTableTests
{
    private sealed class ItemState;

    /// <summary>정해진 값들을 순서대로 돌려주는 결정론 RNG.</summary>
    private sealed class ScriptedRng(params long[] values) : IRandomSource
    {
        private int _cursor;
        public int Calls => _cursor;

        public long Next(long maxExclusive)
        {
            Assert.That(_cursor, Is.LessThan(values.Length), "예상보다 많은 난수 소모");
            var value = values[_cursor++];
            Assert.That(value, Is.InRange(0, maxExclusive - 1), "시나리오 값이 범위 밖");
            return value;
        }
    }

    private ItemCatalog<string> _catalog = null!;
    private ItemId _gold;
    private ItemId _gem;
    private ItemId _sword;

    [SetUp]
    public void SetUp()
    {
        _catalog = new ItemCatalogBuilder<string>()
            .Register("gold", "골드")
            .Register("gem", "보석")
            .Register("sword", "검", unstackable: true)
            .Build();
        _gold = _catalog.GetRequired("gold");
        _gem = _catalog.GetRequired("gem");
        _sword = _catalog.GetRequired("sword");
    }

    [Test]
    public void Sample_rolls_guaranteed_group_without_probability_rng()
    {
        var table = new RewardTable(new[]
        {
            new RewardGroup(10000, grantAll: true,
                new RewardEntry(_gold, 1, 100, 200),     // 롤 1회
                new RewardEntry(_gem, 1, 3, 3)),         // 고정 — 롤 없음
        });
        var rng = new ScriptedRng(50);                    // 100 + 50 = 150
        var buffer = new List<ItemDelta>();

        table.Sample(rng, buffer);

        Assert.That(buffer, Has.Count.EqualTo(2));
        Assert.That(buffer[0].Item, Is.EqualTo(_gold));
        Assert.That(buffer[0].Amount, Is.EqualTo((BigNum)150));
        Assert.That(buffer[1].Amount, Is.EqualTo((BigNum)3));
        Assert.That(rng.Calls, Is.EqualTo(1), "확정 그룹·고정 수량은 난수를 안 쓴다");
    }

    [Test]
    public void Sample_weighted_group_triggers_by_permyriad_and_picks_by_weight()
    {
        var table = new RewardTable(new[]
        {
            new RewardGroup(2500, grantAll: false,
                new RewardEntry(_sword, 1, 1, 1),
                new RewardEntry(_gem, 9, 5, 5)),
        });
        var buffer = new List<ItemDelta>();

        // 발동 실패 (2500 <= 2500 아님: roll 2500 >= 2500)
        table.Sample(new ScriptedRng(2500), buffer);
        Assert.That(buffer, Is.Empty);

        // 발동(roll 0) + 가중 롤 0 → 첫 항목(검)
        table.Sample(new ScriptedRng(0, 0), buffer);
        Assert.That(buffer[^1].Item, Is.EqualTo(_sword));

        // 발동(roll 2499) + 가중 롤 9 → 둘째 항목(보석, 가중 1~9 구간)
        table.Sample(new ScriptedRng(2499, 9), buffer);
        Assert.That(buffer[^1].Item, Is.EqualTo(_gem));

        // 만분율 0 그룹은 난수 소모 없이 스킵
        var zero = new RewardTable(new[] { new RewardGroup(0, true, new RewardEntry(_gold, 1, 1, 1)) });
        var zeroRng = new ScriptedRng();
        zero.Sample(zeroRng, buffer);
        Assert.That(zeroRng.Calls, Is.EqualTo(0));
    }

    [Test]
    public void Constructor_rejects_bad_data_at_boot()
    {
        Assert.That(() => new RewardEntry(ItemId.None, 1, 1, 1), Throws.ArgumentException);
        Assert.That(() => new RewardEntry(_gold, -1, 1, 1), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => new RewardEntry(_gold, 1, 5, 4), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => new RewardGroup(10001, true), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => new RewardGroup(100, grantAll: false, new RewardEntry(_gold, 0, 1, 1)),
            Throws.ArgumentException, "가중 추첨은 가중치 합 양수 필수");
    }

    [Test]
    public void TryGrant_samples_and_applies_atomically()
    {
        long nextId = 0;
        var inventory = new ItemInventory<ItemState>(_catalog, () => ++nextId, _ => new ItemState());
        var table = new RewardTable(new[]
        {
            new RewardGroup(10000, grantAll: true,
                new RewardEntry(_gold, 1, 100, 100),
                new RewardEntry(_sword, 1, 1, 1)),
        });
        var created = new List<ItemInstance<ItemState>>();

        Assert.That(inventory.TryGrant(table, new ScriptedRng(), out _, created), Is.EqualTo(InventoryError.None));
        Assert.That(inventory.GetQuantity(_gold), Is.EqualTo((BigNum)100));
        Assert.That(inventory.GetQuantity(_sword), Is.EqualTo(BigNum.One));
        Assert.That(created, Has.Count.EqualTo(2));
    }
}
